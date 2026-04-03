using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static PowerShellRunResult RunScript(IPowerShellRunner runner, string scriptText, IReadOnlyList<string> args, TimeSpan timeout, bool preferPwsh = true)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "modulepipeline");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"modulepipeline_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, scriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            return runner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
        }
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string EncodeLines(IEnumerable<string> lines)
    {
        var joined = string.Join("\n", lines ?? Array.Empty<string>());
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(joined));
    }

    private static string Decode(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return string.Empty;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return string.Empty; }
    }

    private static string BuildArtefactsReportPath(string projectRoot, string? reportFileName, string fallbackFileName)
    {
        var name = string.IsNullOrWhiteSpace(reportFileName) ? fallbackFileName : reportFileName!.Trim();
        var artefacts = Path.Combine(projectRoot, "Artefacts");
        Directory.CreateDirectory(artefacts);
        return Path.GetFullPath(Path.Combine(artefacts, name));
    }

    private static string? AddFileNameSuffix(string? fileName, string suffix)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var trimmedSuffix = (suffix ?? string.Empty).Trim().Trim('.');
        var trimmed = fileName!.Trim();
        if (string.IsNullOrWhiteSpace(trimmedSuffix)) return trimmed;

        var ext = Path.GetExtension(trimmed);
        if (string.IsNullOrWhiteSpace(ext)) return trimmed + "." + trimmedSuffix;

        var baseName = trimmed.Substring(0, trimmed.Length - ext.Length);
        return baseName + "." + trimmedSuffix + ext;
    }

    private static string[] MergeExcludeDirectories(IEnumerable<string>? primary, IEnumerable<string>? extra)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in (primary ?? Array.Empty<string>()))
            if (!string.IsNullOrWhiteSpace(s)) set.Add(s.Trim());
        foreach (var s in (extra ?? Array.Empty<string>()))
            if (!string.IsNullOrWhiteSpace(s)) set.Add(s.Trim());
        return set.ToArray();
    }

    private static void ValidateDeliveryPathConflicts(
        string projectRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IEnumerable<string>? excludedDirectories,
        DeliveryOptionsConfiguration? delivery,
        IReadOnlyList<ConfigurationArtefactSegment>? artefacts)
    {
        if (delivery is null || !delivery.Enable)
            return;

        var internalsPath = ArtefactLayoutPathResolver.NormalizeDeliveryInternalsPath(delivery.InternalsPath);

        var deliveryRoot = ResolveProjectRelativePath(projectRoot, internalsPath);
        var conflicts = new List<string>();
        AddDeliveryExcludedDirectoryConflict(conflicts, projectRoot, deliveryRoot, internalsPath, excludedDirectories);
        AddDeliveryArtefactOverlapConflicts(conflicts, projectRoot, moduleName, moduleVersion, preRelease, deliveryRoot, internalsPath, artefacts);

        if (conflicts.Count == 0)
            return;

        throw new InvalidOperationException(
            "Delivery configuration is unsafe:" + Environment.NewLine +
            string.Join(Environment.NewLine, conflicts.Select(static message => "- " + message)));
    }

    private static void AddDeliveryExcludedDirectoryConflict(
        List<string> conflicts,
        string projectRoot,
        string deliveryRoot,
        string configuredInternalsPath,
        IEnumerable<string>? excludedDirectories)
    {
        var excluded = new HashSet<string>(
            (excludedDirectories ?? Array.Empty<string>())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (excluded.Count == 0)
            return;

        if (!IsSameOrChildPath(projectRoot, deliveryRoot))
            return;

        // ModuleBuildPipeline excludes directories by folder name anywhere in the tree, not only at the root.
        var relativePath = ProjectTextInspection.ComputeRelativePath(projectRoot, deliveryRoot);

        var conflictingSegments = GetPathSegments(relativePath)
            .Where(excluded.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (conflictingSegments.Length == 0)
            return;

        conflicts.Add(
            $"Delivery.InternalsPath '{configuredInternalsPath}' resolves to '{deliveryRoot}' and uses excluded directory name(s): {string.Join(", ", conflictingSegments)}. " +
            "Build staging skips those directory names via Build.ExcludeDirectories, so delivery content may not be copied into the built module. " +
            "Rename the folder or remove it from ExcludeDirectories if you intend to package it.");
    }

    private static void AddDeliveryArtefactOverlapConflicts(
        List<string> conflicts,
        string projectRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        string deliveryRoot,
        string configuredInternalsPath,
        IReadOnlyList<ConfigurationArtefactSegment>? artefacts)
    {
        if (artefacts is null || artefacts.Count == 0)
            return;

        var warnedScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var artefact in artefacts.Where(static item => item is not null))
        {
            var cfg = artefact.Configuration ?? new ArtefactConfiguration();
            var outputRoot = ArtefactLayoutPathResolver.ResolveOutputRoot(
                cfg.Path,
                projectRoot,
                moduleName,
                moduleVersion,
                preRelease,
                artefact.ArtefactType);

            AddPathOverlapConflict(
                conflicts,
                warnedScopes,
                deliveryRoot,
                configuredInternalsPath,
                outputRoot,
                $"artefact output root for '{artefact.ArtefactType}'");

            if (artefact.ArtefactType is not ArtefactType.Unpacked)
                continue;

            if (cfg.RequiredModules.Enabled == true)
            {
                var requiredModulesRoot = ArtefactLayoutPathResolver.ResolveRequiredModulesRootForUnpacked(
                    cfg,
                    outputRoot,
                    moduleName,
                    moduleVersion,
                    preRelease);

                AddPathOverlapConflict(
                    conflicts,
                    warnedScopes,
                    deliveryRoot,
                    configuredInternalsPath,
                    requiredModulesRoot,
                    $"required modules root for '{artefact.ArtefactType}'");
            }

            var modulesRoot = ArtefactLayoutPathResolver.ResolveModulesRootForUnpacked(
                cfg,
                outputRoot,
                cfg.RequiredModules.Enabled == true
                    ? ArtefactLayoutPathResolver.ResolveRequiredModulesRootForUnpacked(
                        cfg,
                        outputRoot,
                        moduleName,
                        moduleVersion,
                        preRelease)
                    : outputRoot,
                moduleName,
                moduleVersion,
                preRelease);

            AddPathOverlapConflict(
                conflicts,
                warnedScopes,
                deliveryRoot,
                configuredInternalsPath,
                modulesRoot,
                $"module copy root for '{artefact.ArtefactType}'");
        }
    }

    private static void AddPathOverlapConflict(
        List<string> conflicts,
        HashSet<string> warnedScopes,
        string deliveryRoot,
        string configuredInternalsPath,
        string candidatePath,
        string candidateLabel)
    {
        if (!DoPathsOverlap(deliveryRoot, candidatePath))
            return;

        var key = candidateLabel + "|" + Path.GetFullPath(candidatePath);
        if (!warnedScopes.Add(key))
            return;

        conflicts.Add(
            $"Delivery.InternalsPath '{configuredInternalsPath}' resolves to '{deliveryRoot}' and overlaps {candidateLabel} '{Path.GetFullPath(candidatePath)}'. " +
            "Keep delivery source content and artefact outputs in separate trees to avoid packaging previous artefact outputs or clearing part of the same source tree during artefact creation.");
    }

    private static string ResolveProjectRelativePath(string projectRoot, string configuredPath)
    {
        var normalized = NormalizeConfiguredPathValue(configuredPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return Path.GetFullPath(projectRoot);

        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(projectRoot, normalized));
    }

    private static string NormalizeConfiguredPathValue(string? value)
        => (value ?? string.Empty).Trim().Trim('"');

    private static string[] GetPathSegments(string path)
    {
        var normalized = (path ?? string.Empty)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim();

        // Paths are normalized through Path.GetFullPath before we compute a relative path,
        // so dot-dot segments are already resolved away when this helper runs.
        return normalized
            .Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Where(static segment => !string.Equals(segment, ".", StringComparison.Ordinal))
            .ToArray();
    }

    private static bool DoPathsOverlap(string pathA, string pathB)
        => IsSameOrChildPath(pathA, pathB) || IsSameOrChildPath(pathB, pathA);

    private static bool IsSameOrChildPath(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        var candidateWithSeparator = candidate + Path.DirectorySeparatorChar;
        return candidateWithSeparator.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

}
