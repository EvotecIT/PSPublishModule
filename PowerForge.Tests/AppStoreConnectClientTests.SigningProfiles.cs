using System.Net;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task SigningProfiles_ReadsPublicCertificatesAndScopedProfiles()
    {
        var handler = new SequenceHandler(
            new SequenceResponse(HttpStatusCode.OK, """
                { "data": [{ "type": "certificates", "id": "cert-1", "attributes": {
                  "name": "Distribution", "certificateType": "DISTRIBUTION", "activated": false, "serialNumber": "1234",
                  "expirationDate": "2027-10-12T01:00:00Z", "certificateContent": "REVSTw==" } }],
                  "links": { "next": "https://api.appstoreconnect.apple.com/v1/certificates?cursor=2" } }
                """),
            new SequenceResponse(HttpStatusCode.OK, """{ "data": [{ "id": "cert-2", "attributes": { "certificateType": "FUTURE_TYPE" } }] }"""),
            new SequenceResponse(HttpStatusCode.OK, """
                { "data": [{ "type": "profiles", "id": "profile-1", "attributes": {
                  "uuid": "profile-uuid", "name": "Helper Store", "profileType": "MAC_APP_STORE",
                  "platform": "MAC_OS", "profileState": "ACTIVE", "expirationDate": "2027-10-12T01:00:00Z", "profileContent": "UE9GSUxF" } }] }
                """));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var certificates = await client.GetSigningCertificatesAsync(limit: 500);
        var profile = Assert.Single(await client.GetBundleIdProfilesAsync(" bundle/id ", limit: 0));

        Assert.Equal(2, certificates.Length);
        Assert.Equal("cert-1", certificates[0].Id);
        Assert.Equal("1234", certificates[0].SerialNumber);
        Assert.False(certificates[0].Activated);
        Assert.Equal("REVSTw==", certificates[0].CertificateContent);
        Assert.Equal("FUTURE_TYPE", certificates[1].CertificateType);
        Assert.Null(certificates[1].CertificateContent);
        Assert.Null(certificates[1].Activated);
        Assert.Equal(DateTimeOffset.Parse("2027-10-12T01:00:00Z"), profile.ExpirationDate);
        Assert.Equal("profile-uuid", profile.Uuid);
        Assert.Equal("Helper Store", profile.Name);
        Assert.Equal("MAC_APP_STORE", profile.ProfileType);
        Assert.Equal("MAC_OS", profile.Platform);
        Assert.Equal("ACTIVE", profile.ProfileState);
        Assert.Equal("UE9GSUxF", profile.ProfileContent);
        Assert.Contains("fields%5Bcertificates%5D=name%2CcertificateType%2Cactivated%2CserialNumber%2CexpirationDate%2CcertificateContent", handler.RequestUris[0].Query, StringComparison.Ordinal);
        Assert.Contains("limit=200", handler.RequestUris[0].Query, StringComparison.Ordinal);
        Assert.EndsWith("bundleIds/bundle%2Fid/profiles?limit=1", handler.RequestUris[2].AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SigningProfiles_PreservesActivatedCertificateStatus()
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.OK, """
            { "data": [{ "id": "active-cert", "attributes": { "certificateType": "DISTRIBUTION", "activated": true } }] }
            """));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        Assert.True(Assert.Single(await client.GetSigningCertificatesAsync()).Activated);
    }

    [Theory]
    [InlineData(AppStoreConnectAppStoreProfileType.iOS, "IOS_APP_STORE")]
    [InlineData(AppStoreConnectAppStoreProfileType.macOS, "MAC_APP_STORE")]
    [InlineData(AppStoreConnectAppStoreProfileType.MacCatalyst, "MAC_CATALYST_APP_STORE")]
    public async Task SigningProfiles_CreatesRequestedBundleWithOneDistributionCertificate(AppStoreConnectAppStoreProfileType type, string expected)
    {
        var handler = new SequenceHandler(new SequenceResponse(HttpStatusCode.Created, """
            { "data": { "id": "profile-new", "type": "profiles", "attributes": { "uuid": "uuid-new", "profileContent": "Q01T" } } }
            """));
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);

        var result = await client.CreateAppStoreProvisioningProfileAsync(" bundle-1 ", " Helper Store ", type, " cert-1 ");

        Assert.Equal("profile-new", result.Id);
        Assert.Equal("Q01T", result.ProfileContent);
        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Methods));
        Assert.EndsWith("/v1/profiles", Assert.Single(handler.RequestUris).AbsoluteUri, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("profiles", data.GetProperty("type").GetString());
        Assert.Equal(expected, data.GetProperty("attributes").GetProperty("profileType").GetString());
        Assert.Equal("Helper Store", data.GetProperty("attributes").GetProperty("name").GetString());
        var links = data.GetProperty("relationships");
        Assert.Equal("bundle-1", links.GetProperty("bundleId").GetProperty("data").GetProperty("id").GetString());
        var certificate = Assert.Single(links.GetProperty("certificates").GetProperty("data").EnumerateArray());
        Assert.Equal("certificates", certificate.GetProperty("type").GetString());
        Assert.Equal("cert-1", certificate.GetProperty("id").GetString());
        Assert.False(links.TryGetProperty("devices", out _));
    }

    [Fact]
    public async Task SigningProfiles_RejectsInvalidCreationInputsBeforeHttp()
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetBundleIdProfilesAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateAppStoreProvisioningProfileAsync("", "Name", AppStoreConnectAppStoreProfileType.macOS, "cert"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateAppStoreProvisioningProfileAsync("id", "", AppStoreConnectAppStoreProfileType.macOS, "cert"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateAppStoreProvisioningProfileAsync("id", "Name", AppStoreConnectAppStoreProfileType.macOS, " "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateAppStoreProvisioningProfileAsync("id", "Name", (AppStoreConnectAppStoreProfileType)99, "cert"));
        Assert.Empty(handler.RequestUris);
    }
}
