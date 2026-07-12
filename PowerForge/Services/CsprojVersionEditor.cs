using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Reads and updates version values inside SDK-style or legacy csproj files.
/// </summary>
internal static class CsprojVersionEditor
{
    private const string VersionValuePattern = @"\s*(?<value>[^<]+?)\s*";

    private static readonly string[] PackageVersionTags =
    {
        "Version",
        "PackageVersion",
        "InformationalVersion"
    };

    private static readonly string[] NumericVersionTags =
    {
        "VersionPrefix",
        "AssemblyVersion",
        "FileVersion"
    };

    private static readonly string[] VersionTags = PackageVersionTags.Concat(NumericVersionTags).Append("VersionSuffix").ToArray();

    internal static bool TryGetVersion(string csprojPath, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(csprojPath) || !File.Exists(csprojPath))
            return false;

        try
        {
            var content = File.ReadAllText(csprojPath);
            foreach (var tag in PackageVersionTags)
            {
                if (TryMatchVersionTag(content, tag, out var v))
                {
                    version = v;
                    return true;
                }
            }

            if (TryMatchVersionTag(content, "VersionPrefix", out var prefix))
            {
                version = TryMatchVersionTag(content, "VersionSuffix", out var suffix)
                    ? prefix + "-" + suffix
                    : prefix;
                return true;
            }

            foreach (var tag in NumericVersionTags.Where(tag => !tag.Equals("VersionPrefix", StringComparison.Ordinal)))
            {
                if (TryMatchVersionTag(content, tag, out var v))
                {
                    version = v;
                    return true;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return false;
    }

    internal static string UpdateVersionText(string content, string version, out bool hadVersionTag)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        hadVersionTag = false;
        var escapedVersion = SecurityElement.Escape(version) ?? string.Empty;
        var escapedNumericVersion = SecurityElement.Escape(PackageVersionUtility.GetNumericVersion(version)) ?? string.Empty;
        var prereleaseVersion = PackageVersionUtility.GetPrereleaseVersion(version);
        var escapedPrereleaseVersion = SecurityElement.Escape(prereleaseVersion) ?? string.Empty;
        var hasPackageVersionTag = PackageVersionTags.Any(tag => Regex.IsMatch(content, BuildVersionTagPattern(tag), RegexOptions.IgnoreCase));
        var hasVersionPrefix = Regex.IsMatch(content, BuildVersionTagPattern("VersionPrefix"), RegexOptions.IgnoreCase);

        foreach (var tag in VersionTags)
        {
            if (Regex.IsMatch(content, BuildVersionTagPattern(tag), RegexOptions.IgnoreCase))
                hadVersionTag = true;
        }

        var updated = content;
        foreach (var tag in PackageVersionTags)
        {
            updated = Regex.Replace(
                updated,
                BuildVersionTagPattern(tag),
                $"<{tag}>{escapedVersion}</{tag}>",
                RegexOptions.IgnoreCase);
        }
        foreach (var tag in NumericVersionTags)
        {
            updated = Regex.Replace(
                updated,
                BuildVersionTagPattern(tag),
                $"<{tag}>{escapedNumericVersion}</{tag}>",
                RegexOptions.IgnoreCase);
        }

        if (hasVersionPrefix && !string.IsNullOrEmpty(prereleaseVersion))
        {
            if (Regex.IsMatch(updated, BuildVersionTagPattern("VersionSuffix"), RegexOptions.IgnoreCase))
            {
                updated = Regex.Replace(
                    updated,
                    BuildVersionTagPattern("VersionSuffix"),
                    $"<VersionSuffix>{escapedPrereleaseVersion}</VersionSuffix>",
                    RegexOptions.IgnoreCase);
            }
            else
            {
                updated = InsertAfterVersionPrefix(updated, "VersionSuffix", escapedPrereleaseVersion);
            }
        }
        else if (Regex.IsMatch(updated, BuildVersionTagPattern("VersionSuffix"), RegexOptions.IgnoreCase))
        {
            updated = Regex.Replace(
                updated,
                BuildVersionTagPattern("VersionSuffix"),
                "<VersionSuffix></VersionSuffix>",
                RegexOptions.IgnoreCase);
        }

        if (!hasPackageVersionTag && !hasVersionPrefix)
            updated = InsertVersion(updated, string.IsNullOrEmpty(prereleaseVersion) ? "VersionPrefix" : "Version", string.IsNullOrEmpty(prereleaseVersion) ? escapedNumericVersion : escapedVersion);

        return updated;
    }

    private static bool TryMatchVersionTag(string content, string tag, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrEmpty(content)) return false;
        var re = new Regex(BuildVersionTagPattern(tag), RegexOptions.IgnoreCase);
        var m = re.Match(content);
        if (!m.Success) return false;
        version = m.Groups["value"].Value.Trim();
        return !string.IsNullOrWhiteSpace(version);
    }

    private static string BuildVersionTagPattern(string tag)
        => $"<{Regex.Escape(tag)}>{VersionValuePattern}</{Regex.Escape(tag)}>";

    private static string InsertVersion(string content, string tag, string escapedVersion)
    {
        var match = Regex.Match(content, "<PropertyGroup[^>]*>", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return content + Environment.NewLine + $"  <PropertyGroup>{Environment.NewLine}    <{tag}>{escapedVersion}</{tag}>{Environment.NewLine}  </PropertyGroup>{Environment.NewLine}";
        }

        var insertAt = match.Index + match.Length;
        var lineBreak = DetectLineBreak(content);
        var indent = DetectIndentation(content, match.Index);
        var insert = $"{lineBreak}{indent}  <{tag}>{escapedVersion}</{tag}>";
        return content.Insert(insertAt, insert);
    }

    private static string InsertAfterVersionPrefix(string content, string tag, string escapedVersion)
    {
        var match = Regex.Match(content, BuildVersionTagPattern("VersionPrefix"), RegexOptions.IgnoreCase);
        if (!match.Success)
            return content;

        var lineBreak = DetectLineBreak(content);
        var lineStart = content.LastIndexOf('\n', Math.Max(0, match.Index - 1));
        var indent = lineStart < 0 ? string.Empty : DetectIndentation(content, match.Index);
        return content.Insert(match.Index + match.Length, $"{lineBreak}{indent}<{tag}>{escapedVersion}</{tag}>");
    }

    private static string DetectLineBreak(string content)
        => content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string DetectIndentation(string content, int anchorIndex)
    {
        if (anchorIndex <= 0 || anchorIndex >= content.Length)
            return string.Empty;

        var lineStart = content.LastIndexOf('\n', Math.Max(0, anchorIndex - 1));
        if (lineStart < 0) return string.Empty;

        var i = lineStart + 1;
        while (i < content.Length && (content[i] == ' ' || content[i] == '\t'))
            i++;

        return content.Substring(lineStart + 1, i - (lineStart + 1));
    }
}
