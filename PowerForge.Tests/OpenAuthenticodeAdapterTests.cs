using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class OpenAuthenticodeAdapterTests
{
    [Theory]
    [InlineData("unsigned", 1, 0, 0)]
    [InlineData("existing", 0, 0, 0)]
    [InlineData("vendor", 0, 0, 1)]
    [InlineData("overwrite", 0, 1, 0)]
    public void CmsProviderResultsPreserveOrReplaceSignaturesWithVerifiedIdentity(string mode, int newlySigned, int resigned, int other)
    {
        var result = Run(mode, expectFailure: false);
        Assert.Equal(newlySigned, result.SignedNew);
        Assert.Equal(resigned, result.Resigned);
        Assert.Equal(other, result.AlreadySignedOther);
        Assert.Equal(0, result.Failed);
        Assert.Equal(other, result.PreservedThirdPartySignatures.Length);
    }

    [Theory]
    [InlineData("invalid", 1, 0)]
    [InlineData("write-fails", 0, 1)]
    [InlineData("wrong-signer", 0, 1)]
    public void ProviderValidationAndWriteFailuresCannotBeReportedAsSuccessfulSigning(string mode, int precheckFailures, int signingExceptions)
    {
        var result = Run(mode, expectFailure: true);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.SignedTotal);
        Assert.Equal(precheckFailures, result.PrecheckFailure);
        Assert.Equal(signingExceptions, result.SigningException);
    }

    private static ModuleSigningResult Run(string mode, bool expectFailure)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Ephemeral OpenAuthenticode adapter fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var target = Path.Combine(root.FullName, "Fixture.ps1");
        File.WriteAllText(target, "'fixture'");
        var runner = new AdapterRunner(child =>
        {
            var provider = $$"""
                $script:written = $false
                $script:mode = '{{mode}}'
                function Get-OpenAuthenticodeSignature {
                  [CmdletBinding()] param([string]$LiteralPath)
                  if ($script:mode -eq 'invalid') {
                    $PSCmdlet.ThrowTerminatingError([Management.Automation.ErrorRecord]::new([Security.Cryptography.CryptographicException]::new('invalid signature fixture'),'GetSignatureError',[Management.Automation.ErrorCategory]::InvalidData,$LiteralPath))
                  }
                  if (-not $script:written -and $script:mode -in @('unsigned','write-fails','wrong-signer')) {
                    $PSCmdlet.ThrowTerminatingError([Management.Automation.ErrorRecord]::new([Management.Automation.ItemNotFoundException]::new('unsigned fixture'),'NoSignature',[Management.Automation.ErrorCategory]::ObjectNotFound,$LiteralPath))
                  }
                  $thumbprint = if (($script:mode -in @('vendor','overwrite') -and -not $script:written) -or $script:mode -eq 'wrong-signer') { 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' } else { '{{certificate.Thumbprint}}' }
                  # The real provider exposes Certificate on a CMS object, with no Status field.
                  [pscustomobject]@{ Certificate = [pscustomobject]@{ Thumbprint = $thumbprint; Subject = 'CN=Fixture' } }
                }
                function Set-OpenAuthenticodeSignature {
                  [CmdletBinding()] param([string]$LiteralPath,[object]$Certificate,[string]$TimeStampServer,[Security.Cryptography.X509Certificates.X509IncludeOption]$IncludeOption,[string]$HashAlgorithm)
                  if ($script:mode -eq 'write-fails') { throw 'provider write failed' }
                  $script:written = $true
                  # The real provider emits no output unless PassThru is requested.
                }
                function Start-Sleep { param([int]$Milliseconds) }
                """;
            var script = File.ReadAllText(child.ScriptPath!);
            script = script.Replace("$ErrorActionPreference = 'Stop'", provider + "\n$ErrorActionPreference = 'Stop'")
                .Replace("if ($IsWindows -or $PSVersionTable.PSVersion.Major -le 5)", "if ($false)");
            File.WriteAllText(child.ScriptPath!, script);
            return new PowerShellRunner().Run(child);
        });
        try
        {
            var operations = new PowerShellModulePipelineHostedOperations(runner, new NullLogger());
            var signing = new SigningOptionsConfiguration
            {
                CertificatePFXBase64 = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, "fixture")),
                CertificatePFXPassword = "fixture", AllowedCertificateThumbprints = [certificate.Thumbprint], OverwriteSigned = mode == "overwrite"
            };
            if (expectFailure)
            {
                var error = Assert.Throws<ModuleSigningException>(() => operations.SignModuleOutput("Fixture", root.FullName, [target], ["*.ps1"], [], signing));
                return Assert.IsType<ModuleSigningResult>(error.Result);
            }
            var result = operations.SignModuleOutput("Fixture", root.FullName, [target], ["*.ps1"], [], signing);
            Assert.Equal(certificate.Thumbprint, result.CertificateThumbprint);
            return result;
        }
        finally { root.Delete(true); }
    }

    private sealed class AdapterRunner(Func<PowerShellRunRequest, PowerShellRunResult> run) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request) => run(request);
    }
}
