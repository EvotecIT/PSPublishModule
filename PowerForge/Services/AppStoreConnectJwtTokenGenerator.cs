using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Creates App Store Connect API JWT bearer tokens.
/// </summary>
public sealed class AppStoreConnectJwtTokenGenerator
{
    private const string Audience = "appstoreconnect-v1";

    /// <summary>
    /// Creates an ES256 signed JWT for App Store Connect API requests.
    /// </summary>
    public string CreateToken(AppStoreConnectApiCredential credential)
    {
        if (credential is null) throw new ArgumentNullException(nameof(credential));
        if (string.IsNullOrWhiteSpace(credential.IssuerId))
            throw new ArgumentException("IssuerId is required.", nameof(credential));
        if (string.IsNullOrWhiteSpace(credential.KeyId))
            throw new ArgumentException("KeyId is required.", nameof(credential));
        if (string.IsNullOrWhiteSpace(credential.PrivateKey))
            throw new ArgumentException("PrivateKey is required.", nameof(credential));

        var lifetime = credential.TokenLifetime <= TimeSpan.Zero ? TimeSpan.FromMinutes(15) : credential.TokenLifetime;
        if (lifetime > TimeSpan.FromMinutes(20))
            throw new ArgumentException("App Store Connect API token lifetime must not exceed 20 minutes.", nameof(credential));

        var now = DateTimeOffset.UtcNow;
        var header = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["alg"] = "ES256",
            ["kid"] = credential.KeyId.Trim(),
            ["typ"] = "JWT"
        });
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["iss"] = credential.IssuerId.Trim(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.Add(lifetime).ToUnixTimeSeconds(),
            ["aud"] = Audience
        });

        var signingInput = Base64Url(header) + "." + Base64Url(payload);
        var signature = Sign(signingInput, credential.PrivateKey);
        return signingInput + "." + Base64Url(signature);
    }

    private static byte[] Sign(string signingInput, string privateKey)
    {
#if NET472
        throw new PlatformNotSupportedException("App Store Connect JWT signing requires .NET 8 or newer.");
#else
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKey.AsSpan());
        return ecdsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256);
#endif
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
