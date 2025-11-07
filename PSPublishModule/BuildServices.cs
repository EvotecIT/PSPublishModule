using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;


namespace PSPublishModule;

/// <summary>
/// Internal facade used by the PowerShell module to call robust C# implementations
/// without exposing new public cmdlets. Methods are static and return results for
/// the callers to log using existing conventions.
/// </summary>
public static class BuildServices
{
    /// <summary>
    /// Runs the out-of-process PSSA formatter on the provided files using optional settings JSON.
    /// </summary>
    /// <param name="files">Files to format.</param>
    /// <param name="settingsJson">PSScriptAnalyzer settings in JSON form or null for defaults.</param>
    /// <param name="timeoutSeconds">Timeout in seconds for the formatting process.</param>
    public static IList<PowerForge.FormatterResult> FormatFiles(IEnumerable<string> files, string? settingsJson = null, int timeoutSeconds = 120)
    {
        var logger = new ConsoleLogger { IsVerbose = false };
        var runner = new PowerShellRunner();
        var formatter = new PssaFormatter(runner, logger);
        var timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
        return formatter.FormatFilesWithSettings(files ?? Array.Empty<string>(), settingsJson, timeout).ToList();
    }

    /// <summary>
    /// Normalizes encoding and line endings for the provided files deterministically.
    /// </summary>
    /// <param name="files">Files to normalize.</param>
    /// <param name="lineEnding">Target line ending; default CRLF.</param>
    /// <param name="utf8Bom">True to save UTF-8 with BOM; recommended for PS 5.1.</param>
    public static IList<PowerForge.NormalizationResult> NormalizeFiles(IEnumerable<string> files, PowerForge.LineEnding lineEnding = PowerForge.LineEnding.CRLF, bool utf8Bom = true)
    {
        var normalizer = new LineEndingsNormalizer();
        var opts = new PowerForge.NormalizationOptions(lineEnding, utf8Bom);
        var list = new List<PowerForge.NormalizationResult>();
        foreach (var f in files ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            if (!System.IO.File.Exists(f)) continue;
            list.Add(normalizer.NormalizeFile(f, opts));
        }
        return list;
    }

