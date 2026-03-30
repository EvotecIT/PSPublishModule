using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Imports a PowerShell-authored project release object from JSON.
/// </summary>
/// <example>
/// <summary>Load a project configuration file</summary>
/// <code>Import-ConfigurationProject -Path '.\Build\project.release.json'</code>
/// </example>
[Cmdlet(VerbsData.Import, "ConfigurationProject")]
[OutputType(typeof(ConfigurationProject))]
public sealed class ImportConfigurationProjectCommand : PSCmdlet
{
    /// <summary>
    /// Path to the JSON configuration file.
    /// </summary>
    [Parameter(Mandatory = true)]
    [Alias("ConfigPath", "OutputPath")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Imports the project configuration from JSON.
    /// </summary>
    protected override void ProcessRecord()
    {
        try
        {
            var fullPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
            var service = new PowerForgeProjectConfigurationJsonService();
            WriteObject(service.Load(fullPath));
        }
        catch (Exception ex)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "ImportConfigurationProjectFailed", ErrorCategory.ReadError, Path));
        }
    }
}
