using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using Torch.API;
using Torch.Utils;
using VRage;
using VRage.ModAPI;
using VRage.Scripting;

namespace Torch.Managers;

public class ScriptCompilationManager : Manager
{
    [ReflectedSetter(Name = "m_terminationReason")]
    private static Action<MyProgrammableBlock, MyProgrammableBlock.ScriptTerminationReason> TerminationReasonSetter = null!;

    [ReflectedGetter(Name = "ScriptComponent")]
    private static Func<MyProgrammableBlock, MyIngameScriptComponent> ScriptComponentGetter = null!;

    [ReflectedMethod]
    private static Action<MyProgrammableBlock, string> SetDetailedInfo = null!;

    [ReflectedSetter(Name = "m_instance")]
    private static Action<MyProgrammableBlock, IMyGridProgram> InstanceSetter = null!;

    [ReflectedSetter(Name = "m_assembly")]
    private static Action<MyProgrammableBlock, Assembly> AssemblySetter = null!;

    [ReflectedMethod]
    private static Func<MyProgrammableBlock, Assembly, IEnumerable<string>, string, bool> CreateInstance = null!;

    [ReflectedGetter(Name = "m_compilerErrors")]
    private static Func<MyProgrammableBlock, List<string>> CompilerErrorsGetter = null!;

    [ReflectedGetter(Name = "m_modApiWhitelistDiagnosticAnalyzer")]
    private static Func<MyScriptCompiler, DiagnosticAnalyzer> ModWhitelistAnalyzer = null!;

    [ReflectedGetter(Name = "m_ingameWhitelistDiagnosticAnalyzer")]
    private static Func<MyScriptCompiler, DiagnosticAnalyzer> ScriptWhitelistAnalyzer = null!;

    [ReflectedMethod]
    private static Func<MyScriptCompiler, CSharpCompilation, SyntaxTree, int, SyntaxTree> InjectMod = null!;

    [ReflectedMethod]
    private static Func<MyScriptCompiler, CSharpCompilation, SyntaxTree, SyntaxTree> InjectInstructionCounter = null!;

    [ReflectedMethod]
    private static Func<MyScriptCompiler, CompilationWithAnalyzers, EmitResult, List<Message>, bool, Task<bool>> EmitDiagnostics = null!;

    [ReflectedMethod]
    private static Func<MyScriptCompiler, MyApiTarget, string, IList<SyntaxTree>, string, Task> WriteDiagnostics = null!;

    [ReflectedMethod(Name = "WriteDiagnostics")]
    private static Func<MyScriptCompiler, MyApiTarget, string, IEnumerable<Message>, bool, Task> WriteDiagnostics2 = null!;
    
    [ReflectedGetter(Name = "m_metadataReferences")]
    private static Func<MyScriptCompiler, List<MetadataReference>> MetadataReferencesGetter = null!;

    private readonly ConditionalWeakTable<MyProgrammableBlock, AssemblyLoadContext> _contexts = new();

    public ScriptCompilationManager(ITorchBase torchInstance) : base(torchInstance)
    {
    }

    public async void CompileAsync(MyProgrammableBlock block, string program, string storage, bool instantiate)
    {
        TerminationReasonSetter(block, MyProgrammableBlock.ScriptTerminationReason.None);

        var component = ScriptComponentGetter(block);
        component.NextUpdate = UpdateType.None;
        component.NeedsUpdate = MyEntityUpdateEnum.NONE;

        try
        {
            if (_contexts.TryGetValue(block, out var context))
            {
                InstanceSetter(block, null);
                AssemblySetter(block, null);
                context!.Unload();
            }

            _contexts.AddOrUpdate(block, context = new AssemblyLoadContext(null, true));

            var messages = new List<Message>();
            var assembly = await CompileAsync(context, MyApiTarget.Ingame,
                                              $"pb_{block.EntityId}_{Random.Shared.NextInt64()}",
                                              new[]
                                              {
                                                  MyVRage.Platform.Scripting.GetIngameScript(
                                                      program, "Program", nameof(MyGridProgram))
                                              }, messages, $"PB: {block.DisplayName} ({block.EntityId})");
            AssemblySetter(block, assembly);
            var errors = CompilerErrorsGetter(block);

            errors.Clear();
            errors.AddRange(messages.Select(b => b.Text));

            if (instantiate)
                await Torch.InvokeAsync(() => CreateInstance(block, assembly, errors, storage));
        }
        catch (Exception e)
        {
            await Torch.InvokeAsync(() => SetDetailedInfo(block, e.ToString()));
            throw;
        }
    }

