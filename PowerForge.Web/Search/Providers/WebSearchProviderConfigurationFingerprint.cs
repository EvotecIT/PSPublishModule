namespace PowerForge.Web;

/// <summary>Creates a deterministic non-secret identity for provider configuration.</summary>
internal static class WebSearchProviderConfigurationFingerprint
{
    /// <summary>Computes a SHA-256 identity after ordering fleet sites, providers, capabilities and settings.</summary>
    /// <param name="configuration">Provider configuration to fingerprint.</param>
    /// <returns>Lowercase SHA-256 identity prefixed with <c>sha256:</c>.</returns>
    internal static string Compute(WebSearchProviderConfiguration configuration)
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
                    values.Add(WebSearchProviderSecretPolicy.CanFingerprintSettingValue(provider?.Kind, setting.Key)
                        ? WebSearchProviderSecretPolicy.NormalizeFingerprintSettingValue(
                            provider?.Kind,
                            setting.Key,
                            setting.Value)
                        : "[redacted-setting]");
                }
            }
        }

        return "sha256:" + WebSearchIdentityHasher.Compute(values.ToArray());
    }

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    internal static string NormalizeUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
            return Normalize(value);
        try
        {
            return new UriBuilder(uri) { Host = NormalizeDnsHost(uri.IdnHost) }.Uri.AbsoluteUri;
        }
        catch (UriFormatException)
        {
            return uri.AbsoluteUri;
        }
    }

    /// <summary>Normalizes equivalent Unicode, punycode, and DNS root-dot host spellings.</summary>
    /// <param name="host">DNS host or domain name.</param>
    /// <returns>Lowercase ASCII host without the optional terminal DNS root dot.</returns>
    internal static string NormalizeDnsHost(string? host)
    {
        var normalized = host?.Trim() ?? string.Empty;
        if (normalized.EndsWith(".", StringComparison.Ordinal))
            normalized = normalized[..^1];
        try
        {
            return new System.Globalization.IdnMapping().GetAscii(normalized).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return normalized.ToLowerInvariant();
        }
    }
}

internal static class WebSearchProviderSecretPolicy
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> NonSecretSettingNamesByProvider =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
    {
        ["google-search-console"] = new HashSet<string>(["property"], StringComparer.Ordinal),
        ["bing-webmaster"] = new HashSet<string>(["siteUrl"], StringComparer.Ordinal),
        ["bing-webmaster-export"] = new HashSet<string>(["siteUrl"], StringComparer.Ordinal),
        ["cloudflare-analytics"] = new HashSet<string>(["zoneId"], StringComparer.Ordinal)
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

    internal static bool CanFingerprintSettingValue(string? providerKind, string? name) =>
        NonSecretSettingNamesByProvider.TryGetValue(providerKind?.Trim().ToLowerInvariant() ?? string.Empty, out var names) &&
        names.Contains(name ?? string.Empty);

    internal static string? NormalizeFingerprintSettingValue(string? providerKind, string? name, string? value)
    {
        var normalizedKind = providerKind?.Trim().ToLowerInvariant() ?? string.Empty;
        var trimmedValue = value?.Trim();
        if (trimmedValue is null)
            return null;

        if (normalizedKind == "google-search-console" && name == "property" &&
            trimmedValue.StartsWith("sc-domain:", StringComparison.OrdinalIgnoreCase))
        {
            return "sc-domain:" + NormalizeDomain(trimmedValue["sc-domain:".Length..]);
        }

        if ((normalizedKind == "google-search-console" && name == "property") ||
            (normalizedKind is "bing-webmaster" or "bing-webmaster-export" && name == "siteUrl"))
        {
            return Uri.TryCreate(trimmedValue, UriKind.Absolute, out _)
                ? WebSearchProviderConfigurationFingerprint.NormalizeUrl(trimmedValue)
                : trimmedValue;
        }

        return normalizedKind == "cloudflare-analytics" && name == "zoneId"
            ? trimmedValue.ToLowerInvariant()
            : trimmedValue;
    }

    private static string NormalizeSettingName(string? name) => new(
        (name ?? string.Empty)
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant)
        .ToArray());

    private static string NormalizeDomain(string domain) =>
        WebSearchProviderConfigurationFingerprint.NormalizeDnsHost(domain);
}
