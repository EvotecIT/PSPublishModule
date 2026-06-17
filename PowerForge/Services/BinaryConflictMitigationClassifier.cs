using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

internal static class BinaryConflictMitigationClassifier
{
    private const long MaxMarkerScanBytes = 1024 * 1024;

    internal static BinaryConflictDetectionResult SuppressCurrentModuleConflictsMitigatedByAlc(
        BinaryConflictDetectionResult result,
        bool useAssemblyLoadContext,
        bool strictAnalysis,
        IDictionary<string, bool>? moduleIsolationCache = null,
        ILogger? logger = null)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));
        if (result.Issues.Length == 0)
            return result;

        if (!IsCurrentModuleConflictMitigatedByAlc(
                useAssemblyLoadContext,
                result.PowerShellEdition,
                strictAnalysis,
                result.ModuleRoot,
                moduleIsolationCache))
            return result;

        var suppressed = result.Issues.Length;
        if (logger?.IsVerbose == true)
            logger.Verbose($"Suppressed {suppressed} binary conflict advisory item(s) for {result.PowerShellEdition}; UseAssemblyLoadContext isolates the module payload on PowerShell Core.");

        return new BinaryConflictDetectionResult(
            result.PowerShellEdition,
            result.ModuleRoot,
            result.AssemblyRootPath,
            result.AssemblyRootRelativePath,
            Array.Empty<BinaryConflictDetectionIssue>(),
            $"0 conflicts ({suppressed} mitigated by AssemblyLoadContext)");
    }

    internal static bool IsCurrentModuleConflictMitigatedByAlc(
        bool useAssemblyLoadContext,
        string? powerShellEdition,
        bool strictAnalysis,
        string? moduleRoot,
        IDictionary<string, bool>? moduleIsolationCache = null)
    {
        return !strictAnalysis &&
               useAssemblyLoadContext &&
               IsCoreEdition(powerShellEdition) &&
               ModuleHasAssemblyLoadContextIsolation(moduleRoot, moduleIsolationCache);
    }

    internal static bool IsRequiredModuleConflictMitigatedByAlc(
        InstalledModuleMetadata? currentModule,
        InstalledModuleMetadata? otherModule,
        string? powerShellEdition,
        bool strictAnalysis,
        IDictionary<string, bool>? moduleIsolationCache = null)
    {
        if (strictAnalysis || !IsCoreEdition(powerShellEdition))
            return false;

        return ModuleHasAssemblyLoadContextIsolation(currentModule?.ModuleBasePath, moduleIsolationCache) ||
               ModuleHasAssemblyLoadContextIsolation(otherModule?.ModuleBasePath, moduleIsolationCache);
    }

    internal static bool ModuleHasAssemblyLoadContextIsolation(
        string? moduleRoot,
        IDictionary<string, bool>? cache = null)
    {
        if (string.IsNullOrWhiteSpace(moduleRoot) || !Directory.Exists(moduleRoot))
            return false;

        var key = Path.GetFullPath(moduleRoot);
        if (cache is not null && cache.TryGetValue(key, out var cached))
            return cached;

        var result = ModuleHasAssemblyLoadContextIsolationCore(key);
        if (cache is not null)
            cache[key] = result;
        return result;
    }

    private static bool ModuleHasAssemblyLoadContextIsolationCore(string moduleRoot)
    {
        foreach (var file in EnumerateCandidateFiles(moduleRoot))
        {
            string text;
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length > MaxMarkerScanBytes)
                    continue;
                text = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            if (ContainsIsolationMarker(text))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string moduleRoot)
    {
        foreach (var pattern in new[] { "*.psm1", "*.ps1" })
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(moduleRoot, pattern, SearchOption.TopDirectoryOnly).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
                yield return file;
        }
    }

    private static bool ContainsIsolationMarker(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.IndexOf("ModuleAssemblyLoadContext", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("PowerForge.ModuleIsolation.ModuleLoadContext", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("ModuleLoadContext]::LoadModule", StringComparison.OrdinalIgnoreCase) >= 0 ||
               (text.IndexOf("AssemblyLoadContext", StringComparison.OrdinalIgnoreCase) >= 0 &&
                text.IndexOf("Import-Module -Assembly", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsCoreEdition(string? powerShellEdition)
        => string.Equals(powerShellEdition, "Core", StringComparison.OrdinalIgnoreCase);
}
