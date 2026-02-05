using System;
using System.IO;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Reads and updates version values inside SDK-style or legacy csproj files.
/// </summary>
internal static class CsprojVersionEditor
{
    private static readonly string[] VersionTags =
    {
        "Version",
        "VersionPrefix",
        "PackageVersion",
        "AssemblyVersion",
        "FileVersion",
        "InformationalVersion"
    };

    internal static bool TryGetVersion(string csprojPath, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(csprojPath) || !File.Exists(csprojPath))
            return false;

        try
        {
            var content = File.ReadAllText(csprojPath);
            foreach (var tag in VersionTags)
            {
                if (TryMatchVersionTag(content, tag, out var v))
                {
                    version = v;
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    internal static string UpdateVersionText(string content, string version, out bool hadVersionTag)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        hadVersionTag = false;

        foreach (var tag in VersionTags)
        {
            if (Regex.IsMatch(content, $"<{Regex.Escape(tag)}>[\\d\\.]+</{Regex.Escape(tag)}>", RegexOptions.IgnoreCase))
                hadVersionTag = true;
        }

        var updated = content;
        foreach (var tag in VersionTags)
        {
            updated = Regex.Replace(
                updated,
                $"<{Regex.Escape(tag)}>[\\d\\.]+</{Regex.Escape(tag)}>",
                $"<{tag}>{version}</{tag}>",
                RegexOptions.IgnoreCase);
        }

        if (!hadVersionTag)
            updated = InsertVersionPrefix(updated, version);

        return updated;
    }

    private static bool TryMatchVersionTag(string content, string tag, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrEmpty(content)) return false;
        var re = new Regex("<" + Regex.Escape(tag) + ">([\\d\\.]+)</" + Regex.Escape(tag) + ">", RegexOptions.IgnoreCase);
        var m = re.Match(content);
        if (!m.Success) return false;
        version = m.Groups[1].Value;
        return !string.IsNullOrWhiteSpace(version);
    }

    private static string InsertVersionPrefix(string content, string version)
    {
        var match = Regex.Match(content, "<PropertyGroup[^>]*>", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return content + Environment.NewLine + $"  <PropertyGroup>{Environment.NewLine}    <VersionPrefix>{version}</VersionPrefix>{Environment.NewLine}  </PropertyGroup>{Environment.NewLine}";
        }

        var insertAt = match.Index + match.Length;
        var lineBreak = DetectLineBreak(content);
        var indent = DetectIndentation(content, match.Index);
        var insert = $"{lineBreak}{indent}  <VersionPrefix>{version}</VersionPrefix>";
        return content.Insert(insertAt, insert);
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
