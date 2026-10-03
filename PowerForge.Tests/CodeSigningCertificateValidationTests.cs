using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge.Tests;

public sealed class CodeSigningCertificateValidationTests
{
    [Theory]
    [InlineData(-2, -1, true, "expired")]
    [InlineData(1, 2, true, "not valid until")]
    [InlineData(-1, 1, false, "does not allow code signing")]
    public void Validate_RejectsUnusableSigningCertificates(int starts, int ends, bool codeSigning, string message)
    {
        using var certificate = CreateCertificate(starts, ends, codeSigning);
        var error = Assert.Throws<InvalidOperationException>(() => CodeSigningCertificateValidation.Validate(certificate));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Validate_AcceptsCurrentCodeSigningCertificateAndRejectsMissingPrivateKey()
    {
        using var certificate = CreateCertificate(-1, 1, true);
        CodeSigningCertificateValidation.Validate(certificate);
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        Assert.Contains("private key", Assert.Throws<InvalidOperationException>(() =>
            CodeSigningCertificateValidation.Validate(publicOnly)).Message);
    }

    private static X509Certificate2 CreateCertificate(int starts, int ends, bool codeSigning)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=PowerForge signing validation fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid(codeSigning ? "1.3.6.1.5.5.7.3.3" : "1.3.6.1.5.5.7.3.1") }, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(starts), DateTimeOffset.UtcNow.AddDays(ends));
    }
}
