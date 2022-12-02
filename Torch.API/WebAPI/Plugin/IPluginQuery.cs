using System;
using System.Threading.Tasks;

namespace Torch.API.WebAPI.Plugin;

public interface IPluginQuery
{
    Task<PluginsResponse> QueryAll();
    Task<PluginItem> QueryOne(Guid guid);
    Task<bool> DownloadPlugin(Guid guid, string path = null);
    Task<bool> DownloadPlugin(PluginItem item, string path = null);
}