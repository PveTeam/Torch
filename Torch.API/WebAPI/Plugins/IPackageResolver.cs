#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Torch.API.WebAPI.Plugins;

public interface IPackageResolver
{
    Task<IEnumerable<Package>> ResolvePackagesAsync(IReadOnlyDictionary<string, string> packages);
    Task<IPackageReader> GetPackageAsync(Package package);
}