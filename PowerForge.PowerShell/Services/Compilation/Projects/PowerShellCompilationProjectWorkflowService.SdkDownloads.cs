using System.Text.Json;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    /// <summary>
    /// Authenticates the actual SDK download selection independently of packages.lock.json.
    /// NuGet records downloads under project.frameworks rather than the libraries map.
    /// </summary>
    private static PowerShellCompilationProjectPackage[] ReadSdkDownloadPackages(
        string assetsPath,
        string packageRoot,
        PowerShellCompilationProjectArtifact artifact)
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        if (!assets.RootElement.TryGetProperty("packageFolders", out var folders) || folders.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"NuGet acquisition for '{artifact.Name}' has no package folder inventory.");
        var selectedFolders = folders.EnumerateObject().ToArray();
        if (selectedFolders.Length != 1 || !PowerShellCompilationPathSafety.PathEquals(selectedFolders[0].Name, packageRoot))
            throw new InvalidDataException($"NuGet acquisition for '{artifact.Name}' selected a package folder outside its isolated environment.");
        if (!assets.RootElement.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object ||
            !project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Object ||
            !frameworks.TryGetProperty(artifact.Target.TargetFramework, out var framework) || framework.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"NuGet acquisition for '{artifact.Name}' has no SDK framework selection for '{artifact.Target.TargetFramework}'.");
        // Targets without SDK download requirements legitimately omit this optional collection.
        if (!framework.TryGetProperty("downloadDependencies", out var downloads))
            return Array.Empty<PowerShellCompilationProjectPackage>();
        if (downloads.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"SDK download dependencies for '{artifact.Name}' are malformed.");

        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var download in downloads.EnumerateArray())
        {
            if (download.ValueKind != JsonValueKind.Object ||
                !download.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                !PowerShellCompilationPackageIdentity.IsValidId(name.GetString()) ||
                !download.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String ||
                !VersionRange.TryParse(version.GetString() ?? string.Empty, out var range) || range.MinVersion is null || range.MaxVersion is null ||
                !range.IsMinInclusive || !range.IsMaxInclusive || !range.MinVersion.Equals(range.MaxVersion))
                throw new InvalidDataException($"SDK download dependency for '{artifact.Name}' must name one valid package at an exact NuGet version.");
            var id = name.GetString()!;
            var exactVersion = range.MinVersion.ToNormalizedString();
            if (selected.TryGetValue(id, out var existing) && !existing.Equals(exactVersion, StringComparison.Ordinal))
                throw new InvalidDataException($"SDK download package '{id}' selects ambiguous versions for '{artifact.Name}'.");
            selected[id] = exactVersion;
        }
        return selected.OrderBy(static package => package.Key, StringComparer.OrdinalIgnoreCase)
            .Select(package => PowerShellCompilationNuGetPackageVerifier.ReadPackage(packageRoot, package.Key, package.Value))
            .ToArray();
    }
}
