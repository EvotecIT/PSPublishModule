using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Scaffolds a starter <c>powerforge.dotnetpublish.json</c> configuration file.
/// </summary>
/// <remarks>
/// <para>
/// Use this cmdlet for JSON-first onboarding of the DotNet publish engine.
/// The generated config can be executed with <c>Invoke-DotNetPublish -ConfigPath ...</c>.
/// </para>
/// </remarks>
/// <example>
/// <summary>Create a starter config from the current repository root</summary>
/// <code>New-DotNetPublishConfig -ProjectRoot '.' -PassThru</code>
/// </example>
/// <example>
/// <summary>Generate config for a selected project and overwrite existing file</summary>
/// <code>New-DotNetPublishConfig -Project '.\src\App\App.csproj' -Runtimes 'win-x64','win-arm64' -Styles PortableCompat,AotSpeed -Force</code>
/// </example>
[Cmdlet(VerbsCommon.New, "DotNetPublishConfig", SupportsShouldProcess = true)]
[OutputType(typeof(string))]
[OutputType(typeof(DotNetPublishConfigScaffoldResult))]
public sealed class NewDotNetPublishConfigCommand : PSCmdlet
{
    /// <summary>
    /// Project root used to resolve relative paths.
    /// </summary>
    [Parameter]
    public string ProjectRoot { get; set; } = ".";

    /// <summary>
    /// Optional path to a specific project file. When omitted, the first matching <c>.csproj</c> is used.
    /// </summary>
    [Parameter]
    [Alias("Project")]
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Optional target name override. Defaults to project file name.
    /// </summary>
    [Parameter]
    public string? TargetName { get; set; }

    /// <summary>
    /// Optional framework override (for example <c>net10.0</c>).
    /// </summary>
    [Parameter]
    public string? Framework { get; set; }

    /// <summary>
    /// Optional runtime identifiers override.
    /// </summary>
    [Parameter]
    public string[]? Runtimes { get; set; }

    /// <summary>
    /// Optional publish styles override.
    /// </summary>
    [Parameter]
    public DotNetPublishStyle[]? Styles { get; set; }

    /// <summary>
    /// Build configuration (default: <c>Release</c>).
    /// </summary>
    [Parameter]
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// Output config path (default: <c>powerforge.dotnetpublish.json</c>).
    /// </summary>
    [Parameter]
    [Alias("Path", "ConfigPath")]
    public string OutputPath { get; set; } = "powerforge.dotnetpublish.json";

    /// <summary>
    /// Overwrite existing config file.
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
    /// Returns detailed scaffold metadata instead of only config path.
    /// </summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>
    /// Creates the starter DotNet publish config file.
    /// </summary>
    protected override void ProcessRecord()
    {
        var request = new DotNetPublishConfigScaffoldRequest
        {
            ProjectRoot = ProjectRoot,
            ProjectPath = ProjectPath,
            TargetName = TargetName,
            Framework = Framework,
            Runtimes = Runtimes,
            Styles = Styles,
            Configuration = Configuration,
            OutputPath = OutputPath,
            Force = Force.IsPresent,
            IncludeSchema = !NoSchema.IsPresent,
            WorkingDirectory = SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Environment.CurrentDirectory
        };
        var service = new DotNetPublishConfigScaffoldService();
        var resolvedOutputPath = service.ResolveOutputPath(request);

        var action = File.Exists(resolvedOutputPath)
            ? "Overwrite DotNet publish configuration"
            : "Create DotNet publish configuration";
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
            var record = new ErrorRecord(ex, "NewDotNetPublishConfigFailed", ErrorCategory.WriteError, resolvedOutputPath);
            ThrowTerminatingError(record);
        }
    }
}
