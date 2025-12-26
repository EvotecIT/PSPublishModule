using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
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

        var moduleName = ParameterSetName == ParameterSetConfiguration
            ? ResolveModuleNameFromConfiguration(Configuration)
            : ModuleName;

        if (string.IsNullOrWhiteSpace(moduleName))
            throw new PSArgumentException("ModuleName is required.");

        var (projectRoot, basePathForScaffold) = ResolveProjectPaths(moduleName);
        if (basePathForScaffold is not null)
        {
            if (!Directory.Exists(basePathForScaffold))
            {
                WriteHostMessage($"[-] Path {basePathForScaffold} doesn't exists. Please create it, before continuing.", ConsoleColor.Red);
                if (ExitCode.IsPresent) Host.SetShouldExit(1);
                return;
            }

            EnsureModuleScaffold(basePathForScaffold, projectRoot, moduleName);
        }

        var useLegacy =
            Legacy.IsPresent ||
            ParameterSetName == ParameterSetConfiguration ||
            Settings is not null;

#pragma warning disable CA1031 // Legacy cmdlet UX: capture and report errors consistently
        var success = false;
        try
        {
            var logger = new HostLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
            if (Legacy.IsPresent && Settings is null && ParameterSetName != ParameterSetConfiguration)
                logger.Warn("Legacy PowerShell build pipeline has been removed; using PowerForge pipeline.");

            var segments = useLegacy
                ? (ParameterSetName == ParameterSetConfiguration
                    ? LegacySegmentAdapter.CollectFromLegacyConfiguration(Configuration)
                    : LegacySegmentAdapter.CollectFromSettings(Settings))
                : Array.Empty<IConfigurationSegment>();

            var baseVersion = ResolveModuleVersion(projectRoot, moduleName);
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

            var runner = new ModulePipelineRunner(logger);
            var result = runner.Run(pipelineSpec);
            if (result.InstallResult is not null)
                logger.Success($"Installed {moduleName} {result.InstallResult.Version}");

            success = true;
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, useLegacy ? "InvokeModuleBuildDslFailed" : "InvokeModuleBuildPowerForgeFailed", ErrorCategory.NotSpecified, null));
            success = false;
        }
