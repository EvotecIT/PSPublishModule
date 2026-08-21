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

        var resolved = new List<ModuleBinaryPayload>(parsed.Length);
        foreach (var group in parsed.GroupBy(static payload => payload.Kind))
        {
            var ordered = group
                .OrderBy(static payload => payload.Version)
                .ThenBy(static payload => payload.Framework, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (group.Key == ModuleBinaryPayloadKind.Core &&
                ordered.Length > 1 &&
                ordered.Any(static payload => HasPlatformQualifier(payload.Framework)))
            {
                throw new InvalidOperationException(
                    $"Side-by-side platform-qualified Core target frameworks are not supported because runtime platform selection is undefined: {string.Join(", ", ordered.Select(static payload => payload.Framework))}. " +
                    "Use portable Core target frameworks or package a single platform-qualified Core target framework.");
            }
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

        foreach (var kind in new[] { ModuleBinaryPayloadKind.Standard, ModuleBinaryPayloadKind.Core, ModuleBinaryPayloadKind.Default })
        {
            var baseName = GetBaseFolderName(kind);
            var matches = directories
                .Where(item => item.Name!.Equals(baseName, StringComparison.OrdinalIgnoreCase) ||
                               item.Name.StartsWith(baseName + "-", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Name!.Equals(baseName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static item => item.Path)
                .ToArray();
            if (matches.Length > 0)
                return matches;
        }

        return Array.Empty<string>();
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

        var baseFolder = isCore
            ? hasStandard ? "Standard" : hasCore ? "Core" : string.Empty
            : hasDefault ? "Default" : hasStandard ? "Standard" : string.Empty;

        if (string.IsNullOrWhiteSpace(baseFolder))
            return string.Empty;
        if (!isCore || !baseFolder.Equals("Core", StringComparison.OrdinalIgnoreCase))
            return baseFolder;

        var hostVersion = runtimeVersion ?? Environment.Version;
        var selected = baseFolder;
        var selectedVersion = new Version(0, 0);
        foreach (var folder in folders)
        {
            const string prefix = "Core-";
            if (!folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var framework = folder.Substring(prefix.Length);
            if (!TryParseModernFrameworkVersion(framework, out var candidateVersion) || candidateVersion > hostVersion)
                continue;
            if (candidateVersion <= selectedVersion)
                continue;

            selected = folder;
            selectedVersion = candidateVersion;
        }

        return selected;
    }

    internal static string BuildPowerShellRuntimeSelector(IEnumerable<string>? frameworks)
    {
        var candidates = ResolveBuildPayloads(frameworks)
            .Where(static payload => payload.Kind == ModuleBinaryPayloadKind.Core &&
                                     !payload.FolderName.Equals("Core", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static payload => payload.Version)
            .ToArray();
        if (candidates.Length == 0)
            return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine("if ($PSEdition -eq 'Core' -and $Framework -eq 'Core') {");
        builder.AppendLine("    $PowerForgeRuntimeVersion = [Environment]::Version");
        foreach (var candidate in candidates)
        {
            builder.Append("    if ($Framework -eq 'Core' -and $PowerForgeRuntimeVersion -ge [Version]'")
                .Append(candidate.Version.Major)
                .Append('.')
                .Append(candidate.Version.Minor)
                .Append("' -and (Test-Path -LiteralPath ([IO.Path]::Combine($LibRoot, '")
                .Append(EscapePowerShellSingleQuoted(candidate.FolderName))
                .AppendLine("')))) {");
            builder.Append("        $Framework = '")
                .Append(EscapePowerShellSingleQuoted(candidate.FolderName))
                .AppendLine("'");
            builder.AppendLine("    }");
        }
        builder.AppendLine("}");
        return builder.ToString();
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

    private static bool HasPlatformQualifier(string framework)
        => !string.IsNullOrWhiteSpace(framework) && framework.IndexOf('-') >= 0;

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

    private static string EscapePowerShellSingleQuoted(string value)
        => value.Replace("'", "''");
}
