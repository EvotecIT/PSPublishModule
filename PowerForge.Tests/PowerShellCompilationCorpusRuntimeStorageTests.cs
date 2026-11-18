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

    public static IEnumerable<object[]> RuntimeTraceStorageCases()
    {
        yield return new object[] { "ext4", true, true };
        if (OperatingSystem.IsLinux()) yield return new object[] { "9p", true, false };
        yield return new object[] { "ext4", false, false };
    }

    [Theory]
    [MemberData(nameof(RuntimeTraceStorageCases))]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeTraceResolutionChecksTheResolvedLibrariesBeforeAcceptingSamples(string fileSystem, bool coreClrPresent, bool accepted)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge runtime trace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var coreClr = Path.Combine(root, OperatingSystem.IsWindows() ? "coreclr.dll" : "libcoreclr.so");
            var policy = Path.Combine(root, OperatingSystem.IsWindows() ? "hostpolicy.dll" : "libhostpolicy.so");
            File.WriteAllText(coreClr, "CoreCLR fixture");
            File.WriteAllText(policy, "host policy fixture");
            var trace = "Resolved host policy path = '" + policy + "'" + Environment.NewLine +
                (coreClrPresent ? "CoreCLR path = '" + coreClr + "'" : "No resolved runtime");
            var script = Path.Combine(FindCorpusRepositoryRoot(), "Benchmarks", "PowerShellCompilation", "Corpus", "Corpus.RuntimeStorage.ps1");
            var expectedError = coreClrPresent ? "is on 9p" : "Host trace did not resolve CoreCLR";
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{script.Replace("'", "''", StringComparison.Ordinal)}}'
                $script:trace = @'
                {{trace}}
                '@
                $script:policy = '{{policy.Replace("'", "''", StringComparison.Ordinal)}}'
                $env:DOTNET_HOST_TRACE_VERBOSITY = '1'
                $env:COREHOST_TRACE_VERBOSITY = '1'
                function Invoke-OwnedProcess {
                    param($FileName, $Arguments, $WorkingDirectory, $Environment, $TimeoutSeconds)
                    if ($FileName -eq 'artifact-probe') {
                        if ($Environment.DOTNET_HOST_TRACE_VERBOSITY -cne '4' -or $Environment.COREHOST_TRACE_VERBOSITY -cne '4') {
                            throw 'Inherited trace verbosity was not overridden'
                        }
                        [IO.File]::WriteAllText($Environment.DOTNET_HOST_TRACEFILE, $script:trace)
                        return [pscustomobject]@{ExitCode=0;StandardOutput='';StandardError=''}
                    }
                    if ($FileName -eq 'findmnt') {
                        $mount = if ($Arguments[-1] -ceq $script:policy) { '{{fileSystem}}' } else { 'ext4' }
                        return [pscustomobject]@{ExitCode=0;StandardOutput=$mount;StandardError=''}
                    }
                    throw 'Unexpected process'
                }
                try {
                    $storage = Get-CorpusRuntimeStorage -ArtifactPath 'artifact-probe' -WorkingDirectory '{{root.Replace("'", "''", StringComparison.Ordinal)}}'
                    if ($storage.libraries.Count -ne 2) { throw 'A resolved library was omitted' }
                    [Console]::Out.Write('accepted traced runtime')
                } catch {
                    if (-not $_.Exception.Message.Contains('{{expectedError}}')) { throw }
                    [Console]::Out.Write('rejected traced runtime')
                }
                """;
            var result = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", command);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(accepted ? "accepted traced runtime" : "rejected traced runtime", result.StandardOutput);
            Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
