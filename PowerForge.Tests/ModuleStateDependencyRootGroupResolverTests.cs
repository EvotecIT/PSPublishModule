using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ModuleStateDependencyRootGroupResolverTests
{
    [Fact]
    public void Resolve_GroupsAnonymousRootsFromTheSameVisibilityContext()
    {
        var targetRoot = Path.Combine(Path.GetTempPath(), "target-modules");
        var dependentRoot = Path.Combine(Path.GetTempPath(), "dependent-modules");
        var alternativeRoot = Path.Combine(Path.GetTempPath(), "alternative-modules");
        var paths = new[]
        {
            CreatePath(targetRoot, "CurrentProcessPSModulePath"),
            CreatePath(dependentRoot, "CurrentProcessPSModulePath"),
            CreatePath(alternativeRoot, "CurrentProcessPSModulePath")
        };

        var groups = ModuleStateDependencyRootGroupResolver.Resolve(
            paths,
            targetPowerShellEdition: null,
            targetScope: "Custom",
            targetProfileName: null,
            targetRoot);

        var group = Assert.Single(groups);
        Assert.Contains(targetRoot, group, ModuleStatePathIdentity.Comparer);
        Assert.Contains(dependentRoot, group, ModuleStatePathIdentity.Comparer);
        Assert.Contains(alternativeRoot, group, ModuleStatePathIdentity.Comparer);
    }

    [Fact]
    public void Resolve_KeepsUnattributedAnonymousRootsInSeparateVisibilityContexts()
    {
        var targetRoot = Path.Combine(Path.GetTempPath(), "target-modules");
        var dependentRoot = Path.Combine(Path.GetTempPath(), "dependent-modules");
        var unrelatedRoot = Path.Combine(Path.GetTempPath(), "unrelated-modules");
        var paths = new[]
        {
            CreatePath(targetRoot),
            CreatePath(dependentRoot),
            CreatePath(unrelatedRoot)
        };

        var groups = ModuleStateDependencyRootGroupResolver.Resolve(
            paths,
            targetPowerShellEdition: null,
            targetScope: "Custom",
            targetProfileName: null,
            targetRoot);

        Assert.Equal(3, groups.Count);
        Assert.DoesNotContain(groups, group =>
            group.Contains(dependentRoot, ModuleStatePathIdentity.Comparer) &&
            group.Contains(unrelatedRoot, ModuleStatePathIdentity.Comparer));
    }

    private static ModuleStateInventoryPathResult CreatePath(string path, string? dependencyVisibilityGroup = null)
        => new()
        {
            Path = path,
            Scope = "Custom",
            WasAvailable = true,
            DependencyVisibilityGroup = dependencyVisibilityGroup
        };
}
