using System.Text.Json.Serialization;

namespace Torch.API.WebAPI.Plugin;

public record VersionItem(string Version, string Note, [property: JsonPropertyName("is_beta")] bool IsBeta,
                          string Url);