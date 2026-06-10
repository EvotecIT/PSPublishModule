using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Reads and updates <c>MARKETING_VERSION</c> and <c>CURRENT_PROJECT_VERSION</c> values in Xcode projects.
/// </summary>
public sealed class XcodeProjectVersionEditor
{
    private static readonly Regex MarketingVersionRegex = CreateAssignmentRegex("MARKETING_VERSION");
    private static readonly Regex BuildNumberRegex = CreateAssignmentRegex("CURRENT_PROJECT_VERSION");

    /// <summary>
    /// Reads version values from an Xcode <c>.xcodeproj</c> directory or <c>project.pbxproj</c> file.
    /// </summary>
    public XcodeProjectVersionInfo Read(string projectPath)
    {
        var pbxprojPath = ResolveProjectFilePath(projectPath);
        var content = File.ReadAllText(pbxprojPath);
        return ReadText(pbxprojPath, content);
    }

    /// <summary>
    /// Updates all version values in an Xcode <c>.xcodeproj</c> directory or <c>project.pbxproj</c> file.
    /// </summary>
    public XcodeProjectVersionUpdateResult Update(
        string projectPath,
        string marketingVersion,
        string? buildNumber = null,
        bool whatIf = false)
    {
        if (string.IsNullOrWhiteSpace(marketingVersion))
            throw new ArgumentException("Marketing version is required.", nameof(marketingVersion));

        var pbxprojPath = ResolveProjectFilePath(projectPath);
        var content = File.ReadAllText(pbxprojPath);
        var before = ReadText(pbxprojPath, content);
        if (before.MarketingVersions.Length == 0)
            throw new InvalidOperationException($"No MARKETING_VERSION entries were found in '{pbxprojPath}'.");
        if (!string.IsNullOrWhiteSpace(buildNumber) && before.BuildNumbers.Length == 0)
            throw new InvalidOperationException($"No CURRENT_PROJECT_VERSION entries were found in '{pbxprojPath}'.");

        var updated = UpdateVersionText(
            content,
            marketingVersion.Trim(),
            string.IsNullOrWhiteSpace(buildNumber) ? null : buildNumber!.Trim(),
            out var changed);

        var after = ReadText(pbxprojPath, updated);
        if (changed && !whatIf)
            File.WriteAllText(pbxprojPath, updated);

        return new XcodeProjectVersionUpdateResult
        {
            ProjectFilePath = pbxprojPath,
            Before = before,
            After = after,
            Changed = changed,
            WhatIf = whatIf
        };
    }

    internal static XcodeProjectVersionInfo ReadText(string projectFilePath, string content)
    {
        if (content is null)
            throw new ArgumentNullException(nameof(content));

        return new XcodeProjectVersionInfo
        {
            ProjectFilePath = projectFilePath,
            MarketingVersions = DistinctAssignmentValues(MarketingVersionRegex, content),
            BuildNumbers = DistinctAssignmentValues(BuildNumberRegex, content)
        };
    }

    internal static string UpdateVersionText(
        string content,
        string marketingVersion,
        string? buildNumber,
        out bool changed)
    {
        if (content is null)
            throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrWhiteSpace(marketingVersion))
            throw new ArgumentException("Marketing version is required.", nameof(marketingVersion));

        var updated = ReplaceAssignment(MarketingVersionRegex, content, marketingVersion.Trim(), out var marketingChanged);
        var buildChanged = false;
        if (!string.IsNullOrWhiteSpace(buildNumber))
            updated = ReplaceAssignment(BuildNumberRegex, updated, buildNumber!.Trim(), out buildChanged);

        changed = marketingChanged || buildChanged;
        return updated;
    }

    private static string ResolveProjectFilePath(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("Xcode project path is required.", nameof(projectPath));

        var fullPath = Path.GetFullPath(projectPath.Trim().Trim('"'));
        if (Directory.Exists(fullPath))
        {
            if (string.Equals(Path.GetExtension(fullPath), ".xcodeproj", StringComparison.OrdinalIgnoreCase))
            {
                var pbxprojPath = Path.Combine(fullPath, "project.pbxproj");
                if (File.Exists(pbxprojPath))
                    return pbxprojPath;
            }

            throw new FileNotFoundException($"Xcode project file was not found under '{fullPath}'.", Path.Combine(fullPath, "project.pbxproj"));
        }

        if (File.Exists(fullPath))
            return fullPath;

        throw new FileNotFoundException($"Xcode project file was not found: {fullPath}", fullPath);
    }

    private static Regex CreateAssignmentRegex(string name)
        => new(
            @"(?m)(?<prefix>\b" + Regex.Escape(name) + @"\s*=\s*)(?<value>[^;]+?)(?<suffix>\s*;)",
            RegexOptions.Compiled);

    private static string[] DistinctAssignmentValues(Regex regex, string content)
        => regex.Matches(content)
            .Cast<Match>()
            .Select(match => Unquote(match.Groups["value"].Value.Trim()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ReplaceAssignment(Regex regex, string content, string value, out bool changed)
    {
        var replacementValue = FormatPbxprojValue(value);
        var localChanged = false;

        var updated = regex.Replace(content, match =>
        {
            if (!localChanged && !string.Equals(Unquote(match.Groups["value"].Value.Trim()), value, StringComparison.Ordinal))
                localChanged = true;

            return match.Groups["prefix"].Value + replacementValue + match.Groups["suffix"].Value;
        });

        changed = localChanged;
        return updated;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            return value.Substring(1, value.Length - 2);

        return value;
    }

    private static string FormatPbxprojValue(string value)
    {
        if (value.IndexOfAny(new[] { ' ', '\t', ';', '"' }) < 0)
            return value;

        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
