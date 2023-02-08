using System.Collections.Generic;

namespace Torch.API.WebAPI.Plugins;

#nullable enable
public record PackageSource
#nullable restore
    (string Name, string Url, IReadOnlyList<string> Patterns, PackageSourceType Type);