using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_host_interface_builder_plugin_search_roots()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "InterfaceBuilderHostPluginRepo",
            "IBC_PLUGIN_SEARCH_PATHS = /tmp/InjectedPlugin\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("IBC_PLUGIN_SEARCH_PATHS", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_linked_interface_builder_plugins()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "InterfaceBuilderLinkedPluginRepo",
            "IBC_PLUGIN_SEARCH_PATHS = Plugins\n");
        var plugins = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Plugins"));
        var outside = Path.Combine(scope.CreateDirectory("InterfaceBuilderPluginExternal"), "Injected.ibplugin");
        File.WriteAllText(outside, "mutable plugin");
        try
        {
            File.CreateSymbolicLink(Path.Combine(plugins.FullName, "Injected.ibplugin"), outside);
        }
        catch (Exception linkError) when (linkError is PlatformNotSupportedException or UnauthorizedAccessException)
        {
            return;
        }
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("IBC_PLUGIN_SEARCH_PATHS", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
