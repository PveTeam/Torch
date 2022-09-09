using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Xml;
using NLog;
using NLog.Config;
using NLog.Targets;
using Torch.API;
using Torch.Utils;

namespace Torch.Server
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var configurationBuilder = new ConfigurationBuilder()
                .AddEnvironmentVariables("TORCH")
                .AddCommandLine(args);
            var configuration = configurationBuilder.Build();
            
            var context = CreateApplicationContext(configuration);

            SetupLogging(context);
            var config = SetupConfiguration(context, configurationBuilder);
            
            var handler = new UnhandledExceptionHandler(config.Data);
            AppDomain.CurrentDomain.UnhandledException += handler.OnUnhandledException;

            var initializer = new Initializer(config);
            if (!initializer.Initialize(configuration))
                Environment.Exit(1);

#if DEBUG
            TorchLauncher.Launch(context.TorchDirectory.FullName, context.GameBinariesDirectory.FullName);
#else
            TorchLauncher.Launch(context.TorchDirectory.FullName, Path.Combine(context.TorchDirectory.FullName, "torch64"),
                context.GameBinariesDirectory.FullName);
#endif

            initializer.Run();
        }

        private static void SetupLogging(IApplicationContext context)
        {
            var oldNlog = Path.Combine(context.TorchDirectory.FullName, "NLog.config");
            var newNlog = Path.Combine(context.InstanceDirectory.FullName, "NLog.config");
            if (File.Exists(oldNlog) && !File.ReadAllText(oldNlog).Contains("FlowDocument"))
                File.Move(oldNlog, newNlog);
            else if (!File.Exists(newNlog))
                using (var f = File.Create(newNlog))
                    typeof(Program).Assembly.GetManifestResourceStream("Torch.Server.NLog.config")!.CopyTo(f);
            
            Target.Register<LogViewerTarget>(nameof(LogViewerTarget));
            TorchLogManager.RegisterTargets(Environment.GetEnvironmentVariable("TORCH_LOG_EXTENSIONS_PATH") ??
                                            Path.Combine(context.InstanceDirectory.FullName, "LoggingExtensions"));
            
            TorchLogManager.SetConfiguration(new XmlLoggingConfiguration(newNlog));
        }

        private static Persistent<TorchConfig> SetupConfiguration(IApplicationContext context, IConfigurationBuilder builder)
        {
            var oldTorchCfg = Path.Combine(context.TorchDirectory.FullName, "Torch.cfg");
            var torchCfg = Path.Combine(context.InstanceDirectory.FullName, "Torch.cfg");
            
            if (File.Exists(oldTorchCfg))
                File.Move(oldTorchCfg, torchCfg);

            var configurationSource = new XmlConfigurationSource
            {
                Path = torchCfg
            };
            
            configurationSource.ResolveFileProvider();
            builder.Sources.Insert(0, configurationSource);

            var configuration = builder.Build();

            var config = new Persistent<TorchConfig>(torchCfg, configuration.Get<TorchConfig>());
            config.Data.InstanceName = context.InstanceName;
            config.Data.InstancePath = context.InstanceDirectory.FullName;

            return config;
        }

        private static IApplicationContext CreateApplicationContext(IConfiguration configuration)
        {
            var isService = configuration.GetValue("service", false);
            
            var workingDir = AppContext.BaseDirectory;
            var gamePath = configuration.GetValue("gamePath", workingDir);
            var binDir = Path.Combine(gamePath, "DedicatedServer64");
            
            Directory.SetCurrentDirectory(gamePath);

            var instanceName = configuration.GetValue("instanceName", "Instance");
            string instancePath;
            
            if (Path.IsPathRooted(instanceName))
            {
                instancePath = instanceName;
                instanceName = Path.GetDirectoryName(instanceName);
            }
            else
            {
                instancePath = Directory.CreateDirectory(instanceName!).FullName;
            }
            
            return new ApplicationContext(new(workingDir), new(gamePath), new(binDir), 
                new(instancePath), instanceName, isService);
        }
    }
}
