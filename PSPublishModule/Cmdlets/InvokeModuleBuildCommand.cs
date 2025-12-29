using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates/updates a module structure and triggers the build pipeline (legacy DSL compatible).
/// </summary>
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
    public string[] IncludeRoot { get; set; } = { "*.psm1", "*.psd1", "License*" };

    /// <summary>
    /// Folders from which to include <c>.ps1</c> files in artefacts.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string[] IncludePS1 { get; set; } = { "Private", "Public", "Enums", "Classes" };

    /// <summary>
    /// Folders from which to include all files in artefacts.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public string[] IncludeAll { get; set; } = { "Images", "Resources", "Templates", "Bin", "Lib", "Data" };

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

    /// <summary>Keep staging directory after build/install.</summary>
    [Parameter(ParameterSetName = ParameterSetModern)]
    public SwitchParameter KeepStaging { get; set; }

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
        var isVerbose = MyInvocation.BoundParameters.ContainsKey("Verbose");

        ConsoleEncoding.EnsureUtf8();
        ILogger logger = new SpectreConsoleLogger { IsVerbose = isVerbose };

        var moduleName = ParameterSetName == ParameterSetConfiguration
            ? LegacySegmentAdapter.ResolveModuleNameFromLegacyConfiguration(Configuration)
            : ModuleName;

        if (string.IsNullOrWhiteSpace(moduleName))
            throw new PSArgumentException("ModuleName is required.");

        var (projectRoot, basePathForScaffold) = ResolveProjectPaths(moduleName);
        if (basePathForScaffold is not null)
        {
            if (!Directory.Exists(basePathForScaffold))
            {
                logger.Error($"Path '{basePathForScaffold}' does not exist. Please create it before continuing.");
                if (ExitCode.IsPresent) Host.SetShouldExit(1);
                return;
            }

            var scaffolder = new ModuleScaffoldService(logger);
            scaffolder.EnsureScaffold(new ModuleScaffoldSpec { ProjectRoot = projectRoot, ModuleName = moduleName });
        }

        var useLegacy =
            Legacy.IsPresent ||
            ParameterSetName == ParameterSetConfiguration ||
            Settings is not null;

#pragma warning disable CA1031 // Legacy cmdlet UX: capture and report errors consistently
        var success = false;
        BufferingLogger? interactiveBuffer = null;
        try
        {
            if (Legacy.IsPresent && Settings is null && ParameterSetName != ParameterSetConfiguration)
                logger.Warn("Legacy PowerShell build pipeline has been removed; using PowerForge pipeline.");

            var segments = useLegacy
                ? (ParameterSetName == ParameterSetConfiguration
                    ? LegacySegmentAdapter.CollectFromLegacyConfiguration(Configuration)
                    : LegacySegmentAdapter.CollectFromSettings(Settings))
                : Array.Empty<IConfigurationSegment>();

            var baseVersion = "1.0.0";
            var psd1 = System.IO.Path.Combine(projectRoot, $"{moduleName}.psd1");
            if (File.Exists(psd1) &&
                ManifestEditor.TryGetTopLevelString(psd1, "ModuleVersion", out var version) &&
                !string.IsNullOrWhiteSpace(version))
            {
                baseVersion = version!;
            }
            var frameworks = useLegacy && !MyInvocation.BoundParameters.ContainsKey(nameof(DotNetFramework))
                ? Array.Empty<string>()
                : DotNetFramework;

            var pipelineSpec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot,
                    StagingPath = StagingPath,
                    CsprojPath = CsprojPath,
                    Version = baseVersion,
                    Configuration = DotNetConfiguration,
                    Frameworks = frameworks,
                    KeepStaging = KeepStaging.IsPresent,
                    ExcludeDirectories = ExcludeDirectories ?? Array.Empty<string>(),
                    ExcludeFiles = BuildStageExcludeFiles(moduleName),
                },
                Install = new ModulePipelineInstallOptions
                {
                    Enabled = !SkipInstall.IsPresent,
                    Strategy = MyInvocation.BoundParameters.ContainsKey(nameof(InstallStrategy)) ? InstallStrategy : null,
                    KeepVersions = MyInvocation.BoundParameters.ContainsKey(nameof(KeepVersions)) ? KeepVersions : null,
                    Roots = MyInvocation.BoundParameters.ContainsKey(nameof(InstallRoots)) ? (InstallRoots ?? Array.Empty<string>()) : null,
                },
                Segments = segments,
            };

            var planningRunner = new ModulePipelineRunner(logger);
            var plan = planningRunner.Plan(pipelineSpec);

            var interactive = SpectrePipelineConsoleUi.ShouldUseInteractiveView(isVerbose);
            var result = interactive
                ? SpectrePipelineConsoleUi.RunInteractive(
                    runner: new ModulePipelineRunner(interactiveBuffer = new BufferingLogger { IsVerbose = isVerbose }),
                    spec: pipelineSpec,
                    plan: plan,
                    configLabel: useLegacy ? "dsl" : "cmdlet")
                : planningRunner.Run(pipelineSpec, plan);

            SpectrePipelineConsoleUi.WriteSummary(result);

            success = true;
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, useLegacy ? "InvokeModuleBuildDslFailed" : "InvokeModuleBuildPowerForgeFailed", ErrorCategory.NotSpecified, null));
            if (interactiveBuffer is not null && interactiveBuffer.Entries.Count > 0)
                WriteLogTail(interactiveBuffer, logger);
            success = false;
        }
#pragma warning restore CA1031

        var elapsed = sw.Elapsed;
        var elapsedText = FormatDuration(elapsed);

        if (success)
        {
            logger.Success($"Module build completed in {elapsedText}");
            if (ExitCode.IsPresent) Host.SetShouldExit(0);
        }
        else
        {
            logger.Error($"Module build failed in {elapsedText}");
            if (ExitCode.IsPresent) Host.SetShouldExit(1);
        }

        return;
    }

    private (string ProjectRoot, string? BasePathForScaffold) ResolveProjectPaths(string moduleName)
    {
        if (ParameterSetName == ParameterSetModern && !string.IsNullOrWhiteSpace(Path))
        {
            var basePath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
            var fullProjectPath = System.IO.Path.Combine(basePath, moduleName);
            return (fullProjectPath, basePath);
        }

        var scriptRoot = MyInvocation.PSScriptRoot;
        string rootToUse;
        if (!string.IsNullOrWhiteSpace(scriptRoot))
        {
            rootToUse = System.IO.Path.GetFullPath(System.IO.Path.Combine(scriptRoot, ".."));
        }
        else
        {
            rootToUse = SessionState.Path.CurrentFileSystemLocation.Path;
        }

        return (rootToUse, null);
    }

    private string[] BuildStageExcludeFiles(string moduleName)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in (ExcludeFiles ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)))
            set.Add(entry.Trim());

        if (!string.IsNullOrWhiteSpace(moduleName))
            set.Add($"{moduleName}.Tests.ps1");

        return set.ToArray();
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
}

