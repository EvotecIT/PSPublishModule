using System;
using System.Collections.Generic;
using System.Linq;
#if !NETSTANDARD2_0
using PowerForge;
#endif

#nullable enable

namespace PSPublishModule;

/// <summary>
/// Internal facade used by the PowerShell module to call robust C# implementations
/// without exposing new public cmdlets. Methods are static and return results for
/// the callers to log using existing conventions.
/// </summary>
public static class BuildServices
{
#if !NETSTANDARD2_0
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
#if !NETSTANDARD2_0
        var resolved = ModuleInstaller.ResolveTargetVersion(roots, moduleName, moduleVersion, strategy);
        if (updateManifestToResolvedVersion)
        {
            try { PowerForge.ManifestEditor.TrySetTopLevelModuleVersion(System.IO.Path.Combine(stagingPath, $"{moduleName}.psd1"), resolved); } catch { }
        }
        // Install exactly into the resolved version (prevents re-bumping)
        var opts = new ModuleInstallerOptions(roots, PowerForge.InstallationStrategy.Exact, keepVersions);
        return installer.InstallFromStaging(stagingPath, moduleName, resolved, opts);
#else
        var opts = new ModuleInstallerOptions(roots, strategy, keepVersions);
        return installer.InstallFromStaging(stagingPath, moduleName, moduleVersion, opts);
#endif
    }
#else
    /// <summary>Placeholder for netstandard2.0 target; no-op formatter.</summary>
    /// <param name="files">Ignored.</param>
    /// <param name="settingsJson">Ignored.</param>
    /// <param name="timeoutSeconds">Ignored.</param>
    public static IList<object> FormatFiles(IEnumerable<string> files, string? settingsJson = null, int timeoutSeconds = 120)
        => Array.Empty<object>();
    /// <summary>Placeholder for netstandard2.0 target; no-op normalizer.</summary>
    /// <param name="files">Ignored.</param>
    /// <param name="lineEnding">Ignored.</param>
    /// <param name="utf8Bom">Ignored.</param>
    public static IList<object> NormalizeFiles(IEnumerable<string> files, object? lineEnding = null, bool utf8Bom = true)
        => Array.Empty<object>();
    /// <summary>Placeholder for netstandard2.0 target; no-op installer.</summary>
    /// <param name="stagingPath">Ignored.</param>
    /// <param name="moduleName">Ignored.</param>
    /// <param name="moduleVersion">Ignored.</param>
    /// <param name="strategy">Ignored.</param>
    /// <param name="keepVersions">Ignored.</param>
    /// <param name="roots">Ignored.</param>
    public static object InstallVersioned(string stagingPath, string moduleName, string moduleVersion, object? strategy = null, int keepVersions = 3, IEnumerable<string>? roots = null)
        => new object();
#endif

    /// <summary>
    /// Returns processes that currently lock any of the specified paths (Windows only; empty on other OSes).
    /// </summary>
    public static IList<(int Pid, string Name)> GetLockingProcesses(IEnumerable<string> paths)
    {
#if !NETSTANDARD2_0
        return PowerForge.LockInspector.GetLockingProcesses(paths ?? Array.Empty<string>()).ToList();
#else
        return Array.Empty<(int, string)>();
#endif
    }

    /// <summary>
    /// Attempts to terminate processes that lock any of the specified paths. Returns count terminated.
    /// </summary>
    public static int TerminateLockingProcesses(IEnumerable<string> paths, bool force = false)
    {
#if !NETSTANDARD2_0
        return PowerForge.LockInspector.TerminateLockingProcesses(paths ?? Array.Empty<string>(), force);
#else
        return 0;
#endif
    }
}