    /// <summary>
    /// End-to-end formatting pipeline with configurable preprocessing, PSSA settings and normalization.
    /// </summary>
    public static IList<PowerForge.FormatterResult> Format(
        IEnumerable<string> files,
        bool removeCommentsInParamBlock,
        bool removeCommentsBeforeParamBlock,
        bool removeAllEmptyLines,
        bool removeEmptyLines,
        string? pssaSettingsJson,
        int timeoutSeconds,
        PowerForge.LineEnding lineEnding,
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

    /// <summary>
    /// Installs a staged module into versioned Module roots using the given strategy and retention.
    /// </summary>
    /// <param name="stagingPath">Source directory with staged module files.</param>
    /// <param name="moduleName">Module name (folder under Modules).</param>
    /// <param name="moduleVersion">Module version to target.</param>
    /// <param name="strategy">Versioning strategy (Exact or AutoRevision).</param>
    /// <param name="keepVersions">How many versions to retain.</param>
    /// <param name="roots">Optional explicit module roots; when null, defaults are used.</param>
    /// <param name="updateManifestToResolvedVersion">When true, updates ModuleVersion in the installed PSD1 to the resolved version (e.g., 2.0.27.3) to match the versioned folder name.</param>
    public static PowerForge.ModuleInstallerResult InstallVersioned(string stagingPath, string moduleName, string moduleVersion, PowerForge.InstallationStrategy strategy, int keepVersions = 3, IEnumerable<string>? roots = null, bool updateManifestToResolvedVersion = true)
    {
        var logger = new ConsoleLogger { IsVerbose = false };
        var installer = new ModuleInstaller(logger);
        // Pre-resolve version; adjust staged manifest before install
        var resolved = ModuleInstaller.ResolveTargetVersion(roots, moduleName, moduleVersion, strategy);
        if (updateManifestToResolvedVersion)
        {
            try { PowerForge.ManifestEditor.TrySetTopLevelModuleVersion(System.IO.Path.Combine(stagingPath, $"{moduleName}.psd1"), resolved); } catch { }
        }
        // Install exactly into the resolved version (prevents re-bumping)
        var opts = new ModuleInstallerOptions(roots, PowerForge.InstallationStrategy.Exact, keepVersions);
        return installer.InstallFromStaging(stagingPath, moduleName, resolved, opts);
    }

    /// <summary>Detects function names across PowerShell script files.</summary>
    public static IList<string> DetectScriptFunctions(IEnumerable<string> scriptFiles)
        => PowerForge.ExportDetector.DetectScriptFunctions(scriptFiles ?? Array.Empty<string>()).ToList();

    /// <summary>Detects cmdlet names (Verb-Noun) from binary assemblies.</summary>
    public static IList<string> DetectBinaryCmdlets(IEnumerable<string> assemblies)
        => PowerForge.ExportDetector.DetectBinaryCmdlets(assemblies ?? Array.Empty<string>()).ToList();

    /// <summary>Detects alias names from binary assemblies.</summary>
    public static IList<string> DetectBinaryAliases(IEnumerable<string> assemblies)
        => PowerForge.ExportDetector.DetectBinaryAliases(assemblies ?? Array.Empty<string>()).ToList();

    /// <summary>Sets FunctionsToExport/CmdletsToExport/AliasesToExport in a PSD1 manifest.</summary>
    public static bool SetManifestExports(string psd1Path, IEnumerable<string>? functions, IEnumerable<string>? cmdlets, IEnumerable<string>? aliases)
    {
        bool changed = false;
        if (functions != null)
            changed |= PowerForge.ManifestEditor.TrySetTopLevelStringArray(psd1Path, "FunctionsToExport", functions.ToArray());
        if (cmdlets != null)
            changed |= PowerForge.ManifestEditor.TrySetTopLevelStringArray(psd1Path, "CmdletsToExport", cmdlets.ToArray());
        if (aliases != null)
            changed |= PowerForge.ManifestEditor.TrySetTopLevelStringArray(psd1Path, "AliasesToExport", aliases.ToArray());
        return changed;
    }

    /// <summary>
    /// Computes exports from a public scripts folder and a set of assemblies.
    /// </summary>
    public static PowerForge.ExportSet ComputeExports(string publicFolderPath, IEnumerable<string> assemblies)
    {
        var scripts = new List<string>();
        if (!string.IsNullOrWhiteSpace(publicFolderPath) && System.IO.Directory.Exists(publicFolderPath))
        {
            try { scripts.AddRange(System.IO.Directory.GetFiles(publicFolderPath, "*.ps1", System.IO.SearchOption.AllDirectories)); } catch { }
        }
        var funcs = PowerForge.ExportDetector.DetectScriptFunctions(scripts);
        var cmds  = PowerForge.ExportDetector.DetectBinaryCmdlets(assemblies);
        var alis  = PowerForge.ExportDetector.DetectBinaryAliases(assemblies);
        return new PowerForge.ExportSet(funcs.ToArray(), cmds.ToArray(), alis.ToArray());
    }

    /// <summary>Sets the top-level RootModule in the PSD1.</summary>
    public static bool SetRootModule(string psd1Path, string rootModule)
        => PowerForge.ManifestEditor.TrySetTopLevelString(psd1Path, "RootModule", rootModule);

    /// <summary>
    /// Returns processes that currently lock any of the specified paths (Windows only; empty on other OSes).
    /// </summary>
    public static IList<(int Pid, string Name)> GetLockingProcesses(IEnumerable<string> paths)
        => PowerForge.LockInspector.GetLockingProcesses(paths ?? Array.Empty<string>()).ToList();

    /// <summary>
    /// Attempts to terminate processes that lock any of the specified paths. Returns count terminated.
    /// </summary>
    public static int TerminateLockingProcesses(IEnumerable<string> paths, bool force = false)
        => PowerForge.LockInspector.TerminateLockingProcesses(paths ?? Array.Empty<string>(), force);
}
