using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static readonly Regex AuthorizedConfigurationFileName = new(
        @"^\.release\.authorized\.\d+\.\d+\.\d+\.[0-9a-f]{40}\.json$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static void BindGeneratedConfigurationInput(
        DotNetPublishPlan plan,
        PowerForgeReleaseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EffectiveConfigurationPath))
            return;

        string effectiveConfigPath = Path.GetFullPath(request.EffectiveConfigurationPath!.Trim().Trim('"'));
        if (!AuthorizedConfigurationFileName.IsMatch(Path.GetFileName(effectiveConfigPath)))
        {
            throw new InvalidOperationException(
                "Generated configuration attestation is accepted only for the deterministic " +
                ".release.authorized.<version>.<commit>.json wrapper output.");
        }

        string sourceConfigDirectory = Path.GetDirectoryName(Path.GetFullPath(request.ConfigPath))
            ?? Directory.GetCurrentDirectory();
        GitCommandResult gitRootResult = GitClient.CreateTrustedSystemClient()
            .ShowTopLevelAsync(sourceConfigDirectory, request.CancellationToken)
            .GetAwaiter()
            .GetResult();
        if (!gitRootResult.Succeeded || string.IsNullOrWhiteSpace(gitRootResult.StdOut))
        {
            throw new InvalidOperationException(
                "Generated authorized configuration requires a source configuration inside a Git checkout.");
        }

        string gitRoot = Path.GetFullPath(gitRootResult.StdOut.Trim());
        if (PathIsWithin(effectiveConfigPath, gitRoot))
        {
            throw new InvalidOperationException(
                "Generated authorized configuration must be stored outside the release checkout.");
        }

        if (!File.Exists(effectiveConfigPath) ||
            string.IsNullOrWhiteSpace(request.LoadedConfigurationSha256) ||
            !string.Equals(
                AppleNotarizationService.ComputeFileSha256(effectiveConfigPath),
                request.LoadedConfigurationSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Generated configuration attestation does not match the exact configuration loaded by the release engine.");
        }

        plan.ConfigurationInputPaths = plan.ConfigurationInputPaths
            .Concat(new[] { effectiveConfigPath })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        plan.GeneratedConfigurationInputPaths = new[] { effectiveConfigPath };
        plan.GeneratedProvenancePaths = request.GeneratedProvenancePaths.ToArray();
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

    private static bool PathIsWithin(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
