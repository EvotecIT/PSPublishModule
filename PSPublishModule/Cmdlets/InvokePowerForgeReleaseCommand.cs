using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Executes the unified repository release workflow from a JSON configuration.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet is the PowerShell entry point for the same unified release engine used by
/// <c>powerforge release</c>. It can coordinate repository package publishing and downloadable
/// tool/app artefacts from one configuration file.
/// </para>
/// </remarks>
/// <example>
/// <summary>Plan the configured release without running build/publish steps</summary>
/// <code>Invoke-PowerForgeRelease -ConfigPath '.\Build\release.json' -Plan</code>
/// </example>
/// <example>
/// <summary>Run tool releases only and publish tool assets to GitHub</summary>
/// <code>Invoke-PowerForgeRelease -ConfigPath '.\Build\release.json' -ToolsOnly -PublishToolGitHub -ExitCode</code>
/// </example>
[Cmdlet(VerbsLifecycle.Invoke, "PowerForgeRelease", SupportsShouldProcess = true, DefaultParameterSetName = ParameterSetConfig)]
[OutputType(typeof(PowerForgeReleaseResult))]
public sealed class InvokePowerForgeReleaseCommand : PSCmdlet
{
    private const string ParameterSetConfig = "Config";
    private const string ParameterSetProject = "Project";

    /// <summary>
    /// Path to the unified release configuration file. When omitted, the cmdlet searches current
    /// and parent directories for standard release config file names.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetConfig)]
    public string? ConfigPath { get; set; }

