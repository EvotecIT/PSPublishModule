namespace PowerForge.Web;

/// <summary>Creates a deterministic non-secret identity for provider configuration.</summary>
public static class WebSearchProviderConfigurationFingerprint
{
    /// <summary>Computes a SHA-256 identity after ordering fleet sites, providers, capabilities and settings.</summary>
    /// <param name="configuration">Provider configuration to fingerprint.</param>
    /// <returns>Lowercase SHA-256 identity prefixed with <c>sha256:</c>.</returns>
    public static string Compute(WebSearchProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var values = new List<string?>
        {
            "powerforge.web.search-providers",
            configuration.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        foreach (var site in (configuration.Sites ?? Array.Empty<WebSearchSiteProviderConfiguration>())
                     .OrderBy(site => Normalize(site?.Id), StringComparer.Ordinal)
                     .ThenBy(site => NormalizeUrl(site?.BaseUrl), StringComparer.Ordinal))
        {
            values.Add("site");
            values.Add(Normalize(site?.Id));
            values.Add(NormalizeUrl(site?.BaseUrl));
            foreach (var provider in (site?.Providers ?? Array.Empty<WebSearchProviderRegistration>())
                         .OrderBy(provider => Normalize(provider?.Id), StringComparer.Ordinal)
                         .ThenBy(provider => Normalize(provider?.Kind), StringComparer.Ordinal))
            {
                values.Add("provider");
                values.Add(Normalize(provider?.Id));
                values.Add(Normalize(provider?.Kind));
                values.Add(provider?.Enabled == true ? "enabled" : "disabled");
                foreach (var capability in (provider?.Capabilities ?? Array.Empty<string>())
                             .Select(Normalize)
                             .OrderBy(value => value, StringComparer.Ordinal))
                {
                    values.Add("capability");
                    values.Add(capability);
                }

                values.Add("credential");
                values.Add(Normalize(provider?.Credential?.Kind));
                values.Add(provider?.Credential?.EnvironmentVariable?.Trim());
                foreach (var setting in (provider?.Settings ?? new Dictionary<string, string?>())
                             .OrderBy(setting => Normalize(setting.Key), StringComparer.Ordinal))
                {
                    values.Add("setting");
                    values.Add(Normalize(setting.Key));
                    values.Add(WebSearchProviderSecretPolicy.CanFingerprintSettingValue(setting.Key)
                        ? setting.Value?.Trim()
                        : "[redacted-setting]");
                }
            }
        }

        return "sha256:" + WebSearchIdentityHasher.Compute(values.ToArray());
    }

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string NormalizeUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
            return Normalize(value);
        return uri.AbsoluteUri;
    }
}

internal static class WebSearchProviderSecretPolicy
{
    private static readonly HashSet<string> NonSecretSettingNames = new(StringComparer.Ordinal)
    {
        "property",
        "siteurl",
        "zoneid"
    };

    private static readonly string[] SecretSettingTokens =
    [
        "apikey", "credential", "password", "private", "secret", "token"
    ];

    internal static bool IsSecretSettingName(string? name)
    {
        var normalized = NormalizeSettingName(name);
        return SecretSettingTokens.Any(token => normalized.Contains(token, StringComparison.Ordinal));
    }

    internal static bool CanFingerprintSettingValue(string? name) =>
        NonSecretSettingNames.Contains(NormalizeSettingName(name));

    private static string NormalizeSettingName(string? name) => new(
        (name ?? string.Empty)
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant)
        .ToArray());
}
