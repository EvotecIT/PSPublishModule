using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Checks or refreshes explicitly marked Markdown blocks from local source without changing surrounding prose.</summary>
public static class MarkdownCodeSynchronizer
{
    private const string Start = "<!-- powerforge:code";
    private const string End = "<!-- /powerforge:code -->";
    private static readonly Regex Attribute = new("(?<key>source|region|language)=\"(?<value>[^\"\\r\\n]+)\"", RegexOptions.CultureInvariant);
    private static readonly Regex Fence = new(@"^(?<fence>`{3,}|~{3,})(?<info>.*)$", RegexOptions.CultureInvariant);

    /// <summary>Validates all selected files before writing. Check mode reports drift without modifying files.</summary>
    /// <param name="spec">Explicit files and allowed repository root.</param>
    /// <param name="apply">Whether to replace stale blocks after successful validation.</param>
    /// <returns>Checked block counts and root-relative changed files.</returns>
    public static MarkdownCodeSyncResult Synchronize(MarkdownCodeSyncSpec spec, bool apply = false)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (spec.Files is null || spec.Files.Length == 0) throw new ArgumentException("Select at least one Markdown file.");
        var pending = new List<PendingFile>();
        var result = new MarkdownCodeSyncResult { Applied = apply };
        var paths = new HashSet<string>(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var file in spec.Files)
        {
            var path = SourceCodeReader.ResolveFile(spec.RootPath, file);
            if (!paths.Add(path)) throw new ArgumentException("Duplicate Markdown file: " + file);
            if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Expected a Markdown .md file: " + file);
            var bytes = File.ReadAllBytes(path);
            var bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
            var text = SourceCodeReader.Utf8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            var updated = Transform(spec.RootPath, file, text, out var blocks);
            if (blocks == 0) throw new InvalidDataException("Selected document has no source-backed code blocks: " + file);
            result.FileCount++;
            result.BlockCount += blocks;
            if (text != updated)
            {
                var payload = SourceCodeReader.Utf8.GetBytes(updated);
                if (bom) payload = new byte[] { 0xef, 0xbb, 0xbf }.Concat(payload).ToArray();
                pending.Add(new PendingFile(file.Replace('\\', '/'), path, bytes, payload));
            }
        }
        result.ChangedFiles = pending.Select(file => file.RelativePath).ToArray();
        if (apply)
        {
            foreach (var file in pending)
                if (!File.ReadAllBytes(file.Path).SequenceEqual(file.Original))
                    throw new IOException("Markdown changed during synchronization: " + file.RelativePath);
            foreach (var file in pending)
            {
                var temporary = file.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, file.Updated);
                    File.Replace(temporary, file.Path, null);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        return result;
    }

    private static string Transform(string root, string file, string text, out int blocks)
    {
        var lines = text.Split('\n');
        var output = new StringBuilder();
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        string? fence = null;
        blocks = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();
            TrackFence(trimmed, ref fence);
            if (fence is null && trimmed.StartsWith(Start, StringComparison.Ordinal))
            {
                if (line != trimmed || !line.EndsWith(" -->", StringComparison.Ordinal))
                    throw new InvalidDataException($"Invalid code marker in {file}:{i + 1}; markers must occupy an unindented line.");
                var attributes = ParseAttributes(line.Substring(Start.Length, line.Length - Start.Length - 4), file);
                var source = attributes["source"];
                attributes.TryGetValue("region", out var region);
                var code = SourceCodeReader.Read(root, source, region);
                var language = attributes.TryGetValue("language", out var specified) ? specified : InferLanguage(source);
                if (!Regex.IsMatch(language, @"^[A-Za-z0-9_+-]*$")) throw new InvalidDataException("Invalid code language in " + file);
                var last = i + 1;
                string? blockFence = null;
                while (last < lines.Length)
                {
                    var contentLine = lines[last].TrimEnd('\r');
                    if (blockFence is null && contentLine == End) break;
                    if (blockFence is null && contentLine.TrimStart().StartsWith(Start, StringComparison.Ordinal))
                        throw new InvalidDataException("Nested code marker in " + file);
                    TrackFence(contentLine.TrimStart(), ref blockFence);
                    last++;
                }
                if (last == lines.Length) throw new InvalidDataException("Missing closing code marker in " + file);
                var width = Math.Max(3, Regex.Matches(code, "`+").Cast<Match>().Select(match => match.Length + 1).DefaultIfEmpty(3).Max());
                var delimiter = new string('`', width);
                output.Append(lines[i]).Append('\n').Append(delimiter).Append(language).Append(newline)
                    .Append(code.Replace("\n", newline)).Append(newline).Append(delimiter).Append(newline).Append(lines[last]);
                if (last < lines.Length - 1) output.Append('\n');
                i = last;
                blocks++;
                continue;
            }
            if (fence is null && trimmed.StartsWith("<!-- /powerforge:code", StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected closing code marker in " + file);
            output.Append(lines[i]);
            if (i < lines.Length - 1) output.Append('\n');
        }
        return output.ToString();
    }

    private static void TrackFence(string line, ref string? fence)
    {
        var match = Fence.Match(line);
        if (!match.Success) return;
        var marker = match.Groups["fence"].Value;
        if (fence is null) fence = marker;
        else if (marker[0] == fence[0] && marker.Length >= fence.Length && string.IsNullOrWhiteSpace(match.Groups["info"].Value)) fence = null;
    }

    private static Dictionary<string, string> ParseAttributes(string text, string file)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        var position = 0;
        foreach (Match match in Attribute.Matches(text))
        {
            if (!string.IsNullOrWhiteSpace(text.Substring(position, match.Index - position)) || attributes.ContainsKey(match.Groups["key"].Value))
                throw new InvalidDataException("Invalid or duplicate code marker attribute in " + file);
            attributes.Add(match.Groups["key"].Value, match.Groups["value"].Value);
            position = match.Index + match.Length;
        }
        if (!string.IsNullOrWhiteSpace(text.Substring(position)) || !attributes.ContainsKey("source"))
            throw new InvalidDataException("Code marker requires source and optional region/language attributes in " + file);
        return attributes;
    }

    private static string InferLanguage(string source) => Path.GetExtension(source).ToLowerInvariant() switch
    {
        ".cs" => "csharp", ".ps1" or ".psm1" => "powershell", ".json" => "json", ".xml" => "xml", _ => "text"
    };

    private sealed class PendingFile
    {
        internal PendingFile(string relativePath, string path, byte[] original, byte[] updated)
        { RelativePath = relativePath; Path = path; Original = original; Updated = updated; }
        internal string RelativePath { get; }
        internal string Path { get; }
        internal byte[] Original { get; }
        internal byte[] Updated { get; }
    }
}
