using System;
using System.Reflection;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.World;
using Torch.API.Managers;
using Torch.Managers;
using Torch.Managers.PatchManager;
using Torch.Utils;

namespace Torch.Patches;

[PatchShim]
public static class ProgramableBlockPatch
{
    [ReflectedMethodInfo(typeof(MyProgrammableBlock), "Compile")]
    private static MethodInfo CompileMethod = null!;
    
    public static void Patch(PatchContext context)
    {
        context.GetPattern(CompileMethod).AddPrefix();
    }

    private static bool Prefix(MyProgrammableBlock __instance, string program, string storage, bool instantiate)
    {
        if (!MySession.Static.EnableIngameScripts || __instance.CubeGrid is {IsPreview: true} or {CreatePhysics: false})
            return false;

#pragma warning disable CS0618
        TorchBase.Instance.CurrentSession.Managers.GetManager<ScriptCompilationManager>().CompileAsync(__instance, program, storage, instantiate);
#pragma warning restore CS0618
        return false;
    }
}