using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Torch.API;
using Torch.Views;

namespace Torch.Server;

public class TorchConfig : ViewModel, ITorchConfig
{
    public bool ShouldUpdatePlugins => (GetPluginUpdates && !NoUpdate) || ForceUpdate;
    public bool ShouldUpdateTorch => (GetTorchUpdates && !NoUpdate) || ForceUpdate;

    /// <inheritdoc />
    [XmlIgnore]
    public bool NoUpdate { get; set; }

    /// <inheritdoc />
    [XmlIgnore]
    public bool ForceUpdate { get; set; }

    /// <summary>
    /// Permanent flag to ALWAYS automatically start the server
    /// </summary>
    [Display(Name = "Auto Start", Description = "Permanent flag to ALWAYS automatically start the server.", GroupName = "Server")]
    public bool Autostart { get; set; }

    /// <summary>
    /// Temporary flag to automatically start the server only on the next run
    /// </summary>
    [XmlIgnore]
    public bool TempAutostart { get; set; }

    /// <inheritdoc />
    [Display(Name = "Restart On Crash", Description = "Automatically restart the server if it crashes.", GroupName = "Server")]
    public bool RestartOnCrash { get; set; }

    public string InstancePath { get; set; }

    /// <inheritdoc />
    [Display(Name = "No GUI", Description = "Do not show the Torch UI.", GroupName = "Window")]
    public bool NoGui { get; set; }

    /// <inheritdoc />
    [Display(Name = "Update Torch", Description = "Check every start for new versions of torch.",
             GroupName = "Server")]
    public bool GetTorchUpdates { get; set; } = true;

    public string InstanceName { get; set; }

    /// <inheritdoc />
    [Display(Name = "Update Plugins", Description = "Check every start for new versions of plugins.",
             GroupName = "Server")]
    public bool GetPluginUpdates { get; set; } = true;

    /// <inheritdoc />
    [Display(Name = "Watchdog Timeout", Description = "Watchdog timeout (in seconds).", GroupName = "Server")]
    public int TickTimeout { get; set; } = 60;

    /// <inheritdoc />
    public List<Guid> Plugins { get; set; } = new();

    [Display(Name = "Local Plugins", Description = "Loads all pluhins from disk, ignores the plugins defined in config.", GroupName = "In-Game")]
    public bool LocalPlugins { get; set; }

    [Display(Name = "Auto Disconnect", Description = "When server restarts, all clients are rejected to main menu to prevent auto rejoin.", GroupName = "In-Game")]
    public bool DisconnectOnRestart { get; set; }

    [Display(Name = "Chat Name", Description = "Default name for chat from gui, broadcasts etc..",
             GroupName = "In-Game")]
    public string ChatName { get; set; } = "Server";

    [Display(Name = "Chat Color",
             Description = "Default color for chat from gui, broadcasts etc.. (Red, Blue, White, Green)",
             GroupName = "In-Game")]
    public string ChatColor { get; set; } = "Red";

    [Display(Name = "Enable Whitelist", Description = "Enable Whitelist to prevent random players join while maintance, tests or other.", GroupName = "In-Game")]
    public bool EnableWhitelist { get; set; }

    [Display(Name = "Whitelist", Description = "Collection of whitelisted steam ids.", GroupName = "In-Game")]
    public List<ulong> Whitelist { get; set; } = new();

    [Display(Name = "Width", Description = "Default window width.", GroupName = "Window")]
    public int WindowWidth { get; set; } = 980;

    [Display(Name = "Height", Description = "Default window height", GroupName = "Window")]
    public int WindowHeight { get; set; } = 588;

    [Display(Name = "Font Size", Description = "Font size for logging text box. (default is 16)",
             GroupName = "Window")]
    public int FontSize { get; set; } = 16;

    [Display(Name = "UGC Service Type", Description = "Service for downloading mods", GroupName = "Server")]
    public UGCServiceType UgcServiceType { get; set; } = UGCServiceType.Steam;

    public string LastUsedTheme { get; set; } = "Torch Theme";

    [Display(Name = "Independent Console", Description = "Keeps a separate console window open after the main UI loads.", GroupName = "Window")]
    public bool IndependentConsole { get; set; }

    [XmlIgnore]
    public string TestPlugin { get; set; }

    [Display(Name = "Enable Asserts", Description = "Enable Keen's assert logging.", GroupName = "Server")]
    public bool EnableAsserts { get; set; }

    [Display(Name = "Enable Entity Manager", Description = "Enable Entity Manager tab. (can affect performance)",
             GroupName = "Server")]
    public bool EntityManagerEnabled { get; set; } = true;

    [Display(Name = "Login Token", Description = "Steam GSLT (can be used if you have dynamic ip)", GroupName = "Server")]
    public string LoginToken { get; set; }
    
    // for backward compatibility
    public void Save(string path = null) => Initializer.Instance?.ConfigPersistent?.Save(path);
}