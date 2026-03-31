using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Exports a PowerShell-authored project release object to JSON.
/// </summary>
/// <example>
/// <summary>Write a project configuration file</summary>
/// <code>Export-ConfigurationProject -Project $project -OutputPath '.\Build\project.release.json'</code>
/// </example>
[Cmdlet(VerbsData.Export, "ConfigurationProject", SupportsShouldProcess = true)]
[OutputType(typeof(string))]
public sealed class ExportConfigurationProjectCommand : PSCmdlet
{
    /// <summary>
    /// Project configuration object to export.
    /// </summary>
    [Parameter(Mandatory = true)]
    public ConfigurationProject Project { get; set; } = new();

    /// <summary>
    /// Output JSON path.
    /// </summary>
    [Parameter(Mandatory = true)]
    [Alias("Path")]
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// Overwrites an existing file.
    /// </summary>
    [Parameter]
    [Alias("Overwrite")]
    public SwitchParameter Force { get; set; }

    /// <summary>
    /// Exports the project configuration to JSON.
    /// </summary>
    protected override void ProcessRecord()
    {
        var fullPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(OutputPath);
        if (!ShouldProcess(fullPath, File.Exists(fullPath) ? "Overwrite project configuration" : "Create project configuration"))
            return;

        try
        {
            var service = new PowerForgeProjectConfigurationJsonService();
            var savedPath = service.Save(Project, fullPath, Force.IsPresent);
            WriteObject(savedPath);
        }
        catch (Exception ex)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "ExportConfigurationProjectFailed", ErrorCategory.WriteError, OutputPath));
        }
    }
}