    public async Task<Assembly> CompileAsync(AssemblyLoadContext context, MyApiTarget target, string assemblyName, IEnumerable<Script> scripts, List<Message> messages, string friendlyName, bool enableDebugInformation = false)
    {
        friendlyName ??= "<No Name>";
        Func<CSharpCompilation, SyntaxTree, SyntaxTree> syntaxTreeInjector;
        DiagnosticAnalyzer whitelistAnalyzer;
        switch (target)
        {
            case MyApiTarget.None:
                whitelistAnalyzer = null;
                syntaxTreeInjector = null;
                break;
            case MyApiTarget.Mod:
            {
                var modId = MyModWatchdog.AllocateModId(friendlyName);
                whitelistAnalyzer = ModWhitelistAnalyzer(MyScriptCompiler.Static);
                syntaxTreeInjector = (c, st) => InjectMod(MyScriptCompiler.Static, c, st, modId);
                break;
            }
            case MyApiTarget.Ingame:
                syntaxTreeInjector = (c, t) => InjectInstructionCounter(MyScriptCompiler.Static, c, t);
                whitelistAnalyzer = ScriptWhitelistAnalyzer(MyScriptCompiler.Static);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Invalid compilation target");
        }
        var compilation = CreateCompilation(assemblyName, scripts);
        
        await WriteDiagnostics(MyScriptCompiler.Static, target, assemblyName, compilation.SyntaxTrees, null).ConfigureAwait(false);
        var injectionFailed = false;
        var compilationWithoutInjection = compilation;
        if (syntaxTreeInjector != null)
        {
            SyntaxTree[] newSyntaxTrees = null;
            try
            {
                var syntaxTrees = compilation.SyntaxTrees;
                if (syntaxTrees.Length == 1)
                {
                    newSyntaxTrees = new[] { syntaxTreeInjector(compilation, syntaxTrees[0]) };
                }
                else
                {
                    newSyntaxTrees = await Task
                                           .WhenAll(syntaxTrees.Select(
                                                        x => Task.Run(() => syntaxTreeInjector(compilation, x))))
                                           .ConfigureAwait(false);
                }
            }
            catch
            {
                injectionFailed = true;
            }
            if (!injectionFailed)
            {
                await WriteDiagnostics(MyScriptCompiler.Static, target, assemblyName, newSyntaxTrees, ".injected").ConfigureAwait(false);
                compilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(newSyntaxTrees);
            }
        }
        CompilationWithAnalyzers analyticCompilation = null;
        if (whitelistAnalyzer != null)
        {
            analyticCompilation = compilation.WithAnalyzers(ImmutableArray.Create(whitelistAnalyzer));
            compilation = (CSharpCompilation)analyticCompilation.Compilation;
        }

        using var assemblyStream = new MemoryStream();

        var emitResult = compilation.Emit(assemblyStream);
        var success = emitResult.Success;
        var myBlacklistSyntaxVisitor = new MyBlacklistSyntaxVisitor();
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            myBlacklistSyntaxVisitor.SetSemanticModel(compilation.GetSemanticModel(syntaxTree, false));
            myBlacklistSyntaxVisitor.Visit(await syntaxTree.GetRootAsync());
        }
        if (myBlacklistSyntaxVisitor.HasAnyResult())
        {
            myBlacklistSyntaxVisitor.GetResultMessages(messages);
        }
        else
        {
            success = await EmitDiagnostics(MyScriptCompiler.Static, analyticCompilation, emitResult, messages, success).ConfigureAwait(false);
            await WriteDiagnostics2(MyScriptCompiler.Static, target, assemblyName, messages, success).ConfigureAwait(false);
            assemblyStream.Seek(0, SeekOrigin.Begin);
            if (injectionFailed) return null;
            if (success)
                return context.LoadFromStream(assemblyStream);

            await EmitDiagnostics(MyScriptCompiler.Static, analyticCompilation, compilationWithoutInjection.Emit(assemblyStream), messages, false).ConfigureAwait(false);
        }

        return null;
    }

    private readonly CSharpCompilationOptions _compilationOptions = new(OutputKind.DynamicallyLinkedLibrary);
    private readonly CSharpParseOptions _parseOptions = new(LanguageVersion.CSharp10, DocumentationMode.None);
    
    private CSharpCompilation CreateCompilation(string assemblyFileName, IEnumerable<Script> scripts)
    {
        if (scripts == null)
            return CSharpCompilation.Create(assemblyFileName, null, MetadataReferencesGetter(MyScriptCompiler.Static),
                                            _compilationOptions);
        
        var parseOptions = _parseOptions.WithPreprocessorSymbols(MyScriptCompiler.Static.ConditionalCompilationSymbols);
        var enumerable = scripts.Select(s => CSharpSyntaxTree.ParseText(s.Code, parseOptions, s.Name, Encoding.UTF8));
        
        return CSharpCompilation.Create(assemblyFileName, enumerable, MetadataReferencesGetter(MyScriptCompiler.Static), _compilationOptions);
    }
}