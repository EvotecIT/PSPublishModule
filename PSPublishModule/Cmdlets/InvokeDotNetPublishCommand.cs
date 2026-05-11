using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Executes DotNet publish engine from DSL settings or an existing JSON config.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet follows the same authoring pattern as module build cmdlets:
/// create a config using <c>New-ConfigurationDotNet*</c> and run it directly,
/// or export config first via <c>-JsonOnly</c> + <c>-JsonPath</c>.
/// </para>
/// </remarks>
/// <example>
/// <summary>Generate DotNet publish JSON from DSL without execution</summary>
/// <code>
/// Invoke-DotNetPublish -JsonOnly -JsonPath '.\powerforge.dotnetpublish.json' -Settings {
///     New-ConfigurationDotNetPublish -IncludeSchema -ProjectRoot '.' -Configuration 'Release'
///     New-ConfigurationDotNetTarget -Name 'PowerForge.Cli' -ProjectPath 'PowerForge.Cli/PowerForge.Cli.csproj' -Framework 'net10.0' -Runtimes 'win-x64' -Style PortableCompat -Zip
/// }
/// </code>
/// </example>
/// <example>
/// <summary>Run DotNet publish from existing JSON config</summary>
/// <code>Invoke-DotNetPublish -ConfigPath '.\powerforge.dotnetpublish.json' -ExitCode</code>
/// </example>
[Cmdlet(VerbsLifecycle.Invoke, "DotNetPublish", DefaultParameterSetName = ParameterSetSettings)]
[OutputType(typeof(DotNetPublishPlan))]
[OutputType(typeof(DotNetPublishResult))]
public sealed class InvokeDotNetPublishCommand : PSCmdlet
{
    private const string ParameterSetSettings = "Settings";
    private const string ParameterSetConfig = "Config";

    /// <summary>
    /// DSL settings block that emits DotNet publish objects.
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetSettings)]
    public ScriptBlock Settings { get; set; } = ScriptBlock.Create(string.Empty);

    /// <summary>
    /// Path to existing DotNet publish JSON config.
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetConfig)]
    [ValidateNotNullOrEmpty]
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional project root override used to resolve relative publish inputs and outputs.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// Optional profile override.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public string? Profile { get; set; }

    /// <summary>
    /// Optional target-name filter override.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    [Alias("Targets")]
    public string[]? Target { get; set; }

    /// <summary>
    /// Optional runtime override for selected targets.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    [Alias("Runtime", "Rid")]
    public string[]? Runtimes { get; set; }

    /// <summary>
    /// Optional framework override for selected targets.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    [Alias("Framework")]
    public string[]? Frameworks { get; set; }

    /// <summary>
    /// Optional style override for selected targets.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    [Alias("Style")]
    public DotNetPublishStyle[]? Styles { get; set; }

    /// <summary>
    /// Disables restore steps for this run.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public SwitchParameter SkipRestore { get; set; }

    /// <summary>
    /// Disables build steps for this run.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public SwitchParameter SkipBuild { get; set; }

    /// <summary>
    /// Exports JSON config and exits without running the engine.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public SwitchParameter JsonOnly { get; set; }

    /// <summary>
    /// Output path for JSON config used with <see cref="JsonOnly"/>.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public string? JsonPath { get; set; }

    /// <summary>
    /// Builds and emits resolved plan without executing steps.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public SwitchParameter Plan { get; set; }

    /// <summary>
    /// Validates configuration by planning only; does not execute run steps.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public SwitchParameter Validate { get; set; }

    /// <summary>
    /// Disables interactive output mode. Reserved for future UI parity.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public SwitchParameter NoInteractive { get; set; }

    /// <summary>
    /// Sets host exit code: 0 on success, 1 on failure.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public SwitchParameter ExitCode { get; set; }

    /// <summary>
    /// Executes DotNet publish plan/run or exports JSON config based on switches.
    /// </summary>
    protected override void ProcessRecord()
    {
        var boundParameters = MyInvocation?.BoundParameters;
        var isVerbose = boundParameters?.ContainsKey("Verbose") == true;
        var logger = new CmdletLogger(this, isVerbose);
        var exitCodeMode = ExitCode.IsPresent;

        try
        {
            var preparation = new DotNetPublishPreparationService(logger).Prepare(
                new DotNetPublishPreparationRequest
                {
                    ParameterSetName = ParameterSetName,
                    CurrentPath = SessionState.Path.CurrentFileSystemLocation.Path,
                    ResolvePath = path => SessionState.Path.GetUnresolvedProviderPathFromPSPath(path),
                    Settings = Settings,
                    ConfigPath = ConfigPath,
                    ProjectRoot = ProjectRoot,
                    Profile = Profile,
                    Target = Target,
                    Runtimes = Runtimes,
                    Frameworks = Frameworks,
                    Styles = Styles,
                    SkipRestore = SkipRestore.IsPresent,
                    SkipBuild = SkipBuild.IsPresent,
                    JsonOnly = JsonOnly.IsPresent,
                    JsonPath = JsonPath,
                    Plan = Plan.IsPresent,
                    Validate = Validate.IsPresent
                },
                warn: message => WriteWarning(message));

            var workflow = new DotNetPublishWorkflowService(logger).Execute(preparation);

            if (!string.IsNullOrWhiteSpace(workflow.JsonOutputPath))
            {
                WriteObject(workflow.JsonOutputPath);
                if (exitCodeMode) Host.SetShouldExit(0);
                return;
            }

            if (workflow.Plan is not null)
            {
                WriteObject(workflow.Plan);
                if (exitCodeMode) Host.SetShouldExit(0);
                return;
            }

            var result = workflow.Result ?? throw new InvalidOperationException("DotNet publish workflow did not produce a result.");
            WriteObject(result);
            if (!result.Succeeded)
            {
                var error = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "DotNet publish failed."
                    : result.ErrorMessage!;
                throw new InvalidOperationException(error);
            }

            if (exitCodeMode) Host.SetShouldExit(0);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "InvokeDotNetPublishFailed", ErrorCategory.NotSpecified, null));
            if (exitCodeMode) Host.SetShouldExit(1);
        }
    }
}
