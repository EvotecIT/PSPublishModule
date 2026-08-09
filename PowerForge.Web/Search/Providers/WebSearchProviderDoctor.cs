using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Validates fleet provider identities, requested capabilities, settings and credential references.</summary>
public static partial class WebSearchProviderDoctor
{
    private static readonly Regex IdentifierRegex = new(
        "^[a-z0-9][a-z0-9._-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex EnvironmentVariableRegex = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex CloudflareZoneRegex = new(
        "^[A-Fa-f0-9]{32}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly IReadOnlyDictionary<string, ProviderDescriptor> ProviderCatalog =
        new Dictionary<string, ProviderDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["google-search-console"] = new(
                [
                    WebSearchProviderCapabilities.SearchAnalytics,
                    WebSearchProviderCapabilities.SearchSitemaps,
                    WebSearchProviderCapabilities.SearchUrlInspection
                ],
                ["property"],
                ["google-service-account-json", "google-service-account-file"],
                ValidateGoogleSearchConsole),
            ["bing-webmaster"] = new(
                [WebSearchProviderCapabilities.SearchAnalytics, WebSearchProviderCapabilities.SearchSitemaps],
                ["siteUrl"],
                ["bing-api-key"],
                ValidateBingWebmaster),
            ["bing-webmaster-export"] = new(
                [WebSearchProviderCapabilities.SearchAnalytics],
                [],
                [],
                null),
            ["cloudflare-analytics"] = new(
                [WebSearchProviderCapabilities.TrafficAnalytics],
                ["zoneId"],
                ["cloudflare-api-token"],
                ValidateCloudflare),
            ["lighthouse"] = new(
                [WebSearchProviderCapabilities.PerformanceLighthouse],
                [],
                [],
                null),
            ["google-crux"] = new(
                [WebSearchProviderCapabilities.PerformanceCrux],
                [],
                ["google-api-key"],
                null)
        };

