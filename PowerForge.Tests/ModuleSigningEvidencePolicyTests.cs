using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge.Tests;

public sealed class ModuleSigningEvidencePolicyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompilationEvidenceUsesTheSameApprovedPfxIdentity(bool approved)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Ephemeral evidence policy fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var signing = new SigningOptionsConfiguration
        {
            CertificatePFXBase64 = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, "fixture")),
            CertificatePFXPassword = "fixture",
            AllowedCertificateThumbprints = [approved ? certificate.Thumbprint : new string('1', 40)]
        };
        byte[] content = [1, 2, 3];
        if (approved)
        {
            var signature = PowerShellCompilationEvidenceAuthenticator.Sign(content, signing);
            Assert.Equal(certificate.Thumbprint, signature.Thumbprint);
            Assert.Equal(certificate.Thumbprint, PowerShellCompilationEvidenceAuthenticator.Verify(content, signature.Signature, signing).Thumbprint);
        }
        else
        {
            var error = Assert.Throws<InvalidOperationException>(() => PowerShellCompilationEvidenceAuthenticator.Sign(content, signing));
            Assert.Contains("not in AllowedCertificateThumbprints", error.Message);
        }
    }

    [Fact]
    public void CompilationEvidenceResolvesReferencedPfxWithoutChangingParentEnvironment()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Ephemeral referenced evidence fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        string name = "PF_EVIDENCE_FIXTURE_" + Guid.NewGuid().ToString("N");
        string pfx = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, "fixture"));
        Environment.SetEnvironmentVariable(name, pfx);
        Environment.SetEnvironmentVariable(name + "_PASSWORD", "fixture");
        try
        {
            var signing = new SigningOptionsConfiguration
            {
                CertificatePFXBase64EnvironmentVariable = name, CertificatePFXPasswordEnvironmentVariable = name + "_PASSWORD",
                AllowedCertificateThumbprints = [certificate.Thumbprint]
            };
            var signature = PowerShellCompilationEvidenceAuthenticator.Sign([1, 2, 3], signing);
            Assert.Equal(certificate.Thumbprint, PowerShellCompilationEvidenceAuthenticator.GetSignerThumbprint(signing));
            Assert.Equal(certificate.Thumbprint, PowerShellCompilationEvidenceAuthenticator.Verify([1, 2, 3], signature.Signature, signing).Thumbprint);
            Assert.Equal(pfx, Environment.GetEnvironmentVariable(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
            Environment.SetEnvironmentVariable(name + "_PASSWORD", null);
        }
    }
}
