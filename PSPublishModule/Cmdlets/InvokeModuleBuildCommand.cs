using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates/updates a module structure and triggers the build pipeline (legacy DSL compatible).
/// </summary>
/// <remarks>
/// <para>
/// This is the primary entry point for building a PowerShell module using PSPublishModule.
/// Configuration is provided via a DSL using <c>New-Configuration*</c> cmdlets (typically inside the <c>-Settings</c>
/// scriptblock) and then executed by the PowerForge pipeline runner.
/// </para>
/// <para>
/// To generate a reusable <c>powerforge.json</c> configuration file (for the PowerForge CLI) without running any build
/// steps, use <c>-JsonOnly</c> with <c>-JsonPath</c>.
/// </para>
/// <para>
/// When running in an interactive terminal, pipeline execution uses a Spectre.Console progress UI.
/// Redirect output or use <c>-Verbose</c> to force plain, line-by-line output (useful for CI logs).
/// </para>
/// </remarks>
/// <example>
/// <summary>Build a module (DSL) and keep docs in sync</summary>
/// <code>
/// Invoke-ModuleBuild -ModuleName 'MyModule' -Path 'C:\Git\MyModule' -Settings {
///     New-ConfigurationDocumentation -Enable -UpdateWhenNew -StartClean -Path 'Docs' -PathReadme 'Docs\Readme.md'
/// }
/// </code>
/// </example>
/// <example>
/// <summary>Generate a PowerForge JSON pipeline without running the build</summary>
/// <code>
/// Invoke-ModuleBuild -ModuleName 'MyModule' -Path 'C:\Git\MyModule' -JsonOnly -JsonPath 'C:\Git\MyModule\powerforge.json'
/// </code>
/// </example>
/// <example>
/// <summary>Enforce consistency and compatibility during build (fail CI on issues)</summary>
/// <code>
/// Invoke-ModuleBuild -ModuleName 'MyModule' -Path 'C:\Git\MyModule' -ExitCode -Settings {
///     New-ConfigurationFileConsistency -Enable -FailOnInconsistency -AutoFix -CreateBackups -ExportReport
///     New-ConfigurationCompatibility -Enable -RequireCrossCompatibility -FailOnIncompatibility -ExportReport
/// }
/// </code>
/// </example>
/// <example>
/// <summary>Publish a .NET project into the module as part of the build</summary>
/// <code>
/// Invoke-ModuleBuild -ModuleName 'MyModule' -Path 'C:\Git\MyModule' `
///     -CsprojPath 'C:\Git\MyModule\src\MyModule\MyModule.csproj' -DotNetFramework net8.0 -DotNetConfiguration Release `
///     -Settings { New-ConfigurationBuild -Enable -MergeModuleOnBuild }
/// </code>
/// </example>
/// <example>
/// <summary>Fail CI only on new diagnostics compared to a committed baseline</summary>
/// <code>
/// Invoke-ModuleBuild -ModuleName 'MyModule' -Path 'C:\Git\MyModule' `
///     -DiagnosticsBaselinePath 'C:\Git\MyModule\.powerforge\module-diagnostics-baseline.json' `
///     -FailOnNewDiagnostics -FailOnDiagnosticsSeverity Warning
/// </code>
/// </example>
[Cmdlet(VerbsLifecycle.Invoke, "ModuleBuild", DefaultParameterSetName = ParameterSetModern)]
[Alias("New-PrepareModule", "Build-Module", "Invoke-ModuleBuilder")]
public sealed partial class InvokeModuleBuildCommand : PSCmdlet
{
    private const string ParameterSetModern = "Modern";
    private const string ParameterSetConfiguration = "Configuration";

    /// <summary>
    /// Provides settings for the module in the form of a script block (DSL).
    /// </summary>
    [Parameter(Position = 0, ParameterSetName = ParameterSetModern)]
    public ScriptBlock? Settings { get; set; }

    /// <summary>
    /// Path to the folder where the project exists or should be created. When omitted, uses the parent of the calling script directory.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string? Path { get; set; }

