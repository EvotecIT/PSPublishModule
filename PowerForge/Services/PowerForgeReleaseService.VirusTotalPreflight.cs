using System.Text.Json;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    internal static bool ShouldPublishVirusTotalMonitorFromCheckpoint(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseResult result)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (result is null)
            throw new ArgumentNullException(nameof(result));
        if (spec.VirusTotal is not { Enabled: true })
            return false;

        return result.ReleaseAssetEntries?.Any(static entry => entry.IsFinalPackageOutput) == true ||
               result.ModulePlan is not null ||
               result.ModulePublication is not null ||
               (result.ModulePackagePlans?.Length ?? 0) > 0 ||
               result.Packages is not null ||
               result.ToolPlan is not null ||
               result.Tools is not null ||
               result.DotNetToolPlan is not null ||
               result.DotNetTools is not null ||
               (result.WingetManifestPaths?.Length ?? 0) > 0 ||
               (result.WingetManifests?.Length ?? 0) > 0;
    }

    internal static string? PrepareVirusTotalPublishPreflight(
        PowerForgeReleaseSpec spec,
        string configPath,
        PowerForgeReleaseResult result)
    {
        if (!ShouldPublishVirusTotalMonitorFromCheckpoint(spec, result))
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
        EnsureVirusTotalReceiptWritable(spec.VirusTotal!, configDirectory);
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

    private static void ValidateExistingVirusTotalReceiptIdentity(string receiptPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(receiptPath));
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyIgnoreCase(document.RootElement, "schemaVersion", out var schema) ||
            schema.ValueKind != JsonValueKind.Number ||
            !schema.TryGetInt32(out var schemaVersion) ||
            !TryGetRequiredString(document.RootElement, "provider", out var provider) ||
            !TryGetRequiredString(document.RootElement, "project", out _) ||
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
