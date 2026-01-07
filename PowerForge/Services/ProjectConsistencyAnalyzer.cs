using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Produces a typed combined report for encoding and line ending consistency across a project.
/// </summary>
public sealed class ProjectConsistencyAnalyzer
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new analyzer instance.
    /// </summary>
    public ProjectConsistencyAnalyzer(ILogger logger) => _logger = logger;

    /// <summary>
    /// Analyzes the project for encoding and line ending consistency.
    /// </summary>
    public ProjectConsistencyReport Analyze(
        ProjectEnumeration enumeration,
        string projectType,
        TextEncodingKind? recommendedEncoding,
        FileConsistencyLineEnding? recommendedLineEnding,
        bool includeDetails,
        string? exportPath)
        => Analyze(
            enumeration: enumeration,
            projectType: projectType,
            recommendedEncoding: recommendedEncoding,
            recommendedLineEnding: recommendedLineEnding,
            includeDetails: includeDetails,
            exportPath: exportPath,
            encodingOverrides: null,
            lineEndingOverrides: null);

    /// <summary>
    /// Analyzes the project for encoding and line ending consistency with optional per-path encoding overrides.
    /// </summary>
    public ProjectConsistencyReport Analyze(
        ProjectEnumeration enumeration,
        string projectType,
        TextEncodingKind? recommendedEncoding,
        FileConsistencyLineEnding? recommendedLineEnding,
        bool includeDetails,
        string? exportPath,
        IReadOnlyDictionary<string, FileConsistencyEncoding>? encodingOverrides,
        IReadOnlyDictionary<string, FileConsistencyLineEnding>? lineEndingOverrides)
    {
        var files = ProjectFileEnumerator.Enumerate(enumeration)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var resolvedEncoding = recommendedEncoding ?? ResolveDefaultEncoding(projectType);
        var resolvedLineEnding = recommendedLineEnding ?? ResolveDefaultLineEnding();

        var finalNewlineExtensions = new HashSet<string>(new[]
        {
            ".ps1",".psm1",".psd1",".cs",".js",".py",".rb",".java",".cpp",".h",".hpp",".sql",".md",".txt",".yaml",".yml"
        }, StringComparer.OrdinalIgnoreCase);

        var encodingStats = new Dictionary<TextEncodingKind, int>();
        var lineEndingStats = new Dictionary<DetectedLineEndingKind, int>();
        var extensionEncodingStats = new Dictionary<string, Dictionary<TextEncodingKind, int>>(StringComparer.OrdinalIgnoreCase);
        var extensionLineEndingStats = new Dictionary<string, Dictionary<DetectedLineEndingKind, int>>(StringComparer.OrdinalIgnoreCase);

        var consistencyFiles = new List<ProjectConsistencyFileDetail>(files.Length);
        var encodingFileDetails = new List<ProjectEncodingFileDetail>(files.Length);
        var lineEndingFileDetails = new List<ProjectLineEndingFileDetail>(files.Length);
        var problemLineEndingFiles = new List<ProjectLineEndingFileDetail>();

        foreach (var file in files)
        {
            FileInfo info;
            try { info = new FileInfo(file); }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to analyze {file}: {ex.Message}");
                continue;
            }

            var ext = info.Extension.ToLowerInvariant();
            var rel = ProjectTextDetector.ComputeRelativePath(enumeration.RootPath, file);

            TextEncodingKind? currentEncoding = null;
            DetectedLineEndingKind currentLineEnding = DetectedLineEndingKind.Error;
            bool hasFinalNewline = false;
            string? error = null;

            try { currentEncoding = ProjectTextDetector.DetectEncodingKind(file); }
            catch (Exception ex)
            {
                error = ex.Message;
                _logger.Warn($"Failed to detect encoding for {file}: {ex.Message}");
            }

            try
            {
                var le = ProjectTextDetector.DetectLineEnding(file);
                currentLineEnding = le.Kind;
                hasFinalNewline = le.HasFinalNewline;
            }
            catch (Exception ex)
            {
                error = string.IsNullOrWhiteSpace(error) ? ex.Message : (error + "; " + ex.Message);
                currentLineEnding = DetectedLineEndingKind.Error;
                _logger.Warn($"Failed to detect line endings for {file}: {ex.Message}");
            }

            encodingFileDetails.Add(new ProjectEncodingFileDetail(
                relativePath: rel,
                fullPath: file,
                extension: ext,
                encoding: currentEncoding,
                size: info.Exists ? info.Length : 0,
                lastModified: info.Exists ? info.LastWriteTime : DateTime.MinValue,
                directory: info.DirectoryName ?? string.Empty,
                error: error));

            var led = new ProjectLineEndingFileDetail(
                relativePath: rel,
                fullPath: file,
                extension: ext,
                lineEnding: currentLineEnding,
                hasFinalNewline: hasFinalNewline,
                size: info.Exists ? info.Length : 0,
                lastModified: info.Exists ? info.LastWriteTime : DateTime.MinValue,
                directory: info.DirectoryName ?? string.Empty,
                error: error);
            lineEndingFileDetails.Add(led);
            if (currentLineEnding == DetectedLineEndingKind.Mixed) problemLineEndingFiles.Add(led);

            if (currentEncoding.HasValue)
            {
                if (!encodingStats.ContainsKey(currentEncoding.Value)) encodingStats[currentEncoding.Value] = 0;
                encodingStats[currentEncoding.Value]++;

                if (!extensionEncodingStats.TryGetValue(ext, out var perExtEnc))
                {
                    perExtEnc = new Dictionary<TextEncodingKind, int>();
                    extensionEncodingStats[ext] = perExtEnc;
                }
                if (!perExtEnc.ContainsKey(currentEncoding.Value)) perExtEnc[currentEncoding.Value] = 0;
                perExtEnc[currentEncoding.Value]++;
            }

            if (!lineEndingStats.ContainsKey(currentLineEnding)) lineEndingStats[currentLineEnding] = 0;
            lineEndingStats[currentLineEnding]++;

            if (!extensionLineEndingStats.TryGetValue(ext, out var perExtLe))
            {
                perExtLe = new Dictionary<DetectedLineEndingKind, int>();
                extensionLineEndingStats[ext] = perExtLe;
            }
            if (!perExtLe.ContainsKey(currentLineEnding)) perExtLe[currentLineEnding] = 0;
            perExtLe[currentLineEnding]++;

            var expectedEncoding = FileConsistencyOverrideResolver.ResolveExpectedEncoding(rel, resolvedEncoding, encodingOverrides);
            var expectedLineEnding = FileConsistencyOverrideResolver.ResolveExpectedLineEnding(rel, resolvedLineEnding, lineEndingOverrides);
            bool needsEncodingConversion = currentEncoding.HasValue && currentEncoding.Value != expectedEncoding;
            bool needsLineEndingConversion = currentLineEnding switch
            {
                DetectedLineEndingKind.None => false,
                DetectedLineEndingKind.Error => false,
                DetectedLineEndingKind.Mixed => true,
                DetectedLineEndingKind.CR => true,
                DetectedLineEndingKind.CRLF => expectedLineEnding != FileConsistencyLineEnding.CRLF,
                DetectedLineEndingKind.LF => expectedLineEnding != FileConsistencyLineEnding.LF,
                _ => true
            };

            bool hasMixedLineEndings = currentLineEnding == DetectedLineEndingKind.Mixed;
            bool missingFinalNewline = !hasFinalNewline && info.Length > 0 && finalNewlineExtensions.Contains(ext);

            consistencyFiles.Add(new ProjectConsistencyFileDetail(
                relativePath: rel,
                fullPath: file,
                extension: ext,
                currentEncoding: currentEncoding,
                currentLineEnding: currentLineEnding,
                hasFinalNewline: hasFinalNewline,
                recommendedEncoding: expectedEncoding,
                recommendedLineEnding: expectedLineEnding,
                needsEncodingConversion: needsEncodingConversion,
                needsLineEndingConversion: needsLineEndingConversion,
                hasMixedLineEndings: hasMixedLineEndings,
                missingFinalNewline: missingFinalNewline,
                size: info.Exists ? info.Length : 0,
                lastModified: info.Exists ? info.LastWriteTime : DateTime.MinValue,
                directory: info.DirectoryName ?? string.Empty,
                error: error));
        }

        var totalFiles = consistencyFiles.Count;
        var filesWithIssues = consistencyFiles.Count(f => f.HasIssues);
        var filesCompliant = totalFiles - filesWithIssues;
        var compliancePercentage = totalFiles > 0 ? Math.Round((double)filesCompliant / totalFiles * 100, 1) : 0;

        var filesNeedingEncodingConversion = consistencyFiles.Count(f => f.NeedsEncodingConversion);
        var filesNeedingLineEndingConversion = consistencyFiles.Count(f => f.NeedsLineEndingConversion);
        var filesWithMixedLineEndings = consistencyFiles.Count(f => f.HasMixedLineEndings);
        var filesMissingFinalNewline = consistencyFiles.Count(f => f.MissingFinalNewline);

        var encodingDistribution = encodingStats
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ProjectEncodingDistributionItem(kv.Key, kv.Value))
            .ToArray();

        var lineEndingDistribution = lineEndingStats
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ProjectLineEndingDistributionItem(kv.Key, kv.Value))
            .ToArray();

        var extensionIssues = BuildExtensionIssues(consistencyFiles);

        var summary = new ProjectConsistencySummary(
            projectPath: enumeration.RootPath,
            projectType: projectType,
            kind: enumeration.Kind,
            analysisDate: DateTime.Now,
            totalFiles: totalFiles,
            filesCompliant: filesCompliant,
            filesWithIssues: filesWithIssues,
            compliancePercentage: compliancePercentage,
            currentEncodingDistribution: encodingDistribution,
            filesNeedingEncodingConversion: filesNeedingEncodingConversion,
            recommendedEncoding: resolvedEncoding,
            currentLineEndingDistribution: lineEndingDistribution,
            filesNeedingLineEndingConversion: filesNeedingLineEndingConversion,
            filesWithMixedLineEndings: filesWithMixedLineEndings,
            filesMissingFinalNewline: filesMissingFinalNewline,
            recommendedLineEnding: resolvedLineEnding,
            extensionIssues: extensionIssues);

        var encodingReport = BuildEncodingReport(enumeration, projectType, encodingStats, extensionEncodingStats, encodingFileDetails, includeDetails);
        var lineEndingReport = BuildLineEndingReport(enumeration, projectType, lineEndingStats, extensionLineEndingStats, lineEndingFileDetails, problemLineEndingFiles, includeDetails);

        var problematic = consistencyFiles.Where(f => f.HasIssues).ToArray();

        if (!string.IsNullOrWhiteSpace(exportPath) && consistencyFiles.Count > 0)
        {
            try
            {
                CsvWriter.Write(
                    exportPath!,
                    headers: new[]
                    {
                        "RelativePath","FullPath","Extension",
                        "CurrentEncoding","CurrentLineEnding",
                        "RecommendedEncoding","RecommendedLineEnding",
                        "NeedsEncodingConversion","NeedsLineEndingConversion","HasMixedLineEndings","MissingFinalNewline","HasIssues",
                        "Size","LastModified","Directory","Error"
                    },
                    rows: consistencyFiles.Select(f => new[]
                    {
                        f.RelativePath,
                        f.FullPath,
                        f.Extension,
                        f.CurrentEncoding?.ToString() ?? string.Empty,
                        f.CurrentLineEnding.ToString(),
                        f.RecommendedEncoding.ToString(),
                        f.RecommendedLineEnding.ToString(),
                        f.NeedsEncodingConversion.ToString(CultureInfo.InvariantCulture),
                        f.NeedsLineEndingConversion.ToString(CultureInfo.InvariantCulture),
                        f.HasMixedLineEndings.ToString(CultureInfo.InvariantCulture),
                        f.MissingFinalNewline.ToString(CultureInfo.InvariantCulture),
                        f.HasIssues.ToString(CultureInfo.InvariantCulture),
                        f.Size.ToString(CultureInfo.InvariantCulture),
                        f.LastModified == DateTime.MinValue ? string.Empty : f.LastModified.ToString("o", CultureInfo.InvariantCulture),
                        f.Directory,
                        f.Error ?? string.Empty
                    }));
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to export consistency report to {exportPath}: {ex.Message}");
            }
        }

        return new ProjectConsistencyReport(
            summary: summary,
            encodingReport: encodingReport,
            lineEndingReport: lineEndingReport,
            files: includeDetails ? consistencyFiles.ToArray() : null,
            problematicFiles: problematic,
            exportPath: exportPath);
    }

    private static TextEncodingKind ResolveDefaultEncoding(string projectType)
    {
        return projectType switch
        {
            "PowerShell" => TextEncodingKind.UTF8BOM,
            "Mixed" => TextEncodingKind.UTF8BOM,
            _ => TextEncodingKind.UTF8
        };
    }

    private static FileConsistencyLineEnding ResolveDefaultLineEnding()
        => Environment.NewLine == "\r\n" ? FileConsistencyLineEnding.CRLF : FileConsistencyLineEnding.LF;

    private static ProjectEncodingReport BuildEncodingReport(
        ProjectEnumeration enumeration,
        string projectType,
        IReadOnlyDictionary<TextEncodingKind, int> encodingStats,
        IReadOnlyDictionary<string, Dictionary<TextEncodingKind, int>> extensionEncodingStats,
        IReadOnlyList<ProjectEncodingFileDetail> files,
        bool includeFiles)
    {
        var uniqueEncodings = encodingStats.Keys.OrderBy(k => k.ToString(), StringComparer.OrdinalIgnoreCase).ToArray();
        TextEncodingKind? mostCommon = encodingStats.Count == 0 ? null : encodingStats.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase).First().Key;
        var inconsistentExtensions = extensionEncodingStats
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var distribution = encodingStats
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ProjectEncodingDistributionItem(kv.Key, kv.Value))
            .ToArray();

        var extensionMap = extensionEncodingStats.Keys
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(ext => new ProjectEncodingExtensionDistribution(
                ext,
                extensionEncodingStats[ext]
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new ProjectEncodingDistributionItem(kv.Key, kv.Value))
                    .ToArray()))
            .ToArray();

        var summary = new ProjectEncodingSummary(
            projectPath: enumeration.RootPath,
            projectType: projectType,
            kind: enumeration.Kind,
            totalFiles: files.Count,
            errorFiles: files.Count(f => !f.Encoding.HasValue),
            mostCommonEncoding: mostCommon,
            uniqueEncodings: uniqueEncodings,
            inconsistentExtensions: inconsistentExtensions,
            distribution: distribution,
            extensionMap: extensionMap,
            analysisDate: DateTime.Now);

        return new ProjectEncodingReport(
            summary: summary,
            files: includeFiles ? files.ToArray() : null,
            groupedByEncoding: null,
            exportPath: null);
    }

    private static ProjectLineEndingReport BuildLineEndingReport(
        ProjectEnumeration enumeration,
        string projectType,
        IReadOnlyDictionary<DetectedLineEndingKind, int> lineEndingStats,
        IReadOnlyDictionary<string, Dictionary<DetectedLineEndingKind, int>> extensionLineEndingStats,
        IReadOnlyList<ProjectLineEndingFileDetail> files,
        IReadOnlyList<ProjectLineEndingFileDetail> problemFiles,
        bool includeFiles)
    {
        var uniqueLineEndings = lineEndingStats.Keys.OrderBy(k => k.ToString(), StringComparer.OrdinalIgnoreCase).ToArray();
        var mostCommon = lineEndingStats.Count == 0 ? DetectedLineEndingKind.None : lineEndingStats.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase).First().Key;
        var inconsistentExtensions = extensionLineEndingStats
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var distribution = lineEndingStats
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ProjectLineEndingDistributionItem(kv.Key, kv.Value))
            .ToArray();

        var extensionMap = extensionLineEndingStats.Keys
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(ext => new ProjectLineEndingExtensionDistribution(
                ext,
                extensionLineEndingStats[ext]
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new ProjectLineEndingDistributionItem(kv.Key, kv.Value))
                    .ToArray()))
            .ToArray();

        var status = problemFiles.Count == 0 && inconsistentExtensions.Length == 0 ? CheckStatus.Pass : (problemFiles.Count == 0 && inconsistentExtensions.Length <= 2 ? CheckStatus.Warning : CheckStatus.Fail);
        var message = status switch
        {
            CheckStatus.Pass => $"All {files.Count} files have consistent line endings",
            CheckStatus.Warning => $"Minor line ending issues found: {problemFiles.Count} mixed files",
            _ => $"Significant line ending issues: {problemFiles.Count} mixed files, {inconsistentExtensions.Length} inconsistent extensions"
        };

        var recommendations = status == CheckStatus.Pass
            ? Array.Empty<string>()
            : new[]
            {
                "Fix files with mixed line endings",
                "Standardize line endings by file extension",
                "Use New-ConfigurationFileConsistency -AutoFix during builds"
            };

        var summary = new ProjectLineEndingSummary(
            status: status,
            projectPath: enumeration.RootPath,
            projectType: projectType,
            kind: enumeration.Kind,
            totalFiles: files.Count,
            errorFiles: files.Count(f => f.LineEnding == DetectedLineEndingKind.Error),
            mostCommonLineEnding: mostCommon,
            uniqueLineEndings: uniqueLineEndings,
            inconsistentExtensions: inconsistentExtensions,
            problemFiles: problemFiles.Count,
            filesMissingFinalNewline: 0,
            message: message,
            recommendations: recommendations,
            distribution: distribution,
            extensionMap: extensionMap,
            analysisDate: DateTime.Now);

        return new ProjectLineEndingReport(
            summary: summary,
            files: includeFiles ? files.ToArray() : null,
            problemFiles: problemFiles.ToArray(),
            groupedByLineEnding: null,
            exportPath: null);
    }

    private static ProjectConsistencyExtensionIssue[] BuildExtensionIssues(IReadOnlyList<ProjectConsistencyFileDetail> files)
    {
        var map = new Dictionary<string, ProjectConsistencyExtensionIssueBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files.Where(f => f.HasIssues))
        {
            if (!map.TryGetValue(f.Extension, out var b))
            {
                b = new ProjectConsistencyExtensionIssueBuilder(f.Extension);
                map[f.Extension] = b;
            }

            b.Total++;
            if (f.NeedsEncodingConversion) b.EncodingIssues++;
            if (f.NeedsLineEndingConversion) b.LineEndingIssues++;
            if (f.HasMixedLineEndings) b.MixedLineEndings++;
            if (f.MissingFinalNewline) b.MissingFinalNewline++;
        }

        return map.Values
            .OrderBy(v => v.Extension, StringComparer.OrdinalIgnoreCase)
            .Select(v => v.ToIssue())
            .ToArray();
    }

    private sealed class ProjectConsistencyExtensionIssueBuilder
    {
        internal string Extension { get; }
        internal int Total { get; set; }
        internal int EncodingIssues { get; set; }
        internal int LineEndingIssues { get; set; }
        internal int MixedLineEndings { get; set; }
        internal int MissingFinalNewline { get; set; }

        internal ProjectConsistencyExtensionIssueBuilder(string extension) => Extension = extension;

        internal ProjectConsistencyExtensionIssue ToIssue()
            => new(
                extension: Extension,
                total: Total,
                encodingIssues: EncodingIssues,
                lineEndingIssues: LineEndingIssues,
                mixedLineEndings: MixedLineEndings,
                missingFinalNewline: MissingFinalNewline);
    }
}
