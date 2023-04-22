using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NLog;
using Torch.Event;
using Torch.Managers.PatchManager;

namespace Torch.Utils
{
    public static class TorchLauncher
    {
        private static readonly ILogger Log = LogManager.GetCurrentClassLogger();
        private static readonly HashSet<Assembly> RegisteredAssemblies = new();
        private static readonly Dictionary<string, string> Assemblies = new();

        public static void Launch(params string[] binaryPaths)
        {
            CopyNative();
            
            foreach (var file in binaryPaths.SelectMany(path => Directory.EnumerateFiles(path, "*.dll")))
            {
                try
                {
                    var name = AssemblyName.GetAssemblyName(file);
                    var key = name.Name ?? name.FullName[..','];
                    Assemblies.TryAdd(key, file);
                }
                catch (BadImageFormatException)
                {
                    // if we are trying to load native image
                }
            }

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainOnAssemblyResolve;
        }

        public static void RegisterAssembly(Assembly assembly)
        {
            if (!RegisteredAssemblies.Add(assembly))
                return;
            ReflectedManager.Process(assembly);
            EventManager.AddDispatchShims(assembly);
            PatchManager.AddPatchShims(assembly);
        }
        
        private static void CopyNative()
        {
            if (ApplicationContext.Current.IsService || ApplicationContext.Current.GameFilesDirectory.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                Log.Warn("Torch directory is readonly. You should copy steam_api64.dll, Havok.dll from bin manually");
                return;
            }

            try
            {
                var apiSource = Path.Combine(ApplicationContext.Current.GameBinariesDirectory.FullName, "steam_api64.dll");
                var apiTarget = Path.Combine(ApplicationContext.Current.GameFilesDirectory.FullName, "steam_api64.dll");
                if (!File.Exists(apiTarget))
                {
                    File.Copy(apiSource, apiTarget);
                }
                else if (File.GetLastWriteTime(apiTarget) < ApplicationContext.Current.GameBinariesDirectory.LastWriteTime)
                {
                    File.Delete(apiTarget);
                    File.Copy(apiSource, apiTarget);
                }

                var havokSource = Path.Combine(ApplicationContext.Current.GameBinariesDirectory.FullName, "Havok.dll");
                var havokTarget = Path.Combine(ApplicationContext.Current.GameFilesDirectory.FullName, "Havok.dll");

                if (!File.Exists(havokTarget))
                {
                    File.Copy(havokSource, havokTarget);
                }
                else if (File.GetLastWriteTime(havokTarget) < File.GetLastWriteTime(havokSource))
                {
                    File.Delete(havokTarget);
                    File.Copy(havokSource, havokTarget);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // file is being used by another process, probably previous torch has not been closed yet
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        private static Assembly CurrentDomainOnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = args.Name;
            return Assemblies.TryGetValue(name.IndexOf(',') > 0 ? name[..name.IndexOf(',')] : name, out var path) ? Assembly.LoadFrom(path) : null;
        }
    }
}
