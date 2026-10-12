using System.Globalization;
using System.Text.Json;

namespace PowerForge;

public sealed partial class AppStoreConnectClient
{
    /// <summary>Lists registered public signing certificates, following pagination.</summary>
    public Task<AppStoreConnectSigningCertificateInfo[]> GetSigningCertificatesAsync(
        int limit = 200,
        CancellationToken cancellationToken = default)
        => GetArrayAsync("certificates" + BuildQuery(new Dictionary<string, string?>
        {
            ["fields[certificates]"] = "name,certificateType,activated,serialNumber,expirationDate,certificateContent",
            ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
        }), ParseSigningCertificate, cancellationToken);

    /// <summary>Lists provisioning profiles for one signing bundle identifier resource.</summary>
    public Task<AppStoreConnectProvisioningProfileInfo[]> GetBundleIdProfilesAsync(
        string bundleIdResourceId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bundleIdResourceId))
            throw new ArgumentException("Bundle identifier resource id is required.", nameof(bundleIdResourceId));
        return GetArrayAsync($"bundleIds/{Uri.EscapeDataString(bundleIdResourceId.Trim())}/profiles" +
            BuildQuery(new Dictionary<string, string?>
            {
                ["limit"] = ClampLimit(limit).ToString(CultureInfo.InvariantCulture)
            }), ParseProvisioningProfile, cancellationToken);
    }

    /// <summary>
    /// Creates an App Store profile for an existing bundle identifier and one public distribution
    /// certificate resource. Capabilities and App Group associations must already be configured.
    /// Does not create or revoke certificates, change capabilities, or install files locally.
    /// </summary>
    public Task<AppStoreConnectProvisioningProfileInfo> CreateAppStoreProvisioningProfileAsync(
        string bundleIdResourceId,
        string name,
        AppStoreConnectAppStoreProfileType profileType,
        string certificateId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bundleIdResourceId))
            throw new ArgumentException("Bundle identifier resource id is required.", nameof(bundleIdResourceId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Profile name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(certificateId))
            throw new ArgumentException("One distribution certificate resource id is required.", nameof(certificateId));
        var appleProfileType = profileType switch
        {
            AppStoreConnectAppStoreProfileType.iOS => "IOS_APP_STORE",
            AppStoreConnectAppStoreProfileType.macOS => "MAC_APP_STORE",
            AppStoreConnectAppStoreProfileType.MacCatalyst => "MAC_CATALYST_APP_STORE",
            _ => throw new ArgumentOutOfRangeException(nameof(profileType), profileType, "Unsupported App Store profile type.")
        };
        var body = new
        {
            data = new
            {
                type = "profiles",
                attributes = new { name = name.Trim(), profileType = appleProfileType },
                relationships = new
                {
                    bundleId = new { data = new { type = "bundleIds", id = bundleIdResourceId.Trim() } },
                    certificates = new { data = new[] { new { type = "certificates", id = certificateId.Trim() } } }
                }
            }
        };
        return PostSingleAsync("profiles", body, ParseProvisioningProfile, cancellationToken);
    }

    private static AppStoreConnectSigningCertificateInfo ParseSigningCertificate(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectSigningCertificateInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Name = GetString(attrs, "name"),
            CertificateType = GetString(attrs, "certificateType"),
            Activated = GetBool(attrs, "activated"),
            SerialNumber = GetString(attrs, "serialNumber"),
            ExpirationDate = GetDateTimeOffset(attrs, "expirationDate"),
            CertificateContent = GetString(attrs, "certificateContent")
        };
    }

    private static AppStoreConnectProvisioningProfileInfo ParseProvisioningProfile(JsonElement item)
    {
        var attrs = GetAttributes(item);
        return new AppStoreConnectProvisioningProfileInfo
        {
            Id = GetString(item, "id") ?? string.Empty,
            Uuid = GetString(attrs, "uuid"),
            Name = GetString(attrs, "name"),
            ProfileType = GetString(attrs, "profileType"),
            Platform = GetString(attrs, "platform"),
            ProfileState = GetString(attrs, "profileState"),
            ExpirationDate = GetDateTimeOffset(attrs, "expirationDate"),
            ProfileContent = GetString(attrs, "profileContent")
        };
    }
}
