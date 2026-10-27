using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PowerForge.Tests;

public sealed class ModuleSigningCertificateResolverTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("no-key")]
    [InlineData("wrong-eku")]
    public void OrderedSelectionSkipsUnusableApprovedCertificatesWithoutSearchingOtherIdentities(string condition)
    {
        using var first = CreateCertificate(condition);
        using var second = CreateCertificate("valid");
        var calls = new List<(string, StoreLocation)>();
        var input = ModuleSigningCertificateResolver.ResolveInput(new SigningOptionsConfiguration { AllowedCertificateThumbprints = [first.Thumbprint, second.Thumbprint] }, lookup: (thumbprint, location) =>
        {
            calls.Add((thumbprint, location));
            return condition == "missing" && thumbprint == first.Thumbprint ? null : new X509Certificate2(thumbprint == first.Thumbprint ? first : second);
        });
        Assert.Equal(second.Thumbprint, input.Thumbprint);
        Assert.Equal(new[] { (first.Thumbprint, StoreLocation.CurrentUser), (first.Thumbprint, StoreLocation.LocalMachine), (second.Thumbprint, StoreLocation.CurrentUser) }, calls);
    }

    [Fact]
    public void OrderedSelectionUsesUsableMachineCopyBeforeMovingToNextApprovedIdentity()
    {
        using var certificate = CreateCertificate("valid");
        var calls = new List<StoreLocation>();
        var input = ModuleSigningCertificateResolver.ResolveInput(new SigningOptionsConfiguration { AllowedCertificateThumbprints = [certificate.Thumbprint] }, lookup: (_, location) =>
        {
            calls.Add(location);
            return location == StoreLocation.CurrentUser ? null : new X509Certificate2(certificate);
        });
        Assert.Equal(certificate.Thumbprint, input.Thumbprint);
        Assert.Equal(new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine }, calls);
    }

    [Fact]
    public void OrderedSelectionUsesFirstApprovedIdentityAndCurrentUserBeforeLocalMachine()
    {
        using var first = CreateCertificate("valid");
        using var second = CreateCertificate("valid");
        int calls = 0;
        var input = ModuleSigningCertificateResolver.ResolveInput(new SigningOptionsConfiguration { AllowedCertificateThumbprints = [first.Thumbprint, second.Thumbprint] }, lookup: (thumbprint, location) =>
        {
            calls++;
            Assert.Equal(first.Thumbprint, thumbprint);
            Assert.Equal(StoreLocation.CurrentUser, location);
            return new X509Certificate2(first);
        });
        Assert.Equal(first.Thumbprint, input.Thumbprint);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ExplicitUnapprovedIdentityFailsBeforeCertificateStoreAccess()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ModuleSigningCertificateResolver.ResolveInput(
            new SigningOptionsConfiguration { CertificateThumbprint = new string('A', 40), AllowedCertificateThumbprints = [new string('B', 40)] },
            lookup: (_, _) => throw new Exception("Store must not be accessed")));
        Assert.Contains("not in AllowedCertificateThumbprints", error.Message);
    }

    [Fact]
    public void MissingApprovedIdentitiesFailInsteadOfSelectingAnotherStoreCertificate()
    {
        int calls = 0;
        var error = Assert.Throws<InvalidOperationException>(() => ModuleSigningCertificateResolver.ResolveInput(
            new SigningOptionsConfiguration { AllowedCertificateThumbprints = [new string('A', 40), new string('B', 40)] },
            lookup: (_, _) => { calls++; return null; }));
        Assert.Contains("No usable approved", error.Message);
        Assert.Equal(4, calls);
    }

    [Fact]
    public void ExplicitApprovedOverrideIsNormalizedWithoutLookingForAnotherIdentity()
    {
        var input = ModuleSigningCertificateResolver.ResolveInput(new SigningOptionsConfiguration
        {
            CertificateThumbprint = string.Join(" ", Enumerable.Repeat("aa", 20)),
            AllowedCertificateThumbprints = [new string('A', 40), new string('B', 40)]
        }, lookup: (_, _) => throw new Exception("Explicit identity must not trigger automatic selection"));
        Assert.Equal(new string('A', 40), input.Thumbprint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void InvalidApprovedIdentityIsRejectedInsteadOfDisablingPolicy(string value)
    {
        Assert.Contains("complete 40-character", Assert.Throws<InvalidOperationException>(() => ModuleSigningCertificateResolver.ResolveInput(
            new SigningOptionsConfiguration { AllowedCertificateThumbprints = [value] }, lookup: (_, _) => throw new Exception("Store must not be accessed"))).Message);
    }

    private static X509Certificate2 CreateCertificate(string condition)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Ephemeral selection fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(condition == "wrong-eku" ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.3") }, false));
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(condition == "future" ? 1 : -2), DateTimeOffset.UtcNow.AddDays(condition == "expired" ? -1 : 2));
        if (condition != "no-key") return certificate;
        using (certificate) return X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
    }
}