    /// <summary>
    /// Name of the module being built.
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetModern)]
    [Alias("ProjectName")]
    [ValidateNotNullOrEmpty]
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>
    /// Folder name containing functions to export. Default: <c>Public</c>.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string FunctionsToExportFolder { get; set; } = "Public";

    /// <summary>
    /// Folder name containing aliases to export. Default: <c>Public</c>.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string AliasesToExportFolder { get; set; } = "Public";

    /// <summary>
    /// Legacy configuration dictionary for backwards compatibility.
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetConfiguration)]
    public IDictionary Configuration { get; set; } = new OrderedDictionary();

    /// <summary>
    /// Exclude patterns for artefact packaging.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string[] ExcludeFromPackage { get; set; } = { ".*", "Ignore", "Examples", "package.json", "Publish", "Docs" };

    /// <summary>
    /// Directory names excluded from staging copy (matched by directory name, not by path).
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    public string[] ExcludeDirectories { get; set; } =
    {
        ".git",
        ".vs",
        ".vscode",
        "bin",
        "obj",
        "packages",
        "node_modules",
        "Artefacts",
        "Build",
        "Docs",
        "Documentation",
        "Examples",
        "Ignore",
        "Publish",
        "Tests",
    };

    /// <summary>
    /// File names excluded from staging copy (matched by file name, not by path).
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    public string[] ExcludeFiles { get; set; } = { ".gitignore" };

    /// <summary>
    /// Include patterns for root files in artefacts.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string[] IncludeRoot { get; set; } = { "*.psm1", "*.psd1", "*.Libraries.ps1", "License*" };

    /// <summary>
    /// Folders from which to include <c>.ps1</c> files in artefacts.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string[] IncludePS1 { get; set; } = { "Private", "Public", "Enums", "Classes" };

    /// <summary>
    /// Folders from which to include all files in artefacts.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string[] IncludeAll { get; set; } = { "Images", "Resources", "Templates", "Bin", "Lib", "Data", "en-US" };

    /// <summary>
    /// Optional script block executed during staging that can add custom files/folders to the build.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public ScriptBlock? IncludeCustomCode { get; set; }

    /// <summary>
    /// Advanced hashtable form for includes (maps IncludeRoot/IncludePS1/IncludeAll etc).
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public IDictionary? IncludeToArray { get; set; }

    /// <summary>
    /// Alternate relative path for .NET Core-targeted libraries folder. Default: <c>Lib/Core</c>.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string LibrariesCore { get; set; } = System.IO.Path.Combine("Lib", "Core");

    /// <summary>
    /// Alternate relative path for .NET Framework-targeted libraries folder. Default: <c>Lib/Default</c>.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string LibrariesDefault { get; set; } = System.IO.Path.Combine("Lib", "Default");

    /// <summary>
    /// Alternate relative path for .NET Standard-targeted libraries folder. Default: <c>Lib/Standard</c>.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string LibrariesStandard { get; set; } = System.IO.Path.Combine("Lib", "Standard");

    /// <summary>
    /// Compatibility switch. Historically forced the PowerShell-script build pipeline; the build now always runs through the C# PowerForge pipeline.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    public SwitchParameter Legacy { get; set; }

    /// <summary>
    /// Disables the interactive progress UI and emits plain log output.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    public SwitchParameter NoInteractive { get; set; }

    /// <summary>Staging directory for the PowerForge pipeline. When omitted, a temporary folder is generated.</summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string? StagingPath { get; set; }

    /// <summary>Optional path to a .NET project (.csproj) to publish into the module.</summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string? CsprojPath { get; set; }

    /// <summary>Build configuration for publishing the .NET project (Release or Debug).</summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [ValidateSet("Release", "Debug")]
    public string DotNetConfiguration { get; set; } = "Release";

    /// <summary>Target frameworks to publish (e.g., net472, net8.0).</summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string[] DotNetFramework { get; set; } = { "net472", "net8.0" };

    /// <summary>Skips installing the module after build.</summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public SwitchParameter SkipInstall { get; set; }

    /// <summary>Installation strategy used when installing the module.</summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public InstallationStrategy InstallStrategy { get; set; } = InstallationStrategy.AutoRevision;

    /// <summary>Number of versions to keep per module root when installing.</summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public int KeepVersions { get; set; } = 3;

    /// <summary>Destination module roots for install. When omitted, defaults are used.</summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string[]? InstallRoots { get; set; }

    /// <summary>How to handle legacy flat installs found under module roots.</summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public LegacyFlatModuleHandling LegacyFlatHandling { get; set; } = LegacyFlatModuleHandling.Warn;

    /// <summary>Version folders to preserve when pruning installed versions.</summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string[]? PreserveInstallVersions { get; set; }

    /// <summary>Keep staging directory after build/install.</summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public SwitchParameter KeepStaging { get; set; }

    /// <summary>
    /// Generates a PowerForge pipeline JSON file and exits without running the build pipeline.
    /// Intended for migrating legacy DSL scripts to <c>powerforge</c> CLI configuration.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    public SwitchParameter JsonOnly { get; set; }

    /// <summary>
    /// Output path for the generated pipeline JSON file (used with <see cref="JsonOnly"/>).
    /// Defaults to <c>powerforge.json</c> in the project root.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    public string? JsonPath { get; set; }

    /// <summary>
    /// Optional path to a diagnostics baseline file used to compare current issues with known issues.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    public string? DiagnosticsBaselinePath { get; set; }

    /// <summary>
    /// Writes a diagnostics baseline file from the current run.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    public SwitchParameter GenerateDiagnosticsBaseline { get; set; }

    /// <summary>
    /// Updates a diagnostics baseline file from the current run.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    public SwitchParameter UpdateDiagnosticsBaseline { get; set; }

    /// <summary>
    /// Fails the build when diagnostics appear that are not present in the loaded baseline.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    public SwitchParameter FailOnNewDiagnostics { get; set; }

    /// <summary>
    /// Fails the build when diagnostics at or above the specified severity are present.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    [ValidateSet(nameof(BuildDiagnosticSeverity.Warning), nameof(BuildDiagnosticSeverity.Error))]
    public BuildDiagnosticSeverity? FailOnDiagnosticsSeverity { get; set; }

    /// <summary>
    /// Optional module roots to scan for deterministic binary conflict diagnostics.
    /// When provided, conflict findings can participate in diagnostics baselines and policy.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    public string[]? DiagnosticsBinaryConflictSearchRoot { get; set; }

    /// <summary>
    /// When specified, requests the host to exit with code 0 on success and 1 on failure.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    [Parameter(ParameterSetName = ParameterSetConfiguration)]
    public SwitchParameter ExitCode { get; set; }

    /// <summary>
    /// Executes module scaffolding (when needed) and triggers the build pipeline.
    /// </summary>
    protected override void ProcessRecord()
    {
        var sw = Stopwatch.StartNew();
        var boundParameters = MyInvocation?.BoundParameters;
        var isVerbose = boundParameters?.ContainsKey("Verbose") == true;
        var exitCodeMode = ExitCode.IsPresent ||
                           (boundParameters?.TryGetValue(nameof(ExitCode), out var exitCodeValue) == true && IsTrue(exitCodeValue));

        ConsoleEncoding.EnsureUtf8();
        try
        {
            if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
                Spectre.Console.AnsiConsole.Profile.Capabilities.Unicode = true;
        }
        catch
        {
            // best effort only
        }
        ILogger logger = new SpectreConsoleLogger { IsVerbose = isVerbose };
        var preparation = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
        {
            ParameterSetName = ParameterSetName,
            Settings = Settings,
            Configuration = Configuration,
            ModuleName = ModuleName,
            InputPath = Path,
            StagingPath = StagingPath,
            CsprojPath = CsprojPath,
            DotNetConfiguration = DotNetConfiguration,
            DotNetFramework = DotNetFramework,
            DotNetFrameworkWasBound = boundParameters?.ContainsKey(nameof(DotNetFramework)) == true,
            Legacy = Legacy.IsPresent,
            SkipInstall = SkipInstall.IsPresent,
            InstallStrategy = InstallStrategy,
            InstallStrategyWasBound = boundParameters?.ContainsKey(nameof(InstallStrategy)) == true,
            KeepVersions = KeepVersions,
            KeepVersionsWasBound = boundParameters?.ContainsKey(nameof(KeepVersions)) == true,
            InstallRoots = InstallRoots,
            InstallRootsWasBound = boundParameters?.ContainsKey(nameof(InstallRoots)) == true,
            LegacyFlatHandling = LegacyFlatHandling,
            LegacyFlatHandlingWasBound = boundParameters?.ContainsKey(nameof(LegacyFlatHandling)) == true,
            PreserveInstallVersions = PreserveInstallVersions,
            PreserveInstallVersionsWasBound = boundParameters?.ContainsKey(nameof(PreserveInstallVersions)) == true,
            KeepStaging = KeepStaging.IsPresent,
            ExcludeDirectories = ExcludeDirectories ?? Array.Empty<string>(),
            ExcludeFiles = ExcludeFiles ?? Array.Empty<string>(),
            DiagnosticsBaselinePath = DiagnosticsBaselinePath,
            GenerateDiagnosticsBaseline = GenerateDiagnosticsBaseline.IsPresent,
            UpdateDiagnosticsBaseline = UpdateDiagnosticsBaseline.IsPresent,
            FailOnNewDiagnostics = FailOnNewDiagnostics.IsPresent,
            FailOnDiagnosticsSeverity = FailOnDiagnosticsSeverity,
            DiagnosticsBinaryConflictSearchRoot = DiagnosticsBinaryConflictSearchRoot,
            JsonOnly = JsonOnly.IsPresent,
            JsonPath = JsonPath,
            CurrentPath = SessionState.Path.CurrentFileSystemLocation.Path,
            ScriptRoot = MyInvocation?.PSScriptRoot,
            ResolvePath = path => SessionState.Path.GetUnresolvedProviderPathFromPSPath(path)
        });
        var moduleName = preparation.ModuleName;
        var projectRoot = preparation.ProjectRoot;
        var basePathForScaffold = preparation.BasePathForScaffold;
        if (basePathForScaffold is not null)
        {
            if (!Directory.Exists(basePathForScaffold))
            {
                logger.Error($"Path '{basePathForScaffold}' does not exist. Please create it before continuing.");
                if (exitCodeMode) Host.SetShouldExit(1);
                return;
            }

            var scaffolder = new ModuleScaffoldService(logger);
            var moduleBase = MyInvocation?.MyCommand?.Module?.ModuleBase;
            var templates = !string.IsNullOrWhiteSpace(moduleBase) ? System.IO.Path.Combine(moduleBase, "Data") : null;
            scaffolder.EnsureScaffold(new ModuleScaffoldSpec
            {
                ProjectRoot = projectRoot,
                ModuleName = moduleName,
                TemplateRootPath = templates
            });
        }

        var useLegacy =
            Legacy.IsPresent ||
            ParameterSetName == ParameterSetConfiguration ||
            Settings is not null;

