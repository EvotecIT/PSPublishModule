using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

internal sealed class AboutTopicWriter
{
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

        var selected = SelectAboutTopics(aboutFiles)
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Markdown))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        var written = new List<AboutTopicInfo>();
        foreach (var topic in selected.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var candidate = selected[topic];
            var outPath = Path.Combine(aboutOutDir, SanitizeFileName(candidate.TopicName) + ".md");
            GeneratedTextNormalizer.WriteUtf8NoBom(outPath, candidate.Markdown);

            written.Add(new AboutTopicInfo(candidate.TopicName, outPath, candidate.ShortDescription));
        }

        WriteIndexFile(aboutOutDir, written);
        return new AboutTopicWriteResult(written.ToArray());
    }

    public AboutTopicWriteResult WriteExternalHelpFiles(
        string stagingPath,
        string culturePath,
        IEnumerable<string>? additionalSourcePaths = null)
    {
        if (string.IsNullOrWhiteSpace(stagingPath)) throw new ArgumentException("StagingPath is required.", nameof(stagingPath));
        if (string.IsNullOrWhiteSpace(culturePath)) throw new ArgumentException("CulturePath is required.", nameof(culturePath));

        var fullStaging = Path.GetFullPath(stagingPath.Trim().Trim('"'));
        var fullCulturePath = Path.GetFullPath(culturePath.Trim().Trim('"'));

        if (!Directory.Exists(fullStaging)) return new AboutTopicWriteResult(Array.Empty<AboutTopicInfo>());

        var roots = ResolveSourceRoots(fullStaging, additionalSourcePaths);
        var aboutFiles = roots
            .SelectMany(EnumerateAboutTopicFiles)
            .Where(file => !IsUnderDirectory(file, fullCulturePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selected = SelectAboutTopics(aboutFiles);

        Directory.CreateDirectory(fullCulturePath);

        var expectedFiles = selected.Values
            .Select(candidate => Path.GetFileName(Path.Combine(fullCulturePath, SanitizeFileName(candidate.TopicName) + ".help.txt")))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in Directory.EnumerateFiles(fullCulturePath, "about_*.help.txt", SearchOption.TopDirectoryOnly))
        {
            if (!expectedFiles.Contains(Path.GetFileName(stale)))
                File.Delete(stale);
        }

        if (selected.Count == 0) return new AboutTopicWriteResult(Array.Empty<AboutTopicInfo>());

        var written = new List<AboutTopicInfo>();
        foreach (var topic in selected.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var candidate = selected[topic];
            var outPath = Path.Combine(fullCulturePath, SanitizeFileName(candidate.TopicName) + ".help.txt");
            GeneratedTextNormalizer.WriteUtf8NoBom(outPath, candidate.HelpText);
            written.Add(new AboutTopicInfo(candidate.TopicName, outPath, candidate.ShortDescription));
        }

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
            var normalized = NormalizeSourcePath(source);
            if (Path.IsPathRooted(normalized))
                AddRoot(normalized);
            else
                AddRoot(Path.Combine(stagingPath, normalized));
        }

        return roots;
    }

    private static string NormalizeSourcePath(string path)
    {
        var trimmed = path.Trim().Trim('"');
        return trimmed
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
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

    private static Dictionary<string, AboutTopicCandidate> SelectAboutTopics(IEnumerable<string> aboutFiles)
    {
        var selected = new Dictionary<string, AboutTopicCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in aboutFiles)
        {
            AboutTopicMarkdownResult converted;
            try { converted = ConvertSourceFile(file); }
            catch { continue; }
            if (string.IsNullOrWhiteSpace(converted.TopicName))
                continue;
            if (string.IsNullOrWhiteSpace(converted.Markdown) || string.IsNullOrWhiteSpace(converted.HelpText))
                continue;

            var topic = converted.TopicName.Trim();
            var candidate = new AboutTopicCandidate(
                topic,
                converted.Markdown,
                converted.HelpText,
                converted.ShortDescription,
                file,
                GetSourcePriority(file));

            if (!selected.TryGetValue(topic, out var existing))
            {
                selected[topic] = candidate;
                continue;
            }

            if (candidate.SourcePriority > existing.SourcePriority)
            {
                selected[topic] = candidate;
                continue;
            }

            if (candidate.SourcePriority < existing.SourcePriority)
                continue;

            if (string.Compare(candidate.SourcePath, existing.SourcePath, StringComparison.OrdinalIgnoreCase) < 0)
                selected[topic] = candidate;
        }

        return selected;
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
        GeneratedTextNormalizer.WriteUtf8NoBom(indexPath, sb.ToString());
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

    private static bool IsUnderDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            return false;

        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class AboutTopicCandidate
{
    public string TopicName { get; }
    public string Markdown { get; }
    public string HelpText { get; }
    public string? ShortDescription { get; }
    public string SourcePath { get; }
    public int SourcePriority { get; }

    public AboutTopicCandidate(string topicName, string markdown, string helpText, string? shortDescription, string sourcePath, int sourcePriority)
    {
        TopicName = topicName ?? string.Empty;
        Markdown = markdown ?? string.Empty;
        HelpText = helpText ?? string.Empty;
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

            AppendHelpTextBodyMarkdown(sb, title, body);
        }

        var helpText = GeneratedTextNormalizer.Normalize(string.Join("\n", SplitLines(content)));
        return new AboutTopicMarkdownResult(topic, sb.ToString(), helpText, shortDesc);
    }

    public static AboutTopicMarkdownResult ConvertMarkdown(string fileStem, string content)
    {
        var topic = NormalizeTopicFromStem(fileStem);
        var markdown = (content ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Trim('\n');
        if (string.IsNullOrWhiteSpace(markdown))
            return new AboutTopicMarkdownResult(topic, string.Empty, string.Empty, null);

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

        var helpText = ConvertMarkdownToHelpText(topic, markdown);
        return new AboutTopicMarkdownResult(topic, GeneratedTextNormalizer.Normalize(markdown), helpText, shortDescription);
    }

    private static string ConvertMarkdownToHelpText(string topic, string markdown)
    {
        var lines = SplitLines(RemoveYamlFrontMatter(markdown));
        var output = new List<string>();
        var wroteTopic = false;

        foreach (var raw in lines)
        {
            var line = raw ?? string.Empty;
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
                continue;

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                var heading = trimmed.TrimStart('#').Trim();
                if (string.IsNullOrWhiteSpace(heading))
                    continue;

                if (!wroteTopic)
                {
                    output.Add("TOPIC");
                    output.Add(heading);
                    output.Add(string.Empty);
                    wroteTopic = true;
                }
                else
                {
                    output.Add(heading.ToUpperInvariant());
                }

                continue;
            }

            output.Add(line);
        }

        if (!wroteTopic)
        {
            output.Insert(0, string.Empty);
            output.Insert(0, topic);
            output.Insert(0, "TOPIC");
        }

        while (output.Count > 0 && string.IsNullOrWhiteSpace(output[output.Count - 1]))
            output.RemoveAt(output.Count - 1);

        return GeneratedTextNormalizer.Normalize(string.Join("\n", output));
    }

    private static string RemoveYamlFrontMatter(string markdown)
    {
        var normalized = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            return normalized;

        var end = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0)
            return normalized;

        var contentStart = normalized.IndexOf('\n', end + 1);
        return contentStart < 0 ? string.Empty : normalized.Substring(contentStart + 1);
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

            current.BodyLines.Add(line.TrimEnd());
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
        var commonIndent = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(CountLeadingSpaces)
            .DefaultIfEmpty(0)
            .Min();

        foreach (var l in lines)
        {
            var line = (l ?? string.Empty).TrimEnd();
            if (commonIndent > 0 && line.Length >= commonIndent)
                line = line.Substring(commonIndent);

            output.Add(line);
        }

        // Trim leading/trailing empties
        while (output.Count > 0 && string.IsNullOrWhiteSpace(output[0])) output.RemoveAt(0);
        while (output.Count > 0 && string.IsNullOrWhiteSpace(output[output.Count - 1])) output.RemoveAt(output.Count - 1);
        return output;
    }

    private static void AppendHelpTextBodyMarkdown(StringBuilder sb, string sectionTitle, List<string> lines)
    {
        var inCodeBlock = false;
        var wroteBlank = false;
        var isExamplesSection = sectionTitle.Equals("EXAMPLES", StringComparison.OrdinalIgnoreCase);

        foreach (var raw in lines)
        {
            var line = raw ?? string.Empty;
            var trimmed = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (inCodeBlock)
                {
                    sb.AppendLine("```");
                    inCodeBlock = false;
                }

                if (!wroteBlank)
                {
                    sb.AppendLine();
                    wroteBlank = true;
                }

                continue;
            }

            if (IsHelpTextCodeLine(line) || (isExamplesSection && IsExampleCodeLine(trimmed)))
            {
                if (!inCodeBlock)
                {
                    if (!wroteBlank)
                        sb.AppendLine();

                    sb.AppendLine("```powershell");
                    inCodeBlock = true;
                }

                sb.AppendLine(trimmed);
                wroteBlank = false;
                continue;
            }

            if (inCodeBlock)
            {
                sb.AppendLine("```");
                sb.AppendLine();
                inCodeBlock = false;
            }

            sb.AppendLine(trimmed);
            wroteBlank = false;
        }

        if (inCodeBlock)
        {
            sb.AppendLine("```");
        }

        sb.AppendLine();
    }

    private static bool IsExampleCodeLine(string trimmed)
    {
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        if (trimmed.StartsWith("$", StringComparison.Ordinal))
            return true;

        if (trimmed is "{" or "}" or "};" or "})" or "});")
            return true;

        if (trimmed.EndsWith("{", StringComparison.Ordinal) &&
            IsPowerShellControlFlowStart(trimmed))
            return true;

        var first = trimmed.Split(new[] { ' ', '\t', '|' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
            return false;

        var dash = first.IndexOf('-');
        return dash > 0
               && dash < first.Length - 1
               && first.Take(dash).All(char.IsLetter)
               && first.Skip(dash + 1).All(ch => char.IsLetterOrDigit(ch) || ch == '_');
    }

    private static bool IsPowerShellControlFlowStart(string trimmed)
    {
        var first = trimmed.Split(new[] { ' ', '\t', '(' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is not null &&
               (first.Equals("if", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("elseif", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("else", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("foreach", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("for", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("while", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("do", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("switch", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("try", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("catch", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("finally", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("function", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHelpTextCodeLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.Trim();
        if (IsPowerShellPromptLine(trimmed))
            return true;

        if (trimmed.StartsWith(".\\", StringComparison.Ordinal) ||
            trimmed.StartsWith("./", StringComparison.Ordinal) ||
            trimmed.StartsWith("%", StringComparison.Ordinal) ||
            trimmed.StartsWith("$env:", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IsPowerShellCommandLine(trimmed))
            return true;

        if (IsAssignmentLine(trimmed))
            return true;

        if (IsUpperSnakeIdentifier(trimmed))
            return true;

        return CountLeadingSpaces(line) >= 8 && !IsMarkdownListLine(trimmed);
    }

    private static bool IsPowerShellPromptLine(string line)
    {
        if (line.StartsWith(">>", StringComparison.Ordinal))
            return true;

        if (!line.StartsWith("PS", StringComparison.OrdinalIgnoreCase))
            return false;

        if (line.Length == 2)
            return false;

        if (line[2] == '>')
            return true;

        var promptEnd = line.IndexOf('>');
        return promptEnd > 2 && char.IsWhiteSpace(line[2]);
    }

    private static bool IsPowerShellCommandLine(string line)
    {
        if (line.IndexOf(" -", StringComparison.Ordinal) < 0)
            return false;

        var first = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
            return false;

        var dash = first.IndexOf('-');
        if (dash <= 0 || dash == first.Length - 1)
            return false;

        return first.Take(dash).All(char.IsLetter)
               && first.Skip(dash + 1).All(ch => char.IsLetterOrDigit(ch) || ch == '_');
    }

    private static bool IsAssignmentLine(string line)
    {
        var equals = line.IndexOf('=');
        if (equals <= 0)
            return false;

        var left = line.Substring(0, equals).Trim();
        var right = line.Substring(equals + 1).Trim();
        if (left.Length == 0 || left.Length > 80)
            return false;

        if (right.IndexOf(". ", StringComparison.Ordinal) >= 0)
            return false;

        return left.All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '-')
               && !line.EndsWith(".", StringComparison.Ordinal);
    }

    private static bool IsUpperSnakeIdentifier(string line)
    {
        if (line.Length < 8 || line.IndexOf(' ') >= 0)
            return false;

        var hasUnderscore = false;
        var hasLetter = false;
        foreach (var ch in line)
        {
            if (ch == '_')
            {
                hasUnderscore = true;
                continue;
            }

            if (char.IsLetter(ch))
            {
                hasLetter = true;
                if (!char.IsUpper(ch))
                    return false;
                continue;
            }

            if (!char.IsDigit(ch))
                return false;
        }

        return hasUnderscore && hasLetter;
    }

    private static bool IsMarkdownListLine(string line)
    {
        if (line.StartsWith("- ", StringComparison.Ordinal) ||
            line.StartsWith("* ", StringComparison.Ordinal))
            return true;

        var dot = line.IndexOf('.');
        return dot > 0
               && dot < 4
               && line.Take(dot).All(char.IsDigit)
               && dot + 1 < line.Length
               && char.IsWhiteSpace(line[dot + 1]);
    }

    private static int CountLeadingSpaces(string line)
    {
        var count = 0;
        foreach (var ch in line ?? string.Empty)
        {
            if (ch == ' ')
            {
                count++;
                continue;
            }

            if (ch == '\t')
            {
                count += 4;
                continue;
            }

            break;
        }

        return count;
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
    public string HelpText { get; }
    public string? ShortDescription { get; }

    public AboutTopicMarkdownResult(string topicName, string markdown, string helpText, string? shortDescription)
    {
        TopicName = topicName ?? string.Empty;
        Markdown = markdown ?? string.Empty;
        HelpText = helpText ?? string.Empty;
        ShortDescription = shortDescription;
    }
}

internal sealed class AboutSection
{
    public string Title { get; }
    public List<string> BodyLines { get; } = new();

    public AboutSection(string title) => Title = title ?? string.Empty;
}
