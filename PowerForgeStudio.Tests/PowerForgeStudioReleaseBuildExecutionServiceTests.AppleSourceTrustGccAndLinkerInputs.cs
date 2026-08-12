using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Theory]
    [InlineData("--gcc-toolchain Rules", "Rules")]
    [InlineData("--gcc-toolchain=Rules", "Rules")]
    [InlineData("--gcc-install-dir Rules", "Rules")]
    [InlineData("--gcc-install-dir=Rules", "Rules")]
    public void ResolveExactAppleSourceCommit_attests_double_hyphen_gcc_search_roots(
        string option,
        string expectedPath)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "GccSearchRootRepo" + option.Length,
            $"OTHER_CPLUSPLUSFLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Equal(Path.Combine(repositoryRoot, expectedPath), exception.FileName);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_tracked_segcreate_file_input()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "SegcreateInputRepo",
            "OTHER_LDFLAGS = -segcreate __DATA __rules Rules\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Rules"), "approved section bytes");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }
}
