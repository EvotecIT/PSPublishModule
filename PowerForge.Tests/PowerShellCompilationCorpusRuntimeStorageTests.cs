using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Theory]
    [InlineData("9p", false)]
    [InlineData("drvfs", false)]
    [InlineData("", false)]
    [InlineData("ext4", true)]
    [InlineData("xfs", true)]
    [Trait("Category", "PowerShellCompilerGate")]
    public void CorpusTimingRejectsWindowsMountedOrUnknownLinuxRuntime(string fileSystem, bool accepted)
    {
        var script = Path.Combine(FindCorpusRepositoryRoot(), "Benchmarks", "PowerShellCompilation", "Corpus", "Corpus.RuntimeStorage.ps1");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script.Replace("'", "''", StringComparison.Ordinal)}}'
            try {
                Assert-CorpusRuntimeMount -Path '/runtime/shared/Microsoft.NETCore.App/10.0/libcoreclr.so' -FileSystem '{{fileSystem}}'
                [Console]::Out.Write('accepted')
            } catch {
                if ($_.Exception.Message -notmatch 'timing budgets') { throw }
                [Console]::Out.Write('rejected')
            }
            """;
        var result = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(accepted ? "accepted" : "rejected", result.StandardOutput);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
    }
}
