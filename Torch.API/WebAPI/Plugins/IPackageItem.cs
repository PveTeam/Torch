#nullable enable
using System.IO;
using System.Threading.Tasks;

namespace Torch.API.WebAPI.Plugins;

public interface IPackageItem
{
    Task<Stream> OpenFileAsync();
    public string FileName { get; }
}