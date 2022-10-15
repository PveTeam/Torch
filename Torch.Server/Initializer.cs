using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using NLog;
using NLog.Targets;
using Sandbox.Engine.Utils;
using Torch.Utils;
using VRage.FileSystem;

namespace Torch.Server
{
    public class Initializer
    {
        internal static Initializer Instance { get; private set; }

        private static readonly Logger Log = LogManager.GetLogger(nameof(Initializer));
        private bool _init;
        private const string STEAMCMD_DIR = "steamcmd";
        private const string STEAMCMD_ZIP = "temp.zip";
        private static readonly string STEAMCMD_EXE = "steamcmd.exe";
        private const string STEAMCMD_ARGS = "+force_install_dir \"{0}\" +login anonymous +app_update 298740 +quit";
        private TorchServer _server;

        internal Persistent<TorchConfig> ConfigPersistent { get; }
        public TorchConfig Config => ConfigPersistent?.Data;
        public TorchServer Server => _server;

        public Initializer(Persistent<TorchConfig> torchConfig)
        {
            Instance = this;
            ConfigPersistent = torchConfig;
        }

        public bool Initialize(IConfiguration configuration)
        {
            if (_init)
                return false;
#if DEBUG
            //enables logging debug messages when built in debug mode. Amazing.
            LogManager.Configuration.AddRule(LogLevel.Debug, LogLevel.Debug, "main");
            LogManager.Configuration.AddRule(LogLevel.Debug, LogLevel.Debug, "console");
            LogManager.Configuration.AddRule(LogLevel.Debug, LogLevel.Debug, "wpf");
            LogManager.ReconfigExistingLoggers();
            Log.Debug("Debug logging enabled.");
#endif
            
            if (!configuration.GetValue("noupdate", false))
                RunSteamCmd(configuration);

            var processPid = configuration.GetValue<int>("waitForPid");
            if (processPid != 0)
            {
                try
                {
                    var waitProc = Process.GetProcessById(processPid);
                    Log.Info("Continuing in 5 seconds.");
                    Log.Warn($"Waiting for process {processPid} to close");
                    while (!waitProc.HasExited)
                    {
                        Console.Write(".");
                        Thread.Sleep(1000);
                    }
                }
                catch (Exception e)
                {
                    Log.Warn(e);
                }
            }

            _init = true;
            return true;
        }

        public void Run()
        {
            _server = new TorchServer(Config, ApplicationContext.Current.InstanceDirectory.FullName, ApplicationContext.Current.InstanceName);

            if (ApplicationContext.Current.IsService || Config.NoGui)
            {
                _server.Init();
                _server.Start();
            }
            else
            {
#if !DEBUG
                if (!Config.IndependentConsole)
                {
                    Console.SetOut(TextWriter.Null);
                    NativeMethods.FreeConsole();
                }
#endif
                
                var gameThread = new Thread(() =>
                {
                    _server.Init();

                    if (Config.Autostart || Config.TempAutostart)
                    {
                        Config.TempAutostart = false;
                        _server.Start();
                    }
                });
                
                gameThread.Start();
                
                var ui = new TorchUI(_server);
                
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                
                ui.ShowDialog();
            }
        }
        
        public static void RunSteamCmd(IConfiguration configuration)
        {
            var log = LogManager.GetLogger("SteamCMD");

            var path = configuration.GetValue<string>("steamCmdPath") ?? ApplicationContext.Current.TorchDirectory
                .CreateSubdirectory(STEAMCMD_DIR).FullName;
            
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            var steamCmdExePath = Path.Combine(path, STEAMCMD_EXE);
            if (!File.Exists(steamCmdExePath))
            {
                try
                {
                    log.Info("Downloading SteamCMD.");
                    using (var client = new HttpClient()) 
                    using (var file = File.Create(STEAMCMD_ZIP))
                    using (var stream = client.GetStreamAsync("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip").Result)
                        stream.CopyTo(file);

                    ZipFile.ExtractToDirectory(STEAMCMD_ZIP, path);
                    File.Delete(STEAMCMD_ZIP);
                    log.Info("SteamCMD downloaded successfully!");
                }
                catch (Exception e)
                {
                    log.Error(e, "Failed to download SteamCMD, unable to update the DS.");
                    return;
                }
            }

            log.Info("Checking for DS updates.");
            var steamCmdProc = new ProcessStartInfo(steamCmdExePath)
            {
                Arguments = string.Format(STEAMCMD_ARGS, configuration.GetValue("gamePath", "../")),
                WorkingDirectory = path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.ASCII
            };
            var cmd = Process.Start(steamCmdProc);
            
            // ReSharper disable once PossibleNullReferenceException
            while (!cmd.HasExited)
            {
                log.Info(cmd.StandardOutput.ReadLine());
                Thread.Sleep(100);
            }
        }
    }
}
