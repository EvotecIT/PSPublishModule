namespace PowerForge;

/// <summary>
/// Describes a resolved or generated about-topic scaffold file.
/// </summary>
public sealed class AboutTopicTemplateResult
{
    /// <summary>
    /// Gets or sets the normalized topic name.
    /// </summary>
    public string TopicName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved output directory.
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved file path.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the output format.
    /// </summary>
    public AboutTopicTemplateFormat Format { get; set; }

    /// <summary>
    /// Gets or sets whether the target file already existed when the request was evaluated.
    /// </summary>
    public bool Exists { get; set; }
}
