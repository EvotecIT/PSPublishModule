using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Executes a PowerShell-authored project release object through the unified PowerForge release engine.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "ProjectRelease", SupportsShouldProcess = true)]
[OutputType(typeof(PowerForgeReleaseResult))]
public sealed class InvokeProjectReleaseCommand : PSCmdlet
{
    /// <summary>
    /// PowerShell-authored project/release object.
    /// </summary>
    [Parameter(Mandatory = true)]
    public ConfigurationProject Project { get; set; } = new();

    /// <summary>
    /// Builds the release plan without executing steps.
    /// </summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>
    /// Validates configuration through plan-only execution.
    /// </summary>
    [Parameter]
    public SwitchParameter Validate { get; set; }

    /// <summary>
    /// Enables tool/app GitHub release publishing for this run.
    /// </summary>
    [Parameter]
    public SwitchParameter PublishToolGitHub { get; set; }

    /// <summary>
    /// Skips workspace validation defined by the project object.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipWorkspaceValidation { get; set; }

    /// <summary>
    /// Optional workspace validation config override.
    /// </summary>
    [Parameter]
    public string? WorkspaceConfigPath { get; set; }

    /// <summary>
    /// Optional workspace validation profile override.
    /// </summary>
    [Parameter]
    public string? WorkspaceProfile { get; set; }

    /// <summary>
    /// Optional workspace feature enable list override.
    /// </summary>
    [Parameter]
    public string[]? WorkspaceEnableFeature { get; set; }

    /// <summary>
    /// Optional workspace feature disable list override.
    /// </summary>
    [Parameter]
    public string[]? WorkspaceDisableFeature { get; set; }

    /// <summary>
    /// Disables restore operations for the tool/app publish flow.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipRestore { get; set; }

    /// <summary>
    /// Disables build operations for the tool/app publish flow.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipBuild { get; set; }

    /// <summary>
    /// Optional target-name filter.
    /// </summary>
    [Parameter]
    [Alias("Targets")]
    public string[]? Target { get; set; }

    /// <summary>
    /// Optional runtime filter.
    /// </summary>
    [Parameter]
    [Alias("Runtime", "Rid")]
    public string[]? Runtimes { get; set; }

    /// <summary>
    /// Optional framework filter.
    /// </summary>
    [Parameter]
    [Alias("Framework")]
    public string[]? Frameworks { get; set; }

    /// <summary>
    /// Optional publish style filter.
    /// </summary>
    [Parameter]
    [Alias("Style")]
    public DotNetPublishStyle[]? Styles { get; set; }

    /// <summary>
    /// Optional tool/app output selection.
    /// </summary>
    [Parameter]
    [ValidateSet("Tool", "Portable", "Installer", "Store")]
    public string[]? ToolOutput { get; set; }

    /// <summary>
    /// Optional tool/app output exclusion.
    /// </summary>
    [Parameter]
    [ValidateSet("Tool", "Portable", "Installer", "Store")]
    public string[]? SkipToolOutput { get; set; }

    /// <summary>
    /// Optional output root override for tool/app assets.
    /// </summary>
    [Parameter]
    public string? OutputRoot { get; set; }

    /// <summary>
    /// Optional staged release root override.
    /// </summary>
    [Parameter]
    public string? StageRoot { get; set; }

    /// <summary>
    /// Optional release manifest output path override.
    /// </summary>
    [Parameter]
    public string? ManifestJsonPath { get; set; }

    /// <summary>
    /// Optional release checksums output path override.
    /// </summary>
    [Parameter]
    public string? ChecksumsPath { get; set; }

    /// <summary>
    /// Skips top-level release checksums generation.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipReleaseChecksums { get; set; }

    /// <summary>
    /// Keeps symbol files for tool/app artefacts.
    /// </summary>
    [Parameter]
    public SwitchParameter KeepSymbols { get; set; }

    /// <summary>
    /// Enables signing for tool/app outputs when supported by the project object.
    /// </summary>
    [Parameter]
    public SwitchParameter Sign { get; set; }

    /// <summary>
    /// Optional signing profile override.
    /// </summary>
    [Parameter]
    public string? SignProfile { get; set; }

    /// <summary>
    /// Optional signing tool path override.
    /// </summary>
    [Parameter]
    public string? SignToolPath { get; set; }

