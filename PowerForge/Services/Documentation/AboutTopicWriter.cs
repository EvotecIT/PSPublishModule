using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

internal sealed class AboutTopicWriter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public AboutTopicWriteResult Write(string stagingPath, string docsPath)
    {
        if (string.IsNullOrWhiteSpace(stagingPath)) throw new ArgumentException("StagingPath is required.", nameof(stagingPath));
        if (string.IsNullOrWhiteSpace(docsPath)) throw new ArgumentException("DocsPath is required.", nameof(docsPath));

        var fullStaging = Path.GetFullPath(stagingPath.Trim().Trim('"'));
        var fullDocs = Path.GetFullPath(docsPath.Trim().Trim('"'));

        if (!Directory.Exists(fullStaging)) return new AboutTopicWriteResult(Array.Empty<AboutTopicInfo>());

        var aboutFiles = EnumerateAboutTopicFiles(fullStaging)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (aboutFiles.Length == 0) return new AboutTopicWriteResult(Array.Empty<AboutTopicInfo>());

        var aboutOutDir = Path.Combine(fullDocs, "About");
        Directory.CreateDirectory(aboutOutDir);

        var written = new List<AboutTopicInfo>();
        foreach (var file in aboutFiles)
        {
            string content;
            try { content = File.ReadAllText(file); }
            catch { continue; }

            var converted = AboutTopicMarkdown.Convert(Path.GetFileNameWithoutExtension(file), content);
            if (string.IsNullOrWhiteSpace(converted.TopicName) || string.IsNullOrWhiteSpace(converted.Markdown))
                continue;

            var outPath = Path.Combine(aboutOutDir, SanitizeFileName(converted.TopicName) + ".md");
            File.WriteAllText(outPath, converted.Markdown, Utf8NoBom);

            written.Add(new AboutTopicInfo(converted.TopicName, outPath, converted.ShortDescription));
        }

        return new AboutTopicWriteResult(written.ToArray());
    }

    private static IEnumerable<string> EnumerateAboutTopicFiles(string stagingPath)
    {
        IEnumerable<string> SafeEnum(string pattern)
        {
            try { return Directory.EnumerateFiles(stagingPath, pattern, SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }

        foreach (var f in SafeEnum("about_*.help.txt")) yield return f;
        foreach (var f in SafeEnum("about_*.txt")) yield return f;
    }

    private static string SanitizeFileName(string name)
    {
        var n = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(n)) return "about_topic";

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            n = n.Replace(c, '_');
        }

        return n;
    }
}

internal sealed class AboutTopicWriteResult
{
    public AboutTopicInfo[] Topics { get; }

    public AboutTopicWriteResult(AboutTopicInfo[] topics)
        => Topics = topics ?? Array.Empty<AboutTopicInfo>();
}

internal sealed class AboutTopicInfo
{
    public string TopicName { get; }
    public string MarkdownPath { get; }
    public string? ShortDescription { get; }

    public AboutTopicInfo(string topicName, string markdownPath, string? shortDescription)
    {
        TopicName = topicName ?? string.Empty;
        MarkdownPath = markdownPath ?? string.Empty;
        ShortDescription = shortDescription;
    }
}

