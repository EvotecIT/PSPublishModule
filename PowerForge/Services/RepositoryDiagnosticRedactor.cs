using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Sanitizes repository diagnostics before they enter logs, reports, or exceptions.</summary>
internal sealed class RepositoryDiagnosticRedactor
{
    private readonly string[] _secrets;
    private static readonly Regex Url = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal RepositoryDiagnosticRedactor(params string?[] secrets)
        => _secrets = secrets.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).Distinct().OrderByDescending(s => s.Length).ToArray();

    internal string Text(string? value)
    {
        var text = value ?? string.Empty;
        foreach (var secret in _secrets)
        {
            text = text.Replace(secret, "<redacted>");
            text = text.Replace(Uri.EscapeDataString(secret), "<redacted>");
        }
        text = Url.Replace(text, match => Endpoint(match.Value));
        foreach (var profile in new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable("USERPROFILE"), Environment.GetEnvironmentVariable("HOME") })
            if (!string.IsNullOrEmpty(profile) && profile!.Length > 3)
                text = text.Replace(profile, "<user-profile>");
        return text;
    }

    internal static string Endpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return "<non-http endpoint>";
        var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty, Query = string.Empty, Fragment = string.Empty };
        return builder.Uri.AbsoluteUri;
    }
}
