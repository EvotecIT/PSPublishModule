using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static readonly Regex AuthorizedConfigurationFileName = new(
        @"^\.release\.authorized\.\d+\.\d+\.\d+\.[0-9a-f]{40}\.json$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static void BindGeneratedConfigurationInput(
        DotNetPublishPlan plan,
        PowerForgeReleaseRequest request,
        string releaseConfigPath)
    {
        if (string.IsNullOrWhiteSpace(request.GeneratedConfigurationInputSha256))
            return;

        string expectedSha256 = request.GeneratedConfigurationInputSha256!.Trim();
        if (!Regex.IsMatch(expectedSha256, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException(
                "GeneratedConfigurationInputSha256 must be an exact 64-character SHA-256 digest.",
                nameof(request));
        }

        string fullReleaseConfigPath = Path.GetFullPath(releaseConfigPath);
        if (!AuthorizedConfigurationFileName.IsMatch(Path.GetFileName(fullReleaseConfigPath)))
        {
            throw new InvalidOperationException(
                "Generated configuration attestation is accepted only for the deterministic " +
                ".release.authorized.<version>.<commit>.json wrapper output.");
        }

        if (string.IsNullOrWhiteSpace(request.LoadedConfigurationSha256) ||
            !string.Equals(request.LoadedConfigurationSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Generated configuration attestation does not match the exact configuration loaded by the release engine.");
        }

        plan.GeneratedConfigurationInputPaths = new[] { fullReleaseConfigPath };
    }

    private static IEnumerable<PowerForgeReleaseAssetEntry> CreateGeneratedConfigurationAssetEntries(
        DotNetPublishPlan? plan)
    {
        foreach (string path in plan?.GeneratedConfigurationInputPaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            yield return new PowerForgeReleaseAssetEntry
            {
                Path = Path.GetFullPath(path),
                Category = PowerForgeReleaseAssetCategory.Metadata,
                Source = "AuthorizedConfiguration"
            };
        }
    }
}
