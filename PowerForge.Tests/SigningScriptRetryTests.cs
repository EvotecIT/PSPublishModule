using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class SigningScriptRetryTests
{
    [Theory]
    [InlineData(false, 1, 0, 0, 1)]
    [InlineData(true, 0, 1, 1, 0)]
    public void ExistingThirdPartySignature_IsPreservedOrOverwrittenWithConsistentEvidence(
        bool overwrite,
        int expectedAlreadyOther,
        int expectedAttempted,
        int expectedResigned,
        int expectedPreserved)
    {
        if (Path.DirectorySeparatorChar != '\\')
            return;

        var evidence = RunExistingSignatureHarness(overwrite);

        Assert.Equal(expectedAlreadyOther, evidence.AlreadySignedOther);
        Assert.Equal(expectedAttempted, evidence.Attempted);
        Assert.Equal(expectedResigned, evidence.Resigned);
        Assert.Equal(expectedPreserved, evidence.PreservedThirdPartySignatures.Length);
        if (!overwrite)
        {
            ModuleSigningPreservedSignature signature = Assert.Single(evidence.PreservedThirdPartySignatures);
            Assert.Equal("CN=Vendor", signature.Subject);
            Assert.Equal("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", signature.Thumbprint);
        }
    }

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

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void CertificateLookupFallsBackToLocalMachineWhenCurrentUserCopyIsUnsuitable(
        bool currentUserHasPrivateKey,
        bool currentUserHasCodeSigningEku)
    {
        if (Path.DirectorySeparatorChar != '\\')
            return;

        var evidence = RunSigningProviderFailureHarness(
            includePrecheckFailure: false,
            includeNonCompatibilitySigningFailure: false,
            currentUserHasPrivateKey,
            currentUserHasCodeSigningEku,
            currentUserHasEkuRestriction: true);

        Assert.Equal(2, evidence.CertificateLookupPaths.Length);
        Assert.Contains("Cert:\\CurrentUser\\My", evidence.CertificateLookupPaths[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cert:\\LocalMachine\\My", evidence.CertificateLookupPaths[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CertificateLookupAcceptsCurrentUserCopyWithoutEkuRestriction()
    {
        if (Path.DirectorySeparatorChar != '\\')
            return;

        var evidence = RunSigningProviderFailureHarness(
            includePrecheckFailure: false,
            includeNonCompatibilitySigningFailure: false,
            currentUserHasPrivateKey: true,
            currentUserHasCodeSigningEku: false,
            currentUserHasEkuRestriction: false);

        var lookupPath = Assert.Single(evidence.CertificateLookupPaths);
        Assert.Contains("Cert:\\CurrentUser\\My", lookupPath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0x00080000)]
    [InlineData(0x00100000)]
    [InlineData(0x00180000)]
    public void CloudPlacementAttributesAreClearedBeforeSigning(int cloudAttribute)
    {
        if (Path.DirectorySeparatorChar != '\\')
            return;

        FileAttributes stagedAttributes = FileAttributes.Archive | (FileAttributes)cloudAttribute;
        var evidence = RunSigningProviderFailureHarness(
            includePrecheckFailure: false,
            includeNonCompatibilitySigningFailure: false,
            signingSucceeds: true,
            signingTargetAttributes: stagedAttributes);

        Assert.Equal(12, evidence.Summary.SignedNew);
        Assert.Equal(0, evidence.Summary.Failed);
        Assert.All(evidence.SigningTargetAttributes, attributes =>
        {
            Assert.Equal(0, attributes & cloudAttribute);
            Assert.NotEqual(0, attributes & (int)FileAttributes.Archive);
        });
    }

    private static (
        ModuleSigningResult Summary,
        int SigningCallCount,
        string[] PackageFilePaths,
        string[] CertificateLookupPaths,
        int[] SigningTargetAttributes)
        RunSigningProviderFailureHarness(
            bool includePrecheckFailure,
            bool includeNonCompatibilitySigningFailure,
            bool currentUserHasPrivateKey = true,
            bool currentUserHasCodeSigningEku = true,
            bool currentUserHasEkuRestriction = true,
            bool signingSucceeds = false,
            FileAttributes? signingTargetAttributes = null)
    {
        var rootPath = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N"))).FullName;
        var packageFileListPath = Path.Combine(rootPath, "package-files.txt");
        var callLogPath = Path.Combine(rootPath, "signing-calls.txt");
        var signingAttributeLogPath = Path.Combine(rootPath, "signing-attributes.txt");
        var certificateLookupLogPath = Path.Combine(rootPath, "certificate-lookups.txt");
        try
        {
            var packageFilePaths = Enumerable.Range(1, 12)
                .Select(index => Path.Combine(rootPath, $"File{index:D2}.ps1"))
                .ToArray();
            foreach (var path in packageFilePaths)
            {
                File.WriteAllText(path, "# test");
                if (signingTargetAttributes.HasValue)
                    File.SetAttributes(path, signingTargetAttributes.Value);
            }
            File.WriteAllLines(packageFileListPath, packageFilePaths);

            var script = EmbeddedScripts.Load("Scripts/Signing/Sign-Module.ps1");
            var includeB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("*.ps1"));
            var precheckFailurePath = includePrecheckFailure ? packageFilePaths[0] : string.Empty;
            var harness = $$"""
                $scriptText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(script))}}'))
                $rootPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(rootPath))}}'))
                $packageFileListPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(packageFileListPath))}}'))
                $callLogPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(callLogPath))}}'))
                $signingAttributeLogPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(signingAttributeLogPath))}}'))
                $certificateLookupLogPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(certificateLookupLogPath))}}'))
                $precheckFailurePath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(precheckFailurePath))}}'))
                $includeNonCompatibilitySigningFailure = [Convert]::ToBoolean('{{includeNonCompatibilitySigningFailure}}')
                $currentUserHasPrivateKey = [Convert]::ToBoolean('{{currentUserHasPrivateKey}}')
                $currentUserHasCodeSigningEku = [Convert]::ToBoolean('{{currentUserHasCodeSigningEku}}')
                $currentUserHasEkuRestriction = [Convert]::ToBoolean('{{currentUserHasEkuRestriction}}')
                $signingSucceeds = [Convert]::ToBoolean('{{signingSucceeds}}')
                $script:firstSigningCall = $true
                function Get-Item {
                  [CmdletBinding()]
                  param([string]$LiteralPath)
                  [IO.File]::AppendAllText($certificateLookupLogPath, $LiteralPath + [Environment]::NewLine)
                  $isCurrentUser = $LiteralPath -like 'Cert:\CurrentUser\My\*'
                  [pscustomobject]@{
                    Thumbprint = '0123456789ABCDEF'
                    NotBefore = [DateTime]::Now.AddDays(-1)
                    NotAfter = [DateTime]::Now.AddDays(1)
                    HasPrivateKey = if ($isCurrentUser) { $currentUserHasPrivateKey } else { $true }
                    Extensions = if ($isCurrentUser -and -not $currentUserHasEkuRestriction) {
                      @()
                    } else {
                      @([pscustomobject]@{
                        EnhancedKeyUsages = if (-not $isCurrentUser -or $currentUserHasCodeSigningEku) {
                          @([pscustomobject]@{ Value = '1.3.6.1.5.5.7.3.3' })
                        } else {
                          @([pscustomobject]@{ Value = '1.3.6.1.5.5.7.3.1' })
                        }
                      })
                    }
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
                  [IO.File]::AppendAllText(
                    $signingAttributeLogPath,
                    ([int][IO.File]::GetAttributes($FilePath)).ToString() + [Environment]::NewLine)
                  if ($signingSucceeds) {
                    return [pscustomobject]@{ Status = 'Valid'; StatusMessage = '' }
                  }
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
            return (
                summary!,
                File.ReadAllText(callLogPath).Length,
                packageFilePaths,
                File.ReadAllLines(certificateLookupLogPath),
                File.Exists(signingAttributeLogPath)
                    ? File.ReadAllLines(signingAttributeLogPath).Select(int.Parse).ToArray()
                    : Array.Empty<int>());
        }
        finally
        {
            try { Directory.Delete(rootPath, recursive: true); } catch { /* best effort */ }
        }
    }

    private static ModuleSigningResult RunExistingSignatureHarness(bool overwrite)
    {
        string rootPath = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N"))).FullName;
        string packageFilePath = Path.Combine(rootPath, "Vendor.ps1");
        string packageFileListPath = Path.Combine(rootPath, "package-files.txt");
        try
        {
            File.WriteAllText(packageFilePath, "# test");
            File.WriteAllText(packageFileListPath, packageFilePath);
            string script = EmbeddedScripts.Load("Scripts/Signing/Sign-Module.ps1");
            string includeB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("*.ps1"));
            string harness = $$"""
                $scriptText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(script))}}'))
                $rootPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(rootPath))}}'))
                $packageFileListPath = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(packageFileListPath))}}'))
                function Get-Item {
                  [CmdletBinding()]
                  param([string]$LiteralPath)
                  [pscustomobject]@{
                    Thumbprint = 'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB'
                    Subject = 'CN=Publisher'
                    NotBefore = [DateTime]::Now.AddDays(-1)
                    NotAfter = [DateTime]::Now.AddDays(1)
                    HasPrivateKey = $true
                    Extensions = @()
                  }
                }
                function Get-AuthenticodeSignature {
                  [CmdletBinding()]
                  param([string]$FilePath)
                  [pscustomobject]@{
                    Status = 'Valid'
                    SignerCertificate = [pscustomobject]@{
                      Thumbprint = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'
                      Subject = 'CN=Vendor'
                    }
                  }
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
                  [pscustomobject]@{ Status = 'Valid'; StatusMessage = '' }
                }
                & ([scriptblock]::Create($scriptText)) `
                  -RootPath $rootPath `
                  -PackageFileListPath $packageFileListPath `
                  -IncludeB64 '{{includeB64}}' `
                  -ExcludeB64 '' `
                  -Thumbprint 'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB' `
                  -PfxPath '' `
                  -PfxBase64 '' `
                  -PfxPassword '' `
                  -OverwriteSigned '{{(overwrite ? "1" : "0")}}'
                """;

            using var powerShell = System.Management.Automation.PowerShell.Create();
            powerShell.AddScript(harness);
            string summaryLine = powerShell.Invoke().Select(item => item.ToString())
                .Single(line => line.StartsWith("PFSIGN::SUMMARY::", StringComparison.Ordinal));
            Assert.Empty(powerShell.Streams.Error);
            string summaryJson = Encoding.UTF8.GetString(
                Convert.FromBase64String(summaryLine["PFSIGN::SUMMARY::".Length..]));
            return JsonSerializer.Deserialize<ModuleSigningResult>(
                       summaryJson,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidOperationException("Signing summary was not produced.");
        }
        finally
        {
            try { Directory.Delete(rootPath, recursive: true); } catch { /* best effort */ }
        }
    }
}
