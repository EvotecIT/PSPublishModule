using PowerForge;
using System.Text.RegularExpressions;

namespace PowerForgeStudio.Orchestrator.Host;

/// <summary>Bounds displayed diagnostics and redacts recognized command secrets and URL credentials.
/// Arbitrary secret values written by project code cannot be inferred by this filter.</summary>
public static class StudioOutputSanitizer
{
    private static readonly Regex WebAddress = new(@"\bhttps?:[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Redacts recognized secret arguments and bounds the displayed diagnostic.</summary>
    public static string Sanitize(string? value)
    {
        var safe = DotNetPublishPipelineRunner.RedactCommandLineSecrets(value);
        safe = SanitizeAddressText(safe);
        return safe.Length > 4096 ? safe[..4096] : safe;
    }

    /// <summary>Removes credential-bearing web addresses without truncating serialized checkpoint values.</summary>
    internal static string SanitizeAddressText(string value)
    {
        try { return WebAddress.Replace(value, match => SanitizeAddress(match.Value)); }
        catch (RegexMatchTimeoutException) { return "Address-bearing diagnostic omitted because redaction timed out."; }
    }

    /// <summary>Removes URL credentials and query values before a destination enters release evidence.</summary>
    public static string? SanitizeDestination(string? value)
    {
        if (value is null) return null;
        if (!value.Contains("://", StringComparison.Ordinal) &&
            !value.TrimStart().StartsWith("//", StringComparison.Ordinal) &&
            !value.TrimStart().StartsWith("http:", StringComparison.OrdinalIgnoreCase) &&
            !value.TrimStart().StartsWith("https:", StringComparison.OrdinalIgnoreCase))
            return Sanitize(value);
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return SanitizeAddress(uri);
        return "Invalid publication URL (details omitted)";
    }

    /// <summary>Whether a usable URL carried components that cannot be retained in release history.</summary>
    public static bool DestinationCredentialsOmitted(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query));

    private static string SanitizeAddress(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? SanitizeAddress(uri)
            : "Invalid publication URL (details omitted)";

    private static string SanitizeAddress(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
            return uri.AbsoluteUri;
        return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri;
    }
}
