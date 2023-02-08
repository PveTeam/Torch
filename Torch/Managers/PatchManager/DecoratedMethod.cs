using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MonoMod.Utils;
using NLog;
using Torch.Managers.PatchManager.MSIL;
using Torch.Managers.PatchManager.Transpile;

namespace Torch.Managers.PatchManager
{
    internal class DecoratedMethod : MethodRewritePattern
    {
        private static readonly ConcurrentDictionary<MethodBase, DecoratedMethod> Methods = new();

        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private readonly MethodBase _method;
        private readonly Harmony _harmony;

        private readonly PatchProcessor _processor;
        private bool _hasRan;

        internal DecoratedMethod(MethodBase method, Harmony harmony) : base(null)
        {
            _method = method;
            _harmony = harmony;
            _processor = harmony.CreateProcessor(method);
            Methods[method] = this;
        }

        internal bool HasChanged()
        {
            return Prefixes.HasChanges() || Suffixes.HasChanges() || Transpilers.HasChanges() || PostTranspilers.HasChanges();
        }

        internal void Commit()
        {
            try
            {
                // non-greedy so they are all reset
                if (!Prefixes.HasChanges(true) & !Suffixes.HasChanges(true) & !Transpilers.HasChanges(true) & !PostTranspilers.HasChanges(true))
                    return;
                Revert();

                if (Prefixes.Count == 0 && Suffixes.Count == 0 && Transpilers.Count == 0 && PostTranspilers.Count == 0)
                    return;
                _log.Log(PrintMode != 0 ? LogLevel.Info : LogLevel.Debug,
                    $"Begin patching {_method.DeclaringType?.FullName}#{_method.Name}({string.Join(", ", _method.GetParameters().Select(x => x.ParameterType.Name))})");

                foreach (var prefix in Prefixes)
                {
                    _processor.AddPrefix(prefix);
                }
                
                foreach (var suffix in Suffixes)
                {
                    _processor.AddPostfix(suffix);
                }

                if (Transpilers.Any() || PostTranspilers.Any())
                    _processor.AddTranspiler(SymbolExtensions.GetMethodInfo(() => TranspilerProxy(null, null, null)));

                _processor.Patch();

                _log.Log(PrintMode != 0 ? LogLevel.Info : LogLevel.Debug,
                    $"Done patching {_method.GetID()})");
                
                _hasRan = true;
            }
            catch (Exception exception)
            {
                _log.Fatal(exception, $"Error patching {_method.GetID()}");
                throw;
            }
        }

        private static IEnumerable<CodeInstruction> TranspilerProxy(IEnumerable<CodeInstruction> instructions,
                                                                    MethodBase __originalMethod,
                                                                    ILGenerator generator)
        {
            if (!Methods.TryGetValue(__originalMethod, out var decoratedMethod))
                throw new Exception($"Unknown method {__originalMethod.GetID()}");

            var loggingGenerator = new LoggingIlGenerator(generator, decoratedMethod.PrintMode != 0 ? LogLevel.Info : LogLevel.Debug);

            MsilLocal LocalFactory(Type type) => new(loggingGenerator.DeclareLocal(type));

            foreach (var transpiler in decoratedMethod.Transpilers.Concat(decoratedMethod.PostTranspilers))
            {
                var ins = (IEnumerable<MsilInstruction>) transpiler.Invoke(null, transpiler.GetParameters().Select<ParameterInfo, object>(b => b switch
                {
                    _ when b.ParameterType.IsAssignableTo(typeof(MethodBase)) => __originalMethod,
                    _ when b.ParameterType.IsAssignableTo(typeof(IEnumerable<MsilInstruction>)) => instructions.Select(c => new MsilInstruction(c)),
                    _ when b.ParameterType.IsAssignableTo(typeof(Func<Type, MsilLocal>)) => new Func<Type, MsilLocal>(LocalFactory),
                    _ => null
                }).ToArray());

                instructions = ins!.Select(b => b.ToCodeIns(loggingGenerator)).ToList();
            }

            return instructions;
        }

        internal void Revert()
        {
            if (!_hasRan)
                return;
            
            _log.Debug($"Revert {_method.GetID()}");
            _processor.Unpatch(HarmonyPatchType.All, _harmony.Id);
        }
    }
}