#pragma warning disable CA1031 // Legacy cmdlet UX: capture and report errors consistently
        var success = false;
        BufferingLogger? interactiveBuffer = null;
        ModulePipelinePlan? lastPlan = null;
        bool usedInteractiveView = false;
        try
        {
            if (Legacy.IsPresent && Settings is null && ParameterSetName != ParameterSetConfiguration)
                logger.Warn("Legacy PowerShell build pipeline has been removed; using PowerForge pipeline.");
            var pipelineSpec = preparation.PipelineSpec;

            if (JsonOnly.IsPresent)
            {
                var jsonFullPath = preparation.JsonOutputPath!;
                new ModuleBuildPreparationService().WritePipelineSpecJson(pipelineSpec, jsonFullPath);
                logger.Success($"Wrote pipeline JSON: {jsonFullPath}");
            }
            else
            {
                var planningRunner = new ModulePipelineRunner(logger);
                var plan = planningRunner.Plan(pipelineSpec);
                lastPlan = plan;

                var interactive = !NoInteractive.IsPresent &&
                    SpectrePipelineConsoleUi.ShouldUseInteractiveView(isVerbose);
                usedInteractiveView = interactive;
                var result = interactive
                    ? SpectrePipelineConsoleUi.RunInteractive(
                        runner: new ModulePipelineRunner(interactiveBuffer = new BufferingLogger { IsVerbose = isVerbose }),
                        spec: pipelineSpec,
                        plan: plan,
                        configLabel: useLegacy ? "dsl" : "cmdlet")
                    : planningRunner.Run(pipelineSpec, plan);

                SpectrePipelineConsoleUi.WriteSummary(result);
            }

            success = true;
        }
        catch (Exception ex)
        {
            var policyFailure = ex as ModulePipelineDiagnosticsPolicyException;
            var wrotePolicySummary = false;
            if (policyFailure is not null)
            {
                try
                {
                    SpectrePipelineConsoleUi.WriteSummary(policyFailure.Result);
                    wrotePolicySummary = true;
                }
                catch
                {
                    wrotePolicySummary = false;
                }
            }

            // When -ExitCode is used, treat the cmdlet as an "app-like" command:
            // print friendly output and set the host exit code, but avoid emitting a PowerShell error record
            // (which adds noisy invocation context like script line numbers).
            // Additionally, avoid emitting a PowerShell error record when using the interactive Spectre.Console view
            // because it breaks the live UI experience with the default PowerShell formatting.
            var emitErrorRecord = !exitCodeMode && !usedInteractiveView && policyFailure is null;
            if (emitErrorRecord)
                WriteError(new ErrorRecord(ex, useLegacy ? "InvokeModuleBuildDslFailed" : "InvokeModuleBuildPowerForgeFailed", ErrorCategory.NotSpecified, null));
            if (interactiveBuffer is not null && interactiveBuffer.Entries.Count > 0)
                WriteLogTail(interactiveBuffer, logger);
            if (usedInteractiveView && lastPlan is not null && !wrotePolicySummary)
            {
                try { SpectrePipelineConsoleUi.WriteFailureSummary(lastPlan, ex); }
                catch { /* best effort */ }
            }
            success = false;
        }
