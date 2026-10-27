using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ApprovedSigningPolicyTests
{
    private const string First = "1111111111111111111111111111111111111111";
    private const string Second = "2222222222222222222222222222222222222222";

    [Fact]
    public void BuildFactoryAndProfilePreservePolicyThroughJsonAndDeliverySigning()
    {
        var request = new BuildConfigurationRequest { AllowedCertificateThumbprintsSpecified = true, AllowedCertificateThumbprints = [First, Second] };
        var segment = Assert.IsType<ConfigurationOptionsSegment>(Assert.Single(new BuildConfigurationFactory().Create(request)));
        request.AllowedCertificateThumbprints[0] = "changed";
        Assert.Equal(new[] { First, Second }, segment.Options.Signing!.AllowedCertificateThumbprints);
        var profile = new ModuleBuildProfileFactory().Create(new ModuleBuildProfileRequest { AllowedCertificateThumbprints = [First, Second] });
        Assert.Equal(new[] { First, Second }, Assert.Single(profile.OfType<ConfigurationOptionsSegment>()).Options.Signing!.AllowedCertificateThumbprints);
        var roundtrip = JsonSerializer.Deserialize<SigningOptionsConfiguration>(JsonSerializer.Serialize(segment.Options.Signing))!;
        var effective = ModulePipelineRunner.ApplyDeliverySigningPreference(roundtrip, new DeliveryOptionsConfiguration { Sign = true });
        Assert.Equal(new[] { First, Second }, effective!.AllowedCertificateThumbprints);
        Assert.True(effective.IncludeInternals);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void PfxTransportIsChildScopedAndEnforcesApprovedIdentity(bool approved, bool useEnvironmentReferences, bool usePfxPath)
    {
        using var key = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=PowerForge ephemeral signing fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certificateRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.3") }, false));
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        const string password = "synthetic fixture password \"$&'";
        string pfx = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, password));
        string? inherited = Environment.GetEnvironmentVariable("POWERFORGE_SIGNING_PFX_PASSWORD");
        string payloadVariable = "PF_TEST_PFX_" + Guid.NewGuid().ToString("N");
        string passwordVariable = payloadVariable + "_PASSWORD";
        if (useEnvironmentReferences)
        {
            if (!usePfxPath) Environment.SetEnvironmentVariable(payloadVariable, pfx);
            Environment.SetEnvironmentVariable(passwordVariable, password);
        }
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string pfxPath = Path.Combine(root, "Fixture.pfx");
        if (usePfxPath) File.WriteAllBytes(pfxPath, certificate.Export(X509ContentType.Pfx, password));
        string target = Path.Combine(root, "Fixture.ps1");
        File.WriteAllText(target, "'fixture'");
        PowerShellRunRequest? captured = null;
        var runner = new InspectingRunner(request =>
        {
            captured = request;
            // Stub the platform signature-status boundary only; execute the real child,
            // PFX import, policy and secret consumption without using real private keys.
            string script = File.ReadAllText(request.ScriptPath!);
            string probe = $$"""
                function Get-AuthenticodeSignature {
                  [CmdletBinding()] param([string]$FilePath)
                  foreach ($name in @('POWERFORGE_SIGNING_PFX_PATH', 'POWERFORGE_SIGNING_PFX_BASE64', 'POWERFORGE_SIGNING_PFX_PASSWORD', '{{payloadVariable}}', '{{passwordVariable}}')) {
                    if ([Environment]::GetEnvironmentVariable($name, 'Process')) { throw 'Signing secret remained in descendant environment.' }
                  }
                  [pscustomobject]@{ Status = 'Valid'; SignerCertificate = [pscustomobject]@{ Thumbprint = '{{certificate.Thumbprint}}' } }
                }
                function Get-OpenAuthenticodeSignature {
                  [CmdletBinding()] param([string]$LiteralPath)
                  $state = Get-AuthenticodeSignature -FilePath $LiteralPath
                  [pscustomobject]@{ Certificate = $state.SignerCertificate }
                }
                function Set-OpenAuthenticodeSignature { throw 'No signing operation is expected for the valid fixture signature.' }
                """;
            File.WriteAllText(request.ScriptPath!, script.Replace("$ErrorActionPreference = 'Stop'", probe + "\n$ErrorActionPreference = 'Stop'"));
            return new PowerShellRunner().Run(request);
        });
        try
        {
            var operations = new PowerShellModulePipelineHostedOperations(runner, new NullLogger());
            var signing = new SigningOptionsConfiguration
            {
                CertificatePFXBase64 = useEnvironmentReferences || usePfxPath ? null : pfx,
                CertificatePFXPath = usePfxPath ? pfxPath : null,
                CertificatePFXPassword = useEnvironmentReferences ? null : password,
                CertificatePFXBase64EnvironmentVariable = useEnvironmentReferences && !usePfxPath ? payloadVariable : null,
                CertificatePFXPasswordEnvironmentVariable = useEnvironmentReferences ? passwordVariable : null,
                AllowedCertificateThumbprints = [approved ? certificate.Thumbprint : First]
            };
            if (approved)
            {
                var result = operations.SignModuleOutput("Fixture", root, [target], ["*.ps1"], [], signing);
                Assert.Equal(certificate.Thumbprint, result.CertificateThumbprint);
                Assert.Equal(0, result.Failed);
            }
            else
            {
                var error = Assert.Throws<ModuleSigningException>(() => operations.SignModuleOutput("Fixture", root, [target], ["*.ps1"], [], signing));
                Assert.Contains("not in AllowedCertificateThumbprints", error.Message);
                Assert.DoesNotContain(password, error.ToString());
                Assert.DoesNotContain(pfx, error.ToString());
            }
            Assert.NotNull(captured);
            Assert.DoesNotContain(captured.Arguments, value => value.Contains(password) || value.Contains(pfx));
            Assert.Equal(password, captured.EnvironmentVariables!["POWERFORGE_SIGNING_PFX_PASSWORD"]);
            Assert.Equal(usePfxPath ? null : pfx, captured.EnvironmentVariables!["POWERFORGE_SIGNING_PFX_BASE64"]);
            Assert.Equal(usePfxPath ? pfxPath : null, captured.EnvironmentVariables!["POWERFORGE_SIGNING_PFX_PATH"]);
            Assert.Equal(inherited ?? string.Empty, Environment.GetEnvironmentVariable("POWERFORGE_SIGNING_PFX_PASSWORD") ?? string.Empty);
            if (useEnvironmentReferences)
            {
                Assert.Null(captured.EnvironmentVariables[passwordVariable]);
                if (!usePfxPath)
                {
                    Assert.Null(captured.EnvironmentVariables[payloadVariable]);
                    Assert.Equal(pfx, Environment.GetEnvironmentVariable(payloadVariable));
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(payloadVariable, null);
            Environment.SetEnvironmentVariable(passwordVariable, null);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EnvironmentReferencesRemainUnresolvedInConfigurationAndMissingSecretsFailBeforeChildLaunch()
    {
        string missing = "POWERFORGE_TEST_MISSING_" + Guid.NewGuid().ToString("N");
        var segments = new BuildConfigurationFactory().Create(new BuildConfigurationRequest
        {
            CertificatePFXBase64EnvironmentVariableSpecified = true,
            CertificatePFXBase64EnvironmentVariable = missing,
            CertificatePFXPasswordEnvironmentVariableSpecified = true,
            CertificatePFXPasswordEnvironmentVariable = missing + "_PASSWORD"
        });
        var signing = Assert.Single(segments.OfType<ConfigurationOptionsSegment>()).Options.Signing!;
        string json = JsonSerializer.Serialize(signing);
        Assert.Contains(missing, json);
        Assert.Null(signing.CertificatePFXBase64);
        var operations = new PowerShellModulePipelineHostedOperations(new InspectingRunner(_ => throw new Exception("Child must not start")), new NullLogger());
        var error = Assert.Throws<InvalidOperationException>(() => operations.SignModuleOutput("Fixture", Path.GetTempPath(), [], [], [], signing));
        Assert.Contains("missing or empty", error.Message);
        Assert.Contains(missing, error.Message);
    }

    [Fact]
    public void LiteralAndEnvironmentSecretSourcesAreRejectedBeforeChildLaunch()
    {
        var operations = new PowerShellModulePipelineHostedOperations(new InspectingRunner(_ => throw new Exception("Child must not start")), new NullLogger());
        var error = Assert.Throws<InvalidOperationException>(() => operations.SignModuleOutput("Fixture", Path.GetTempPath(), [], [], [],
            new SigningOptionsConfiguration { CertificatePFXBase64 = "synthetic-secret", CertificatePFXBase64EnvironmentVariable = "FIXTURE_PFX" }));
        Assert.Contains("not both", error.Message);
        Assert.DoesNotContain("synthetic-secret", error.Message);
    }

    private sealed class InspectingRunner(Func<PowerShellRunRequest, PowerShellRunResult> run) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request) => run(request);
    }
}
