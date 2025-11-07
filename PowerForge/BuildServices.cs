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
        var cmds  = ExportDetector.DetectBinaryCmdlets(assemblies);
        var alis  = ExportDetector.DetectBinaryAliases(assemblies);
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
}
