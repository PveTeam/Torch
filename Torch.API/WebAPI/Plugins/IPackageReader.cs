#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Torch.API.WebAPI.Plugins;

public interface IPackageReader
{
    Task<(IEnumerable<IPackageItem> Root, IReadOnlyDictionary<PackageDependency, IEnumerable<IPackageItem>> Dependencies)> GetItemsAsync();
}