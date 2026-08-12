using PowerForge;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Theory]
    [InlineData("SYSTEM_FRAMEWORK_SEARCH_PATHS")]
    [InlineData("SWIFT_SYSTEM_INCLUDE_PATHS")]
    public void ResolveExactAppleSourceCommit_rejects_host_system_search_paths(string setting)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "SystemSearchPathRepo" + setting.Length,
            $"{setting} = /tmp/InjectedRules\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(setting, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
