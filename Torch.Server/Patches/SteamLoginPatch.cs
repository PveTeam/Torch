using System.Reflection;
using NLog;
using Steamworks;
using Torch.Managers.PatchManager;
using Torch.Utils;

namespace Torch.Patches;

[PatchShim]
public static class SteamLoginPatch
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();
    
    [ReflectedMethodInfo(null, "LogOnAnonymous", TypeName = "VRage.Steam.MySteamGameServer, VRage.Steam")]
    private static readonly MethodInfo LoginMethod;

    public static void Patch(PatchContext context)
    {
        context.GetPattern(LoginMethod).AddPrefix();
    }
        
    private static bool Prefix()
    {
#pragma warning disable CS0618
        var token = TorchBase.Instance.Config.LoginToken;
#pragma warning restore CS0618

        if (string.IsNullOrEmpty(token))
            return true;

        Log.Info("Logging in to Steam with GSLT");
        SteamGameServer.LogOn(token);
        return false;
    }
}