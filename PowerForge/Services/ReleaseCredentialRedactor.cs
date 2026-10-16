using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Removes release credentials before diagnostic excerpts or JSON are retained.</summary>
internal static class ReleaseCredentialRedactor
{
    /// <summary>Redacts complete text before truncation can expose part of a credential.</summary>
    internal static string RedactText(string? text, IEnumerable<string> sensitiveValues)
    {
        var safe = text ?? string.Empty;
        foreach (var value in sensitiveValues)
        {
            safe = safe.Replace(value, "[REDACTED]");
            var encoded = JsonEncodedText.Encode(value).ToString();
            if (!encoded.Equals(value, StringComparison.Ordinal))
                safe = safe.Replace(encoded, "[REDACTED]");
        }
        safe = Regex.Replace(
            safe,
            "(?i)(\\\"appStoreConnectApi(?:KeyPath|KeyId|IssuerId)\\\"\\s*:\\s*)\\\"[^\\\"]*\\\"",
            "$1\\\"[REDACTED]\\\"");
        safe = Regex.Replace(
            safe,
            "(?i)(?:[A-Za-z]:)?[^\\s\\\"']*\\.appstoreconnect[/\\\\][^\\s\\\"']+",
            "[REDACTED_PROFILE_PATH]");
        safe = Regex.Replace(
            safe,
            "-----BEGIN(?: [A-Z0-9]+)? PRIVATE KEY-----.*?-----END(?: [A-Z0-9]+)? PRIVATE KEY-----",
            "[REDACTED_PRIVATE_KEY]",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return safe;
    }

}
