#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Torch.API.WebAPI.Plugins;

public class PackageReader : IPackageReader
{
    private readonly Package _package;
    private readonly SourceCacheContext _cacheContext;
    private readonly ILogger _logger;
    private readonly NuGetFramework _framework;
    private readonly IFrameworkCompatibilityProvider _compatibilityProvider;
    private readonly DirectoryInfo _packagesDirectory;

    public PackageReader(Package package, SourceCacheContext cacheContext, ILogger logger, NuGetFramework framework,
                         IFrameworkCompatibilityProvider compatibilityProvider, DirectoryInfo packagesDirectory)
    {
        _package = package;
        _cacheContext = cacheContext;
        _logger = logger;
        _framework = framework;
        _compatibilityProvider = compatibilityProvider;
        _packagesDirectory = packagesDirectory;
    }

    public async Task<(IEnumerable<IPackageItem> Root, IReadOnlyDictionary<PackageDependency, IEnumerable<IPackageItem>>
            Dependencies)>
        GetItemsAsync()
    {
        async Task<IEnumerable<IPackageItem>> GetPackageItemsAsync(string id, NuGetVersion version,
                                                                   IRemoteDependencyProvider provider)
        {
            var downloader =
                await provider.GetPackageDownloaderAsync(new(id, version), _cacheContext, _logger,
                                                         CancellationToken.None);

            await downloader.CopyNupkgFileToAsync(Path.Combine(_packagesDirectory.FullName, $"{id}.{version}.nupkg"),
                                                  CancellationToken.None);

            var frameworks = await downloader.ContentReader.GetReferenceItemsAsync(CancellationToken.None);
            var items = frameworks.Where(b => _compatibilityProvider.IsCompatible(_framework, b.TargetFramework))
                                  .MaxBy(b => b.TargetFramework.Version)?.Items;

            return items?.Select(b => new PackageItem(b, downloader)) ?? ImmutableArray<PackageItem>.Empty;
        }

        var rootIdentity = _package.Graph.Key;
        return (await GetPackageItemsAsync(rootIdentity.Name, rootIdentity.Version, _package.Graph.Data.Match.Provider),
            await _package.Dependencies.ToAsyncEnumerable().SelectManyAwait(async b =>
                                                                                (await GetPackageItemsAsync(
                                                                                    b.Match.Library.Name,
                                                                                    b.Match.Library.Version,
                                                                                    b.Match.Provider))
                                                                                .ToAsyncEnumerable()
                                                                                .Select(c => (b, c)))
                          .GroupBy(b => b.b, b => b.c)
                          .ToDictionaryAwaitAsync<IAsyncGrouping<PackageDependency, IPackageItem>, PackageDependency,
                              IEnumerable<IPackageItem>>(b => ValueTask.FromResult(b.Key),
                                                         async b => await b.ToArrayAsync()));
    }
}

file class PackageItem : IPackageItem
{
    private readonly string _path;
    private readonly IPackageDownloader _downloader;

    public string FileName => Path.GetFileName(_path);

    public PackageItem(string path, IPackageDownloader downloader)
    {
        _path = path;
        _downloader = downloader;
    }

    public Task<Stream> OpenFileAsync()
    {
        return _downloader.CoreReader.GetStreamAsync(_path, CancellationToken.None);
    }
}