    /// <summary>Runs deterministic provider configuration checks without exposing credential values.</summary>
    /// <param name="configuration">Provider configuration to inspect.</param>
    /// <param name="environmentResolver">Optional environment resolver used by tests and alternate hosts.</param>
    /// <param name="availableCollectorKinds">Provider kinds implemented by the current host.</param>
    /// <returns>Fleet capability and readiness report.</returns>
    public static WebSearchProviderDoctorResult Inspect(
        WebSearchProviderConfiguration configuration,
        Func<string, string?>? environmentResolver = null,
        IReadOnlySet<string>? availableCollectorKinds = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        environmentResolver ??= Environment.GetEnvironmentVariable;
        availableCollectorKinds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var checks = new List<WebSearchProviderCheck>();
        var states = new List<WebSearchProviderCapabilityState>();
        if (configuration.SchemaVersion != WebSearchProviderConfiguration.CurrentSchemaVersion)
        {
            AddCheck(
                checks,
                "configuration.schema-version",
                WebSearchProviderCheckSeverity.Error,
                $"Provider configuration schema version {configuration.SchemaVersion} is not supported; expected {WebSearchProviderConfiguration.CurrentSchemaVersion}.");
        }

        var sites = configuration.Sites ?? Array.Empty<WebSearchSiteProviderConfiguration>();
        if (sites.Length == 0)
        {
            AddCheck(
                checks,
                "configuration.sites-empty",
                WebSearchProviderCheckSeverity.Error,
                "Provider configuration must contain at least one site.");
        }

        var siteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in sites)
        {
            var siteId = NormalizeDisplay(site?.Id);
            ValidateSite(site, siteId, siteIds, checks);
            var providers = site?.Providers ?? Array.Empty<WebSearchProviderRegistration>();
            var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var provider in providers)
            {
                var providerId = NormalizeDisplay(provider?.Id);
                var kind = NormalizeDisplay(provider?.Kind).ToLowerInvariant();
                var requestedCapabilities = NormalizeValues(provider?.Capabilities);
                ProviderCatalog.TryGetValue(kind, out var descriptor);

                ValidateProviderIdentity(provider, siteId, providerId, kind, providerIds, checks);
                if (descriptor is null)
                {
                    AddCheck(
                        checks,
                        "provider.kind-unsupported",
                        WebSearchProviderCheckSeverity.Error,
                        $"Provider kind '{SafeLabel(kind)}' is not supported.",
                        siteId,
                        providerId,
                        "Use a provider kind published by the capability doctor schema.");
                }
                else
                {
                    ValidateCapabilities(provider, descriptor, requestedCapabilities, siteId, providerId, checks);
                    ValidateSettings(provider, descriptor, siteId, site?.BaseUrl, providerId, checks);
                    ValidateCredential(provider, descriptor, environmentResolver, siteId, providerId, checks);
                }

                var collectorAvailable = descriptor is not null && availableCollectorKinds.Contains(kind);
                if (provider?.Enabled == true && descriptor is not null && !collectorAvailable)
                {
                    AddCheck(
                        checks,
                        "provider.collector-unavailable",
                        WebSearchProviderCheckSeverity.Warning,
                        $"Provider '{SafeLabel(providerId)}' is configured, but collector kind '{SafeLabel(kind)}' is not available in this executable.",
                        siteId,
                        providerId,
                        "Install or build a PowerForge.Web version that contains this collector.");
                }

                states.Add(new WebSearchProviderCapabilityState
                {
                    SiteId = siteId,
                    ProviderId = providerId,
                    Kind = kind,
                    Enabled = provider?.Enabled == true,
                    ConfigurationReady = false,
                    CollectorAvailable = collectorAvailable,
                    RequestedCapabilities = requestedCapabilities,
                    SupportedCapabilities = descriptor?.Capabilities.ToArray() ?? Array.Empty<string>()
                });
            }
        }

        var orderedChecks = checks
            .OrderByDescending(check => check.Severity)
            .ThenBy(check => check.SiteId, StringComparer.Ordinal)
            .ThenBy(check => check.ProviderId, StringComparer.Ordinal)
            .ThenBy(check => check.Code, StringComparer.Ordinal)
            .ToArray();
        foreach (var state in states)
        {
            state.ConfigurationReady = !orderedChecks.Any(check =>
                check.Severity == WebSearchProviderCheckSeverity.Error &&
                (check.SiteId is null || string.Equals(check.SiteId, state.SiteId, StringComparison.OrdinalIgnoreCase)) &&
                (check.ProviderId is null || string.Equals(check.ProviderId, state.ProviderId, StringComparison.OrdinalIgnoreCase)));
        }
        return new WebSearchProviderDoctorResult
        {
            Success = orderedChecks.All(check => check.Severity != WebSearchProviderCheckSeverity.Error),
            ConfigurationHash = WebSearchProviderConfigurationFingerprint.Compute(configuration),
            SiteCount = sites.Length,
            ProviderCount = states.Count,
            ConfigurationReadyCount = states.Count(state => state.ConfigurationReady),
            CollectorAvailableCount = states.Count(state => state.CollectorAvailable),
            Providers = states
                .OrderBy(state => state.SiteId, StringComparer.Ordinal)
                .ThenBy(state => state.ProviderId, StringComparer.Ordinal)
                .ToArray(),
            Checks = orderedChecks
        };
    }

    private static void ValidateSite(
        WebSearchSiteProviderConfiguration? site,
        string siteId,
        HashSet<string> siteIds,
        List<WebSearchProviderCheck> checks)
    {
        if (!string.Equals(site?.Id, siteId, StringComparison.Ordinal))
        {
            AddCheck(
                checks,
                "site.id-noncanonical",
                WebSearchProviderCheckSeverity.Error,
                "Site id cannot contain surrounding whitespace.",
                siteId);
        }
        if (!IsIdentifier(siteId))
        {
            AddCheck(
                checks,
                "site.id-invalid",
                WebSearchProviderCheckSeverity.Error,
                "Site id must use lowercase letters, digits, dots, underscores or hyphens and start with a letter or digit.",
                siteId);
        }
        else if (!siteIds.Add(siteId))
        {
            AddCheck(
                checks,
                "site.id-duplicate",
                WebSearchProviderCheckSeverity.Error,
                $"Site id '{SafeLabel(siteId)}' is configured more than once.",
                siteId);
        }

        if (!string.Equals(site?.BaseUrl, site?.BaseUrl?.Trim(), StringComparison.Ordinal) ||
            !TryGetHttpUrl(site?.BaseUrl, out _))
        {
            AddCheck(
                checks,
                "site.base-url-invalid",
                WebSearchProviderCheckSeverity.Error,
                "Site baseUrl must be an absolute HTTP(S) URL.",
                siteId);
        }

        if ((site?.Providers ?? Array.Empty<WebSearchProviderRegistration>()).Length == 0)
        {
            AddCheck(
                checks,
                "site.providers-empty",
                WebSearchProviderCheckSeverity.Error,
                "Site must contain at least one provider registration.",
                siteId);
        }
    }

    private static void ValidateProviderIdentity(
        WebSearchProviderRegistration? provider,
        string siteId,
        string providerId,
        string kind,
        HashSet<string> providerIds,
        List<WebSearchProviderCheck> checks)
    {
        if (!string.Equals(provider?.Id, providerId, StringComparison.Ordinal))
        {
            AddCheck(
                checks,
                "provider.id-noncanonical",
                WebSearchProviderCheckSeverity.Error,
                "Provider id cannot contain surrounding whitespace.",
                siteId,
                providerId);
        }
        if (!IsIdentifier(providerId))
        {
            AddCheck(
                checks,
                "provider.id-invalid",
                WebSearchProviderCheckSeverity.Error,
                "Provider id must use lowercase letters, digits, dots, underscores or hyphens and start with a letter or digit.",
                siteId,
                providerId);
        }
        else if (!providerIds.Add(providerId))
        {
            AddCheck(
                checks,
                "provider.id-duplicate",
                WebSearchProviderCheckSeverity.Error,
                $"Provider id '{SafeLabel(providerId)}' is configured more than once for this site.",
                siteId,
                providerId);
        }

        if (!string.Equals(provider?.Kind, kind, StringComparison.Ordinal) || !IsIdentifier(kind))
        {
            AddCheck(
                checks,
                "provider.kind-invalid",
                WebSearchProviderCheckSeverity.Error,
                "Provider kind must be a lowercase stable identifier.",
                siteId,
                providerId);
        }

        if (provider is not null && provider.Capabilities is null)
        {
            AddCheck(
                checks,
                "provider.capabilities-missing",
                WebSearchProviderCheckSeverity.Error,
                "Provider capabilities are required.",
                siteId,
                providerId);
        }
    }

    private static void ValidateCapabilities(
        WebSearchProviderRegistration? provider,
        ProviderDescriptor descriptor,
        string[] requestedCapabilities,
        string siteId,
        string providerId,
        List<WebSearchProviderCheck> checks)
    {
        if (requestedCapabilities.Length == 0)
        {
            AddCheck(
                checks,
                "provider.capabilities-empty",
                WebSearchProviderCheckSeverity.Error,
                "Provider must request at least one capability.",
                siteId,
                providerId);
            return;
        }

        var duplicates = (provider?.Capabilities ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            AddCheck(
                checks,
                "provider.capabilities-duplicate",
                WebSearchProviderCheckSeverity.Error,
                "Provider capabilities contain duplicate values.",
                siteId,
                providerId);
        }

        if ((provider?.Capabilities ?? Array.Empty<string>())
            .Any(value => string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim().ToLowerInvariant(), StringComparison.Ordinal)))
        {
            AddCheck(
                checks,
                "provider.capability-invalid",
                WebSearchProviderCheckSeverity.Error,
                "Provider capabilities must use canonical lowercase identifiers without surrounding whitespace.",
                siteId,
                providerId);
        }

        foreach (var capability in requestedCapabilities)
        {
            if (!descriptor.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
            {
                AddCheck(
                    checks,
                    "provider.capability-unsupported",
                    WebSearchProviderCheckSeverity.Error,
                    $"Capability '{SafeLabel(capability)}' is not supported by provider kind '{SafeLabel(provider?.Kind)}'.",
                    siteId,
                    providerId);
            }
        }
    }

    private static void ValidateSettings(
        WebSearchProviderRegistration? provider,
        ProviderDescriptor descriptor,
        string siteId,
        string? siteBaseUrl,
        string providerId,
        List<WebSearchProviderCheck> checks)
    {
        if (provider is not null && provider.Settings is null)
        {
            AddCheck(
                checks,
                "provider.settings-missing",
                WebSearchProviderCheckSeverity.Error,
                "Provider settings must be an object, even when this provider kind has no settings.",
                siteId,
                providerId);
        }

        var sourceSettings = provider?.Settings ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in sourceSettings)
        {
            var key = setting.Key?.Trim() ?? string.Empty;
            var canonicalKey = descriptor.Settings.FirstOrDefault(candidate =>
                string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase));
            if (canonicalKey is not null && !string.Equals(setting.Key, canonicalKey, StringComparison.Ordinal))
            {
                AddCheck(
                    checks,
                    "provider.setting-key-noncanonical",
                    WebSearchProviderCheckSeverity.Error,
                    $"Setting '{SafeLabel(setting.Key)}' must use the canonical key '{canonicalKey}'.",
                    siteId,
                    providerId);
            }

            if (!settings.TryAdd(key, setting.Value))
            {
                AddCheck(
                    checks,
                    "provider.setting-duplicate",
                    WebSearchProviderCheckSeverity.Error,
                    $"Setting '{SafeLabel(key)}' is configured more than once.",
                    siteId,
                    providerId);
            }
            if (WebSearchProviderSecretPolicy.IsSecretSettingName(key))
            {
                AddCheck(
                    checks,
                    "provider.setting-secret-forbidden",
                    WebSearchProviderCheckSeverity.Error,
                    $"Setting '{SafeLabel(key)}' looks secret and cannot be stored in provider configuration.",
                    siteId,
                    providerId,
                    "Use credential.environmentVariable instead of storing credential material in settings.");
                continue;
            }

            if (!descriptor.Settings.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                AddCheck(
                    checks,
                    "provider.setting-unsupported",
                    WebSearchProviderCheckSeverity.Error,
                    $"Setting '{SafeLabel(key)}' is not supported by this provider kind.",
                    siteId,
                    providerId);
            }
            else if (string.IsNullOrWhiteSpace(setting.Value))
            {
                AddCheck(
                    checks,
                    "provider.setting-empty",
                    WebSearchProviderCheckSeverity.Error,
                    $"Setting '{SafeLabel(key)}' cannot be blank.",
                    siteId,
                    providerId);
            }
            else if (!string.Equals(setting.Value, setting.Value.Trim(), StringComparison.Ordinal))
            {
                AddCheck(
                    checks,
                    "provider.setting-noncanonical",
                    WebSearchProviderCheckSeverity.Error,
                    $"Setting '{SafeLabel(key)}' cannot contain surrounding whitespace.",
                    siteId,
                    providerId);
            }
        }

        foreach (var requiredSetting in descriptor.Settings)
        {
            if (!settings.TryGetValue(requiredSetting, out var value) || string.IsNullOrWhiteSpace(value))
            {
                AddCheck(
                    checks,
                    "provider.setting-required",
                    WebSearchProviderCheckSeverity.Error,
                    $"Provider setting '{requiredSetting}' is required.",
                    siteId,
                    providerId);
            }
        }

        descriptor.SettingValidator?.Invoke(settings, siteId, siteBaseUrl, providerId, checks);
    }

    private static void ValidateCredential(
        WebSearchProviderRegistration? provider,
        ProviderDescriptor descriptor,
        Func<string, string?> environmentResolver,
        string siteId,
        string providerId,
        List<WebSearchProviderCheck> checks)
    {
        if (descriptor.CredentialKinds.Length == 0)
        {
            if (provider?.Credential is not null)
            {
                AddCheck(
                    checks,
                    "provider.credential-unexpected",
                    WebSearchProviderCheckSeverity.Error,
                    "This provider kind does not accept a credential reference.",
                    siteId,
                    providerId);
            }
            return;
        }

        var credential = provider?.Credential;
        if (credential is null)
        {
            AddCheck(
                checks,
                "provider.credential-required",
                WebSearchProviderCheckSeverity.Error,
                "Provider credential reference is required.",
                siteId,
                providerId,
                "Reference a credential through credential.environmentVariable.");
            return;
        }

        var credentialKind = NormalizeDisplay(credential.Kind).ToLowerInvariant();
        if (!string.Equals(credential.Kind, credentialKind, StringComparison.Ordinal))
        {
            AddCheck(
                checks,
                "provider.credential-kind-invalid",
                WebSearchProviderCheckSeverity.Error,
                "Credential kind must use its canonical lowercase identifier without surrounding whitespace.",
                siteId,
                providerId);
        }
        if (!descriptor.CredentialKinds.Contains(credentialKind, StringComparer.OrdinalIgnoreCase))
        {
            AddCheck(
                checks,
                "provider.credential-kind-unsupported",
                WebSearchProviderCheckSeverity.Error,
                $"Credential kind '{SafeLabel(credentialKind)}' is not supported by this provider kind.",
                siteId,
                providerId);
        }

        var variableName = NormalizeDisplay(credential.EnvironmentVariable);
        if (!string.Equals(credential.EnvironmentVariable, variableName, StringComparison.Ordinal) ||
            !EnvironmentVariableRegex.IsMatch(variableName))
        {
            AddCheck(
                checks,
                "provider.credential-environment-invalid",
                WebSearchProviderCheckSeverity.Error,
                "Credential environmentVariable must be a portable environment variable name.",
                siteId,
                providerId);
            return;
        }

        string? resolved;
        try
        {
            resolved = environmentResolver(variableName);
        }
        catch
        {
            resolved = null;
        }

        if (string.IsNullOrWhiteSpace(resolved))
        {
            AddCheck(
                checks,
                "provider.credential-unavailable",
                provider?.Enabled == true ? WebSearchProviderCheckSeverity.Error : WebSearchProviderCheckSeverity.Warning,
                $"Credential environment variable '{SafeLabel(variableName)}' is not available to this process.",
                siteId,
                providerId,
                "Provide the environment variable to the interactive or scheduled collection process.");
        }
    }

    private static bool IsIdentifier(string value) => IdentifierRegex.IsMatch(value);

    private static bool TryGetHttpUrl(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed) ||
            (!parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            !string.IsNullOrEmpty(parsed.UserInfo))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static string[] NormalizeValues(IEnumerable<string>? values) => (values ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim().ToLowerInvariant())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static string NormalizeDisplay(string? value) => value?.Trim() ?? string.Empty;

    private static string SafeLabel(string? value) => string.IsNullOrWhiteSpace(value) ? "(missing)" : value.Trim();

    private static void AddCheck(
        ICollection<WebSearchProviderCheck> checks,
        string code,
        WebSearchProviderCheckSeverity severity,
        string message,
        string? siteId = null,
        string? providerId = null,
        string? remediation = null) => checks.Add(new WebSearchProviderCheck
        {
            Code = code,
            Severity = severity,
            SiteId = siteId,
            ProviderId = providerId,
            Message = message,
            Remediation = remediation
        });

    private sealed record ProviderDescriptor(
        string[] Capabilities,
        string[] Settings,
        string[] CredentialKinds,
        Action<IReadOnlyDictionary<string, string?>, string, string?, string, List<WebSearchProviderCheck>>? SettingValidator);
}
