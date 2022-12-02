using System;
using System.Text.Json.Serialization;

namespace Torch.API.WebAPI.Plugin;

public record PluginItem(Guid Id, string Name, string Author, string Description, string LatestVersion,
                         VersionItem[] Versions)
{
    [JsonIgnore]
    public bool Installed { get; set; }
}