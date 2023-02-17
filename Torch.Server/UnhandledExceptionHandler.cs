using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NLog;
using VRage;

namespace Torch.Server;

internal class UnhandledExceptionHandler
{
    private readonly TorchConfig _config;
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

    public UnhandledExceptionHandler(TorchConfig config)
    {
        _config = config;
    }
    
    internal void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (Debugger.IsAttached)
            return;
        var ex = (Exception)e.ExceptionObject;
        Log.Fatal(ex.ToStringDemystified());
        LogManager.Flush();
        
        if (ApplicationContext.Current.IsService)
            Environment.Exit(1);
        
        if (_config.RestartOnCrash)
        {
            Console.WriteLine("Restarting in 5 seconds.");
            Thread.Sleep(5000);
            
            var exe = Path.Combine(AppContext.BaseDirectory, "Torch.Server.exe");

            var args = Environment.GetCommandLineArgs();

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].Contains(' '))
                    args[i] = $"\"{args[i]}\"";
                    
                if (!args[i].Contains("--tempAutostart", StringComparison.InvariantCultureIgnoreCase) &&
                    !args[i].Contains("--waitForPid", StringComparison.InvariantCultureIgnoreCase)) 
                    continue;
                    
                args[i] = string.Empty;
                args[++i] = string.Empty;
            }

            Process.Start(exe, $"--waitForPid {Environment.ProcessId} --tempAutostart true {string.Join(" ", args)}");
        }
        else
        {
            MyVRage.Platform.Windows.MessageBox(
                "Torch encountered a fatal error and needs to close. Please check the logs for details.",
                "Fatal exception", MessageBoxOptions.OkOnly);
        }

        Environment.Exit(1);
    } 
}