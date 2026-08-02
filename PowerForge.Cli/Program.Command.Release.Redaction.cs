using PowerForge;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static partial class Program
{
    private static string[] CollectAppleCredentialMetadata(PowerForgeReleaseSpec? spec, PowerForgeReleaseResult? result)
    {
        var values = new List<string?>
        {
            spec?.AppleApps?.AppStoreConnectApiKeyPath,
            spec?.AppleApps?.AppStoreConnectApiKeyId,
            spec?.AppleApps?.AppStoreConnectApiIssuerId,
            result?.AppleAppPlan?.AppStoreConnectApiKeyPath,
            result?.AppleAppPlan?.AppStoreConnectApiKeyId,
            result?.AppleAppPlan?.AppStoreConnectApiIssuerId
        };
        foreach (var name in new[]
                 {
                     "APP_STORE_CONNECT_PRIVATE_KEY_PATH", "APP_STORE_CONNECT_PRIVATE_KEY", "APP_STORE_CONNECT_KEY_ID", "APP_STORE_CONNECT_ISSUER_ID",
                     "ASC_PRIVATE_KEY_PATH", "ASC_PRIVATE_KEY", "ASC_KEY_ID", "ASC_ISSUER_ID"
                 })
        {
            values.Add(Environment.GetEnvironmentVariable(name));
        }
        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static value => value.Length)
            .ToArray();
    }

    private static string RedactAppleCredentialText(string? text, IEnumerable<string> sensitiveValues)
    {
        var safe = text ?? string.Empty;
        foreach (var value in sensitiveValues)
        {
            safe = safe.Replace(value, "[REDACTED]", StringComparison.Ordinal);
            var encoded = JsonEncodedText.Encode(value).ToString();
            if (!encoded.Equals(value, StringComparison.Ordinal))
                safe = safe.Replace(encoded, "[REDACTED]", StringComparison.Ordinal);
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

    private static string RedactAppleCredentialJson(string json, IEnumerable<string> sensitiveValues)
    {
        var root = JsonNode.Parse(json) ?? throw new InvalidOperationException("Release JSON output could not be parsed for redaction.");
        var values = sensitiveValues.ToArray();
        RedactJsonStringValues(root, values);
        return root.ToJsonString();
    }

    private static void RedactJsonStringValues(JsonNode node, string[] sensitiveValues)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var name in jsonObject.Select(static property => property.Key).ToArray())
            {
                var child = jsonObject[name];
                if (child is JsonValue value && value.TryGetValue<string>(out var text))
                    jsonObject[name] = RedactAppleCredentialText(text, sensitiveValues);
                else if (child is not null)
                    RedactJsonStringValues(child, sensitiveValues);
            }
            return;
        }
        if (node is not JsonArray jsonArray) return;
        for (var index = 0; index < jsonArray.Count; index++)
        {
            var child = jsonArray[index];
            if (child is JsonValue value && value.TryGetValue<string>(out var text))
                jsonArray[index] = RedactAppleCredentialText(text, sensitiveValues);
            else if (child is not null)
                RedactJsonStringValues(child, sensitiveValues);
        }
    }

    private static void RedactAppleCredentialMetadata(PowerForgeReleaseSpec spec, PowerForgeReleaseResult result)
    {
        if (spec.AppleApps is not null)
        {
            spec.AppleApps.AppStoreConnectApiKeyPath = null;
            spec.AppleApps.AppStoreConnectApiKeyId = null;
            spec.AppleApps.AppStoreConnectApiIssuerId = null;
        }
        if (result.AppleAppPlan is not null)
        {
            result.AppleAppPlan.AppStoreConnectApiKeyPath = null;
            result.AppleAppPlan.AppStoreConnectApiKeyId = null;
            result.AppleAppPlan.AppStoreConnectApiIssuerId = null;
        }
    }
}
