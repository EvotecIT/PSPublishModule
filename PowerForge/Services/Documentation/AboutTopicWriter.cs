using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

internal sealed class AboutTopicWriter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public AboutTopicWriteResult Write(
        string stagingPath,
        string docsPath,
        IEnumerable<string>? additionalSourcePaths = null)
    {
        if (string.IsNullOrWhiteSpace(stagingPath)) throw new ArgumentException("StagingPath is required.", nameof(stagingPath));
        if (string.IsNullOrWhiteSpace(docsPath)) throw new ArgumentException("DocsPath is required.", nameof(docsPath));

        var fullStaging = Path.GetFullPath(stagingPath.Trim().Trim('"'));
        var fullDocs = Path.GetFullPath(docsPath.Trim().Trim('"'));

        if (!Directory.Exists(fullStaging)) return new AboutTopicWriteResult(Array.Empty<AboutTopicInfo>());

        var roots = ResolveSourceRoots(fullStaging, additionalSourcePaths);
        var aboutFiles = roots
            .SelectMany(EnumerateAboutTopicFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (aboutFiles.Length == 0) return new AboutTopicWriteResult(Array.Empty<AboutTopicInfo>());

        var aboutOutDir = Path.Combine(fullDocs, "About");
        Directory.CreateDirectory(aboutOutDir);

        var selected = new Dictionary<string, AboutTopicCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in aboutFiles)
        {
            AboutTopicMarkdownResult converted;
            try { converted = ConvertSourceFile(file); }
            catch { continue; }
            if (string.IsNullOrWhiteSpace(converted.TopicName) || string.IsNullOrWhiteSpace(converted.Markdown))
                continue;

            var topic = converted.TopicName.Trim();
            var candidate = new AboutTopicCandidate(
                topic,
                converted.Markdown,
                converted.ShortDescription,
                file,
                GetSourcePriority(file));

            if (!selected.TryGetValue(topic, out var existing))
            {
                selected[topic] = candidate;
                continue;
            }

            // Prefer higher-priority source types when topic names are duplicated.
            if (candidate.SourcePriority > existing.SourcePriority)
            {
                selected[topic] = candidate;
                continue;
            }

            if (candidate.SourcePriority < existing.SourcePriority)
                continue;

            // Deterministic tie-breaker for same file type.
            if (string.Compare(candidate.SourcePath, existing.SourcePath, StringComparison.OrdinalIgnoreCase) < 0)
                selected[topic] = candidate;
        }

        var written = new List<AboutTopicInfo>();
        foreach (var topic in selected.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var candidate = selected[topic];
            var outPath = Path.Combine(aboutOutDir, SanitizeFileName(candidate.TopicName) + ".md");
            File.WriteAllText(outPath, candidate.Markdown, Utf8NoBom);

            written.Add(new AboutTopicInfo(candidate.TopicName, outPath, candidate.ShortDescription));
        }

        WriteIndexFile(aboutOutDir, written);
        return new AboutTopicWriteResult(written.ToArray());
    }

    private static IEnumerable<string> ResolveSourceRoots(string stagingPath, IEnumerable<string>? additionalSourcePaths)
    {
        var roots = new List<string>();

        void AddRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var full = Path.GetFullPath(root.Trim().Trim('"'));
            if (roots.Any(existing => string.Equals(existing, full, StringComparison.OrdinalIgnoreCase)))
                return;
            if (!Directory.Exists(full)) return;
            roots.Add(full);
        }

        AddRoot(stagingPath);
        foreach (var source in additionalSourcePaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            if (Path.IsPathRooted(source))
                AddRoot(source);
            else
                AddRoot(Path.Combine(stagingPath, source));
        }

        return roots;
    }

    private static IEnumerable<string> EnumerateAboutTopicFiles(string root)
    {
        IEnumerable<string> SafeEnum(string pattern)
        {
            try { return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }

        foreach (var f in SafeEnum("about_*.help.txt")) yield return f;
        foreach (var f in SafeEnum("about_*.txt")) yield return f;
        foreach (var f in SafeEnum("about_*.md")) yield return f;
        foreach (var f in SafeEnum("about_*.markdown")) yield return f;
    }

    private static void WriteIndexFile(string aboutOutDir, IReadOnlyCollection<AboutTopicInfo> topics)
    {
        if (string.IsNullOrWhiteSpace(aboutOutDir)) return;
        if (topics is null || topics.Count == 0) return;

        var indexPath = Path.Combine(aboutOutDir, "README.md");
        var sb = new StringBuilder();
        MarkdownFrontMatterWriter.Append(sb, ("schema", "1.0.0"), ("generated", "true"));
        sb.AppendLine("# About Topics");
        sb.AppendLine();
        sb.AppendLine("This folder is generated from `about_*.help.txt` and `about_*.txt` source files.");
        sb.AppendLine();

        foreach (var topic in topics.OrderBy(t => t.TopicName, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = SanitizeFileName(topic.TopicName) + ".md";
            if (string.IsNullOrWhiteSpace(topic.ShortDescription))
                sb.AppendLine($"- [{topic.TopicName}]({fileName})");
            else
                sb.AppendLine($"- [{topic.TopicName}]({fileName}) - {topic.ShortDescription!.Trim()}");
        }

        sb.AppendLine();
        File.WriteAllText(indexPath, sb.ToString(), Utf8NoBom);
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

    private static AboutTopicMarkdownResult ConvertSourceFile(string path)
    {
        var content = File.ReadAllText(path);
        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            return AboutTopicMarkdown.ConvertMarkdown(Path.GetFileNameWithoutExtension(path), content);
        }

        return AboutTopicMarkdown.Convert(Path.GetFileNameWithoutExtension(path), content);
    }

    private static int GetSourcePriority(string path)
    {
        if (path.EndsWith(".help.txt", StringComparison.OrdinalIgnoreCase)) return 300;
        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return 200;
        if (path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)) return 200;
        if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return 100;
        return 0;
    }
}

internal sealed class AboutTopicCandidate
{
    public string TopicName { get; }
    public string Markdown { get; }
    public string? ShortDescription { get; }
    public string SourcePath { get; }
    public int SourcePriority { get; }

    public AboutTopicCandidate(string topicName, string markdown, string? shortDescription, string sourcePath, int sourcePriority)
    {
        TopicName = topicName ?? string.Empty;
        Markdown = markdown ?? string.Empty;
        ShortDescription = shortDescription;
        SourcePath = sourcePath ?? string.Empty;
        SourcePriority = sourcePriority;
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
        MarkdownFrontMatterWriter.Append(sb, ("topic", topic), ("schema", "1.0.0"));
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

    public static AboutTopicMarkdownResult ConvertMarkdown(string fileStem, string content)
    {
        var topic = NormalizeTopicFromStem(fileStem);
        var markdown = (content ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Trim('\n');
        if (string.IsNullOrWhiteSpace(markdown))
            return new AboutTopicMarkdownResult(topic, string.Empty, null);

        var lines = markdown.Split('\n');
        var shortDescription = lines
            .Select(l => l.Trim())
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#", StringComparison.Ordinal));

        if (!markdown.StartsWith("---", StringComparison.Ordinal))
        {
            var sb = new StringBuilder();
            MarkdownFrontMatterWriter.Append(sb, ("topic", topic), ("schema", "1.0.0"));
            sb.AppendLine(markdown);
            markdown = sb.ToString().TrimEnd('\r', '\n');
        }

        return new AboutTopicMarkdownResult(topic, markdown.Replace("\n", Environment.NewLine), shortDescription);
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