    /// <summary>
    /// PowerShell-authored project/release object that is translated into the unified release engine.
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetProject)]
    public ConfigurationProject? Project { get; set; }

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
    /// Executes only the package portion of the release.
    /// </summary>
    [Parameter]
    public SwitchParameter PackagesOnly { get; set; }

    /// <summary>
    /// Executes only the module portion of the release.
    /// </summary>
    [Parameter]
    public SwitchParameter ModuleOnly { get; set; }

    /// <summary>
    /// Executes only the tool/app portion of the release.
    /// </summary>
    [Parameter]
    public SwitchParameter ToolsOnly { get; set; }

    /// <summary>
    /// Enables NuGet publishing for this run.
    /// </summary>
    [Parameter]
    public SwitchParameter PublishNuget { get; set; }

    /// <summary>
    /// Enables project/package GitHub release publishing for this run.
    /// </summary>
    [Parameter]
    public SwitchParameter PublishProjectGitHub { get; set; }

    /// <summary>
    /// Enables tool/app GitHub release publishing for this run.
    /// </summary>
    [Parameter]
    public SwitchParameter PublishToolGitHub { get; set; }

    /// <summary>
    /// Optional configuration override.
    /// </summary>
    [Parameter]
    [ValidateSet("Release", "Debug")]
    public string? Configuration { get; set; }

    /// <summary>
    /// Skips the dotnet build step inside the native module-release lane.
    /// </summary>
    [Parameter]
    public SwitchParameter ModuleNoDotnetBuild { get; set; }

    /// <summary>
    /// Optional module version override for the native module-release lane.
    /// </summary>
    [Parameter]
    public string? ModuleVersion { get; set; }

    /// <summary>
    /// Optional prerelease tag override for the native module-release lane.
    /// </summary>
    [Parameter]
    public string? ModulePreReleaseTag { get; set; }

    /// <summary>
    /// Disables signing for the native module-release lane.
    /// </summary>
    [Parameter]
    public SwitchParameter ModuleNoSign { get; set; }

    /// <summary>
    /// Enables signing for the native module-release lane.
    /// </summary>
    [Parameter]
    public SwitchParameter ModuleSignModule { get; set; }

    /// <summary>
    /// Skips workspace validation defined by the release config.
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
    /// Optional legacy tool flavor filter.
    /// </summary>
    [Parameter]
    [Alias("Flavor")]
    [ValidateSet("SingleContained", "SingleFx", "Portable", "Fx")]
    public string[]? Flavors { get; set; }

    /// <summary>
    /// Optional tool/app output selection for DotNetPublish-backed release flows.
    /// </summary>
    [Parameter]
    [ValidateSet("Tool", "Portable", "Installer", "Store")]
    public string[]? ToolOutput { get; set; }

    /// <summary>
    /// Optional tool/app output exclusion for DotNetPublish-backed release flows.
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
    /// Enables signing for tool/app outputs when supported by the release config.
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
    /// Optional package-signing thumbprint override.
    /// </summary>
    [Parameter]
    public string? PackageSignThumbprint { get; set; }

    /// <summary>
    /// Optional package-signing certificate store override.
    /// </summary>
    [Parameter]
    [ValidateSet("CurrentUser", "LocalMachine")]
    public string? PackageSignStore { get; set; }

    /// <summary>
    /// Optional package-signing timestamp URL override.
    /// </summary>
    [Parameter]
    public string? PackageSignTimestampUrl { get; set; }

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
    /// Executes the unified release workflow.
    /// </summary>
    protected override void ProcessRecord()
    {
        var boundParameters = MyInvocation?.BoundParameters;
        var isVerbose = boundParameters?.ContainsKey("Verbose") == true;
        var logger = new CmdletLogger(this, isVerbose);
        var exitCodeMode = ExitCode.IsPresent;

        try
        {
            var scopedCount = (PackagesOnly.IsPresent ? 1 : 0) + (ModuleOnly.IsPresent ? 1 : 0) + (ToolsOnly.IsPresent ? 1 : 0);
            if (scopedCount > 1)
                throw new PSArgumentException("Use at most one of -PackagesOnly, -ModuleOnly, or -ToolsOnly.");

            var usingProjectObject = string.Equals(ParameterSetName, ParameterSetProject, StringComparison.Ordinal);
            PowerForgeReleaseSpec spec;
            PowerForgeReleaseRequest requestDefaults;
            string configFullPath;

            if (usingProjectObject)
            {
                if (Project is null)
                    throw new PSArgumentException("Project is required.");

                var projectRoot = ResolveProjectRoot(Project.ProjectRoot);
                configFullPath = Path.Combine(projectRoot, ".powerforge", "release.project.ps1");
                (spec, requestDefaults) = PowerForgeProjectDslMapper.CreateRelease(Project, configFullPath, projectRoot);
            }
            else
            {
                configFullPath = ResolveConfigPath(ConfigPath);
                spec = LoadConfig(configFullPath);
                requestDefaults = new PowerForgeReleaseRequest
                {
                    ConfigPath = configFullPath
                };
            }

            if (!Plan.IsPresent && !Validate.IsPresent &&
                !ShouldProcess(configFullPath, "Execute unified release workflow"))
            {
                if (exitCodeMode)
                    Host.SetShouldExit(0);
                return;
            }

            var request = BuildRequest(configFullPath, requestDefaults, boundParameters);

            var result = new PowerForgeReleaseService(logger).Execute(spec, request);
            WriteObject(result);

            if (!result.Success)
                throw new InvalidOperationException(result.ErrorMessage ?? "Unified release workflow failed.");

            if (exitCodeMode)
                Host.SetShouldExit(0);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "InvokePowerForgeReleaseFailed", ErrorCategory.NotSpecified, ConfigPath));
            if (exitCodeMode)
                Host.SetShouldExit(1);
        }
    }

    private string ResolveConfigPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            return SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);

        var currentDirectory = SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Environment.CurrentDirectory;
        var found = FindDefaultConfigPath(currentDirectory);
        if (!string.IsNullOrWhiteSpace(found))
            return found!;

        throw new PSArgumentException(
            "ConfigPath is required when no default release config could be found. Searched: powerforge.release.json, .powerforge/release.json, Build/release.json, release.json.");
    }

    private string ResolveProjectRoot(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            return SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);

        return SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Environment.CurrentDirectory;
    }

    private static string? FindDefaultConfigPath(string baseDirectory)
    {
        var candidates = new[]
        {
            "powerforge.release.json",
            Path.Combine(".powerforge", "release.json"),
            Path.Combine("Build", "release.json"),
            "release.json"
        };

        foreach (var directory in EnumerateSelfAndParents(baseDirectory))
        {
            foreach (var relativePath in candidates)
            {
                try
                {
                    var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch (IOException)
                {
                    // best effort only
                }
                catch (UnauthorizedAccessException)
                {
                    // best effort only
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSelfAndParents(string startDirectory)
    {
        var current = string.IsNullOrWhiteSpace(startDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(startDirectory);

        while (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;
            var parent = Directory.GetParent(current);
            if (parent is null)
                yield break;

            current = parent.FullName;
        }
    }

    private static PowerForgeReleaseSpec LoadConfig(string configPath)
    {
        var json = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var spec = JsonSerializer.Deserialize<PowerForgeReleaseSpec>(json, options);
        if (spec is null)
            throw new InvalidOperationException($"Unable to deserialize unified release config: {configPath}");

        return spec;
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
            .Select(value => value.Trim())
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
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private PowerForgeReleaseRequest BuildRequest(
        string configFullPath,
        PowerForgeReleaseRequest defaults,
        IDictionary<string, object>? boundParameters)
    {
        return PowerForgeReleaseRequestMapper.Build(configFullPath, defaults, BuildInvocationOptions(boundParameters));
    }

    private PowerForgeReleaseInvocationOptions BuildInvocationOptions(IDictionary<string, object>? boundParameters)
    {
        var options = new PowerForgeReleaseInvocationOptions
        {
            PlanOnly = Plan.IsPresent,
            ValidateOnly = Validate.IsPresent,
            PackagesOnly = PackagesOnly.IsPresent,
            ModuleOnly = ModuleOnly.IsPresent,
            ToolsOnly = ToolsOnly.IsPresent,
            PublishNuget = ResolveRequestedFlag(boundParameters, nameof(PublishNuget)),
            PublishProjectGitHub = ResolveRequestedFlag(boundParameters, nameof(PublishProjectGitHub)),
            PublishToolGitHub = ResolveRequestedFlag(boundParameters, nameof(PublishToolGitHub)),
            ModuleNoDotnetBuild = ResolveRequestedFlag(boundParameters, nameof(ModuleNoDotnetBuild)),
            ModuleNoSign = ResolveRequestedFlag(boundParameters, nameof(ModuleNoSign)),
            ModuleSignModule = ResolveRequestedFlag(boundParameters, nameof(ModuleSignModule)),
            SkipWorkspaceValidation = SkipWorkspaceValidation.IsPresent,
            SkipRestore = SkipRestore.IsPresent,
            SkipBuild = SkipBuild.IsPresent,
            SkipReleaseChecksums = SkipReleaseChecksums.IsPresent,
            KeepSymbols = ResolveRequestedFlag(boundParameters, nameof(KeepSymbols)),
            EnableSigning = ResolveRequestedFlag(boundParameters, nameof(Sign)),
            Configuration = NormalizeNullable(Configuration),
            ModuleVersion = NormalizeNullable(ModuleVersion),
            ModulePreReleaseTag = NormalizeNullable(ModulePreReleaseTag),
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
            SignKeyContainer = NormalizeNullable(SignKeyContainer),
            PackageSignThumbprint = NormalizeNullable(PackageSignThumbprint),
            PackageSignStore = NormalizeNullable(PackageSignStore),
            PackageSignTimestampUrl = NormalizeNullable(PackageSignTimestampUrl)
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
        if (boundParameters?.ContainsKey(nameof(Flavors)) == true)
            options.Flavors = ParseFlavors(Flavors);
        if (boundParameters?.ContainsKey(nameof(ToolOutput)) == true)
            options.ToolOutputs = ParseToolOutputs(ToolOutput);
        if (boundParameters?.ContainsKey(nameof(SkipToolOutput)) == true)
            options.SkipToolOutputs = ParseToolOutputs(SkipToolOutput);
        if (boundParameters?.ContainsKey(nameof(InstallerProperty)) == true)
            options.InstallerMsBuildProperties = ParseKeyValuePairs(InstallerProperty);

        return options;
    }

    private static PowerForgeToolReleaseFlavor[] ParseFlavors(string[]? values)
    {
        if (values is null || values.Length == 0)
            return Array.Empty<PowerForgeToolReleaseFlavor>();

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ParseFlavor)
            .ToArray();
    }

    private static PowerForgeToolReleaseFlavor ParseFlavor(string value)
    {
        if (Enum.TryParse(value, ignoreCase: true, out PowerForgeToolReleaseFlavor flavor))
            return flavor;

        throw new PSArgumentException(
            $"Unknown release flavor '{value}'. Expected one of: {string.Join(", ", Enum.GetNames(typeof(PowerForgeToolReleaseFlavor)))}");
    }

    private static PowerForgeReleaseToolOutputKind[] ParseToolOutputs(string[]? values)
    {
        if (values is null || values.Length == 0)
            return Array.Empty<PowerForgeReleaseToolOutputKind>();

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
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
