using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void SourceTimingRetainsLauncherAndChecksUnderlyingHostFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge source host", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var corpus = Path.Combine(FindCorpusRepositoryRoot(), "Benchmarks", "PowerShellCompilation", "Corpus");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{Path.Combine(corpus, "Corpus.RuntimeStorage.ps1").Replace("'", "''", StringComparison.Ordinal)}}'
                . '{{Path.Combine(corpus, "Corpus.SourceHost.ps1").Replace("'", "''", StringComparison.Ordinal)}}'
                $root = '{{root.Replace("'", "''", StringComparison.Ordinal)}}'
                $script:launcher = Join-Path $root 'pwsh-launcher'
                $executable = Join-Path $root 'dotnet-host'
                $sma = Join-Path $root 'System.Management.Automation.dll'
                $core = Join-Path $root $(if ($IsWindows) { 'coreclr.dll' } else { 'libcoreclr.so' })
                foreach ($path in @($script:launcher, $executable, $sma, $core)) { [IO.File]::WriteAllText($path, 'original') }
                $script:identity = @{executable=$executable;runtimeRoot=$root;sma=$sma;powerShell='7.6';runtime='.NET 10';architecture='X64';operatingSystem='fixture'} | ConvertTo-Json
                function Get-Command { param($Name, $CommandType, $ErrorAction) return [pscustomobject]@{Source=$script:launcher} }
                function Invoke-OwnedProcess {
                    param($FileName, $Arguments, $WorkingDirectory, $TimeoutSeconds)
                    if ($FileName -cne $script:launcher) { throw 'Invoked the underlying process instead of the PowerShell launcher' }
                    if ($FileName -eq 'findmnt') { throw 'Unexpected mount query' }
                    return [pscustomobject]@{ExitCode=0;StandardOutput=$script:identity;StandardError=''}
                }
                function Get-CorpusRuntimeFileSystem { param($Path, $WorkingDirectory) return 'fixture filesystem' }
                $hostIdentity = Get-CorpusSourceHost -WorkingDirectory $root
                $null = Invoke-OwnedProcess -FileName $hostIdentity.executablePath -Arguments @('-NoProfile','-File','sample.ps1') -WorkingDirectory $root
                Assert-CorpusSourceHostUnchanged -SourceHost $hostIdentity
                if ($hostIdentity.files.Count -ne 4) { throw 'Missing launcher or underlying host identity' }
                foreach ($file in $hostIdentity.files) {
                    [IO.File]::WriteAllText($file.path, 'changed')
                    try {
                        Assert-CorpusSourceHostUnchanged -SourceHost $hostIdentity
                        throw 'Accepted changed source host file'
                    } catch {
                        if ($_.Exception.Message -notmatch 'source PowerShell host changed during timing') { throw }
                    } finally { [IO.File]::WriteAllText($file.path, 'original') }
                }
                [Console]::Out.Write('launcher retained and host changes rejected')
                """;
            var result = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", command);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("launcher retained and host changes rejected", result.StandardOutput);
            Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ModuleProfileRejectsAManifestForAnotherPacketModuleBeforeInvocation()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge profile identity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var manifest = Path.Combine(root, "Demo.psd1");
            File.WriteAllText(manifest, "@{RootModule='Demo.psm1';ModuleVersion='1.0';FunctionsToExport=@('Get-Demo')}");
            File.WriteAllText(Path.Combine(root, "Demo.psm1"), "function Get-Demo { 42 }");
            var script = Path.Combine(FindCorpusRepositoryRoot(), "Benchmarks", "PowerShellCompilation", "Corpus", "Invoke-ModuleProfileProbe.ps1");
            var result = Run("pwsh", "-NoProfile", "-NonInteractive", "-File", script, "-ManifestPath", manifest,
                "-ModuleId", "Microsoft.PowerShell.Archive", "-Calls", "1");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Profile manifest does not match the selected packet module", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("Profile probe completed successfully", result.StandardOutput, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
