using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>Imports a typed .NET publish configuration for use in the publish DSL.</summary>
/// <para>Relative project roots are anchored to the defining configuration file, so the resulting specification can be used from another working directory.</para>
/// <example><summary>Customize a configured package build</summary><code>$spec = Import-ConfigurationDotNetPublish -Path './Build/publish.json'; $spec.DotNet.Configuration = 'Debug'; Invoke-DotNetPublish -Settings { $spec }</code></example>
[Cmdlet(VerbsData.Import, "ConfigurationDotNetPublish")]
[OutputType(typeof(DotNetPublishSpec))]
public sealed class ImportConfigurationDotNetPublishCommand : PSCmdlet
{
    /// <para>Publish JSON or unified release JSON containing the tools configuration.</para>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ConfigPath")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Loads the shared typed configuration.</summary>
    protected override void ProcessRecord()
        => WriteObject(DotNetPublishConfiguration.Load(SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path)));
}