    /// <summary>
    /// Optional signing thumbprint override.
    /// </summary>
    [Parameter]
    public string? SignThumbprint { get; set; }

    /// <summary>
    /// Optional signing certificate subject name override.
    /// </summary>
    [Parameter]
    public string? SignSubjectName { get; set; }

    /// <summary>
    /// Optional policy when the configured signing tool is missing.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode? SignOnMissingTool { get; set; }

    /// <summary>
    /// Optional policy when signing fails.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode? SignOnFailure { get; set; }

    /// <summary>
    /// Optional signing timestamp URL override.
    /// </summary>
    [Parameter]
    public string? SignTimestampUrl { get; set; }

    /// <summary>
    /// Optional signing description override.
    /// </summary>
    [Parameter]
    public string? SignDescription { get; set; }

    /// <summary>
    /// Optional signing URL override.
    /// </summary>
    [Parameter]
    public string? SignUrl { get; set; }

    /// <summary>
    /// Optional signing CSP override.
    /// </summary>
    [Parameter]
    public string? SignCsp { get; set; }

    /// <summary>
    /// Optional signing key container override.
    /// </summary>
    [Parameter]
    public string? SignKeyContainer { get; set; }

    /// <summary>
    /// Optional installer MSBuild property overrides in <c>Name=Value</c> form.
    /// </summary>
    [Parameter]
    public string[]? InstallerProperty { get; set; }

    /// <summary>
    /// Sets host exit code: 0 on success, 1 on failure.
    /// </summary>
    [Parameter]
    public SwitchParameter ExitCode { get; set; }

