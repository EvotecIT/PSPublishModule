using System.Security.Cryptography;
using System.Text;

namespace PowerForge.Web;

/// <summary>
/// Stable hashing for audit baseline keys to keep baselines small even on large sites.
/// </summary>
public static class WebAuditKeyHasher
{
    /// <summary>Baseline hash format string stored in baseline JSON.</summary>
    public const string DefaultFormat = "sha256-12-b64url";

    /// <summary>Hashes an audit issue key into a short base64url token.</summary>
    public static string Hash(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(key);
        var hash = SHA256.HashData(bytes);
        // 12 bytes -> 16 base64 chars (without padding) after base64url normalization.
        var token = Convert.ToBase64String(hash, 0, 12)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return token;
    }
}

