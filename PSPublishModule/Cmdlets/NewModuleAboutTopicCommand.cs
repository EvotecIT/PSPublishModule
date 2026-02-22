using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates an <c>about_*.help.txt</c> template source file for module documentation.
/// </summary>
/// <remarks>
/// <para>
/// Use this cmdlet to scaffold about topic source files that are later converted by
/// <c>Invoke-ModuleBuild</c> documentation generation into markdown pages under <c>Docs\About</c>.
/// </para>
/// </remarks>
/// <example>
/// <summary>Create a new topic in a dedicated source folder</summary>
/// <code>New-ModuleAboutTopic -TopicName 'Troubleshooting' -OutputPath '.\Help\About'</code>
/// </example>
/// <example>
/// <summary>Overwrite an existing topic template</summary>
/// <code>New-ModuleAboutTopic -TopicName 'about_Configuration' -OutputPath '.\Help\About' -Force</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ModuleAboutTopic", SupportsShouldProcess = true)]
public sealed class NewModuleAboutTopicCommand : PSCmdlet
{
    /// <summary>
    /// Topic name. The <c>about_</c> prefix is added automatically when missing.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0)]
    public string TopicName { get; set; } = string.Empty;

    /// <summary>
    /// Output directory for the source file.
    /// </summary>
    [Parameter]
    [Alias("Path")]
    public string OutputPath { get; set; } = ".";

    /// <summary>
    /// Optional short description seed for the generated template.
    /// </summary>
    [Parameter]
    public string ShortDescription { get; set; } = "Explain what this topic covers.";

    /// <summary>
    /// Overwrite existing file if it already exists.
    /// </summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>
    /// Returns the created file path.
    /// </summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>
    /// Creates the about topic template file.
    /// </summary>
    protected override void ProcessRecord()
    {
        var root = SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Environment.CurrentDirectory;
        var outputDirectory = ResolveOutputDirectory(root, OutputPath);
        var normalizedTopic = AboutTopicTemplateGenerator.NormalizeTopicName(TopicName);
        var filePath = Path.Combine(outputDirectory, normalizedTopic + ".help.txt");

        var action = File.Exists(filePath) ? "Overwrite about topic template" : "Create about topic template";
        if (!ShouldProcess(filePath, action))
            return;

        try
        {
            var created = AboutTopicTemplateGenerator.WriteTemplateFile(
                outputDirectory: outputDirectory,
                topicName: normalizedTopic,
                force: Force.IsPresent,
                shortDescription: ShortDescription);

            if (PassThru.IsPresent)
                WriteObject(created);
            else
                WriteVerbose($"Created about topic template: {created}");
        }
        catch (Exception ex)
        {
            var record = new ErrorRecord(ex, "NewModuleAboutTopicFailed", ErrorCategory.WriteError, filePath);
            ThrowTerminatingError(record);
        }
    }

    private static string ResolveOutputDirectory(string currentDirectory, string outputPath)
    {
        var trimmed = (outputPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return Path.GetFullPath(currentDirectory);

        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(currentDirectory, trimmed));
    }
}