#pragma warning restore CA1031

        var elapsed = sw.Elapsed;
        var elapsedText =
            $"{elapsed.Days} days, {elapsed.Hours} hours, {elapsed.Minutes} minutes, {elapsed.Seconds} seconds, {elapsed.Milliseconds} milliseconds";

        if (success)
        {
            WriteHostMessage("[i] Module Build Completed ", ConsoleColor.Green, noNewLine: true);
            WriteHostMessage($"[Time Total: {elapsedText}]", ConsoleColor.Green);
            if (ExitCode.IsPresent) Host.SetShouldExit(0);
        }
        else
        {
            WriteHostMessage("[i] Module Build Failed ", ConsoleColor.Red, noNewLine: true);
            WriteHostMessage($"[Time Total: {elapsedText}]", ConsoleColor.Red);
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

    private static string ResolveModuleNameFromConfiguration(IDictionary configuration)
    {
        if (configuration is null) return string.Empty;

        object? info = null;
        if (configuration.Contains("Information"))
        {
            info = configuration["Information"];
        }

        var moduleName = TryGetValue(info, "ModuleName");
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new PSArgumentException("Configuration.Information.ModuleName is required.");

        return moduleName;
    }

    private static string? TryGetValue(object? obj, string key)
    {
        if (obj is null) return null;

        if (obj is IDictionary d && d.Contains(key))
        {
            try { return d[key]?.ToString(); } catch { return null; }
        }

        if (obj is PSObject pso)
        {
            var prop = pso.Properties[key];
            return prop?.Value?.ToString();
        }

        var t = obj.GetType();
        var pi = t.GetProperty(key);
        return pi?.GetValue(obj)?.ToString();
    }

    private void EnsureModuleScaffold(string basePath, string fullProjectPath, string moduleName)
    {
        if (Directory.Exists(fullProjectPath))
        {
            WriteHostMessage($"[i] Module {moduleName} ({fullProjectPath}) already exists. Skipping initial steps", ConsoleColor.DarkGray);
            return;
        }

        WriteHostMessage($"[i] Preparing module structure for {moduleName} in {basePath}", ConsoleColor.DarkGray);

        Directory.CreateDirectory(fullProjectPath);
        foreach (var folder in new[] { "Private", "Public", "Examples", "Ignore", "Build" })
        {
            Directory.CreateDirectory(System.IO.Path.Combine(fullProjectPath, folder));
        }

        var dataDir = ResolveDataDirectory();
        var filesToCopy = new (string Source, string Dest, bool Patch)[]
        {
            (System.IO.Path.Combine(dataDir, "Example-Gitignore.txt"), System.IO.Path.Combine(fullProjectPath, ".gitignore"), false),
            (System.IO.Path.Combine(dataDir, "Example-CHANGELOG.MD"), System.IO.Path.Combine(fullProjectPath, "CHANGELOG.MD"), false),
            (System.IO.Path.Combine(dataDir, "Example-README.MD"), System.IO.Path.Combine(fullProjectPath, "README.MD"), false),
            (System.IO.Path.Combine(dataDir, "Example-LicenseMIT.txt"), System.IO.Path.Combine(fullProjectPath, "LICENSE"), false),
            (System.IO.Path.Combine(dataDir, "Example-ModuleBuilder.txt"), System.IO.Path.Combine(fullProjectPath, "Build", "Build-Module.ps1"), true),
            (System.IO.Path.Combine(dataDir, "Example-ModulePSM1.txt"), System.IO.Path.Combine(fullProjectPath, $"{moduleName}.psm1"), false),
            (System.IO.Path.Combine(dataDir, "Example-ModulePSD1.txt"), System.IO.Path.Combine(fullProjectPath, $"{moduleName}.psd1"), true),
        };

        var guid = Guid.NewGuid().ToString();

        foreach (var f in filesToCopy)
        {
            if (File.Exists(f.Dest)) continue;

            WriteHostMessage($"   [+] Copying '{System.IO.Path.GetFileName(f.Dest)}' file ({f.Source})", ConsoleColor.DarkGray);
            File.Copy(f.Source, f.Dest, overwrite: false);

            if (f.Patch)
            {
                PatchInitialModuleTemplate(f.Dest, moduleName, guid);
            }
        }

        WriteHostMessage($"[i] Preparing module structure for {moduleName} in {basePath}. Completed.", ConsoleColor.DarkGray);
    }

    private string ResolveDataDirectory()
    {
        var moduleBase = MyInvocation.MyCommand?.Module?.ModuleBase;
        if (!string.IsNullOrWhiteSpace(moduleBase))
        {
            var direct = System.IO.Path.Combine(moduleBase, "Data");
            if (Directory.Exists(direct)) return direct;

            var parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(moduleBase, "..", "Data"));
            if (Directory.Exists(parent)) return parent;
        }

        var fallback = System.IO.Path.Combine(SessionState.Path.CurrentFileSystemLocation.Path, "Data");
        if (Directory.Exists(fallback)) return fallback;

        throw new DirectoryNotFoundException("Module Data directory not found (expected 'Data' next to the module).");
    }

    private static void PatchInitialModuleTemplate(string filePath, string moduleName, string guid)
    {
        var content = File.ReadAllText(filePath);
        content = content.Replace("`$GUID", guid).Replace("`$ModuleName", moduleName);
        File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string ResolveModuleVersion(string projectRoot, string moduleName)
    {
        var psd1 = System.IO.Path.Combine(projectRoot, $"{moduleName}.psd1");
        if (File.Exists(psd1) &&
            ManifestEditor.TryGetTopLevelString(psd1, "ModuleVersion", out var version) &&
            !string.IsNullOrWhiteSpace(version))
        {
            return version!;
        }
        return "1.0.0";
    }

    private sealed class HostLogger : ILogger
    {
        private readonly InvokeModuleBuildCommand _cmdlet;

        public bool IsVerbose { get; set; }

        public HostLogger(InvokeModuleBuildCommand cmdlet, bool isVerbose)
        {
            _cmdlet = cmdlet;
            IsVerbose = isVerbose;
        }

        public void Info(string message) => _cmdlet.WriteHostMessage("[i] " + message, ConsoleColor.DarkGray);
        public void Success(string message) => _cmdlet.WriteHostMessage("[+] " + message, ConsoleColor.Green);
        public void Warn(string message) => _cmdlet.WriteHostMessage("[-] " + message, ConsoleColor.Yellow);
        public void Error(string message) => _cmdlet.WriteHostMessage("[e] " + message, ConsoleColor.Red);
        public void Verbose(string message)
        {
            if (!IsVerbose) return;
            _cmdlet.WriteHostMessage("[v] " + message, ConsoleColor.DarkGray);  
        }
    }

    private void WriteHostMessage(string message, ConsoleColor color, bool noNewLine = false)
    {
        try
        {
            if (Host?.UI?.RawUI is not null)
            {
                Host.UI.Write(color, Host.UI.RawUI.BackgroundColor,
                    message + (noNewLine ? string.Empty : Environment.NewLine));
                return;
            }
        }
        catch
        {
            // ignore and fall back
        }

        // Best-effort: do not write to the pipeline (Write-Host semantics).
        WriteVerbose(message);
    }
}

