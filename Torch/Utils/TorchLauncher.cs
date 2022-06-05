using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Torch.Event;
using Torch.Managers.PatchManager;
using VRage.Collections;

namespace Torch.Utils
{
    public static class TorchLauncher
    {
        private static readonly MyConcurrentHashSet<Assembly> RegisteredAssemblies = new();
        private static readonly Dictionary<string, string> Assemblies = new();

        public static void Launch(params string[] binaryPaths)
        {
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

        private static Assembly CurrentDomainOnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = args.Name;
            return Assemblies.TryGetValue(name.IndexOf(',') > 0 ? name[..','] : name, out var path) ? Assembly.LoadFrom(path) : null;
        }
    }
}
