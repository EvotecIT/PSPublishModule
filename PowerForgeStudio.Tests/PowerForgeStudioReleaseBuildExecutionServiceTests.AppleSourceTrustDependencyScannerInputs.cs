using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Theory]
    [InlineData("-fdepscan-daemon Rules")]
    [InlineData("-fdepscan-daemon=Rules")]
    [InlineData("-fdepscan -fdepscan-daemon=Rules")]
    [InlineData("-Xclang -fdepscan-daemon=Rules")]
    public void ResolveExactAppleSourceCommit_classifies_dependency_scanner_daemon_paths(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "DependencyScannerDaemonRepo" + option.Length,
            $"OTHER_CFLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Rules", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_classifies_dependency_scanner_daemon_from_response_file()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "DependencyScannerDaemonResponseRepo",
            "OTHER_CFLAGS = @Compiler.rsp\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Compiler.rsp"), "-fdepscan-daemon=Rules\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Rules", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
