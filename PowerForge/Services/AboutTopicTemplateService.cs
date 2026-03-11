using System;
using System.IO;

namespace PowerForge;

/// <summary>
/// Resolves and scaffolds about-topic template files.
/// </summary>
public sealed class AboutTopicTemplateService
{
    /// <summary>
    /// Resolves the target about-topic scaffold path without writing the file.
    /// </summary>
    public AboutTopicTemplateResult Preview(AboutTopicTemplateRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var outputDirectory = ResolveOutputDirectory(request.WorkingDirectory, request.OutputPath);
        var normalizedTopic = AboutTopicTemplateGenerator.NormalizeTopicName(request.TopicName);
        var extension = request.Format == AboutTopicTemplateFormat.Markdown ? ".md" : ".help.txt";
        var filePath = Path.Combine(outputDirectory, normalizedTopic + extension);

        return new AboutTopicTemplateResult
        {
            TopicName = normalizedTopic,
            OutputDirectory = outputDirectory,
            FilePath = filePath,
            Format = request.Format,
            Exists = File.Exists(filePath)
        };
    }

    /// <summary>
    /// Generates the about-topic scaffold file and returns the resolved result.
    /// </summary>
    public AboutTopicTemplateResult Generate(AboutTopicTemplateRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var preview = Preview(request);
        var created = AboutTopicTemplateGenerator.WriteTemplateFile(
            outputDirectory: preview.OutputDirectory,
            topicName: preview.TopicName,
            force: request.Force,
            shortDescription: request.ShortDescription,
            format: request.Format);

        return new AboutTopicTemplateResult
        {
            TopicName = preview.TopicName,
            OutputDirectory = preview.OutputDirectory,
            FilePath = created,
            Format = preview.Format,
            Exists = preview.Exists
        };
    }

    private static string ResolveOutputDirectory(string workingDirectory, string outputPath)
    {
        var basePath = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : workingDirectory;
        var trimmed = (outputPath ?? string.Empty).Trim().Trim('"');
        if (trimmed.Length == 0)
            return Path.GetFullPath(basePath);

        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(basePath, trimmed));
    }
}
