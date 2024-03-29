﻿using System;
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
using SpaceEngineers.Game;
using Torch.Utils;
using VRage.FileSystem;

namespace Torch.Server
{
    public class Initializer
    {
        internal static Initializer Instance { get; private set; }

        private static readonly Logger Log = LogManager.GetLogger(nameof(Initializer));
        private bool _init;
        private const string TOOL_DIR = "tool";
        private const string TOOL_ZIP = "temp.zip";
        private static readonly string TOOL_EXE = "steamcmd.exe";
        private const string TOOL_ARGS = "+force_install_dir \"{0}\" +login anonymous +app_update 298740 +quit";
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
            
            if (configuration.GetValue("getGameUpdates", true) && !configuration.GetValue("noupdate", false))
                RunSteamCmdAsync(configuration).Wait();

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

        public void Run(IConfiguration configuration)
        {
            _server = new TorchServer(Config, ApplicationContext.Current.InstanceDirectory.FullName, ApplicationContext.Current.InstanceName, configuration);

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
                
                _server.Init();
                
                var uiThread = new Thread(() =>
                {
                    var ui = new TorchUI(_server);
                
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                
                    ui.ShowDialog();
                });
                
                uiThread.SetApartmentState(ApartmentState.STA);
                uiThread.Start();
                
                if (Config.Autostart || Config.TempAutostart)
                {
                    Config.TempAutostart = false;
                    _server.Start();
                }

                uiThread.Join();
            }
        }
        
        public static async Task RunSteamCmdAsync(IConfiguration configuration)
        {
            var log = LogManager.GetLogger("SteamCMD");
            
            var path = configuration.GetValue<string>("steamToolPath") ?? ApplicationContext.Current.TorchDirectory
                .CreateSubdirectory(TOOL_DIR).FullName;

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            
            var toolExe = Path.Combine(path, TOOL_EXE);
            if (!File.Exists(toolExe))
            {
                try
                {
                    log.Info("Downloading SteamCMD.");
                    using (var client = new HttpClient())
                    await using (var file = File.Create(TOOL_ZIP))
                    await using (var stream = await client.GetStreamAsync("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip"))
                        await stream.CopyToAsync(file);

                    ZipFile.ExtractToDirectory(TOOL_ZIP, path);
                    File.Delete(TOOL_ZIP);
                    log.Info("SteamCMD downloaded successfully!");
                }
                catch (Exception e)
                {
                    log.Error(e, "Failed to download SteamCMD, unable to update the DS.");
                    return;
                }
            }

            log.Info("Checking for DS updates.");
            var steamCmdProc = new ProcessStartInfo(toolExe)
            {
                Arguments = string.Format(TOOL_ARGS, configuration.GetValue("gamePath", "../")),
                WorkingDirectory = path,
                RedirectStandardOutput = true
            };
            var cmd = Process.Start(steamCmdProc)!;
            
            while (!cmd.HasExited)
            {
                if (await cmd.StandardOutput.ReadLineAsync() is { } line)
                    log.Info(line);
            }
        }
    }
}
