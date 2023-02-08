using NuGet.DependencyResolver;
using SemanticVersioning;

namespace Torch.API.WebAPI.Plugins;

public record PackageDependency(string Name, Version Version, PackageDependencyKind Kind)
{
    internal RemoteMatch Match { get; init; }
}