using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Scaffolds a starter project release configuration file for PowerShell-authored project builds.
/// </summary>
/// <example>
/// <summary>Create a starter config in Build/project.release.json</summary>
/// <code>New-ProjectReleaseConfig -ProjectRoot '.' -PassThru</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ProjectReleaseConfig", SupportsShouldProcess = true)]
[OutputType(typeof(string))]
[OutputType(typeof(PowerForgeProjectConfigurationScaffoldResult))]
public sealed class NewProjectReleaseConfigCommand : PSCmdlet
{
    /// <summary>
    /// Project root used to resolve relative paths.
    /// </summary>
    [Parameter]
    public string ProjectRoot { get; set; } = ".";

    /// <summary>
    /// Optional path to a specific project file.
    /// </summary>
    [Parameter]
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Optional release name override.
    /// </summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>
    /// Optional target name override.
    /// </summary>
    [Parameter]
    public string? TargetName { get; set; }

    /// <summary>
    /// Optional framework override.
    /// </summary>
    [Parameter]
    public string? Framework { get; set; }

    /// <summary>
    /// Optional runtime identifiers override.
    /// </summary>
    [Parameter]
    [Alias("Runtime")]
    public string[]? Runtimes { get; set; }

    /// <summary>
    /// Release configuration value written into the starter config.
    /// </summary>
    [Parameter]
    [ValidateSet("Release", "Debug")]
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// Output config path (default: Build\project.release.json).
    /// </summary>
    [Parameter]
    [Alias("Path", "ConfigPath")]
    public string OutputPath { get; set; } = System.IO.Path.Combine("Build", "project.release.json");

    /// <summary>
    /// Overwrite an existing config file.
    /// </summary>
    [Parameter]
    [Alias("Overwrite")]
    public SwitchParameter Force { get; set; }

    /// <summary>
    /// Configure the starter file to request a portable bundle by default.
    /// </summary>
    [Parameter]
    public SwitchParameter Portable { get; set; }

    /// <summary>
    /// Returns detailed scaffold metadata instead of only the config path.
    /// </summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>
    /// Creates the starter project release config file.
    /// </summary>
    protected override void ProcessRecord()
    {
        var request = new PowerForgeProjectConfigurationScaffoldRequest
        {
            ProjectRoot = ProjectRoot,
            ProjectPath = ProjectPath,
            Name = Name,
            TargetName = TargetName,
            Framework = Framework,
            Runtimes = Runtimes,
            Configuration = Configuration,
            OutputPath = OutputPath,
            Force = Force.IsPresent,
            IncludePortableOutput = Portable.IsPresent,
            WorkingDirectory = SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Environment.CurrentDirectory
        };

        var service = new PowerForgeProjectConfigurationScaffoldService();
        var resolvedOutputPath = service.ResolveOutputPath(request);
        var action = System.IO.File.Exists(resolvedOutputPath)
            ? "Overwrite project release configuration"
            : "Create project release configuration";
        if (!ShouldProcess(resolvedOutputPath, action))
            return;

        try
        {
            var result = service.Generate(request);
            if (PassThru.IsPresent)
                WriteObject(result);
            else
                WriteObject(result.ConfigPath);
        }
        catch (Exception ex)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "NewProjectReleaseConfigFailed", ErrorCategory.WriteError, resolvedOutputPath));
        }
    }
}
