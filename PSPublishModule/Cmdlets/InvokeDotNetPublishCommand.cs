using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using PowerForge;
using PowerForge.ConsoleShared;

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
    /// Optional publish output-path override applied to selected targets.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public string? OutputPath { get; set; }

    /// <summary>
    /// Optional global MSBuild properties passed to restore, build, publish, and installer steps.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public Hashtable? MsBuildProperty { get; set; }

    /// <summary>
    /// Skips installer definitions after selected targets are filtered.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public SwitchParameter SkipInstallers { get; set; }

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
    /// Disables signing only for targets selected by this invocation. The configuration remains unchanged.
    /// Use for a temporary runtime smoke, not a signed release. Bundles selected by the effective profile are rejected
    /// because bundle signing follows the source publish target.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public SwitchParameter NoPublishSign { get; set; }

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
    /// Disables the interactive Spectre progress view.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public SwitchParameter NoInteractive { get; set; }

    /// <summary>Suppresses progress and informational output while preserving results, warnings and errors.</summary>
    [Parameter(ParameterSetName = ParameterSetSettings)]
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public SwitchParameter Quiet { get; set; }

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
        var interactive = !Quiet.IsPresent && !NoInteractive.IsPresent &&
                          SpectrePipelineConsoleUi.ShouldUseInteractiveView(isVerbose);
        var buffer = interactive || Quiet.IsPresent || ExitCode.IsPresent
            ? new BufferedLogger { IsVerbose = isVerbose }
            : null;
        var logger = new CmdletWarningLogger(this, buffer is null ? new CmdletLogger(this, isVerbose) : buffer);
        DotNetPublishWorkflowResult workflow;
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
                    OutputPath = OutputPath,
                    MsBuildProperties = ConvertHashtable(MsBuildProperty),
                    SkipInstallers = SkipInstallers.IsPresent,
                    SkipRestore = SkipRestore.IsPresent,
                    SkipBuild = SkipBuild.IsPresent,
                    NoPublishSign = NoPublishSign.IsPresent,
                    JsonOnly = JsonOnly.IsPresent,
                    JsonPath = JsonPath,
                    Plan = Plan.IsPresent,
                    Validate = Validate.IsPresent
                },
                warn: message => logger.Warn(message));


            workflow = new DotNetPublishWorkflowService(
                logger,
                runPublish: (plan, progress) =>
                {
                    var runner = new DotNetPublishPipelineRunner(logger);
                    return interactive
                        ? SpectreDotNetPublishConsoleUi.RunInteractive(
                            plan, preparation.SourceLabel, detailed => runner.Run(plan, detailed))
                        : runner.Run(plan, progress);
                }).Execute(preparation);
        }
        catch (Exception ex) when (ex is not PipelineStoppedException)
        {
            logger.RethrowWarningStop();
            CompleteResult(new DotNetPublishResult { Succeeded = false, ErrorMessage = ex.Message }, ex);
            return;
        }

        // The engine reports step exceptions as results. A PowerShell warning stop must still terminate the cmdlet.
        logger.RethrowWarningStop();
        if (!Quiet.IsPresent && buffer is not null && (!interactive || workflow.Result is null))
        {
            new BufferedLogSupportService().WriteTail(buffer.Entries, new SpectreConsoleLogger { IsVerbose = isVerbose });
        }

        if (!string.IsNullOrWhiteSpace(workflow.JsonOutputPath))
        {
            WriteObject(workflow.JsonOutputPath);
            if (ExitCode.IsPresent) Host.SetShouldExit(0);
            return;
        }
        if (workflow.Plan is not null)
        {
            WriteObject(DotNetPublishPlanRedactor.RedactInPlace(workflow.Plan));
            if (ExitCode.IsPresent) Host.SetShouldExit(0);
            return;
        }
        CompleteResult(workflow.Result ?? new DotNetPublishResult
        {
            Succeeded = false,
            ErrorMessage = "DotNet publish workflow did not produce a result."
        });
    }

    private void CompleteResult(DotNetPublishResult result, Exception? error = null)
    {
        if (!result.Succeeded && !ExitCode.IsPresent)
            WriteError(new ErrorRecord(
                error ?? new InvalidOperationException(result.ErrorMessage ?? "DotNet publish failed."),
                "InvokeDotNetPublishFailed", ErrorCategory.NotSpecified, ConfigPath));
        WriteObject(result);
        if (ExitCode.IsPresent) Host.SetShouldExit(result.Succeeded ? 0 : 1);
    }

    private static Dictionary<string, string>? ConvertHashtable(Hashtable? values)
    {
        if (values is null || values.Count == 0)
            return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in values)
        {
            var trimmedKey = (entry.Key?.ToString() ?? string.Empty).Trim();
            if (trimmedKey.Length == 0)
                continue;
            result[trimmedKey] = entry.Value?.ToString() ?? string.Empty;
        }

        return result.Count == 0 ? null : result;
    }
}
