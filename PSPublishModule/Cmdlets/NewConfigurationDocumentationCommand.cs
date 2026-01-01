using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Enables or disables creation of documentation from the module using PowerForge.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet emits documentation configuration segments that are consumed by <c>Invoke-ModuleBuild</c> / <c>Build-Module</c>.
/// It controls markdown generation (in <c>-Path</c>), optional external help generation (MAML, e.g. <c>en-US\&lt;ModuleName&gt;-help.xml</c>),
/// and whether generated documentation should be synced back to the project root.
/// </para>
/// <para>
/// About topics are supported via <c>about_*.help.txt</c> / <c>about_*.txt</c> files present in the module source. When enabled,
/// these are converted into markdown pages under <c>Docs\About</c>.
/// </para>
/// </remarks>
/// <example>
/// <summary>Generate markdown docs and external help, and sync back to project root</summary>
/// <code>New-ConfigurationDocumentation -Enable -UpdateWhenNew -StartClean -Path 'Docs' -PathReadme 'Docs\Readme.md' -SyncExternalHelpToProjectRoot</code>
/// </example>
/// <example>
/// <summary>Generate docs but skip about topics conversion and fallback examples</summary>
/// <code>New-ConfigurationDocumentation -Enable -Path 'Docs' -PathReadme 'Docs\Readme.md' -SkipAboutTopics -SkipFallbackExamples</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDocumentation")]
public sealed class NewConfigurationDocumentationCommand : PSCmdlet
{
    /// <summary>Enables creation of documentation from the module.</summary>
    [Parameter] public SwitchParameter Enable { get; set; }

    /// <summary>Removes all files from the documentation folder before creating new documentation.</summary>
    [Parameter] public SwitchParameter StartClean { get; set; }

    /// <summary>
    /// When enabled, generated documentation is also synced back to the project folder
    /// (not only to the staging build output).
    /// </summary>
    [Parameter] public SwitchParameter UpdateWhenNew { get; set; }

    /// <summary>
    /// When enabled and <see cref="UpdateWhenNew"/> is set, the generated external help file is also synced
    /// back to the project root (e.g. <c>en-US\&lt;ModuleName&gt;-help.xml</c>).
    /// </summary>
    [Parameter] public SwitchParameter SyncExternalHelpToProjectRoot { get; set; }

    /// <summary>Disable external help (MAML) generation.</summary>
    [Parameter] public SwitchParameter SkipExternalHelp { get; set; }

    /// <summary>Disable conversion of about_* topics into markdown pages.</summary>
    [Parameter] public SwitchParameter SkipAboutTopics { get; set; }

    /// <summary>Disable generating basic fallback examples for cmdlets missing examples.</summary>
    [Parameter] public SwitchParameter SkipFallbackExamples { get; set; }

    /// <summary>Culture folder for generated external help (default: en-US).</summary>
    [Parameter] public string ExternalHelpCulture { get; set; } = "en-US";      

    /// <summary>Optional file name override for external help (default: &lt;ModuleName&gt;-help.xml).</summary>
    [Parameter] public string ExternalHelpFileName { get; set; } = string.Empty;

    /// <summary>Path to the folder where documentation will be created.</summary>
    [Parameter(Mandatory = true)] public string Path { get; set; } = string.Empty;

    /// <summary>Path to the readme file that will be used for the documentation.</summary>
    [Parameter(Mandatory = true)] public string PathReadme { get; set; } = string.Empty;

    /// <summary>Documentation engine (legacy parameter; kept for compatibility).</summary>
    [Parameter(DontShow = true)] public PowerForge.DocumentationTool Tool { get; set; } = PowerForge.DocumentationTool.PowerForge;

    /// <summary>Emits documentation configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationDocumentationSegment
        {
            Configuration = new DocumentationConfiguration
            {
                Path = Path,
                PathReadme = PathReadme
            }
        });

        var emitBuildSegment =
            Enable.IsPresent ||
            StartClean.IsPresent ||
            UpdateWhenNew.IsPresent ||
            SyncExternalHelpToProjectRoot.IsPresent ||
            SkipExternalHelp.IsPresent ||
            SkipAboutTopics.IsPresent ||
            SkipFallbackExamples.IsPresent ||
            MyInvocation.BoundParameters.ContainsKey(nameof(ExternalHelpCulture)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(ExternalHelpFileName));

        if (emitBuildSegment)
        {
            WriteObject(new ConfigurationBuildDocumentationSegment
            {
                Configuration = new BuildDocumentationConfiguration
                {
                    Enable = Enable.IsPresent,
                    StartClean = StartClean.IsPresent,
                    UpdateWhenNew = UpdateWhenNew.IsPresent,
                    SyncExternalHelpToProjectRoot = SyncExternalHelpToProjectRoot.IsPresent,
                    Tool = PowerForge.DocumentationTool.PowerForge,
                    IncludeAboutTopics = !SkipAboutTopics.IsPresent,
                    GenerateFallbackExamples = !SkipFallbackExamples.IsPresent,
                    GenerateExternalHelp = !SkipExternalHelp.IsPresent,
                    ExternalHelpCulture = ExternalHelpCulture,
                    ExternalHelpFileName = ExternalHelpFileName
                }
            });
        }
    }
}
