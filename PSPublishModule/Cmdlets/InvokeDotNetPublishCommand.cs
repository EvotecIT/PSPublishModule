using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        var sourceLabel = string.Empty;

        try
        {
            var spec = LoadSpec(logger, ref sourceLabel);
            if (!string.IsNullOrWhiteSpace(Profile))
                spec.Profile = Profile!.Trim();
            ApplyOverrides(spec);

            if (JsonOnly.IsPresent)
            {
                var jsonFullPath = ResolveJsonOutputPath(spec, sourceLabel);
                WriteSpecJson(spec, jsonFullPath);
                logger.Success($"Wrote DotNet publish JSON: {jsonFullPath}");
                WriteObject(jsonFullPath);
                if (exitCodeMode) Host.SetShouldExit(0);
                return;
            }

            var runner = new DotNetPublishPipelineRunner(logger);
            var plan = runner.Plan(spec, sourceLabel);

            if (Plan.IsPresent || Validate.IsPresent)
            {
                if (Validate.IsPresent)
                    logger.Success($"DotNet publish config is valid ({plan.Steps.Length} step(s), {plan.Targets.Length} target(s)).");
                WriteObject(plan);
                if (exitCodeMode) Host.SetShouldExit(0);
                return;
            }

            var result = runner.Run(plan, progress: null);
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

    private DotNetPublishSpec LoadSpec(ILogger logger, ref string sourceLabel)
    {
        if (ParameterSetName == ParameterSetConfig)
        {
            var full = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ConfigPath);
            if (!File.Exists(full))
                throw new FileNotFoundException($"Config file not found: {full}");

            sourceLabel = full;
            return ParseSpecJson(File.ReadAllText(full), full);
        }

        var currentPath = SessionState.Path.CurrentFileSystemLocation.Path;
        sourceLabel = Path.Combine(currentPath, "powerforge.dotnetpublish.dsl.json");
        var spec = DotNetPublishDslComposer.ComposeFromSettings(Settings, new DotNetPublishSpec(), message => WriteWarning(message));
        if ((spec.Targets ?? Array.Empty<DotNetPublishTarget>()).Length == 0)
            logger.Warn("No DotNet publish targets were defined.");

        return spec;
    }

    private string ResolveJsonOutputPath(DotNetPublishSpec spec, string sourceLabel)
    {
        if (!string.IsNullOrWhiteSpace(JsonPath))
            return SessionState.Path.GetUnresolvedProviderPathFromPSPath(JsonPath);

        if (ParameterSetName == ParameterSetConfig && !string.IsNullOrWhiteSpace(sourceLabel))
        {
            var baseDir = Path.GetDirectoryName(sourceLabel);
            if (!string.IsNullOrWhiteSpace(baseDir))
                return Path.Combine(baseDir, "powerforge.dotnetpublish.generated.json");
        }

        var projectRoot = spec.DotNet?.ProjectRoot;
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            var root = SessionState.Path.GetUnresolvedProviderPathFromPSPath(projectRoot);
            return Path.Combine(root, "powerforge.dotnetpublish.json");
        }

        return Path.Combine(SessionState.Path.CurrentFileSystemLocation.Path, "powerforge.dotnetpublish.json");
    }

    private static DotNetPublishSpec ParseSpecJson(string json, string pathLabel)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            options.Converters.Add(new JsonStringEnumConverter());

            var spec = JsonSerializer.Deserialize<DotNetPublishSpec>(json, options);
            if (spec is null)
                throw new InvalidOperationException("Parsed config is null.");
            return spec;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse DotNet publish config '{pathLabel}'. {ex.Message}", ex);
        }
    }

    private static void WriteSpecJson(DotNetPublishSpec spec, string jsonFullPath)
    {
        var outDir = Path.GetDirectoryName(jsonFullPath);
        if (!string.IsNullOrWhiteSpace(outDir))
            Directory.CreateDirectory(outDir);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var json = JsonSerializer.Serialize(spec, options) + Environment.NewLine;
        File.WriteAllText(jsonFullPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private void ApplyOverrides(DotNetPublishSpec spec)
    {
        if (spec is null) return;

        var overrideTargets = NormalizeStrings(Target);
        var overrideRuntimes = NormalizeStrings(Runtimes);
        var overrideFrameworks = NormalizeStrings(Frameworks);
        var overrideStyles = NormalizeStyles(Styles);

        if (overrideTargets.Length > 0)
        {
            var knownTargets = spec.Targets ?? Array.Empty<DotNetPublishTarget>();
            var selected = new HashSet<string>(overrideTargets, StringComparer.OrdinalIgnoreCase);
            var missing = selected
                .Where(name => knownTargets.All(t => !string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"Unknown target override value(s): {string.Join(", ", missing)}");

            spec.Targets = knownTargets
                .Where(t => selected.Contains(t.Name))
                .ToArray();

            if (spec.Installers is { Length: > 0 })
            {
                spec.Installers = spec.Installers
                    .Where(i =>
                        i is not null
                        && (string.IsNullOrWhiteSpace(i.PrepareFromTarget)
                            || selected.Contains(i.PrepareFromTarget)))
                    .ToArray();
            }
        }

        if (overrideRuntimes.Length > 0
            || overrideFrameworks.Length > 0
            || overrideStyles.Length > 0)
        {
            foreach (var target in spec.Targets ?? Array.Empty<DotNetPublishTarget>())
            {
                target.Publish ??= new DotNetPublishPublishOptions();

                if (overrideRuntimes.Length > 0)
                    target.Publish.Runtimes = overrideRuntimes;

                if (overrideFrameworks.Length > 0)
                {
                    target.Publish.Framework = overrideFrameworks[0];
                    target.Publish.Frameworks = overrideFrameworks;
                }

                if (overrideStyles.Length > 0)
                {
                    target.Publish.Style = overrideStyles[0];
                    target.Publish.Styles = overrideStyles;
                }
            }
        }

        spec.DotNet ??= new DotNetPublishDotNetOptions();
        if (SkipRestore.IsPresent)
        {
            spec.DotNet.Restore = false;
            spec.DotNet.NoRestoreInPublish = true;
        }

        if (SkipBuild.IsPresent)
        {
            spec.DotNet.Build = false;
            spec.DotNet.NoBuildInPublish = true;
        }
    }

    private static string[] NormalizeStrings(string[]? values)
    {
        if (values is null || values.Length == 0) return Array.Empty<string>();

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DotNetPublishStyle[] NormalizeStyles(DotNetPublishStyle[]? values)
    {
        if (values is null || values.Length == 0) return Array.Empty<DotNetPublishStyle>();
        return values.Distinct().ToArray();
    }
}
