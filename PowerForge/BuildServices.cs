using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Public static facade for PowerForge services, intended to be called from PowerShell scripts.
/// </summary>
/// <summary>
/// Top-level static services for formatting, normalization, install, export detection and manifest edits.
/// </summary>
public static class BuildServices
{
    /// <summary>Formats files using out-of-proc PSScriptAnalyzer with optional settings JSON.</summary>
    public static IList<FormatterResult> FormatFiles(IEnumerable<string> files, string? settingsJson = null, int timeoutSeconds = 120)
    {
        var logger = new ConsoleLogger { IsVerbose = false };
        var runner = new PowerShellRunner();
        var formatter = new PssaFormatter(runner, logger);
        var timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
        return formatter.FormatFilesWithSettings(files ?? Array.Empty<string>(), settingsJson, timeout).ToList();
    }

    /// <summary>Normalizes line endings and encoding for files.</summary>
    public static IList<NormalizationResult> NormalizeFiles(IEnumerable<string> files, LineEnding lineEnding = LineEnding.CRLF, bool utf8Bom = true)
    {
        var normalizer = new LineEndingsNormalizer();
        var opts = new NormalizationOptions(lineEnding, utf8Bom);
        var list = new List<NormalizationResult>();
        foreach (var f in files ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(f) || !System.IO.File.Exists(f)) continue;
            list.Add(normalizer.NormalizeFile(f, opts));
        }
        return list;
    }

    /// <summary>Runs the full formatting pipeline with preprocessing + PSSA + normalization.</summary>
    public static IList<FormatterResult> Format(
        IEnumerable<string> files,
        bool removeCommentsInParamBlock,
        bool removeCommentsBeforeParamBlock,
        bool removeAllEmptyLines,
        bool removeEmptyLines,
        string? pssaSettingsJson,
        int timeoutSeconds,
        LineEnding lineEnding,
        bool utf8Bom)
    {
        var logger = new ConsoleLogger { IsVerbose = false };
        var pipeline = new FormattingPipeline(logger);
        var opts = new FormatOptions
        {
            RemoveCommentsInParamBlock = removeCommentsInParamBlock,
            RemoveCommentsBeforeParamBlock = removeCommentsBeforeParamBlock,
            RemoveAllEmptyLines = removeAllEmptyLines,
            RemoveEmptyLines = removeEmptyLines,
            PssaSettingsJson = pssaSettingsJson,
            TimeoutSeconds = timeoutSeconds,
            LineEnding = lineEnding,
            Utf8Bom = utf8Bom
        };
        return pipeline.Run(files ?? Array.Empty<string>(), opts).ToList();
    }

    /// <summary>Installs a staged module to versioned roots resolving the final version first; optionally patches PSD1.</summary>
    public static ModuleInstallerResult InstallVersioned(string stagingPath, string moduleName, string moduleVersion, InstallationStrategy strategy, int keepVersions = 3, IEnumerable<string>? roots = null, bool updateManifestToResolvedVersion = true)
    {
        var logger = new ConsoleLogger { IsVerbose = false };
        var installer = new ModuleInstaller(logger);
        var resolved = ModuleInstaller.ResolveTargetVersion(roots, moduleName, moduleVersion, strategy);
        if (updateManifestToResolvedVersion)
        {
            try { ManifestEditor.TrySetTopLevelModuleVersion(System.IO.Path.Combine(stagingPath, $"{moduleName}.psd1"), resolved); } catch { }
        }
        var opts = new ModuleInstallerOptions(roots, InstallationStrategy.Exact, keepVersions);
        return installer.InstallFromStaging(stagingPath, moduleName, resolved, opts);
    }

    /// <summary>Gets processes locking any path (Windows only; empty elsewhere).</summary>
    public static IList<(int Pid, string Name)> GetLockingProcesses(IEnumerable<string> paths)
        => LockInspector.GetLockingProcesses(paths ?? Array.Empty<string>()).ToList();

    /// <summary>Attempts to terminate processes locking any path. Returns terminated count.</summary>
    public static int TerminateLockingProcesses(IEnumerable<string> paths, bool force = false)
        => LockInspector.TerminateLockingProcesses(paths ?? Array.Empty<string>(), force);

    /// <summary>Detects function names in PowerShell script files.</summary>
    public static IList<string> DetectScriptFunctions(IEnumerable<string> scriptFiles)
        => ExportDetector.DetectScriptFunctions(scriptFiles ?? Array.Empty<string>()).ToList();

    /// <summary>Detects cmdlet names (Verb-Noun) in binary assemblies.</summary>
    public static IList<string> DetectBinaryCmdlets(IEnumerable<string> assemblies)
        => ExportDetector.DetectBinaryCmdlets(assemblies ?? Array.Empty<string>()).ToList();

    /// <summary>Detects aliases in binary assemblies.</summary>
    public static IList<string> DetectBinaryAliases(IEnumerable<string> assemblies)
        => ExportDetector.DetectBinaryAliases(assemblies ?? Array.Empty<string>()).ToList();

    /// <summary>Sets FunctionsToExport/CmdletsToExport/AliasesToExport in a PSD1 manifest.</summary>
    public static bool SetManifestExports(string psd1Path, IEnumerable<string>? functions, IEnumerable<string>? cmdlets, IEnumerable<string>? aliases)
    {
        bool changed = false;
        if (functions != null) changed |= ManifestEditor.TrySetTopLevelStringArray(psd1Path, "FunctionsToExport", functions.ToArray());
        if (cmdlets != null) changed |= ManifestEditor.TrySetTopLevelStringArray(psd1Path, "CmdletsToExport", cmdlets.ToArray());
        if (aliases != null) changed |= ManifestEditor.TrySetTopLevelStringArray(psd1Path, "AliasesToExport", aliases.ToArray());
        return changed;
    }

    /// <summary>Computes exports from a Public scripts folder and a set of assemblies.</summary>
    public static ExportSet ComputeExports(string publicFolderPath, IEnumerable<string> assemblies)
    {
        var scripts = new List<string>();
        if (!string.IsNullOrWhiteSpace(publicFolderPath) && System.IO.Directory.Exists(publicFolderPath))
        {
            try { scripts.AddRange(System.IO.Directory.GetFiles(publicFolderPath, "*.ps1", System.IO.SearchOption.AllDirectories)); } catch { }
        }
        var funcs = ExportDetector.DetectScriptFunctions(scripts);
        var asmUnique = (assemblies ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var cmds  = ExportDetector.DetectBinaryCmdlets(asmUnique);
        var alis  = ExportDetector.DetectBinaryAliases(asmUnique);
        return new ExportSet(funcs.ToArray(), cmds.ToArray(), alis.ToArray());
    }

    /// <summary>Sets the RootModule in the manifest.</summary>
    public static bool SetRootModule(string psd1Path, string rootModule)
        => ManifestEditor.TrySetTopLevelString(psd1Path, "RootModule", rootModule);

    /// <summary>Sets PrivateData.PSData string entry.</summary>
    public static bool SetPsDataString(string psd1Path, string key, string value)
        => ManifestEditor.TrySetPsDataString(psd1Path, key, value);

    /// <summary>Sets PrivateData.PSData string array entry.</summary>
    public static bool SetPsDataStringArray(string psd1Path, string key, IEnumerable<string> values)
        => ManifestEditor.TrySetPsDataStringArray(psd1Path, key, values.ToArray());

    /// <summary>Sets PrivateData.PSData boolean entry.</summary>
    public static bool SetPsDataBool(string psd1Path, string key, bool value)
        => ManifestEditor.TrySetPsDataBool(psd1Path, key, value);

    /// <summary>Sets PrivateData.PSData.Repository.Branch and .Paths (string[]).</summary>
    public static bool SetRepository(string psd1Path, string? branch, IEnumerable<string>? paths)
    {
        bool changed = false;
        if (!string.IsNullOrWhiteSpace(branch))
            changed |= ManifestEditor.TrySetPsDataSubString(psd1Path, "Repository", "Branch", branch!);
        if (paths != null)
            changed |= ManifestEditor.TrySetPsDataSubStringArray(psd1Path, "Repository", "Paths", paths.ToArray());
        return changed;
    }

    /// <summary>Sets PrivateData.PSData.Delivery.IntroText/UpgradeText (string[]).</summary>
    public static bool SetDeliveryTexts(string psd1Path, IEnumerable<string>? introText, IEnumerable<string>? upgradeText)
    {
        bool changed = false;
        if (introText != null)
            changed |= ManifestEditor.TrySetPsDataSubStringArray(psd1Path, "Delivery", "IntroText", introText.ToArray());
        if (upgradeText != null)
            changed |= ManifestEditor.TrySetPsDataSubStringArray(psd1Path, "Delivery", "UpgradeText", upgradeText.ToArray());
        return changed;
    }

    /// <summary>Sets PrivateData.PSData.Delivery.ImportantLinks from PowerShell dictionaries (e.g., @{ Name='..'; Link='..' }).</summary>
    public static bool SetDeliveryImportantLinks(string psd1Path, System.Collections.IEnumerable links)
    {
        var list = new System.Collections.Generic.List<System.Collections.Generic.IDictionary<string, string>>();
        foreach (var obj in links)
        {
            if (obj is System.Collections.IDictionary d)
            {
                var dict = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var k in d.Keys)
                {
                    var ks = k?.ToString() ?? string.Empty;
                    object? value = null;
                    try { if (k != null) value = d[k]; } catch { }
                    dict[ks] = value?.ToString() ?? string.Empty;
                }
                list.Add(dict);
            }
        }
        return ManifestEditor.TrySetPsDataSubHashtableArray(psd1Path, "Delivery", "ImportantLinks", list);
    }

    /// <summary>Sets RequiredModules from an array of PowerShell dictionaries (ModuleName/ModuleVersion/Guid).</summary>
    public static bool SetRequiredModulesFromDictionaries(string psd1Path, System.Collections.IEnumerable dicts)
    {
        var list = new System.Collections.Generic.List<ManifestEditor.RequiredModule>();
        foreach (var obj in dicts)
        {
            if (obj is System.Collections.IDictionary d)
            {
                var name = d.Contains("ModuleName") ? d["ModuleName"]?.ToString() : d.Contains("Module") ? d["Module"]?.ToString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var ver = d.Contains("ModuleVersion") ? d["ModuleVersion"]?.ToString() : null;
                var req = d.Contains("RequiredVersion") ? d["RequiredVersion"]?.ToString() : null;
                var max = d.Contains("MaximumVersion") ? d["MaximumVersion"]?.ToString() : null;
                var guid = d.Contains("Guid") ? d["Guid"]?.ToString() : null;   
                list.Add(new ManifestEditor.RequiredModule(name!, moduleVersion: ver, requiredVersion: req, maximumVersion: max, guid: guid));
            }
            else if (obj is string s && !string.IsNullOrWhiteSpace(s))
            {
                list.Add(new ManifestEditor.RequiredModule(s));
            }
        }
        return ManifestEditor.TrySetRequiredModules(psd1Path, list.ToArray());  
    }

    /// <summary>
    /// Replaces common PSPublishModule path tokens (e.g., <c>&lt;ModuleName&gt;</c>, <c>&lt;ModuleVersion&gt;</c>, <c>&lt;TagName&gt;</c>).
    /// Intended for legacy PowerShell build scripts.
    /// </summary>
    public static string ReplacePathTokens(string replacementPath, string moduleName, string moduleVersion, string? preRelease = null)
    {
        if (replacementPath is null) return string.Empty;

        moduleName ??= string.Empty;
        moduleVersion ??= string.Empty;

        var tagName = "v" + moduleVersion;
        var moduleVersionWithPreRelease = string.IsNullOrWhiteSpace(preRelease)
            ? moduleVersion
            : moduleVersion + "-" + preRelease;
        var tagModuleVersionWithPreRelease = "v" + moduleVersionWithPreRelease;

        var path = replacementPath;
        path = path.Replace("<TagName>", tagName).Replace("{TagName}", tagName);
        path = path.Replace("<ModuleVersion>", moduleVersion).Replace("{ModuleVersion}", moduleVersion);
        path = path.Replace("<ModuleVersionWithPreRelease>", moduleVersionWithPreRelease).Replace("{ModuleVersionWithPreRelease}", moduleVersionWithPreRelease);
        path = path.Replace("<TagModuleVersionWithPreRelease>", tagModuleVersionWithPreRelease).Replace("{TagModuleVersionWithPreRelease}", tagModuleVersionWithPreRelease);
        path = path.Replace("<ModuleName>", moduleName).Replace("{ModuleName}", moduleName);

        return path;
    }

    /// <summary>
    /// Finds PowerShell resources using PSResourceGet (out-of-process).  
    /// </summary>
    /// <param name="names">Resource names to search for.</param>
    /// <param name="version">Version constraint string (PSResourceGet -Version value).</param>
    /// <param name="prerelease">Whether to include prerelease versions.</param>
    /// <param name="repositories">Optional repository names to restrict search.</param>
    /// <param name="timeoutSeconds">Execution timeout in seconds.</param>
    public static IList<PSResourceInfo> FindPSResources(
        IEnumerable<string> names,
        string? version = null,
        bool prerelease = false,
        IEnumerable<string>? repositories = null,
        int timeoutSeconds = 120)
    {
        var logger = new ConsoleLogger { IsVerbose = false };
        var runner = new PowerShellRunner();
        var client = new PSResourceGetClient(runner, logger);
        var opts = new PSResourceFindOptions(
            names: (names ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToArray(),
            version: version,
            prerelease: prerelease,
            repositories: (repositories ?? Array.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).ToArray());
        var timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
        return client.Find(opts, timeout).ToList();
    }

    /// <summary>
    /// Publishes a PowerShell module/script using PSResourceGet (out-of-process).
    /// </summary>
    /// <param name="path">Module folder path (<c>-Path</c>) or .nupkg path (<c>-NupkgPath</c>).</param>
    /// <param name="repository">Repository name to publish to.</param>
    /// <param name="apiKey">API key used for authentication (if required).</param>
    /// <param name="isNupkg">When true, publishes the given <paramref name="path"/> as a .nupkg.</param>
    /// <param name="destinationPath">Optional destination path passed to PSResourceGet.</param>
    /// <param name="skipDependenciesCheck">Skip dependency check.</param>
    /// <param name="skipModuleManifestValidate">Skip module manifest validation.</param>
    /// <param name="timeoutSeconds">Execution timeout in seconds.</param>
    public static void PublishPSResource(
        string path,
        string? repository = null,
        string? apiKey = null,
        bool isNupkg = false,
        string? destinationPath = null,
        bool skipDependenciesCheck = false,
        bool skipModuleManifestValidate = false,
        int timeoutSeconds = 600)
    {
        var logger = new ConsoleLogger { IsVerbose = false };
        var runner = new PowerShellRunner();
        var client = new PSResourceGetClient(runner, logger);
        var opts = new PSResourcePublishOptions(
            path: path,
            isNupkg: isNupkg,
            repository: repository,
            apiKey: apiKey,
            destinationPath: destinationPath,
            skipDependenciesCheck: skipDependenciesCheck,
            skipModuleManifestValidate: skipModuleManifestValidate);
        var timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
        client.Publish(opts, timeout);
    }
}
