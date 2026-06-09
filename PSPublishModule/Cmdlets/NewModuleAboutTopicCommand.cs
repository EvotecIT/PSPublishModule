using System;
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
/// <example>
/// <summary>Create markdown about topic source</summary>
/// <code>New-ModuleAboutTopic -TopicName 'Troubleshooting' -OutputPath '.\Help\About' -Format Markdown</code>
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
    /// Output format for the scaffolded about topic file.
    /// </summary>
    [Parameter]
    public AboutTopicTemplateFormat Format { get; set; } =
        AboutTopicTemplateFormat.HelpText;

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
        var request = new AboutTopicTemplateRequest
        {
            TopicName = TopicName,
            OutputPath = OutputPath,
            ShortDescription = ShortDescription,
            Format = Format,
            Force = Force.IsPresent,
            WorkingDirectory = SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Environment.CurrentDirectory
        };
        var service = new AboutTopicTemplateService();
        var preview = service.Preview(request);

        var action = preview.Exists ? "Overwrite about topic template" : "Create about topic template";
        if (!ShouldProcess(preview.FilePath, action))
            return;

        try
        {
            var created = service.Generate(request);

            if (PassThru.IsPresent)
                WriteObject(created.FilePath);
            else
                WriteVerbose($"Created about topic template: {created.FilePath}");
        }
        catch (Exception ex)
        {
            var record = new ErrorRecord(ex, "NewModuleAboutTopicFailed", ErrorCategory.WriteError, preview.FilePath);
            ThrowTerminatingError(record);
        }
    }
}
