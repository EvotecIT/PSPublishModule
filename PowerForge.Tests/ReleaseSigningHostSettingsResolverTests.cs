using System.Security.Cryptography.X509Certificates;

namespace PowerForge.Tests;

public sealed class ReleaseSigningHostSettingsResolverTests
{
    [Fact]
    public void Resolve_UsesDefaultsAndReportsMissingThumbprint()
    {
        var resolver = new ReleaseSigningHostSettingsResolver(
            getEnvironmentVariable: _ => null,
            resolveModulePath: () => @"C:\Modules\PSPublishModule.psd1");

        var settings = resolver.Resolve();

        Assert.False(settings.IsConfigured);
        Assert.Equal("CurrentUser", settings.StoreName);
        Assert.Equal("http://timestamp.digicert.com", settings.TimeStampServer);
        Assert.Equal(@"C:\Modules\PSPublishModule.psd1", settings.ModulePath);
        Assert.Contains("RELEASE_OPS_STUDIO_SIGN_THUMBPRINT", settings.MissingConfigurationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_UsesEnvironmentOverrides()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
            ["RELEASE_OPS_STUDIO_SIGN_THUMBPRINT"] = " thumb ",
            ["RELEASE_OPS_STUDIO_SIGN_STORE"] = " LocalMachine ",
            ["RELEASE_OPS_STUDIO_SIGN_TIMESTAMP_URL"] = " https://timestamp.contoso.test ",
            ["RELEASE_OPS_STUDIO_PSPUBLISHMODULE_PATH"] = @" C:\Temp\PSPublishModule.psd1 "
        };

        var resolver = new ReleaseSigningHostSettingsResolver(
            getEnvironmentVariable: name => values.TryGetValue(name, out var value) ? value : null,
            resolveModulePath: () => @"C:\Ignored\PSPublishModule.psd1");

        var settings = resolver.Resolve();

        Assert.True(settings.IsConfigured);
        Assert.Equal("thumb", settings.Thumbprint);
        Assert.Equal("LocalMachine", settings.StoreName);
        Assert.Equal("https://timestamp.contoso.test", settings.TimeStampServer);
        Assert.Equal(@"C:\Temp\PSPublishModule.psd1", settings.ModulePath);
    }
}

public sealed class CertificateFingerprintResolverTests
{
    [Fact]
    public void ResolveSha256_NormalizesThumbprintsAndMapsStoreName()
    {
        StoreLocation? capturedStoreLocation = null;
        string? capturedThumbprint = null;
        var resolver = new CertificateFingerprintResolver((storeLocation, normalizedThumbprint) => {
            capturedStoreLocation = storeLocation;
            capturedThumbprint = normalizedThumbprint;
            return "ABC123";
        });

        var fingerprint = resolver.ResolveSha256("ab cd 12", "LocalMachine");

        Assert.Equal("ABC123", fingerprint);
        Assert.Equal(StoreLocation.LocalMachine, capturedStoreLocation);
        Assert.Equal("ABCD12", capturedThumbprint);
    }
}
