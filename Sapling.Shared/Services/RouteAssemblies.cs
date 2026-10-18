using System.Reflection;

namespace Sapling.Shared.Services;

/// <summary>
/// Assemblies the router should scan on top of Sapling.Shared. Each head has pages of its own, and this cannot be a
/// component parameter because the web head renders the router interactively, where parameters must be serializable.
/// </summary>
public sealed class RouteAssemblies(params Assembly[] assemblies)
{
    public IReadOnlyList<Assembly> All { get; } = assemblies;
}
