using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Scaffolds a starter unified <c>release.json</c> configuration file.
/// </summary>
/// <remarks>
/// <para>
/// Use this cmdlet to assemble a repository-level release config from the build configs the repo
/// already has, typically <c>Build/project.build.json</c> and
/// <c>Build/powerforge.dotnetpublish.json</c>.
/// </para>
/// </remarks>
/// <example>
/// <summary>Create a starter unified release config from the current repository root</summary>
/// <code>New-PowerForgeReleaseConfig -ProjectRoot '.' -PassThru</code>
/// </example>
/// <example>
/// <summary>Generate config from explicit package and DotNet publish config files</summary>
/// <code>New-PowerForgeReleaseConfig -PackagesConfigPath '.\Build\project.build.json' -DotNetPublishConfigPath '.\Build\powerforge.dotnetpublish.json' -Force</code>
/// </example>
[Cmdlet(VerbsCommon.New, "PowerForgeReleaseConfig", SupportsShouldProcess = true)]
[OutputType(typeof(string))]
[OutputType(typeof(PowerForgeReleaseConfigScaffoldResult))]
public sealed class NewPowerForgeReleaseConfigCommand : PSCmdlet
{
    /// <summary>
    /// Project root used to resolve relative paths.
    /// </summary>
    [Parameter]
    public string ProjectRoot { get; set; } = ".";

    /// <summary>
    /// Optional path to an existing project-build config file.
    /// </summary>
    [Parameter]
    public string? PackagesConfigPath { get; set; }

    /// <summary>
    /// Optional path to an existing DotNet publish config file.
    /// </summary>
    [Parameter]
    public string? DotNetPublishConfigPath { get; set; }

    /// <summary>
    /// Output config path (default: <c>Build\release.json</c>).
    /// </summary>
    [Parameter]
    [Alias("Path", "ConfigPath")]
    public string OutputPath { get; set; } = Path.Combine("Build", "release.json");

    /// <summary>
    /// Release configuration value written into the tool section.
    /// </summary>
    [Parameter]
    [ValidateSet("Release", "Debug")]
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// Overwrite an existing config file.
    /// </summary>
    [Parameter]
    [Alias("Overwrite")]
    public SwitchParameter Force { get; set; }

    /// <summary>
    /// Omits the <c>$schema</c> property from generated JSON.
    /// </summary>
    [Parameter]
    public SwitchParameter NoSchema { get; set; }

    /// <summary>
    /// Skips package config discovery.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipPackages { get; set; }

    /// <summary>
    /// Skips tool/app config discovery.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipTools { get; set; }

    /// <summary>
    /// Returns detailed scaffold metadata instead of only the config path.
    /// </summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>
    /// Creates the starter unified release config file.
    /// </summary>
    protected override void ProcessRecord()
    {
        var request = new PowerForgeReleaseConfigScaffoldRequest
        {
            ProjectRoot = ProjectRoot,
            PackagesConfigPath = PackagesConfigPath,
            DotNetPublishConfigPath = DotNetPublishConfigPath,
            OutputPath = OutputPath,
            Configuration = Configuration,
            Force = Force.IsPresent,
            IncludeSchema = !NoSchema.IsPresent,
            SkipPackages = SkipPackages.IsPresent,
            SkipTools = SkipTools.IsPresent,
            WorkingDirectory = SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Environment.CurrentDirectory
        };
        var service = new PowerForgeReleaseConfigScaffoldService();
        var resolvedOutputPath = service.ResolveOutputPath(request);

        var action = File.Exists(resolvedOutputPath)
            ? "Overwrite unified release configuration"
            : "Create unified release configuration";
        if (!ShouldProcess(resolvedOutputPath, action))
            return;

        var isVerbose = MyInvocation?.BoundParameters?.ContainsKey("Verbose") == true;
        var logger = new CmdletLogger(this, isVerbose);

        try
        {
            var result = service.Generate(request, logger);

            if (PassThru.IsPresent)
                WriteObject(result);
            else
                WriteObject(result.ConfigPath);
        }
        catch (Exception ex)
        {
            var record = new ErrorRecord(ex, "NewPowerForgeReleaseConfigFailed", ErrorCategory.WriteError, resolvedOutputPath);
            ThrowTerminatingError(record);
        }
    }
}
