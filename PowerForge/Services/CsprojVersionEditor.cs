using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml;

namespace PowerForge;

/// <summary>
/// Reads and updates version values inside SDK-style or legacy csproj files.
/// </summary>
internal static class CsprojVersionEditor
{
    private static readonly string[] PackageVersionTags =
    {
        "Version",
        "PackageVersion"
    };

    private static readonly string[] FullVersionTags =
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

    private static readonly string[] ReadVersionTags =
    {
        "PackageVersion",
        "Version",
        "VersionPrefix",
        "AssemblyVersion",
        "FileVersion",
        "InformationalVersion"
    };

    internal static bool TryGetVersion(string csprojPath, out string version)
        => TryReadDeclaredVersion(csprojPath, ReadVersionTags, out version);

    // Source checks for package identity must not fall back to assembly metadata.
    internal static bool TryGetPackageVersion(string csprojPath, out string version)
        => TryReadDeclaredVersion(csprojPath, new[] { "PackageVersion", "Version", "VersionPrefix" }, out version);

    private static bool TryReadDeclaredVersion(string csprojPath, string[] tags, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(csprojPath) || !File.Exists(csprojPath))
            return false;

        try
        {
            var content = File.ReadAllText(csprojPath);
            foreach (var tag in tags)
            {
                if (TryMatchVersionTag(content, tag, out var v))
                {
                    version = tag.Equals("VersionPrefix", StringComparison.Ordinal)
                        && TryMatchVersionTag(content, "VersionSuffix", out var suffix)
                        ? v + "-" + suffix
                        : v;
                    return true;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (XmlException) { }

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
        var hasPackageVersionTag = PackageVersionTags.Any(tag => FindValues(content, tag).Any(IsUnconditional));
        var hasVersionPrefix = FindValues(content, "VersionPrefix").Any(IsUnconditional);
        hadVersionTag = ReadVersionTags.Any(tag => FindValues(content, tag).Length != 0) ||
            FindValues(content, "VersionSuffix", includeEmpty: true).Length != 0;

        var updated = content;
        foreach (var tag in FullVersionTags)
        {
            updated = ReplaceValues(updated, tag, escapedVersion);
        }
        foreach (var tag in NumericVersionTags)
        {
            updated = ReplaceValues(updated, tag, escapedNumericVersion);
        }

        if (hasVersionPrefix)
        {
            var suffixes = FindValues(updated, "VersionSuffix", includeEmpty: true);
            var hasUnconditionalSuffix = suffixes.Any(IsUnconditional);
            updated = ReplaceValues(updated, "VersionSuffix", escapedPrereleaseVersion, includeEmpty: true);
            // Conditional definitions remain conditional. Add the default value
            // needed by configurations outside those branches, including stable
            // releases that must clear an inherited prerelease suffix.
            if (!hasUnconditionalSuffix && (suffixes.Length != 0 || !string.IsNullOrEmpty(prereleaseVersion)))
                updated = InsertAfterVersionPrefix(updated, "VersionSuffix", escapedPrereleaseVersion);
        }
        else if (FindValues(updated, "VersionSuffix", includeEmpty: true).Length != 0)
        {
            updated = ReplaceValues(updated, "VersionSuffix", string.Empty, includeEmpty: true);
        }

        if (!hasPackageVersionTag && !hasVersionPrefix)
            updated = InsertVersion(updated, string.IsNullOrEmpty(prereleaseVersion) ? "VersionPrefix" : "Version", string.IsNullOrEmpty(prereleaseVersion) ? escapedNumericVersion : escapedVersion);

        return updated;
    }

    private static bool TryMatchVersionTag(string content, string tag, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrEmpty(content)) return false;
        var element = FindValues(content, tag).FirstOrDefault(IsUnconditional);
        if (element.Element is null) return false;
        version = element.Element.Value.Trim();
        return !string.IsNullOrWhiteSpace(version);
    }

    private static MsBuildProjectXml.ElementSpan[] FindValues(string content, string tag, bool includeEmpty = false)
        => MsBuildProjectXml.FindProperties(content, tag).Where(span => !span.Element.HasElements &&
            (includeEmpty || !string.IsNullOrWhiteSpace(span.Element.Value))).ToArray();

    private static bool IsUnconditional(MsBuildProjectXml.ElementSpan span) =>
        span.Element.Parent?.Parent == span.Element.Document?.Root && !span.Element.AncestorsAndSelf().Any(element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName.Equals("Condition", StringComparison.OrdinalIgnoreCase)));

    private static string ReplaceValues(string content, string tag, string value, bool includeEmpty = false)
    {
        var result = new StringBuilder(content.Length);
        var offset = 0;
        foreach (var span in FindValues(content, tag, includeEmpty))
        {
            result.Append(content, offset, span.Index - offset);
            if (span.IsEmpty)
                result.Append(content, span.Index, span.Length - 2).Append('>').Append(value).Append("</").Append(span.QualifiedName).Append('>');
            else
                result.Append(content, span.Index, span.OpeningEnd - span.Index).Append(value)
                    .Append(content, span.ClosingStart, span.Index + span.Length - span.ClosingStart);
            offset = span.Index + span.Length;
        }
        return result.Append(content, offset, content.Length - offset).ToString();
    }

    private static string InsertVersion(string content, string tag, string escapedVersion)
    {
        var group = MsBuildProjectXml.FindPropertyGroups(content).FirstOrDefault(span =>
            span.Element.Parent == span.Element.Document?.Root && !span.Element.Attributes().Any(attribute =>
                attribute.Name.LocalName.Equals("Condition", StringComparison.OrdinalIgnoreCase)));
        var lineBreak = DetectLineBreak(content);
        if (group.Element is null)
        {
            var root = MsBuildProjectXml.FindRoot(content);
            var insert = $"{lineBreak}  <PropertyGroup>{lineBreak}    <{tag}>{escapedVersion}</{tag}>{lineBreak}  </PropertyGroup>{lineBreak}";
            return root.IsEmpty ? ExpandEmptyElement(content, root, insert) : content.Insert(root.ClosingStart, insert);
        }

        var indent = DetectIndentation(content, group.Index);
        var property = $"{lineBreak}{indent}  <{tag}>{escapedVersion}</{tag}>";
        return group.IsEmpty ? ExpandEmptyElement(content, group, property + lineBreak + indent) : content.Insert(group.OpeningEnd, property);
    }

    private static string ExpandEmptyElement(string content, MsBuildProjectXml.ElementSpan span, string value)
        => content.Substring(0, span.Index + span.Length - 2) + ">" + value + "</" + span.QualifiedName + ">" +
            content.Substring(span.Index + span.Length);

    private static string InsertAfterVersionPrefix(string content, string tag, string escapedVersion)
    {
        var prefix = FindValues(content, "VersionPrefix").FirstOrDefault(IsUnconditional);
        if (prefix.Element is null)
            return content;

        var lineBreak = DetectLineBreak(content);
        var indent = DetectIndentation(content, prefix.Index);
        return content.Insert(prefix.Index + prefix.Length, $"{lineBreak}{indent}<{tag}>{escapedVersion}</{tag}>");
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
