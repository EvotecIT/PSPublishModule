using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Reads whole local source files or uniquely named C#/PowerShell regions for documentation.</summary>
public static class SourceCodeReader
{
    private static readonly Regex Region = new(@"^\s*#(?<kind>region|endregion)\b\s*(?<name>.*)$", RegexOptions.CultureInvariant);
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Reads UTF-8 source, removes region directives from selected regions and normalizes common indentation.</summary>
    /// <param name="rootPath">Allowed repository root.</param>
    /// <param name="sourcePath">Root-relative local file. Links and paths outside the root are rejected.</param>
    /// <param name="region">Exact region name, or null to read the entire file.</param>
    /// <returns>Code with LF line endings and no trailing newline.</returns>
    public static string Read(string rootPath, string sourcePath, string? region = null)
    {
        var path = ResolveFile(rootPath, sourcePath);
        var bytes = File.ReadAllBytes(path);
        var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
        var text = Utf8.GetString(bytes, offset, bytes.Length - offset).Replace("\r\n", "\n");
        if (region is null) return Normalize(text.Split('\n'));
        if (string.IsNullOrWhiteSpace(region)) throw new ArgumentException("Region name is empty.", nameof(region));

        var depth = 0;
        var selectedDepth = -1;
        var matches = 0;
        var selected = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var directive = Region.Match(line);
            if (directive.Success)
            {
                if (directive.Groups["kind"].Value == "region")
                {
                    depth++;
                    if (directive.Groups["name"].Value.Trim() == region)
                    {
                        matches++;
                        selectedDepth = depth;
                    }
                }
                else
                {
                    if (depth == 0) throw new InvalidDataException($"Unmatched #endregion in {sourcePath}.");
                    if (depth == selectedDepth) selectedDepth = -1;
                    depth--;
                }
                continue;
            }
            if (selectedDepth >= 0) selected.Add(line);
        }
        if (depth != 0) throw new InvalidDataException($"Unclosed #region in {sourcePath}.");
        if (matches != 1) throw new InvalidDataException($"Expected one region '{region}' in {sourcePath}; found {matches}.");
        return Normalize(selected);
    }

    internal static string ResolveFile(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("An explicit root-relative file path is required: " + relativePath);
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("File is outside the documentation root: " + relativePath);
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked documentation paths are not supported: " + current);
            if (string.Equals(current, root, comparison)) break;
        }
        if (!File.Exists(path)) throw new FileNotFoundException("Documentation source file does not exist.", path);
        return path;
    }

    private static string Normalize(IEnumerable<string> source)
    {
        var lines = source.ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1])) lines.RemoveAt(lines.Count - 1);
        var nonblank = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        var prefix = nonblank.Length == 0 ? string.Empty : new string(nonblank[0].TakeWhile(char.IsWhiteSpace).ToArray());
        foreach (var line in nonblank)
            while (!line.StartsWith(prefix, StringComparison.Ordinal)) prefix = prefix.Substring(0, prefix.Length - 1);
        return string.Join("\n", lines.Select(line => string.IsNullOrWhiteSpace(line) ? string.Empty : line.Substring(prefix.Length)));
    }
}
