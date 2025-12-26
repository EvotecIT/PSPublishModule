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
public sealed class InvokeModuleBuildCommand : PSCmdlet
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

        if (!useLegacy)
        {
            try
            {
                var version = ResolveModuleVersion(projectRoot, moduleName);
                var logger = new HostLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
                var pipeline = new ModuleBuildPipeline(logger);

                var stagingWasGenerated = string.IsNullOrWhiteSpace(StagingPath);
                var buildSpec = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = projectRoot,
                    StagingPath = StagingPath,
                    CsprojPath = CsprojPath,
                    Version = version,
                    Configuration = DotNetConfiguration,
                    Frameworks = DotNetFramework ?? Array.Empty<string>(),
                    KeepStaging = KeepStaging.IsPresent,
                };

                var buildResult = pipeline.BuildToStaging(buildSpec);

                if (!SkipInstall.IsPresent)
                {
                    var installSpec = new ModuleInstallSpec
                    {
                        Name = moduleName,
                        Version = version,
                        StagingPath = buildResult.StagingPath,
                        Strategy = InstallStrategy,
                        KeepVersions = KeepVersions,
                        Roots = InstallRoots ?? Array.Empty<string>(),
                    };

                    var installResult = pipeline.InstallFromStaging(installSpec);
                    logger.Success($"Installed {moduleName} {installResult.Version}");
                }

                if (!KeepStaging.IsPresent && stagingWasGenerated)
                {
                    try { Directory.Delete(buildResult.StagingPath, recursive: true); }
                    catch { /* best effort */ }
                }

                var elapsedPf = sw.Elapsed;
                var elapsedTextPf =
                    $"{elapsedPf.Days} days, {elapsedPf.Hours} hours, {elapsedPf.Minutes} minutes, {elapsedPf.Seconds} seconds, {elapsedPf.Milliseconds} milliseconds";

                WriteHostMessage("[i] Module Build Completed ", ConsoleColor.Green, noNewLine: true);
                WriteHostMessage($"[Time Total: {elapsedTextPf}]", ConsoleColor.Green);
                if (ExitCode.IsPresent) Host.SetShouldExit(0);
                return;
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "InvokeModuleBuildPowerForgeFailed", ErrorCategory.NotSpecified, null));
                if (ExitCode.IsPresent) Host.SetShouldExit(1);
                return;
            }
        }

        var success = false;
        try
        {
            var loggerDsl = new HostLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
            if (Legacy.IsPresent && Settings is null && ParameterSetName != ParameterSetConfiguration)
                loggerDsl.Warn("Legacy PowerShell build pipeline has been removed; using PowerForge pipeline.");

            var dsl = ParameterSetName == ParameterSetConfiguration
                ? DslBuildData.FromLegacyConfiguration(Configuration)
                : DslBuildData.FromSettings(Settings);

            var expectedVersion = !string.IsNullOrWhiteSpace(dsl.ModuleVersion)
                ? dsl.ModuleVersion!
                : ResolveModuleVersion(projectRoot, moduleName);

            var localPsd1 = dsl.LocalVersioning
                ? System.IO.Path.Combine(projectRoot, $"{moduleName}.psd1")
                : null;

            var stepper = new ModuleVersionStepper(loggerDsl);
            var version = stepper.Step(expectedVersion, moduleName, localPsd1Path: localPsd1).Version;

            var dotnetConfig = MyInvocation.BoundParameters.ContainsKey(nameof(DotNetConfiguration))
                ? DotNetConfiguration
                : (dsl.DotNetConfiguration ?? "Release");

            var frameworks = MyInvocation.BoundParameters.ContainsKey(nameof(DotNetFramework))
                ? (DotNetFramework ?? Array.Empty<string>())
                : (dsl.DotNetFrameworks ?? Array.Empty<string>());

            var csproj = !string.IsNullOrWhiteSpace(CsprojPath)
                ? CsprojPath
                : dsl.TryResolveCsprojPath(projectRoot, moduleName);

            var stagingWasGenerated = string.IsNullOrWhiteSpace(StagingPath);
            var pipeline = new ModuleBuildPipeline(loggerDsl);

            var buildSpec = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = projectRoot,
                StagingPath = StagingPath,
                CsprojPath = csproj,
                Version = version,
                Configuration = dotnetConfig,
                Frameworks = frameworks,
                Author = dsl.Author,
                CompanyName = dsl.CompanyName,
                Description = dsl.Description,
                Tags = dsl.Tags ?? Array.Empty<string>(),
                IconUri = dsl.IconUri,
                ProjectUri = dsl.ProjectUri,
                KeepStaging = KeepStaging.IsPresent,
            };

            var buildResult = pipeline.BuildToStaging(buildSpec);

            if (dsl.CompatiblePSEditions is { Length: > 0 })
                ManifestEditor.TrySetTopLevelStringArray(buildResult.ManifestPath, "CompatiblePSEditions", dsl.CompatiblePSEditions);

            if (dsl.RequiredModules.Count > 0)
                ManifestEditor.TrySetRequiredModules(buildResult.ManifestPath, dsl.RequiredModules.ToArray());

            if (!SkipInstall.IsPresent)
            {
                var strategy = InstallStrategy;
                if (!MyInvocation.BoundParameters.ContainsKey(nameof(InstallStrategy)) && dsl.InstallStrategy.HasValue)
                    strategy = dsl.InstallStrategy.Value;

                var keep = KeepVersions;
                if (!MyInvocation.BoundParameters.ContainsKey(nameof(KeepVersions)) && dsl.KeepVersions.HasValue)
                    keep = dsl.KeepVersions.Value;

                var roots = InstallRoots ?? Array.Empty<string>();
                if (!MyInvocation.BoundParameters.ContainsKey(nameof(InstallRoots)) && dsl.CompatiblePSEditions is { Length: > 0 })
                    roots = dsl.ResolveInstallRootsFromCompatiblePSEditions();

                var installSpec = new ModuleInstallSpec
                {
                    Name = moduleName,
                    Version = version,
                    StagingPath = buildResult.StagingPath,
                    Strategy = strategy,
                    KeepVersions = keep,
                    Roots = roots,
                };

                var installResult = pipeline.InstallFromStaging(installSpec);
                loggerDsl.Success($"Installed {moduleName} {installResult.Version}");
            }

            if (!KeepStaging.IsPresent && stagingWasGenerated)
            {
                try { Directory.Delete(buildResult.StagingPath, recursive: true); }
                catch { /* best effort */ }
            }

            success = true;
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "InvokeModuleBuildDslFailed", ErrorCategory.NotSpecified, null));
            success = false;
        }
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

    private sealed class DslBuildData
    {
        public string? ModuleVersion { get; private set; }
        public string[]? CompatiblePSEditions { get; private set; }

        public string? Author { get; private set; }
        public string? CompanyName { get; private set; }
        public string? Description { get; private set; }
        public string[]? Tags { get; private set; }
        public string? IconUri { get; private set; }
        public string? ProjectUri { get; private set; }

        public bool LocalVersioning { get; private set; }
        public InstallationStrategy? InstallStrategy { get; private set; }
        public int? KeepVersions { get; private set; }

        public string? DotNetConfiguration { get; private set; }
        public string[]? DotNetFrameworks { get; private set; }
        public string? NetProjectName { get; private set; }
        public string? NetProjectPath { get; private set; }

        public List<ManifestEditor.RequiredModule> RequiredModules { get; } = new();

        public static DslBuildData FromSettings(ScriptBlock? settings)
        {
            var data = new DslBuildData();
            if (settings is null) return data;

            var output = settings.Invoke();
            foreach (var obj in output)
            {
                var baseObj = obj?.BaseObject;
                if (baseObj is null) continue;
                data.ApplySegment(baseObj);
            }

            return data;
        }

        public static DslBuildData FromLegacyConfiguration(IDictionary configuration)
        {
            var data = new DslBuildData();
            if (configuration is null) return data;

            var info = GetDictionary(configuration, "Information");
            var manifest = info is null ? null : GetDictionary(info, "Manifest");
            if (manifest is not null)
            {
                data.ModuleVersion = GetString(manifest, "ModuleVersion");
                data.CompatiblePSEditions = GetStringArray(manifest, "CompatiblePSEditions");

                data.Author = GetString(manifest, "Author");
                data.CompanyName = GetString(manifest, "CompanyName");
                data.Description = GetString(manifest, "Description");

                // Prefer explicit top-level keys from config cmdlets, then fall back to PrivateData.PSData.
                data.Tags = GetStringArray(manifest, "Tags") ?? GetNestedStringArray(manifest, "PrivateData", "PSData", "Tags");
                data.IconUri = GetString(manifest, "IconUri") ?? GetNestedString(manifest, "PrivateData", "PSData", "IconUri");
                data.ProjectUri = GetString(manifest, "ProjectUri") ?? GetNestedString(manifest, "PrivateData", "PSData", "ProjectUri");

                data.AddRequiredModules(GetValue(manifest, "RequiredModules"));
            }

            var steps = GetDictionary(configuration, "Steps");
            if (steps is not null)
            {
                var buildModule = GetDictionary(steps, "BuildModule");
                if (buildModule is not null)
                {
                    data.LocalVersioning = GetBool(buildModule, "LocalVersion");
                    data.InstallStrategy = TryParseInstallationStrategy(GetString(buildModule, "VersionedInstallStrategy"));
                    data.KeepVersions = GetInt(buildModule, "VersionedInstallKeep");
                }

                var buildLibraries = GetDictionary(steps, "BuildLibraries");
                if (buildLibraries is not null)
                {
                    data.DotNetConfiguration = GetString(buildLibraries, "Configuration");
                    data.DotNetFrameworks = GetStringArray(buildLibraries, "Framework");
                    data.NetProjectName = GetString(buildLibraries, "ProjectName");
                    data.NetProjectPath = GetString(buildLibraries, "NETProjectPath");
                }
            }

            return data;
        }

        public string? TryResolveCsprojPath(string projectRoot, string moduleName)
        {
            if (string.IsNullOrWhiteSpace(NetProjectPath))
                return null;

            var projectName = string.IsNullOrWhiteSpace(NetProjectName) ? moduleName : NetProjectName!.Trim();
            var rawPath = NetProjectPath!.Trim().Trim('"');

            var basePath = System.IO.Path.IsPathRooted(rawPath)
                ? System.IO.Path.GetFullPath(rawPath)
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, rawPath));

            if (basePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                return basePath;

            return System.IO.Path.Combine(basePath, projectName + ".csproj");
        }

        public string[] ResolveInstallRootsFromCompatiblePSEditions()
        {
            var compatible = CompatiblePSEditions ?? Array.Empty<string>();
            if (compatible.Length == 0) return Array.Empty<string>();

            var hasDesktop = compatible.Any(s => string.Equals(s, "Desktop", StringComparison.OrdinalIgnoreCase));
            var hasCore = compatible.Any(s => string.Equals(s, "Core", StringComparison.OrdinalIgnoreCase));

            var roots = new List<string>();
            if (System.IO.Path.DirectorySeparatorChar == '\\')
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrWhiteSpace(docs))
                    docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                if (!string.IsNullOrWhiteSpace(docs))
                {
                    if (hasCore) roots.Add(System.IO.Path.Combine(docs, "PowerShell", "Modules"));
                    if (hasDesktop) roots.Add(System.IO.Path.Combine(docs, "WindowsPowerShell", "Modules"));
                }
            }
            else
            {
                var home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrWhiteSpace(home))
                    home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                var dataHome = !string.IsNullOrWhiteSpace(xdgDataHome)
                    ? xdgDataHome
                    : (!string.IsNullOrWhiteSpace(home)
                        ? System.IO.Path.Combine(home!, ".local", "share")
                        : null);

                if (!string.IsNullOrWhiteSpace(dataHome))
                    roots.Add(System.IO.Path.Combine(dataHome!, "powershell", "Modules"));
            }

            return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private void ApplySegment(object segment)
        {
            if (segment is PSObject pso)
                segment = pso.BaseObject;

            if (TryApplyTypedSegment(segment))
                return;

            if (segment is not IDictionary dict)
                return;

            var type = GetString(dict, "Type");
            if (string.IsNullOrWhiteSpace(type))
                return;

            if (string.Equals(type, "Manifest", StringComparison.OrdinalIgnoreCase))
            {
                var conf = GetDictionary(dict, "Configuration");
                if (conf is null) return;

                ModuleVersion = GetString(conf, "ModuleVersion") ?? ModuleVersion;
                CompatiblePSEditions = GetStringArray(conf, "CompatiblePSEditions") ?? CompatiblePSEditions;
                Author = GetString(conf, "Author") ?? Author;
                CompanyName = GetString(conf, "CompanyName") ?? CompanyName;
                Description = GetString(conf, "Description") ?? Description;
                Tags = GetStringArray(conf, "Tags") ?? Tags;
                IconUri = GetString(conf, "IconUri") ?? IconUri;
                ProjectUri = GetString(conf, "ProjectUri") ?? ProjectUri;
                return;
            }

            if (string.Equals(type, "Build", StringComparison.OrdinalIgnoreCase))
            {
                var conf = GetDictionary(dict, "BuildModule");
                if (conf is null) return;

                LocalVersioning = GetBool(conf, "LocalVersion");
                InstallStrategy = TryParseInstallationStrategy(GetString(conf, "VersionedInstallStrategy")) ?? InstallStrategy;
                KeepVersions = GetInt(conf, "VersionedInstallKeep") ?? KeepVersions;
                return;
            }

            if (string.Equals(type, "BuildLibraries", StringComparison.OrdinalIgnoreCase))
            {
                var conf = GetDictionary(dict, "BuildLibraries");
                if (conf is null) return;

                DotNetConfiguration = GetString(conf, "Configuration") ?? DotNetConfiguration;
                DotNetFrameworks = GetStringArray(conf, "Framework") ?? DotNetFrameworks;
                NetProjectName = GetString(conf, "ProjectName") ?? NetProjectName;
                NetProjectPath = GetString(conf, "NETProjectPath") ?? NetProjectPath;
                return;
            }

            if (string.Equals(type, "RequiredModule", StringComparison.OrdinalIgnoreCase))
            {
                AddRequiredModules(GetValue(dict, "Configuration"));
            }
        }

        private bool TryApplyTypedSegment(object segment)
        {
            switch (segment)
            {
                case ConfigurationManifestSegment manifest:
                    ApplyManifest(manifest.Configuration);
                    return true;
                case ConfigurationBuildSegment build:
                    ApplyBuild(build.BuildModule);
                    return true;
                case ConfigurationBuildLibrariesSegment buildLibraries:
                    ApplyBuildLibraries(buildLibraries.BuildLibraries);
                    return true;
                case ConfigurationModuleSegment module:
                    if (module.Kind == PowerForge.ModuleDependencyKind.RequiredModule)
                        AddRequiredModule(module.Configuration);
                    return true;
                default:
                    return false;
            }
        }

        private void ApplyManifest(ManifestConfiguration? config)
        {
            if (config is null) return;

            if (!string.IsNullOrWhiteSpace(config.ModuleVersion))
                ModuleVersion = config.ModuleVersion;

            if (config.CompatiblePSEditions is { Length: > 0 })
                CompatiblePSEditions = config.CompatiblePSEditions;

            if (!string.IsNullOrWhiteSpace(config.Author))
                Author = config.Author;

            if (!string.IsNullOrWhiteSpace(config.CompanyName))
                CompanyName = config.CompanyName;

            if (!string.IsNullOrWhiteSpace(config.Description))
                Description = config.Description;

            if (config.Tags is { Length: > 0 })
                Tags = config.Tags;

            if (!string.IsNullOrWhiteSpace(config.IconUri))
                IconUri = config.IconUri;

            if (!string.IsNullOrWhiteSpace(config.ProjectUri))
                ProjectUri = config.ProjectUri;
        }

        private void ApplyBuild(BuildModuleConfiguration? config)
        {
            if (config is null) return;

            if (config.LocalVersion.HasValue)
                LocalVersioning = config.LocalVersion.Value;

            if (config.VersionedInstallStrategy.HasValue)
                InstallStrategy = config.VersionedInstallStrategy.Value;

            if (config.VersionedInstallKeep.HasValue)
                KeepVersions = config.VersionedInstallKeep.Value;
        }

        private void ApplyBuildLibraries(BuildLibrariesConfiguration? config)
        {
            if (config is null) return;

            if (!string.IsNullOrWhiteSpace(config.Configuration))
                DotNetConfiguration = config.Configuration;

            if (config.Framework is { Length: > 0 })
                DotNetFrameworks = config.Framework;

            if (!string.IsNullOrWhiteSpace(config.ProjectName))
                NetProjectName = config.ProjectName;

            if (!string.IsNullOrWhiteSpace(config.NETProjectPath))
                NetProjectPath = config.NETProjectPath;
        }

        private void AddRequiredModule(ModuleDependencyConfiguration? module)
        {
            if (module is null) return;

            var name = module.ModuleName;
            if (string.IsNullOrWhiteSpace(name)) return;

            RequiredModules.Add(new ManifestEditor.RequiredModule(
                name.Trim(),
                module.ModuleVersion,
                module.RequiredVersion,
                module.Guid));
        }

        private void AddRequiredModules(object? value)
        {
            if (value is null) return;

            if (value is PSObject pso)
                value = pso.BaseObject;

            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                RequiredModules.Add(new ManifestEditor.RequiredModule(s.Trim()));
                return;
            }

            if (value is IDictionary d)
            {
                var name = GetString(d, "ModuleName") ?? GetString(d, "Module");
                if (string.IsNullOrWhiteSpace(name)) return;

                var moduleVersion = GetString(d, "ModuleVersion");
                var requiredVersion = GetString(d, "RequiredVersion");
                var guid = GetString(d, "Guid");
                RequiredModules.Add(new ManifestEditor.RequiredModule(name.Trim(), moduleVersion, requiredVersion, guid));
                return;
            }

            if (value is IEnumerable e && value is not string)
            {
                foreach (var item in e)
                    AddRequiredModules(item);
            }
        }

        private static InstallationStrategy? TryParseInstallationStrategy(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return Enum.TryParse<InstallationStrategy>(value.Trim(), ignoreCase: true, out var parsed)
                ? parsed
                : null;
        }

        private static IDictionary? GetDictionary(IDictionary dict, string key)
        {
            var v = GetValue(dict, key);
            if (v is PSObject pso) v = pso.BaseObject;
            return v as IDictionary;
        }

        private static object? GetValue(IDictionary dict, string key)
        {
            if (dict is null || string.IsNullOrWhiteSpace(key)) return null;

            if (dict.Contains(key))
            {
                try { return dict[key]; } catch { return null; }
            }

            foreach (DictionaryEntry entry in dict)
            {
                var k = entry.Key?.ToString();
                if (k is null) continue;
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }

            return null;
        }

        private static string? GetString(IDictionary dict, string key)
        {
            var v = GetValue(dict, key);
            if (v is PSObject pso) v = pso.BaseObject;
            return v?.ToString();
        }

        private static bool GetBool(IDictionary dict, string key)
        {
            var v = GetValue(dict, key);
            if (v is PSObject pso) v = pso.BaseObject;

            if (v is bool b) return b;
            if (v is SwitchParameter sp) return sp.IsPresent;
            if (v is string s && bool.TryParse(s, out var parsed)) return parsed;
            return false;
        }

        private static int? GetInt(IDictionary dict, string key)
        {
            var v = GetValue(dict, key);
            if (v is PSObject pso) v = pso.BaseObject;

            if (v is int i) return i;
            if (v is long l) return checked((int)l);
            if (v is string s && int.TryParse(s, out var parsed)) return parsed;
            return null;
        }

        private static string[]? GetStringArray(IDictionary dict, string key)
        {
            var v = GetValue(dict, key);
            if (v is PSObject pso) v = pso.BaseObject;

            if (v is null) return null;
            if (v is string s) return new[] { s };
            if (v is string[] sa) return sa;

            if (v is IEnumerable e)
            {
                var list = new List<string>();
                foreach (var item in e)
                {
                    if (item is null) continue;
                    if (item is PSObject pp) list.Add(pp.BaseObject?.ToString() ?? string.Empty);
                    else list.Add(item.ToString() ?? string.Empty);
                }
                return list.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            }

            return null;
        }

        private static string? GetNestedString(IDictionary root, string key1, string key2, string key3)
        {
            var d1 = GetDictionary(root, key1);
            var d2 = d1 is null ? null : GetDictionary(d1, key2);
            return d2 is null ? null : GetString(d2, key3);
        }

        private static string[]? GetNestedStringArray(IDictionary root, string key1, string key2, string key3)
        {
            var d1 = GetDictionary(root, key1);
            var d2 = d1 is null ? null : GetDictionary(d1, key2);
            return d2 is null ? null : GetStringArray(d2, key3);
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