#pragma warning restore CA1031

        var elapsed = sw.Elapsed;
        var elapsedText = FormatDuration(elapsed);

        if (success)
        {
            logger.Success(JsonOnly.IsPresent
                ? $"Pipeline config generated in {elapsedText}"
                : $"Module build completed in {elapsedText}");
            if (exitCodeMode) Host.SetShouldExit(0);
        }
        else
        {
            logger.Error(JsonOnly.IsPresent
                ? $"Pipeline config generation failed in {elapsedText}"
                : $"Module build failed in {elapsedText}");
            if (exitCodeMode) Host.SetShouldExit(1);
        }

        return;
    }

    private static void WriteLogTail(BufferingLogger buffer, ILogger logger, int maxEntries = 80)
    {
        if (buffer is null) return;
        if (buffer.Entries.Count == 0) return;

        maxEntries = Math.Max(1, maxEntries);
        var total = buffer.Entries.Count;
        var start = Math.Max(0, total - maxEntries);
        var shown = total - start;

        logger.Warn($"Last {shown}/{total} log lines:");
        for (int i = start; i < total; i++)
        {
            var e = buffer.Entries[i];
            var msg = e.Message;
            switch (e.Level)
            {
                case "success": logger.Success(msg); break;
                case "warn": logger.Warn(msg); break;
                case "error": logger.Error(msg); break;
                case "verbose": logger.Verbose(msg); break;
                default: logger.Info(msg); break;
            }
        }
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalDays >= 1)
            return $"{(int)elapsed.TotalDays}d {elapsed.Hours}h {elapsed.Minutes}m {elapsed.Seconds}s";
        if (elapsed.TotalHours >= 1)
            return $"{elapsed.Hours}h {elapsed.Minutes}m {elapsed.Seconds}s";
        if (elapsed.TotalMinutes >= 1)
            return $"{elapsed.Minutes}m {elapsed.Seconds}s";
        if (elapsed.TotalSeconds >= 1)
            return $"{elapsed.Seconds}s {elapsed.Milliseconds}ms";
        return $"{elapsed.Milliseconds}ms";
    }

    private static bool IsTrue(object? value)
        => value switch
        {
            SwitchParameter sp => sp.IsPresent,
            bool b => b,
            _ => false
        };
}