internal static class AboutTopicMarkdown
{
    public static AboutTopicMarkdownResult Convert(string fileStem, string content)
    {
        var lines = SplitLines(content);
        var sections = ParseSections(lines);

        var topic = ExtractTopicName(sections) ?? NormalizeTopicFromStem(fileStem);
        var shortDesc = ExtractShortDescription(sections);

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"topic: {topic}");
        sb.AppendLine("schema: 1.0.0");
        sb.AppendLine("---");
        sb.AppendLine($"# {topic}");
        sb.AppendLine();

        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section.Title) || section.BodyLines.Count == 0) continue;
            var title = section.Title.Trim();
            if (title.Equals("TOPIC", StringComparison.OrdinalIgnoreCase)) continue;

            sb.AppendLine($"## {ToTitleCase(title)}");
            sb.AppendLine();

            var body = NormalizeBody(section.BodyLines);
            if (body.Count == 0)
            {
                sb.AppendLine();
                continue;
            }

            if (title.Equals("EXAMPLES", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("```text");
                foreach (var l in body) sb.AppendLine(l);
                sb.AppendLine("```");
                sb.AppendLine();
                continue;
            }

            foreach (var paragraph in SplitParagraphs(body))
            {
                sb.AppendLine(paragraph);
                sb.AppendLine();
            }
        }

        return new AboutTopicMarkdownResult(topic, sb.ToString(), shortDesc);
    }

    private static string? ExtractTopicName(List<AboutSection> sections)
    {
        var topicSection = sections.FirstOrDefault(s => s.Title.Equals("TOPIC", StringComparison.OrdinalIgnoreCase));
        if (topicSection is null) return null;
        var first = topicSection.BodyLines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        return string.IsNullOrWhiteSpace(first) ? null : first.Trim();
    }

    private static string? ExtractShortDescription(List<AboutSection> sections)
    {
        var shortSection = sections.FirstOrDefault(s => s.Title.Equals("SHORT DESCRIPTION", StringComparison.OrdinalIgnoreCase));
        if (shortSection is null) return null;
        var lines = NormalizeBody(shortSection.BodyLines);
        var oneLine = string.Join(" ", lines).Trim();
        return string.IsNullOrWhiteSpace(oneLine) ? null : oneLine;
    }

    private static string NormalizeTopicFromStem(string fileStem)
    {
        var s = (fileStem ?? string.Empty).Trim();
        if (s.EndsWith(".help", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(0, s.Length - ".help".Length);
        return string.IsNullOrWhiteSpace(s) ? "about_topic" : s;
    }

    private static List<string> SplitLines(string? text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    private static List<AboutSection> ParseSections(List<string> lines)
    {
        var sections = new List<AboutSection>();

        AboutSection? current = null;
        foreach (var raw in lines)
        {
            var line = raw ?? string.Empty;
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed)) { current?.BodyLines.Add(string.Empty); continue; }

            // Section header: non-indented, mostly uppercase
            var isHeader = !StartsWithIndent(line) && IsAllCapsLike(trimmed);
            if (isHeader)
            {
                current = new AboutSection(trimmed.Trim());
                sections.Add(current);
                continue;
            }

            current ??= new AboutSection("CONTENT");
            if (!sections.Contains(current)) sections.Add(current);

            current.BodyLines.Add(line.Trim());
        }

        return sections;
    }

    private static bool StartsWithIndent(string line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        var c = line[0];
        return c == ' ' || c == '\t';
    }

    private static bool IsAllCapsLike(string text)
    {
        var letters = text.Where(char.IsLetter).ToArray();
        if (letters.Length == 0) return false;
        var upper = letters.Count(char.IsUpper);
        return upper >= letters.Length * 0.9;
    }

    private static List<string> NormalizeBody(List<string> lines)
    {
        var output = new List<string>();
        foreach (var l in lines)
        {
            output.Add((l ?? string.Empty).TrimEnd());
        }

        // Trim leading/trailing empties
        while (output.Count > 0 && string.IsNullOrWhiteSpace(output[0])) output.RemoveAt(0);
        while (output.Count > 0 && string.IsNullOrWhiteSpace(output[output.Count - 1])) output.RemoveAt(output.Count - 1);
        return output;
    }

    private static IEnumerable<string> SplitParagraphs(List<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l))
            {
                var p = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(p)) yield return p;
                sb.Clear();
                continue;
            }

            if (sb.Length > 0) sb.AppendLine();
            sb.Append(l);
        }

        var last = sb.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last)) yield return last;
    }

    private static string ToTitleCase(string upper)
    {
        var words = upper.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w =>
            w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant()));
    }
}

internal sealed class AboutTopicMarkdownResult
{
    public string TopicName { get; }
    public string Markdown { get; }
    public string? ShortDescription { get; }

    public AboutTopicMarkdownResult(string topicName, string markdown, string? shortDescription)
    {
        TopicName = topicName ?? string.Empty;
        Markdown = markdown ?? string.Empty;
        ShortDescription = shortDescription;
    }
}

internal sealed class AboutSection
{
    public string Title { get; }
    public List<string> BodyLines { get; } = new();

    public AboutSection(string title) => Title = title ?? string.Empty;
}
