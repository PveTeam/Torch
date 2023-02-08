using System;
using System.Collections.Generic;
using Torch.API;

namespace Torch
{
    public interface ITorchConfig
    {
        bool Autostart { get; set; }
        bool ForceUpdate { get; set; }
        bool GetPluginUpdates { get; set; }
        bool GetTorchUpdates { get; set; }
        [Obsolete("Use Torch.InstanceName instead")]
        string InstanceName { get; set; }
        [Obsolete("Use Torch.InstancePath instead")]
        string InstancePath { get; set; }
        bool NoGui { get; set; }
        bool NoUpdate { get; set; }
        List<Guid> Plugins { get; set; }
        bool LocalPlugins { get; set; }
        bool RestartOnCrash { get; set; }
        bool ShouldUpdatePlugins { get; }
        bool ShouldUpdateTorch { get; }
        int TickTimeout { get; set; }
        string ChatName { get; set; }
        string ChatColor { get; set; }
        string TestPlugin { get; set; }
        bool DisconnectOnRestart { get; set; }
        int WindowWidth { get; set; }
        int WindowHeight { get; set; }
        int FontSize { get; set; }
        UGCServiceType UgcServiceType { get; set; }
        bool EntityManagerEnabled { get; set; }
        string LoginToken { get; set; }
        UpdateSource UpdateSource { get; set; }
        List<string> Packages { get; set; }

        void Save(string path = null);
    }

    public class UpdateSource
    {
        public UpdateSourceType SourceType { get; set; }
        public string Url { get; set; }
        public string Repository { get; set; }
        public string Branch { get; set; }
    }

    public enum UpdateSourceType
    {
        Github,
        Jenkins
    }
}