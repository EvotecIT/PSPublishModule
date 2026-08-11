using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class SigningScriptRetryTests
{
    [Fact]
    public void PowerShellCoreStoreProviderFailuresAreBoundedAndEmitEveryRetryPath()
    {
        if (Path.DirectorySeparatorChar != '\\')
            return;

        var evidence = RunSigningProviderFailureHarness(
            includePrecheckFailure: false,
            includeNonCompatibilitySigningFailure: false);

        Assert.Equal(12, evidence.Summary.Attempted);
        Assert.Equal(12, evidence.Summary.Failed);
        Assert.Equal(12, evidence.Summary.SigningException);
        Assert.Equal(evidence.PackageFilePaths.OrderBy(path => path), evidence.Summary.FailedFilePaths.OrderBy(path => path));
        Assert.Equal(3, evidence.SigningCallCount);
        Assert.Contains(evidence.Summary.FailedFiles, failure => failure.Contains(
            "deferred to Windows PowerShell compatibility retry",
            StringComparison.Ordinal));
    }

    [Fact]
    public void PrecheckFailuresPreventDeferringTargets()
    {
        if (Path.DirectorySeparatorChar != '\\')
            return;

        var evidence = RunSigningProviderFailureHarness(
            includePrecheckFailure: true,
            includeNonCompatibilitySigningFailure: false);

        Assert.Equal(11, evidence.Summary.Attempted);
        Assert.Equal(12, evidence.Summary.Failed);
        Assert.Equal(1, evidence.Summary.PrecheckFailure);
        Assert.Equal(11, evidence.Summary.SigningException);
        Assert.Equal(evidence.PackageFilePaths.OrderBy(path => path), evidence.Summary.FailedFilePaths.OrderBy(path => path));
        Assert.Equal(11 * 15, evidence.SigningCallCount);
        Assert.DoesNotContain(evidence.Summary.FailedFiles, failure => failure.Contains(
            "deferred to Windows PowerShell compatibility retry",
            StringComparison.Ordinal));
    }

    [Fact]
    public void NonCompatibilitySigningFailuresPreventDeferringLaterTargets()
    {
        if (Path.DirectorySeparatorChar != '\\')
            return;

        var evidence = RunSigningProviderFailureHarness(
            includePrecheckFailure: false,
            includeNonCompatibilitySigningFailure: true);

        Assert.Equal(12, evidence.Summary.Attempted);
        Assert.Equal(12, evidence.Summary.Failed);
        Assert.Equal(0, evidence.Summary.PrecheckFailure);
        Assert.Equal(11, evidence.Summary.SigningException);
        Assert.Equal(1 + (11 * 15), evidence.SigningCallCount);
        Assert.DoesNotContain(evidence.Summary.FailedFiles, failure => failure.Contains(
            "deferred to Windows PowerShell compatibility retry",
            StringComparison.Ordinal));
    }

    private static (ModuleSigningResult Summary, int SigningCallCount, string[] PackageFilePaths)
        RunSigningProviderFailureHarness(
            bool includePrecheckFailure,
            bool includeNonCompatibilitySigningFailure)
    {
        var rootPath = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N"))).FullName;
        var packageFileListPath = Path.Combine(rootPath, "package-files.txt");
        var callLogPath = Path.Combine(rootPath, "signing-calls.txt");
        try
        {
            var packageFilePaths = Enumerable.Range(1, 12)
                .Select(index => Path.Combine(rootPath, $"File{index:D2}.ps1"))
                .ToArray();
            foreach (var path in packageFilePaths)
                File.WriteAllText(path, "# test");
            File.WriteAllLines(packageFileListPath, packageFilePaths);

            var script = EmbeddedScripts.Load("Scripts/Signing/Sign-Module.ps1");
            var includeB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("*.ps1"));
            var precheckFailurePath = includePrecheckFailure ? packageFilePaths[0] : string.Empty;
            var harness = $$"""
                $scriptText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(script))}}'))
                $rootPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(rootPath))}}'))
                $packageFileListPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(packageFileListPath))}}'))
                $callLogPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(callLogPath))}}'))
                $precheckFailurePath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(precheckFailurePath))}}'))
                $includeNonCompatibilitySigningFailure = [Convert]::ToBoolean('{{includeNonCompatibilitySigningFailure}}')
                $script:firstSigningCall = $true
                function Get-ChildItem {
                  [CmdletBinding()]
                  param([string]$Path, [switch]$CodeSigningCert)
                  [pscustomobject]@{
                    Thumbprint = '0123456789ABCDEF'
                    NotBefore = [DateTime]::Now.AddDays(-1)
                    NotAfter = [DateTime]::Now.AddDays(1)
                  }
                }
                function Get-AuthenticodeSignature {
                  [CmdletBinding()]
                  param([string]$FilePath)
                  $status = if ($FilePath -eq $precheckFailurePath) { 'HashMismatch' } else { 'NotSigned' }
                  [pscustomobject]@{ Status = $status; SignerCertificate = $null }
                }
                function Set-AuthenticodeSignature {
                  [CmdletBinding()]
                  param(
                    [string]$FilePath,
                    [object]$Certificate,
                    [string]$TimestampServer,
                    [object]$IncludeChain,
                    [string]$HashAlgorithm,
                    [switch]$Force
                  )
                  [IO.File]::AppendAllText($callLogPath, 'x')
                  if ($includeNonCompatibilitySigningFailure -and $script:firstSigningCall) {
                    $script:firstSigningCall = $false
                    return [pscustomobject]@{ Status = 'HashMismatch'; StatusMessage = 'file is not retryable' }
                  }
                  throw 'hardware provider unavailable'
                }
                function Start-Sleep { param([int]$Milliseconds) }
                & ([scriptblock]::Create($scriptText)) `
                  -RootPath $rootPath `
                  -PackageFileListPath $packageFileListPath `
                  -IncludeB64 '{{includeB64}}' `
                  -ExcludeB64 '' `
                  -Thumbprint '0123456789ABCDEF' `
                  -PfxPath '' `
                  -PfxBase64 '' `
                  -PfxPassword '' `
                  -OverwriteSigned '0'
                """;

            using var powerShell = System.Management.Automation.PowerShell.Create();
            powerShell.AddScript(harness);
            var output = powerShell.Invoke().Select(item => item.ToString()).ToArray();

            var summaryLine = Assert.Single(output, line => line.StartsWith("PFSIGN::SUMMARY::", StringComparison.Ordinal));
            var summaryJson = Encoding.UTF8.GetString(Convert.FromBase64String(summaryLine["PFSIGN::SUMMARY::".Length..]));
            var summary = JsonSerializer.Deserialize<ModuleSigningResult>(
                summaryJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(summary);
            return (summary!, File.ReadAllText(callLogPath).Length, packageFilePaths);
        }
        finally
        {
            try { Directory.Delete(rootPath, recursive: true); } catch { /* best effort */ }
        }
    }
}
