using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Theory]
    [InlineData("-include Rules")]
    [InlineData("-include=Rules")]
    [InlineData("@Metal.rsp")]
    public void ResolveExactAppleSourceCommit_classifies_metal_compiler_file_inputs(string flags)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "MetalCompilerInputRepo" + flags.Length,
            $"MTL_COMPILER_FLAGS = {flags}\n");
        if (flags == "@Metal.rsp")
            File.WriteAllText(Path.Combine(repositoryRoot, "Metal.rsp"), "-include Rules\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Rules", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
