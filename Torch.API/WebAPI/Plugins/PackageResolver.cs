#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NuGet.Commands;
using NuGet.Configuration;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Version = SemanticVersioning.Version;

namespace Torch.API.WebAPI.Plugins;

public class PackageResolver : IPackageResolver
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

    private readonly NuGetFramework _framework = NuGetFramework.Parse("net7.0-windows7.0");
    private readonly NLogLogger _logger = new(Log);
    private readonly SourceCacheContext _sourceCacheContext = new();
    private readonly RemoteWalkContext _remoteWalkContext;
    private readonly DirectoryInfo _packagesDirectory;
    private readonly IFrameworkCompatibilityProvider _compatibilityProvider = DefaultCompatibilityProvider.Instance;

    public PackageResolver(IEnumerable<PackageSource> sources, DirectoryInfo packagesDirectory)
    {
        _packagesDirectory = packagesDirectory;
        IReadOnlySet<PackageSource> packageSources = sources.Where(b => b.Type is PackageSourceType.NuGet).ToImmutableHashSet();

        var mapping = new PackageSourceMapping(packageSources.ToDictionary(b => b.Name, b => b.Patterns));
        _remoteWalkContext = new RemoteWalkContext(_sourceCacheContext, mapping, _logger);

        foreach (var (name, url, _, _) in packageSources)
        {
            var packageSource = new NuGet.Configuration.PackageSource(url, name);
            var sourceRepository = new SourceRepository(packageSource, new INuGetResourceProvider[]
            {
                new DownloadResourceV3Provider(),
                new DependencyInfoResourceV3Provider(),
                new ServiceIndexResourceV3Provider(),
                new RemoteV3FindPackageByIdResourceProvider(),
                new V3FeedListResourceProvider(),
                new HttpSourceResourceProvider(),
                new RegistrationResourceV3Provider(),
                new HttpHandlerResourceV3Provider()
            }.Select(b => new Lazy<INuGetResourceProvider>(b)), FeedType.HttpV3);

            _remoteWalkContext.RemoteLibraryProviders.Add(
                new SourceRepositoryDependencyProvider(sourceRepository, _logger, _sourceCacheContext, true, false));
        }
    }

    public async Task<IEnumerable<Package>> ResolvePackagesAsync(
        IReadOnlyDictionary<string, string> packages)
    {
        Log.Info("Restoring {0} packages", packages.Count);

        var graphs = await Task.WhenAll(packages.Select(b =>
        {
            var (key, versionRange) = b;
            var libraryRange = new LibraryRange(key, VersionRange.Parse(versionRange), LibraryDependencyTarget.All);
            return ResolverUtility.FindLibraryEntryAsync(libraryRange, _framework, "win-x64",
                                                         _remoteWalkContext, CancellationToken.None);
        }));

        return await graphs.ToAsyncEnumerable().SelectAwait(async graph =>
        {
            return new Package(graph.Key.Name, Version.Parse(graph.Key.Version.ToFullString()),
                               await graph.Data.Dependencies
                                          .ToAsyncEnumerable()
                                          .SelectAwait(async b =>
                                          {
                                              var match = await ResolverUtility.FindLibraryByVersionAsync(
                                                  b.LibraryRange, _framework, _remoteWalkContext.RemoteLibraryProviders,
                                                  _sourceCacheContext, _logger, CancellationToken.None);

                                              return new PackageDependency(
                                                  b.Name, Version.Parse(match.Library.Version.ToFullString()),
                                                  (PackageDependencyKind)b.ReferenceType)
                                              {
                                                  Match = match
                                              };
                                          })
                                          .ToHashSetAsync())
            {
                Graph = graph
            };
        }).ToArrayAsync();
    }

    public Task<IPackageReader> GetPackageAsync(Package package)
    {
        var reader = new PackageReader(package, _sourceCacheContext, _logger, _framework, _compatibilityProvider, _packagesDirectory);
        return Task.FromResult<IPackageReader>(reader);
    }
}