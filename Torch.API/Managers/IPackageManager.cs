using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Torch.API.WebAPI.Plugins;

namespace Torch.API.Managers;

public interface IPackageManager : IManager
{
    IReadOnlySet<Package> Packages { get; }

    bool TryGetPackageAssemblies(Package package, [MaybeNullWhen(false)] out Assembly[] assemblies);
}