    /// <summary>
    /// Executes the configured project release workflow.
    /// </summary>
    protected override void ProcessRecord()
    {
        var boundParameters = MyInvocation?.BoundParameters;
        var isVerbose = boundParameters?.ContainsKey("Verbose") == true;
        var logger = new CmdletLogger(this, isVerbose);
        var exitCodeMode = ExitCode.IsPresent;

        try
        {
            var projectRoot = ResolveProjectRoot(Project.ProjectRoot);
            var configFullPath = Path.Combine(projectRoot, ".powerforge", "project.release.json");
            var (spec, requestDefaults) = PowerForgeProjectDslMapper.CreateRelease(Project, configFullPath, projectRoot);

            if (!Plan.IsPresent && !Validate.IsPresent &&
                !ShouldProcess(configFullPath, "Execute project release workflow"))
            {
                if (exitCodeMode)
                    Host.SetShouldExit(0);
                return;
            }

            var request = PowerForgeReleaseRequestMapper.Build(
                configFullPath,
                requestDefaults,
                BuildInvocationOptions(boundParameters));

            var result = new PowerForgeReleaseService(logger).Execute(spec, request);
            WriteObject(result);

            if (!result.Success)
                throw new InvalidOperationException(result.ErrorMessage ?? "Project release workflow failed.");

            if (exitCodeMode)
                Host.SetShouldExit(0);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "InvokeProjectReleaseFailed", ErrorCategory.NotSpecified, Project));
            if (exitCodeMode)
                Host.SetShouldExit(1);
        }
    }

    private string ResolveProjectRoot(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            return SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);

        return SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Environment.CurrentDirectory;
    }

    private PowerForgeReleaseInvocationOptions BuildInvocationOptions(IDictionary<string, object>? boundParameters)
    {
        var options = new PowerForgeReleaseInvocationOptions
        {
            PlanOnly = Plan.IsPresent,
            ValidateOnly = Validate.IsPresent,
            ToolsOnly = true,
            PublishToolGitHub = ResolveRequestedFlag(boundParameters, nameof(PublishToolGitHub)),
            SkipWorkspaceValidation = SkipWorkspaceValidation.IsPresent,
            SkipRestore = SkipRestore.IsPresent,
            SkipBuild = SkipBuild.IsPresent,
            SkipReleaseChecksums = SkipReleaseChecksums.IsPresent,
            KeepSymbols = ResolveRequestedFlag(boundParameters, nameof(KeepSymbols)),
            EnableSigning = ResolveRequestedFlag(boundParameters, nameof(Sign)),
            WorkspaceConfigPath = NormalizeNullable(WorkspaceConfigPath),
            WorkspaceProfile = NormalizeNullable(WorkspaceProfile),
            OutputRoot = NormalizeNullable(OutputRoot),
            StageRoot = NormalizeNullable(StageRoot),
            ManifestJsonPath = NormalizeNullable(ManifestJsonPath),
            ChecksumsPath = NormalizeNullable(ChecksumsPath),
            SignProfile = NormalizeNullable(SignProfile),
            SignToolPath = NormalizeNullable(SignToolPath),
            SignThumbprint = NormalizeNullable(SignThumbprint),
            SignSubjectName = NormalizeNullable(SignSubjectName),
            SignOnMissingTool = SignOnMissingTool,
            SignOnFailure = SignOnFailure,
            SignTimestampUrl = NormalizeNullable(SignTimestampUrl),
            SignDescription = NormalizeNullable(SignDescription),
            SignUrl = NormalizeNullable(SignUrl),
            SignCsp = NormalizeNullable(SignCsp),
            SignKeyContainer = NormalizeNullable(SignKeyContainer)
        };

        if (boundParameters?.ContainsKey(nameof(WorkspaceEnableFeature)) == true)
            options.WorkspaceEnableFeatures = NormalizeStrings(WorkspaceEnableFeature);
        if (boundParameters?.ContainsKey(nameof(WorkspaceDisableFeature)) == true)
            options.WorkspaceDisableFeatures = NormalizeStrings(WorkspaceDisableFeature);
        if (boundParameters?.ContainsKey(nameof(Target)) == true)
            options.Targets = NormalizeStrings(Target);
        if (boundParameters?.ContainsKey(nameof(Runtimes)) == true)
            options.Runtimes = NormalizeStrings(Runtimes);
        if (boundParameters?.ContainsKey(nameof(Frameworks)) == true)
            options.Frameworks = NormalizeStrings(Frameworks);
        if (boundParameters?.ContainsKey(nameof(Styles)) == true)
            options.Styles = Styles?.Distinct().ToArray() ?? Array.Empty<DotNetPublishStyle>();
        if (boundParameters?.ContainsKey(nameof(ToolOutput)) == true)
            options.ToolOutputs = ParseToolOutputs(ToolOutput);
        if (boundParameters?.ContainsKey(nameof(SkipToolOutput)) == true)
            options.SkipToolOutputs = ParseToolOutputs(SkipToolOutput);
        if (boundParameters?.ContainsKey(nameof(InstallerProperty)) == true)
            options.InstallerMsBuildProperties = ParseKeyValuePairs(InstallerProperty);

        return options;
    }

    private static bool? ResolveRequestedFlag(IDictionary<string, object>? boundParameters, string parameterName)
    {
        if (boundParameters?.TryGetValue(parameterName, out var value) != true)
            return null;

        return value switch
        {
            SwitchParameter switchParameter => switchParameter.IsPresent,
            bool boolValue => boolValue,
            _ => true
        };
    }

    private static string[] NormalizeStrings(string[]? values)
    {
        if (values is null || values.Length == 0)
            return Array.Empty<string>();

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, string> ParseKeyValuePairs(string[]? values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawValue in values ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                continue;

            var separatorIndex = rawValue.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == rawValue.Length - 1)
                throw new PSArgumentException($"InstallerProperty entries must use Name=Value form. Invalid value: '{rawValue}'.");

            var key = rawValue.Substring(0, separatorIndex).Trim();
            var value = rawValue.Substring(separatorIndex + 1).Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                throw new PSArgumentException($"InstallerProperty entries must use Name=Value form. Invalid value: '{rawValue}'.");

            result[key] = value;
        }

        return result;
    }

    private static string? NormalizeNullable(string? value)
    {
        if (value is null)
            return null;

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static PowerForgeReleaseToolOutputKind[] ParseToolOutputs(string[]? values)
    {
        if (values is null || values.Length == 0)
            return Array.Empty<PowerForgeReleaseToolOutputKind>();

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ParseToolOutput)
            .ToArray();
    }

    private static PowerForgeReleaseToolOutputKind ParseToolOutput(string value)
    {
        if (Enum.TryParse(value, ignoreCase: true, out PowerForgeReleaseToolOutputKind kind))
            return kind;

        throw new PSArgumentException(
            $"Unknown tool output '{value}'. Expected one of: {string.Join(", ", Enum.GetNames(typeof(PowerForgeReleaseToolOutputKind)))}");
    }
}
