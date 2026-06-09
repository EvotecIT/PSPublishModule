using System;
using System.IO;
using System.Text;

namespace PowerForge;

/// <summary>
/// Supported output formats for about topic templates.
/// </summary>
public enum AboutTopicTemplateFormat
{
    /// <summary>
    /// Legacy about help text format (<c>about_*.help.txt</c>).
    /// </summary>
    HelpText = 0,

    /// <summary>
    /// Markdown format (<c>about_*.md</c>).
    /// </summary>
    Markdown = 1
}

/// <summary>
/// Creates <c>about_*.help.txt</c> template files for module documentation authoring.
/// </summary>
public static class AboutTopicTemplateGenerator
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Normalizes a topic name to a canonical <c>about_*</c> value.
    /// </summary>
    public static string NormalizeTopicName(string topicName)
    {
        var raw = (topicName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Topic name is required.", nameof(topicName));

        raw = raw.Replace(' ', '_');
        if (!raw.StartsWith("about_", StringComparison.OrdinalIgnoreCase))
            raw = "about_" + raw;

        return raw;
    }

    /// <summary>
    /// Creates template text for an about topic source file.
    /// </summary>
    public static string CreateTemplateText(string topicName, string? shortDescription = null)
    {
        var normalizedTopic = NormalizeTopicName(topicName);
        var summary = string.IsNullOrWhiteSpace(shortDescription)
            ? "Explain what this topic covers."
            : shortDescription!.Trim();

        var sb = new StringBuilder();
        sb.AppendLine("TOPIC");
        sb.AppendLine($"    {normalizedTopic}");
        sb.AppendLine();
        sb.AppendLine("SHORT DESCRIPTION");
        sb.AppendLine($"    {summary}");
        sb.AppendLine();
        sb.AppendLine("LONG DESCRIPTION");
        sb.AppendLine("    Add detailed usage guidance, context, and caveats.");
        sb.AppendLine();
        sb.AppendLine("EXAMPLES");
        sb.AppendLine($"    PS> Get-Help {normalizedTopic} -Detailed");
        sb.AppendLine();
        sb.AppendLine("NOTES");
        sb.AppendLine("    This file is source content for generated module documentation.");
        return sb.ToString();
    }

    /// <summary>
    /// Creates markdown template text for an about topic source file.
    /// </summary>
    public static string CreateMarkdownTemplateText(string topicName, string? shortDescription = null)
    {
        var normalizedTopic = NormalizeTopicName(topicName);
        var summary = string.IsNullOrWhiteSpace(shortDescription)
            ? "Explain what this topic covers."
            : shortDescription!.Trim();

        var sb = new StringBuilder();
        MarkdownFrontMatterWriter.Append(sb, ("topic", normalizedTopic), ("schema", "1.0.0"));
        sb.AppendLine($"# {normalizedTopic}");
        sb.AppendLine();
        sb.AppendLine("## Short Description");
        sb.AppendLine();
        sb.AppendLine(summary);
        sb.AppendLine();
        sb.AppendLine("## Long Description");
        sb.AppendLine();
        sb.AppendLine("Add detailed usage guidance, context, and caveats.");
        sb.AppendLine();
        sb.AppendLine("## Examples");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine($"Get-Help {normalizedTopic} -Detailed");
        sb.AppendLine("```");
        return sb.ToString();
    }

    /// <summary>
    /// Writes a template file and returns the full path.
    /// </summary>
    public static string WriteTemplateFile(
        string outputDirectory,
        string topicName,
        bool force,
        string? shortDescription = null,
        AboutTopicTemplateFormat format = AboutTopicTemplateFormat.HelpText)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

        var fullOutputDirectory = Path.GetFullPath(outputDirectory.Trim().Trim('"'));
        Directory.CreateDirectory(fullOutputDirectory);

        var normalizedTopic = NormalizeTopicName(topicName);
        var extension = format == AboutTopicTemplateFormat.Markdown ? ".md" : ".help.txt";
        var outputFile = Path.Combine(fullOutputDirectory, normalizedTopic + extension);
        if (File.Exists(outputFile) && !force)
            throw new IOException($"About topic already exists: {outputFile}");

        var template = format == AboutTopicTemplateFormat.Markdown
            ? CreateMarkdownTemplateText(normalizedTopic, shortDescription)
            : CreateTemplateText(normalizedTopic, shortDescription);
        File.WriteAllText(outputFile, template, Utf8NoBom);
        return outputFile;
    }
}
