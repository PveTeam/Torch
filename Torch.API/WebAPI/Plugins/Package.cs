using System.Collections.Generic;
using NuGet.DependencyResolver;
using SemanticVersioning;

namespace Torch.API.WebAPI.Plugins;

public record Package(string Name, Version Version, IReadOnlySet<PackageDependency> Dependencies)
{
    internal GraphItem<RemoteResolveResult> Graph { get; init; }
}