#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Torch.API;
using Torch.API.Managers;
using Torch.API.WebAPI.Plugins;
using Torch.Managers;
using Torch.Utils;

namespace Torch.Packages;

public class PackageManager : Manager, IPackageManager
{
    private readonly PluginManager _pluginManager;
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

    private IPackageResolver? _packageResolver;

    public PackageManager(ITorchBase torchInstance, PluginManager pluginManager) : base(torchInstance)
    {
        _pluginManager = pluginManager;
    }

    private AssemblyLoadContext? _loadContext;

    private ImmutableHashSet<Package> _packages = ImmutableHashSet<Package>.Empty;
    private ImmutableDictionary<Package,Assembly[]> _packageAssemblies = ImmutableDictionary<Package, Assembly[]>.Empty;
    private DirectoryInfo? _packagesDirectory;
    public IReadOnlySet<Package> Packages => _packages;
    
    public bool TryGetPackageAssemblies(Package package, out Assembly[]? assemblies)
    {
        if (!Packages.Contains(package))
            throw new InvalidOperationException($"Package {package.Name} does not exist");
        
        return _packageAssemblies.TryGetValue(package, out assemblies);
    }

    internal async void LoadAsync(SemaphoreSlim semaphore)
    {
        _packagesDirectory = Directory.CreateDirectory(Path.Combine(Torch.InstancePath, "Packages"));
        _packageResolver = new PackageResolver(new[]
        { // TODO make this configurable
            new PackageSource("nuget", "https://api.nuget.org/v3/index.json", new[] { "*" }, PackageSourceType.NuGet)
        }, _packagesDirectory);
        
        try
        {
            var packages =
                Torch.Config.Packages.ToImmutableDictionary(b => b[..b.IndexOf(':')], b => b[(b.IndexOf(':') + 1)..]);
            await ResolvePackagesAsync(packages);

            var resolvedPackages = await _packages.ToAsyncEnumerable()
                                                  .SelectAwait(async b =>
                                                  {
                                                      var reader = await _packageResolver!.GetPackageAsync(b);
                                                      var items = await reader.GetItemsAsync();

                                                      return (b, items);
                                                  }).ToDictionaryAsync(b => b.b.Name, b => b,
                                                                       StringComparer.OrdinalIgnoreCase);

            var dependencies = new Dictionary<PackageDependency, IEnumerable<IPackageItem>>(
                resolvedPackages.Values.SelectMany(b => b.items.Dependencies)
                                .Where(b => !resolvedPackages.ContainsKey(b.Key.Name))
                                .GroupBy(b => b.Key.Name)
                                .Select(b => b.MaxBy(c => c.Key.Version)));

            _loadContext = new("PackageManager Context");

            var dependencyAssemblies = await dependencies.ToAsyncEnumerable().SelectAwait(async pair =>
            {
                var (dep, items) = pair;
                var assemblies = await LoadAssembliesFromItems(items);

                return (dep, assemblies);
            }).ToDictionaryAsync(b => b.dep, b => b.assemblies);

            var packageAssemblies = await resolvedPackages.Values.ToAsyncEnumerable().SelectAwait(async b =>
            {
                var assemblies = await LoadAssembliesFromItems(b.items.Root);

                return (b.b, assemblies);
            }).ToDictionaryAsync(b => b.b, b => b.assemblies);

            foreach (var assembly in dependencyAssemblies.Values.Concat(packageAssemblies.Values).SelectMany(b => b))
            {
                TorchLauncher.RegisterAssembly(assembly);
            }

            foreach (var (package, assemblies) in packageAssemblies)
            {
                using var hash = MD5.Create();
                hash.Initialize();
                var guid = new Guid(hash.ComputeHash(Encoding.UTF8.GetBytes(package.Name)));
                
                _pluginManager.InstantiatePlugin(new()
                {
                    Name = package.Name,
                    Version = package.Version.ToString(),
                    Guid = guid
                }, assemblies);
            }

            _packageAssemblies = packageAssemblies.ToImmutableDictionary();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<Assembly[]> LoadAssembliesFromItems(IEnumerable<IPackageItem> items)
    {
        var dictionary = items.ToImmutableDictionary(b => b.FileName, StringComparer.OrdinalIgnoreCase);

        var assemblies = await dictionary
                               .Where(b => Path.GetExtension(b.Key)
                                               .Equals("dll", StringComparison.OrdinalIgnoreCase))
                               .ToAsyncEnumerable()
                               .SelectAwait(async b =>
                               {
                                   dictionary.TryGetValue(
                                       Path.ChangeExtension(b.Key, "pdb"), out var pdbItem);
                                   return await LoadFromStream(b.Key, b.Value, pdbItem);
                               }).ToArrayAsync();
        return assemblies;
    }

    private async ValueTask<Assembly> LoadFromStream(string key, IPackageItem dllItem, IPackageItem? pdbItem)
    {
        if (_loadContext is null)
            throw new InvalidOperationException("Load Context should be initialized before calling the method");

        Log.Trace("Loading {0}", key);
        await using var dll = await dllItem.OpenFileAsync();
        if (pdbItem is null)
            return _loadContext.LoadFromStream(dll);

        await using var pdb = await pdbItem.OpenFileAsync();
        return _loadContext.LoadFromStream(dll, pdb);
    }

    private async Task ResolvePackagesAsync(IReadOnlyDictionary<string, string> requestedPackages)
    {
        Log.Info("Resolving packages");
        var packages = await _packageResolver!.ResolvePackagesAsync(requestedPackages);

        _packages = packages.ToImmutableHashSet();
        Log.Info("Got {0} packages", _packages.Count);
    }
}