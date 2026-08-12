using System.Text.Json;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    internal static bool ShouldPublishVirusTotalMonitorFromCheckpoint(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseResult result,
        bool modulePublisherActive = false)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (result is null)
            throw new ArgumentNullException(nameof(result));
        if (spec.VirusTotal is not { Enabled: true })
            return false;

        var hasFinalAssets = result.ReleaseAssetEntries?.Any(static entry => entry.IsFinalPackageOutput) == true;
        var modulePublishing = modulePublisherActive ||
                               result.ModulePlan?.RunMode == ConfigurationGateMode.Publish ||
                               result.ModulePublication is not null;
        var packagePublishing = hasFinalAssets &&
                                (spec.Packages?.PublishNuget == true || spec.Packages?.PublishGitHub == true);
        var modulePackagePublishing = (result.ModulePackagePlans ?? Array.Empty<PowerForgeModulePackageReleaseCheckpoint>())
            .Any(static checkpoint => checkpoint.PublishNuget || checkpoint.PublishGitHub);
        var toolPublishing = (result.ToolPlan is not null ||
                              result.Tools is not null ||
                              result.DotNetToolPlan is not null ||
                              result.DotNetTools is not null) &&
                             spec.Tools?.GitHub.Publish == true;
        var wingetPublishing = ((result.WingetManifestPaths?.Length ?? 0) > 0 ||
                                (result.WingetManifests?.Length ?? 0) > 0) &&
                               spec.Winget is { Enabled: true } winget &&
                               (winget.Submit || winget.Submission?.Enabled == true);
        var unifiedGitHubPublishing = hasFinalAssets && spec.GitHub?.Publish == true;

        return modulePublishing ||
               packagePublishing ||
               modulePackagePublishing ||
               toolPublishing ||
               wingetPublishing ||
               unifiedGitHubPublishing;
    }

    internal static string? PrepareVirusTotalPublishPreflight(
        PowerForgeReleaseSpec spec,
        string configPath,
        PowerForgeReleaseResult result,
        bool modulePublisherActive = false)
    {
        if (!ShouldPublishVirusTotalMonitorFromCheckpoint(spec, result, modulePublisherActive))
            return null;
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("ConfigPath is required.", nameof(configPath));

        ValidateVirusTotalConfiguration(spec.VirusTotal);
        var fullPath = Path.GetFullPath(configPath.Trim().Trim('"'));
        var configDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var apiKey = ResolveVirusTotalApiKeyForExecution(
            spec.VirusTotal,
            configDirectory,
            planOrValidation: false);
        var project = ResolveVirusTotalProjectName(spec, spec.VirusTotal!, configDirectory);
        using (AcquireVirusTotalReceiptLock(spec.VirusTotal!, configDirectory))
            EnsureVirusTotalReceiptWritable(spec.VirusTotal!, configDirectory, project);
        return apiKey;
    }

    internal static bool ShouldRunToolsForProgress(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseRequest request)
    {
        if (spec.Tools is null || request.ModuleOnly || request.PackagesOnly || request.AppleOnly)
            return false;

        var selectedTargets = NormalizeStrings(request.Targets);
        if (selectedTargets.Length == 0 || spec.AppleApps is null || request.SkipAppleApps || request.ToolsOnly)
            return true;

        var appleMatches = ResolveAppleTargetMatches(spec.AppleApps, selectedTargets);
        if (appleMatches.Length == 0)
            return true;

        string[] toolMatches;
        if (UsesDotNetToolWorkflow(spec.Tools))
        {
            ApplyDotNetPublishProfileOverride(spec.Tools);
            toolMatches = ResolveOptionalDotNetToolTargetMatches(spec.Tools.DotNetPublish, selectedTargets);
            if (toolMatches.Length == 0 &&
                DotNetToolsConfigExists(spec.Tools, request.ConfigPath) &&
                AppleTargetSelectionUsesNameOrScheme(spec.AppleApps, selectedTargets))
            {
                toolMatches = ResolveDotNetToolTargetMatches(
                    LoadDotNetToolsSpec(spec.Tools, request.ConfigPath).Spec,
                    selectedTargets);
            }
        }
        else
        {
            toolMatches = ResolveLegacyToolTargetMatches(spec.Tools, selectedTargets);
        }

        return ShouldRunSectionForTargets(selectedTargets, toolMatches, true, appleMatches);
    }

    private static PowerForgeReleaseAssetEntry[] CollectVirusTotalReleaseAssetEntries(
        PowerForgeReleaseResult result)
    {
        var entries = (result.ReleaseAssetEntries ?? Array.Empty<PowerForgeReleaseAssetEntry>()).ToList();
        var paths = entries
            .Select(static (entry, index) => new { Entry = entry, Index = index })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Entry.Path))
            .GroupBy(static item => Path.GetFullPath(item.Entry.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in (result.ModulePackagePlans ?? Array.Empty<PowerForgeModulePackageReleaseCheckpoint>())
                     .SelectMany(static checkpoint => checkpoint.Release.Projects)
                     .SelectMany(CreatePackageAssetEntries))
        {
            var fullPath = Path.GetFullPath(entry.Path);
            if (!paths.TryGetValue(fullPath, out var existingIndex))
            {
                paths.Add(fullPath, entries.Count);
                entries.Add(entry);
                continue;
            }

            var existing = entries[existingIndex];
            if (!existing.IsFinalPackageOutput && entry.IsFinalPackageOutput)
            {
                entry.StagedPath = existing.StagedPath;
                entry.RelativeStagePath = existing.RelativeStagePath;
                entries[existingIndex] = entry;
            }
        }

        return entries.ToArray();
    }

    private static void ValidateExistingVirusTotalReceiptIdentity(
        string receiptPath,
        string expectedProject)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(receiptPath));
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(document.RootElement, "schemaVersion", out var schema) ||
            schema.ValueKind != JsonValueKind.Number ||
            !schema.TryGetInt32(out var schemaVersion) ||
            !TryGetRequiredString(document.RootElement, "provider", out var provider) ||
            !TryGetRequiredString(document.RootElement, "project", out var project) ||
            !TryGetRequiredString(document.RootElement, "version", out _))
        {
            throw new InvalidDataException(
                "VirusTotal Monitor resume receipt must explicitly contain schemaVersion, provider, project, and version identity fields.");
        }

        if (schemaVersion != 1 ||
            !string.Equals(provider, "VirusTotal Monitor", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "VirusTotal Monitor resume receipt has an unsupported schema or provider.");
        }

        if (!string.Equals(project, expectedProject, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"VirusTotal Monitor resume receipt belongs to project '{project}', not '{expectedProject}'.");
        }

        if (!TryGetPropertyIgnoreCase(document.RootElement, "artifacts", out var artifacts) ||
            artifacts.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "VirusTotal Monitor resume receipt must explicitly contain an artifacts array.");
        }

        var receipt = JsonSerializer.Deserialize<VirusTotalMonitorReceiptDocument>(
            document.RootElement.GetRawText(),
            CreateVirusTotalReceiptSerializerOptions(writeIndented: false))
            ?? throw new InvalidDataException("VirusTotal Monitor resume receipt is empty.");
        _ = ValidateVirusTotalReceiptArtifacts(receipt.Artifacts);
    }

    private static bool TryGetRequiredString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!TryGetPropertyIgnoreCase(root, name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
