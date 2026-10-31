using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

internal static class ModulePipelinePlanningHelpers
{
    internal static ConfigurationFormattingSegment? MergeFormattingSegments(
        ConfigurationFormattingSegment? existing,
        ConfigurationFormattingSegment incoming)
    {
        if (incoming is null) return existing;
        if (existing is null) return incoming;

        existing.Options ??= new FormattingOptions();
        incoming.Options ??= new FormattingOptions();

        existing.Options.UpdateProjectRoot |= incoming.Options.UpdateProjectRoot;

        MergeTarget(existing.Options.Standard, incoming.Options.Standard);
        MergeTarget(existing.Options.Merge, incoming.Options.Merge);

        return existing;

        static void MergeTarget(FormattingTargetOptions dst, FormattingTargetOptions src)
        {
            if (src is null) return;

            if (src.FormatCodePS1 is not null) dst.FormatCodePS1 = src.FormatCodePS1;
            if (src.FormatCodePSM1 is not null) dst.FormatCodePSM1 = src.FormatCodePSM1;
            if (src.FormatCodePSD1 is not null) dst.FormatCodePSD1 = src.FormatCodePSD1;

            if (src.Style?.PSD1 is not null)
            {
                dst.Style ??= new FormattingStyleOptions();
                dst.Style.PSD1 = src.Style.PSD1;
            }
        }
    }

    internal static string? TryResolveCsprojPath(string projectRoot, string moduleName, string? netProjectPath, string? netProjectName)
    {
        var projectName = string.IsNullOrWhiteSpace(netProjectName) ? moduleName : netProjectName!.Trim();

        if (string.IsNullOrWhiteSpace(netProjectPath))
        {
            if (string.IsNullOrWhiteSpace(netProjectName))
                return null;

            return TryResolveConventionalCsprojPath(projectRoot, projectName);
        }

        var rawPath = netProjectPath!.Trim().Trim('"');
        var normalizedPath = rawPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        var basePath = Path.IsPathRooted(normalizedPath)
            ? Path.GetFullPath(normalizedPath)
            : Path.GetFullPath(Path.Combine(projectRoot, normalizedPath));

        if (basePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return basePath;

        return Path.Combine(basePath, projectName + ".csproj");
    }

    /// <summary>
    /// Reads every project framework declaration without evaluating its conditions so a source loader
    /// generated on one OS can also locate development binaries built on another OS.
    /// This is candidate metadata, not the set of frameworks to build on the current host.
    /// </summary>
    internal static string[] TryReadDeclaredTargetFrameworks(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            return Array.Empty<string>();

        try
        {
            return XDocument.Load(projectPath, LoadOptions.None)
                .Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "TargetFramework", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.Name.LocalName, "TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                .SelectMany(e => NormalizeFrameworks(e.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string[] NormalizeFrameworks(string? value)
        => (value ?? string.Empty)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static framework => framework.Trim())
            .Where(static framework => !string.IsNullOrWhiteSpace(framework))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? TryResolveConventionalCsprojPath(string projectRoot, string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(projectName))
            return null;

        var candidates = new[]
        {
            Path.Combine(projectRoot, "Sources", projectName, projectName + ".csproj"),
            Path.Combine(projectRoot, "Sources", projectName + ".csproj"),
            Path.Combine(projectRoot, "src", projectName, projectName + ".csproj"),
            Path.Combine(projectRoot, "src", projectName + ".csproj")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    internal static string[] ResolveInstallRootsFromCompatiblePSEditions(string[] compatiblePSEditions)
    {
        var compatible = compatiblePSEditions ?? Array.Empty<string>();
        if (compatible.Length == 0) return Array.Empty<string>();

        var hasDesktop = compatible.Any(s => string.Equals(s, "Desktop", StringComparison.OrdinalIgnoreCase));
        var hasCore = compatible.Any(s => string.Equals(s, "Core", StringComparison.OrdinalIgnoreCase));

        var roots = new List<string>();
        if (Path.DirectorySeparatorChar == '\\')
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(docs))
                docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!string.IsNullOrWhiteSpace(docs))
            {
                if (hasCore) roots.Add(Path.Combine(docs, "PowerShell", "Modules"));
                if (hasDesktop) roots.Add(Path.Combine(docs, "WindowsPowerShell", "Modules"));
            }
        }
        else
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var dataHome = !string.IsNullOrWhiteSpace(xdgDataHome)
                ? xdgDataHome
                : (!string.IsNullOrWhiteSpace(home)
                    ? Path.Combine(home!, ".local", "share")
                    : null);

            if (!string.IsNullOrWhiteSpace(dataHome))
                roots.Add(Path.Combine(dataHome!, "powershell", "Modules"));
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static bool ShouldSkipManifestDependencyModule(string? moduleName)
    {
        var normalizedModuleName = moduleName?.Trim();
        if (normalizedModuleName is null || normalizedModuleName.Length == 0)
            return true;

        return normalizedModuleName.StartsWith("Microsoft.PowerShell.", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldSkipTransitiveRequiredDependencyModule(string? moduleName)
    {
        var normalizedModuleName = moduleName?.Trim();
        if (normalizedModuleName is null || normalizedModuleName.Length == 0)
            return true;

        if (ShouldSkipManifestDependencyModule(normalizedModuleName))
            return true;

        return normalizedModuleName.Equals("PackageManagement", StringComparison.OrdinalIgnoreCase) ||
               normalizedModuleName.Equals("PowerShellGet", StringComparison.OrdinalIgnoreCase) ||
               normalizedModuleName.Equals("PSReadLine", StringComparison.OrdinalIgnoreCase);
    }
}
