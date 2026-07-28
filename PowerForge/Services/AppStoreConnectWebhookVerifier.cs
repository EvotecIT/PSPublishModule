using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>Authenticates and parses App Store Connect webhook notifications.</summary>
public sealed class AppStoreConnectWebhookVerifier
{
    /// <summary>Verifies x-apple-signature with HMAC-SHA256 and parses the event.</summary>
    public AppStoreConnectWebhookNotification VerifyAndParse(
        ReadOnlySpan<byte> payload,
        string signatureHeader,
        string secret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            throw new ArgumentException("x-apple-signature is required.", nameof(signatureHeader));
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Webhook secret is required.", nameof(secret));

        const string prefix = "hmacsha256=";
        var header = signatureHeader.Trim();
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("x-apple-signature must use hmacsha256.");
        byte[] supplied;
        try
        {
            supplied = ParseHex(header.Substring(prefix.Length));
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("x-apple-signature contains an invalid hexadecimal digest.", exception);
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = hmac.ComputeHash(payload.ToArray());
        if (!FixedTimeEquals(supplied, expected))
            throw new InvalidOperationException("App Store Connect webhook signature validation failed.");

        using var document = JsonDocument.Parse(payload.ToArray());
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("App Store Connect webhook payload has no data object.");
        var attributes = data.TryGetProperty("attributes", out var attributeValue)
            ? attributeValue.Clone()
            : default;
        var previous = FirstString(attributes, "oldValue", "oldState", "oldExternalBuildState");
        var next = FirstString(attributes, "newValue", "newState", "newExternalBuildState");
        var instance = data.TryGetProperty("relationships", out var relationships) &&
                       relationships.TryGetProperty("instance", out var instanceRelationship) &&
                       instanceRelationship.TryGetProperty("data", out var instanceData)
            ? instanceData
            : default;
        var type = GetString(data, "type") ?? string.Empty;
        var failure = IsFailureState(next);
        return new AppStoreConnectWebhookNotification
        {
            Id = GetString(data, "id") ?? string.Empty,
            Type = type,
            Version = GetInt32(data, "version"),
            Timestamp = GetDateTimeOffset(attributes, "timestamp"),
            PreviousState = previous,
            NewState = next,
            InstanceType = GetString(instance, "type"),
            InstanceId = GetString(instance, "id"),
            Attributes = attributes,
            IsFailure = failure,
            ShouldRefreshReleaseState = IsReleaseStateEvent(type),
            NextActions = BuildNextActions(type, next, failure)
        };
    }

    private static string[] BuildNextActions(string type, string? state, bool failure)
    {
        if (failure)
            return new[] { "Run powerforge apple-release Doctor, retain the receipt, and inspect the matching build-upload or review resource before retrying." };
        if (type.Contains("buildUpload", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(state, "COMPLETE", StringComparison.OrdinalIgnoreCase))
            return new[] { "Run powerforge apple-release Status to bind the processed build and continue the smallest pending release action." };
        if (type.Contains("appStoreVersion", StringComparison.OrdinalIgnoreCase))
            return new[] { "Run powerforge apple-release Status and notify the release owner when a human review or release gate is now actionable." };
        if (type.Contains("betaFeedback", StringComparison.OrdinalIgnoreCase))
            return new[] { "Fetch and triage the referenced TestFlight feedback or crash submission." };
        return new[] { "Refresh the compact Apple release receipt for the affected app." };
    }

    private static bool IsReleaseStateEvent(string type)
        => type.Contains("buildUpload", StringComparison.OrdinalIgnoreCase) ||
           type.Contains("appStoreVersion", StringComparison.OrdinalIgnoreCase) ||
           type.Contains("buildBetaDetail", StringComparison.OrdinalIgnoreCase) ||
           type.Contains("review", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailureState(string? state)
        => state is not null &&
           (state.Equals("FAILED", StringComparison.OrdinalIgnoreCase) ||
            state.Equals("INVALID", StringComparison.OrdinalIgnoreCase) ||
            state.Equals("REJECTED", StringComparison.OrdinalIgnoreCase) ||
            state.Equals("DEVELOPER_REJECTED", StringComparison.OrdinalIgnoreCase) ||
            state.Equals("METADATA_REJECTED", StringComparison.OrdinalIgnoreCase));

    private static string? FirstString(JsonElement element, params string[] names)
        => names.Select(name => GetString(element, name)).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static string? GetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static int GetInt32(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.TryGetInt32(out var result)
            ? result
            : 0;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string name)
        => DateTimeOffset.TryParse(GetString(element, name), out var value) ? value : null;

    private static byte[] ParseHex(string value)
    {
        if (value.Length == 0 || value.Length % 2 != 0)
            throw new FormatException("Hexadecimal input must contain complete bytes.");

        var bytes = new byte[value.Length / 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            var high = ParseHexDigit(value[index * 2]);
            var low = ParseHexDigit(value[index * 2 + 1]);
            bytes[index] = (byte)((high << 4) | low);
        }
        return bytes;
    }

    private static int ParseHexDigit(char value)
    {
        if (value >= '0' && value <= '9')
            return value - '0';
        if (value >= 'a' && value <= 'f')
            return value - 'a' + 10;
        if (value >= 'A' && value <= 'F')
            return value - 'A' + 10;
        throw new FormatException("Hexadecimal input contains an invalid character.");
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        var difference = left.Length ^ right.Length;
        var length = Math.Max(left.Length, right.Length);
        for (var index = 0; index < length; index++)
        {
            var leftValue = index < left.Length ? left[index] : 0;
            var rightValue = index < right.Length ? right[index] : 0;
            difference |= leftValue ^ rightValue;
        }
        return difference == 0;
    }
}
