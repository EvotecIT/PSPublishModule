using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    internal static WebAgentContentSecurityOptions? BuildAgentContentSecurityOptions(
        string[] args,
        string siteRoot,
        string baseDirectory)
    {
        if (!HasOption(args, "--agent-content-security"))
            return null;

        var files = ReadStrictAgentOptionList(args, "--agent-content-file", "--agent-content-files");
        var catalog = GetStrictAgentOptionValue(args, "--agent-publication-catalog");
        var nuGetOwner = GetStrictAgentOptionValue(args, "--agent-nuget-owner");
        var powerShellGalleryOwner = GetStrictAgentOptionValue(args, "--agent-powershell-gallery-owner");
        var ownerSelectors = ReadStrictAgentOptionList(args, "--agent-require-owner", "--agent-require-owner-verification");
        var registryPackages = ReadStrictAgentOptionList(args, "--agent-registry-package", "--agent-registry-verified-package");
        var trustedDomains = ReadStrictAgentOptionList(args, "--agent-trusted-domain", "--agent-trusted-domains");

        return new WebAgentContentSecurityOptions
        {
            SiteRoot = siteRoot,
            Files = files.Count > 0 ? files.ToArray() : new[] { "llms.txt", "llms-full.txt", "llms.json" },
            PublicationCatalogPath = string.IsNullOrWhiteSpace(catalog)
                ? null
                : ResolvePathRelative(baseDirectory, catalog),
            PublicationCatalogMaxAgeHours = ParseNonNegativeIntOption(
                args, "--agent-publication-catalog-max-age-hours", 0),
            NuGetOwner = nuGetOwner,
            PowerShellGalleryOwner = powerShellGalleryOwner,
            RequireOwnerVerification = ownerSelectors.Count > 0
                ? ownerSelectors.ToArray()
                : BuildDefaultAgentOwnerSelectors(nuGetOwner, powerShellGalleryOwner),
            RegistryVerifiedPackages = registryPackages.ToArray(),
            VerifyPackages = !HasOption(args, "--agent-no-package-verification"),
            VerifyExternalHosts = HasOption(args, "--agent-verify-external-hosts"),
            TrustedDomains = trustedDomains.ToArray(),
            RequestTimeoutSeconds = ParsePositiveIntOption(args, "--agent-request-timeout-seconds", 15),
            MaxArtifactBytes = ParsePositiveLongOption(args, "--agent-max-artifact-bytes", 5 * 1024 * 1024),
            MaxPackageReferences = ParsePositiveIntOption(args, "--agent-max-package-references", 100),
            MaxExternalHosts = ParsePositiveIntOption(args, "--agent-max-external-hosts", 100),
            MaxRegistryResponseBytes = ParsePositiveLongOption(args, "--agent-max-registry-response-bytes", 2 * 1024 * 1024),
            MaxNetworkDurationSeconds = ParsePositiveIntOption(args, "--agent-max-network-duration-seconds", 120),
            CheckPromptInjection = !HasOption(args, "--agent-no-prompt-injection")
        };
    }

    private static int ParseNonNegativeIntOption(string[] args, string name, int fallback)
    {
        var value = GetStrictAgentOptionValue(args, name);
        if (value is null)
            return fallback;
        if (!int.TryParse(value, out var parsed) || parsed < 0)
            throw new ArgumentException($"{name} must be a non-negative integer.");
        return parsed;
    }

    private static int ParsePositiveIntOption(string[] args, string name, int fallback)
    {
        var value = GetStrictAgentOptionValue(args, name);
        if (value is null)
            return fallback;
        if (!int.TryParse(value, out var parsed) || parsed <= 0)
            throw new ArgumentException($"{name} must be a positive integer.");
        return parsed;
    }

    private static long ParsePositiveLongOption(string[] args, string name, long fallback)
    {
        var value = GetStrictAgentOptionValue(args, name);
        if (value is null)
            return fallback;
        if (!long.TryParse(value, out var parsed) || parsed <= 0)
            throw new ArgumentException($"{name} must be a positive integer.");
        return parsed;
    }

    private static string? GetStrictAgentOptionValue(string[] args, string name)
    {
        if (!HasOption(args, name))
            return null;

        var value = TryGetOptionValue(args, name);
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{name} requires a non-empty value.");
        return value.Trim();
    }

    private static List<string> ReadStrictAgentOptionList(string[] args, params string[] names)
    {
        foreach (var name in names)
        {
            for (var index = 0; index < args.Length; index++)
            {
                if (!args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) ||
                    args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"{name} requires a non-empty value.");
                }
            }
        }

        return ReadOptionList(args, names);
    }

    private static string[] BuildDefaultAgentOwnerSelectors(
        string? nuGetOwner,
        string? powerShellGalleryOwner)
    {
        var selectors = new List<string>();
        if (!string.IsNullOrWhiteSpace(nuGetOwner))
            selectors.Add("nuget:*");
        if (!string.IsNullOrWhiteSpace(powerShellGalleryOwner))
            selectors.Add("powershellgallery:*");
        return selectors.ToArray();
    }
}
