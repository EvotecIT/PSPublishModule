using System;

namespace PowerForge;

/// <summary>
/// Describes a request to scaffold an about-topic source file.
/// </summary>
public sealed class AboutTopicTemplateRequest
{
    /// <summary>
    /// Gets or sets the topic name. The <c>about_</c> prefix is added automatically when missing.
    /// </summary>
    public string TopicName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the output directory path. Relative paths are resolved from <see cref="WorkingDirectory"/>.
    /// </summary>
    public string OutputPath { get; set; } = ".";

    /// <summary>
    /// Gets or sets an optional short description seed for the generated template.
    /// </summary>
    public string? ShortDescription { get; set; }

    /// <summary>
    /// Gets or sets the output format for the scaffolded file.
    /// </summary>
    public AboutTopicTemplateFormat Format { get; set; } = AboutTopicTemplateFormat.HelpText;

    /// <summary>
    /// Gets or sets whether an existing file can be overwritten.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>
    /// Gets or sets the working directory used to resolve relative paths.
    /// </summary>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
}
