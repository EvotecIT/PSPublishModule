using PowerForge;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Theory]
    [InlineData("-fembed-offload-object=Rules")]
    [InlineData("-fcuda-include-gpubinary Rules")]
    [InlineData("-fopenmp-host-ir-file-path Rules")]
    [InlineData("--gpu-instrument-lib=Rules")]
    [InlineData("--hip-device-lib=Rules")]
    [InlineData("--hip-device-lib-path=Rules")]
    [InlineData("--offload-arch-tool=Rules")]
    [InlineData("--amdgpu-arch-tool=Rules")]
    [InlineData("--nvptx-arch-tool=Rules")]
    public void ResolveExactAppleSourceCommit_classifies_offload_file_and_tool_inputs(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "OffloadInputRepo" + option.Length,
            $"OTHER_CFLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Rules", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-Xclang -fembed-offload-object=Rules")]
    [InlineData("-Xclang -fcuda-include-gpubinary -Xclang Rules")]
    public void ResolveExactAppleSourceCommit_classifies_forwarded_offload_inputs(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "ForwardedOffloadInputRepo" + option.Length,
            $"OTHER_CFLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Rules", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_classifies_offload_input_from_response_file()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "OffloadResponseInputRepo",
            "OTHER_CFLAGS = @Compiler.rsp\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Compiler.rsp"), "-fembed-offload-object=Rules\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Rules", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_tracked_offload_object_symlink()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "OffloadSymlinkRepo",
            "OTHER_CFLAGS = -fembed-offload-object=Rules\n");
        var outside = Path.Combine(scope.CreateDirectory("OffloadSymlinkExternal"), "payload.o");
        File.WriteAllText(outside, "mutable");
        try
        {
            File.CreateSymbolicLink(Path.Combine(repositoryRoot, "Rules"), outside);
        }
        catch (Exception linkError) when (linkError is PlatformNotSupportedException or UnauthorizedAccessException)
        {
            return;
        }
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("OTHER_CFLAGS", exception.Message, StringComparison.Ordinal);
        Assert.Contains("symlink", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-Xcuda-fatbinary --image=Rules")]
    [InlineData("-Xcuda-ptxas --options-file=Rules")]
    [InlineData("-Xoffload-linker --override-image=openmp=Rules")]
    [InlineData("-Xopenmp-target --image=Rules")]
    [InlineData("-Xopenmp-target=amdgcn --image=Rules")]
    [InlineData("-Xsycl-target-linker --image=Rules")]
    public void ResolveExactAppleSourceCommit_rejects_unclassified_offload_tool_forwarding(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "OffloadForwardingRepo" + option.Length,
            $"OTHER_CFLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("offload tool forwarding", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be classified safely", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
