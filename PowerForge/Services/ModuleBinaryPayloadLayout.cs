using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

internal enum ModuleBinaryPayloadKind
{
    Default,
    Core,
    Standard,
}

internal sealed class ModuleBinaryPayload
{
    internal ModuleBinaryPayload(string framework, ModuleBinaryPayloadKind kind, string folderName, Version version)
    {
        Framework = framework;
        Kind = kind;
        FolderName = folderName;
        Version = version;
    }

    internal string Framework { get; }
    internal ModuleBinaryPayloadKind Kind { get; }
    internal string FolderName { get; }
    internal Version Version { get; }
}

internal static class ModuleBinaryPayloadLayout
{
    internal const string TargetFrameworkMarkerFileName = "PowerForge.TargetFramework.txt";

    internal static bool IsCoreFramework(string? framework)
    {
        if (framework is null)
            return false;
        var normalized = framework.Trim();
        if (normalized.Length == 0)
            return false;

        return Classify(normalized) is ModuleBinaryPayloadKind.Core or ModuleBinaryPayloadKind.Standard;
    }

    internal static ModuleBinaryPayload[] ResolveBuildPayloads(IEnumerable<string>? frameworks)
    {
        var parsed = (frameworks ?? Array.Empty<string>())
            .Where(static framework => !string.IsNullOrWhiteSpace(framework))
            .Select(static framework => Parse(framework.Trim()))
            .GroupBy(static payload => payload.Framework, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        var corePayloads = parsed.Where(static payload => payload.Kind == ModuleBinaryPayloadKind.Core).ToArray();
        if (corePayloads.Any(static payload => HasPlatformQualifier(payload.Framework)) &&
            (corePayloads.Length > 1 || parsed.Any(static payload => payload.Kind == ModuleBinaryPayloadKind.Standard)))
        {
            throw new InvalidOperationException(
                $"Side-by-side platform-qualified Core target frameworks are not supported because runtime platform selection is undefined: {string.Join(", ", parsed.Where(static payload => payload.Kind is ModuleBinaryPayloadKind.Core or ModuleBinaryPayloadKind.Standard).Select(static payload => payload.Framework))}. " +
                "Use portable Core target frameworks or package a single platform-qualified Core target framework without a Standard fallback.");
        }

        var resolved = new List<ModuleBinaryPayload>(parsed.Length);
        foreach (var group in parsed.GroupBy(static payload => payload.Kind))
        {
            var ordered = group
                .OrderBy(static payload => payload.Version)
                .ThenBy(static payload => payload.Framework, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (ordered.Length > 1 && group.Key != ModuleBinaryPayloadKind.Core)
            {
                throw new InvalidOperationException(
                    $"Multiple {GetBaseFolderName(group.Key)} target frameworks are not supported in one module payload: {string.Join(", ", ordered.Select(static payload => payload.Framework))}.");
            }

            for (var index = 0; index < ordered.Length; index++)
            {
                var payload = ordered[index];
                var folderName = index == 0
                    ? GetBaseFolderName(payload.Kind)
                    : GetBaseFolderName(payload.Kind) + "-" + SanitizeFrameworkForFolder(payload.Framework);
                resolved.Add(new ModuleBinaryPayload(payload.Framework, payload.Kind, folderName, payload.Version));
            }
        }

        var byFramework = resolved.ToDictionary(static payload => payload.Framework, StringComparer.OrdinalIgnoreCase);
        return parsed.Select(payload => byFramework[payload.Framework]).ToArray();
    }

    internal static string[] ResolveAssemblyLoadContextTargetDirectories(string libRoot)
    {
        if (!Directory.Exists(libRoot))
            return Array.Empty<string>();

        var directories = Directory.EnumerateDirectories(libRoot)
            .Select(static path => new { Path = path, Name = Path.GetFileName(path) })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray();

        var coreAndStandard = directories
            .Where(static item =>
                item.Name!.Equals("Core", StringComparison.OrdinalIgnoreCase) ||
                item.Name.Equals("Standard", StringComparison.OrdinalIgnoreCase) ||
                IsPortableNamedCorePayload(item.Name))
            .OrderBy(static item => item.Name!.Equals("Core", StringComparison.OrdinalIgnoreCase) ? 0 :
                                    IsPortableNamedCorePayload(item.Name) ? 1 : 2)
            .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Path)
            .ToArray();
        if (coreAndStandard.Length > 0)
            return coreAndStandard;

        return directories
            .Where(static item => item.Name!.Equals("Default", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Path)
            .ToArray();
    }

    internal static string[] ResolveValidationPayloadDirectories(string libRoot, string powerShellEdition)
    {
        if (!Directory.Exists(libRoot))
            return Array.Empty<string>();

        if (string.Equals(powerShellEdition?.Trim(), "Desktop", StringComparison.OrdinalIgnoreCase))
        {
            var selected = ResolveRuntimePayloadFolder(libRoot, "Desktop");
            return string.IsNullOrWhiteSpace(selected)
                ? new[] { libRoot }
                : new[] { Path.Combine(libRoot, selected) };
        }

        var candidates = Directory.EnumerateDirectories(libRoot)
            .Select(static path => new { Path = path, Name = Path.GetFileName(path) ?? string.Empty })
            .Where(static item =>
                item.Name.Equals("Core", StringComparison.OrdinalIgnoreCase) ||
                item.Name.Equals("Standard", StringComparison.OrdinalIgnoreCase) ||
                IsPortableNamedCorePayload(item.Name))
            .OrderBy(static item => item.Name.Equals("Core", StringComparison.OrdinalIgnoreCase) ? 0 :
                                    IsPortableNamedCorePayload(item.Name) ? 1 : 2)
            .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Path)
            .ToArray();
        if (candidates.Length > 0)
            return candidates;

        var fallback = ResolveRuntimePayloadFolder(libRoot, "Core");
        return string.IsNullOrWhiteSpace(fallback)
            ? new[] { libRoot }
            : new[] { Path.Combine(libRoot, fallback) };
    }

    internal static string ResolveRuntimePayloadFolder(string libRoot, string powerShellEdition, Version? runtimeVersion = null)
    {
        if (!Directory.Exists(libRoot))
            return string.Empty;

        var folders = Directory.EnumerateDirectories(libRoot)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .ToArray();
        var folderSet = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);

        var hasStandard = folderSet.Contains("Standard");
        var hasCore = folderSet.Contains("Core");
        var hasDefault = folderSet.Contains("Default");
        var isCore = !string.Equals(powerShellEdition?.Trim(), "Desktop", StringComparison.OrdinalIgnoreCase);

        if (!isCore)
            return hasDefault ? "Default" : hasStandard ? "Standard" : string.Empty;

        var hostVersion = runtimeVersion ?? Environment.Version;
        var selected = string.Empty;
        var selectedVersion = new Version(0, 0);
        if (hasCore &&
            TryReadCoreBaselineVersion(libRoot, out var compatibleBaselineVersion) &&
            compatibleBaselineVersion <= hostVersion)
        {
            selected = "Core";
            selectedVersion = compatibleBaselineVersion;
        }
        foreach (var folder in folders)
        {
            const string prefix = "Core-";
            if (!folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var framework = folder.Substring(prefix.Length);
            if (HasPlatformQualifier(framework) ||
                !TryParseModernFrameworkVersion(framework, out var candidateVersion) ||
                candidateVersion > hostVersion)
                continue;
            if (candidateVersion <= selectedVersion)
                continue;

            selected = folder;
            selectedVersion = candidateVersion;
        }

        if (!string.IsNullOrWhiteSpace(selected))
            return selected;
        if (hasCore && (!hasStandard ||
                        TryReadCoreBaselineVersion(libRoot, out var baselineVersion) && baselineVersion <= hostVersion))
            return "Core";
        return hasStandard ? "Standard" : hasCore ? "Core" : string.Empty;
    }

    /// <summary>
    /// Returns the runtime versions at which <see cref="ResolveRuntimePayloadFolder"/> can select a different
    /// PowerShell 7 payload: the Core baseline and every portable named Core payload, highest first.
    /// </summary>
    internal static Version[] ResolveRuntimeSelectionVersions(string libRoot)
    {
        if (!Directory.Exists(libRoot))
            return Array.Empty<Version>();

        var versions = new List<Version>();
        if (Directory.Exists(Path.Combine(libRoot, "Core")) && TryReadCoreBaselineVersion(libRoot, out var baseline))
            versions.Add(baseline);
        foreach (var folder in Directory.EnumerateDirectories(libRoot).Select(Path.GetFileName))
        {
            if (folder is not null && IsPortableNamedCorePayload(folder) &&
                TryParseModernFrameworkVersion(folder.Substring("Core-".Length), out var version))
                versions.Add(version);
        }

        return versions.Distinct().OrderByDescending(static version => version).ToArray();
    }

    private static ModuleBinaryPayload Parse(string framework)
    {
        var kind = Classify(framework);
        return new ModuleBinaryPayload(framework, kind, framework, ParseFrameworkVersion(framework, kind));
    }

    private static ModuleBinaryPayloadKind Classify(string framework)
    {
        if (framework.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            return ModuleBinaryPayloadKind.Standard;
        if (framework.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase) ||
            TryParseModernFrameworkVersion(framework, out var modernVersion) && modernVersion.Major >= 5)
            return ModuleBinaryPayloadKind.Core;
        return ModuleBinaryPayloadKind.Default;
    }

    private static Version ParseFrameworkVersion(string framework, ModuleBinaryPayloadKind kind)
    {
        if (kind is ModuleBinaryPayloadKind.Core or ModuleBinaryPayloadKind.Standard &&
            TryParseVersionSuffix(framework, kind == ModuleBinaryPayloadKind.Core ? new[] { "netcoreapp", "net" } : new[] { "netstandard" }, out var modern))
            return modern;

        var normalized = framework;
        var platformIndex = normalized.IndexOf('-');
        if (platformIndex >= 0)
            normalized = normalized.Substring(0, platformIndex);
        if (normalized.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(3);
        if (normalized.All(char.IsDigit) && normalized.Length >= 2)
        {
            var parts = normalized.Select(static digit => digit - '0').ToArray();
            return parts.Length >= 3
                ? new Version(parts[0], parts[1], parts[2])
                : new Version(parts[0], parts[1]);
        }

        return new Version(0, 0);
    }

    private static bool TryParseModernFrameworkVersion(string framework, out Version version)
        => TryParseVersionSuffix(framework, new[] { "netcoreapp", "net" }, out version);

    private static bool TryReadCoreBaselineVersion(string libRoot, out Version version)
    {
        version = new Version(0, 0);
        try
        {
            var markerPath = Path.Combine(libRoot, "Core", TargetFrameworkMarkerFileName);
            if (!File.Exists(markerPath))
                return false;
            var framework = File.ReadAllText(markerPath).Trim();
            return !HasPlatformQualifier(framework) && TryParseModernFrameworkVersion(framework, out version);
        }
        catch
        {
            version = new Version(0, 0);
            return false;
        }
    }

    private static bool HasPlatformQualifier(string framework)
        => !string.IsNullOrWhiteSpace(framework) && framework.IndexOf('-') >= 0;

    private static bool IsPortableNamedCorePayload(string folderName)
    {
        const string prefix = "Core-";
        if (string.IsNullOrWhiteSpace(folderName) || !folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var framework = folderName.Substring(prefix.Length);
        return !HasPlatformQualifier(framework) && TryParseModernFrameworkVersion(framework, out _);
    }

    internal static bool IsSelectablePayloadFolderName(string? folderName)
    {
        if (folderName is null || folderName.Trim().Length == 0)
            return false;

        return folderName.Equals("Core", StringComparison.OrdinalIgnoreCase) ||
               folderName.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
               folderName.Equals("Standard", StringComparison.OrdinalIgnoreCase) ||
               IsPortableNamedCorePayload(folderName);
    }

    internal static bool IsPayloadLikeFolderName(string? folderName)
    {
        if (folderName is null || folderName.Trim().Length == 0)
            return false;

        return folderName.Equals("Core", StringComparison.OrdinalIgnoreCase) ||
               folderName.StartsWith("Core-", StringComparison.OrdinalIgnoreCase) ||
               folderName.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
               folderName.StartsWith("Default-", StringComparison.OrdinalIgnoreCase) ||
               folderName.Equals("Standard", StringComparison.OrdinalIgnoreCase) ||
               folderName.StartsWith("Standard-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersionSuffix(string framework, IReadOnlyList<string> prefixes, out Version version)
    {
        version = new Version(0, 0);
        var normalized = framework?.Trim() ?? string.Empty;
        var platformIndex = normalized.IndexOf('-');
        if (platformIndex >= 0)
            normalized = normalized.Substring(0, platformIndex);

        var prefix = prefixes.FirstOrDefault(candidate => normalized.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
        if (prefix is null)
            return false;
        var suffix = normalized.Substring(prefix.Length);
        if (!suffix.Contains('.') || !Version.TryParse(suffix, out var parsed))
            return false;

        version = parsed;
        return true;
    }

    private static string GetBaseFolderName(ModuleBinaryPayloadKind kind)
        => kind switch
        {
            ModuleBinaryPayloadKind.Core => "Core",
            ModuleBinaryPayloadKind.Standard => "Standard",
            _ => "Default",
        };

    private static string SanitizeFrameworkForFolder(string framework)
    {
        var builder = new StringBuilder(framework.Length);
        foreach (var character in framework.Trim())
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? char.ToLowerInvariant(character) : '-');
        return builder.ToString();
    }

}
