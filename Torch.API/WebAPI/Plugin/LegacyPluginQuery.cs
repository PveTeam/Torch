using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Torch.API.WebAPI.Plugin;

public class LegacyPluginQuery : IPluginQuery
{
    private const string BASE_URL = "https://torchapi.com/api/plugins/";
    private readonly HttpClient _client;
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private LegacyPluginQuery()
    {
        _client = new()
        {
            BaseAddress = new(BASE_URL)
        };
    }
    
    public static LegacyPluginQuery Instance { get; } = new();

    public async Task<PluginsResponse> QueryAll()
    {
        return await _client.GetFromJsonAsync<PluginsResponse>("/", CancellationToken.None);
    }

    public async Task<PluginItem> QueryOne(Guid guid)
    {
        using var res = await _client.GetAsync($"/search/{guid}");
        if (!res.IsSuccessStatusCode)
            return null;
        return await res.Content.ReadFromJsonAsync<PluginItem>();
    }

    public async Task<bool> DownloadPlugin(Guid guid, string path = null)
    {
        var item = await QueryOne(guid);
        if (item is null) return false;
        return await DownloadPlugin(item, path);
    }

    public async Task<bool> DownloadPlugin(PluginItem item, string path = null)
    {
        try
        {
            path ??= Path.Combine(AppContext.BaseDirectory, "Plugins", $"{item.Name}.zip");
                
            if (item.Versions.Length == 0)
            {
                Log.Error($"Selected plugin {item.Name} does not have any versions to download!");
                return false;
            }
            var version = item.Versions.FirstOrDefault(v => v.Version == item.LatestVersion);
            if (version is null)
            {
                Log.Error($"Could not find latest version for selected plugin {item.Name}");
                return false;
            }
            var s = await _client.GetStreamAsync(version.Url);

            if(File.Exists(path))
                File.Delete(path);

            await using var f = File.Create(path);
            await s.CopyToAsync(f);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download plugin!");
            return false;
        }

        return true;
    }
}