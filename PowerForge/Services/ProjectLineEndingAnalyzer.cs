using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Produces a typed line ending report for a project.
/// </summary>
public sealed class ProjectLineEndingAnalyzer
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new analyzer instance.
    /// </summary>
    public ProjectLineEndingAnalyzer(ILogger logger) => _logger = logger;

    /// <summary>
    /// Analyzes the project for line ending consistency and distribution.
    /// </summary>
    public ProjectLineEndingReport Analyze(
        ProjectEnumeration enumeration,
        string projectType,
        bool includeFiles,
        bool groupByLineEnding,
        bool checkMixed,
        string? exportPath)
    {
        _ = checkMixed; // kept for compatibility; mixed detection is always performed

        var files = ProjectFileEnumerator.Enumerate(enumeration)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var fileDetails = new List<ProjectLineEndingFileDetail>(files.Length);
        var lineEndingStats = new Dictionary<DetectedLineEndingKind, int>();
        var extensionStats = new Dictionary<string, Dictionary<DetectedLineEndingKind, int>>(StringComparer.OrdinalIgnoreCase);
        var problemFiles = new List<ProjectLineEndingFileDetail>();
        int errorFiles = 0;

        var finalNewlineExtensions = new HashSet<string>(new[]
        {
            ".ps1",".psm1",".psd1",".cs",".js",".py",".rb",".java",".cpp",".h",".hpp",".sql",".md",".txt",".yaml",".yml"
        }, StringComparer.OrdinalIgnoreCase);

        int filesMissingFinalNewline = 0;

        foreach (var file in files)
        {
            DetectedLineEndingKind lineEnding = DetectedLineEndingKind.Error;
            bool hasFinalNewline = false;
            string? error = null;

            FileInfo info;
            try { info = new FileInfo(file); }
            catch (Exception ex)
            {
                errorFiles++;
                error = ex.Message;
                fileDetails.Add(new ProjectLineEndingFileDetail(
                    relativePath: ProjectTextDetector.ComputeRelativePath(enumeration.RootPath, file),
                    fullPath: file,
                    extension: string.Empty,
                    lineEnding: DetectedLineEndingKind.Error,
                    hasFinalNewline: false,
                    size: 0,
                    lastModified: DateTime.MinValue,
                    directory: string.Empty,
                    error: error));
                continue;
            }

            var ext = info.Extension.ToLowerInvariant();
            var rel = ProjectTextDetector.ComputeRelativePath(enumeration.RootPath, file);

            try
            {
                var res = ProjectTextDetector.DetectLineEnding(file);
                lineEnding = res.Kind;
                hasFinalNewline = res.HasFinalNewline;
            }
            catch (Exception ex)
            {
                errorFiles++;
                error = ex.Message;
                lineEnding = DetectedLineEndingKind.Error;
                _logger.Warn($"Failed to detect line endings for {file}: {ex.Message}");
            }

            var detail = new ProjectLineEndingFileDetail(
                relativePath: rel,
                fullPath: file,
                extension: ext,
                lineEnding: lineEnding,
                hasFinalNewline: hasFinalNewline,
                size: info.Exists ? info.Length : 0,
                lastModified: info.Exists ? info.LastWriteTime : DateTime.MinValue,
                directory: info.DirectoryName ?? string.Empty,
                error: error);

            fileDetails.Add(detail);

            if (detail.LineEnding == DetectedLineEndingKind.Mixed)
                problemFiles.Add(detail);

            if (!detail.HasFinalNewline && info.Length > 0 && finalNewlineExtensions.Contains(ext))
                filesMissingFinalNewline++;

            if (!lineEndingStats.ContainsKey(detail.LineEnding)) lineEndingStats[detail.LineEnding] = 0;
            lineEndingStats[detail.LineEnding]++;

            if (!extensionStats.TryGetValue(ext, out var perExt))
            {
                perExt = new Dictionary<DetectedLineEndingKind, int>();
                extensionStats[ext] = perExt;
            }
            if (!perExt.ContainsKey(detail.LineEnding)) perExt[detail.LineEnding] = 0;
            perExt[detail.LineEnding]++;
        }

        var uniqueLineEndings = lineEndingStats.Keys
            .OrderBy(k => k.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mostCommon = lineEndingStats.Count == 0
            ? DetectedLineEndingKind.None
            : lineEndingStats.OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
                .First().Key;

        var inconsistentExtensions = extensionStats
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hasProblems = problemFiles.Count > 0 || filesMissingFinalNewline > 0 || inconsistentExtensions.Length > 0;
        var status = !hasProblems
            ? CheckStatus.Pass
            : (problemFiles.Count == 0 && inconsistentExtensions.Length <= 2 ? CheckStatus.Warning : CheckStatus.Fail);

        var recommendations = Array.Empty<string>();
        if (hasProblems)
        {
            var recs = new List<string>();
            if (problemFiles.Count > 0) recs.Add("Fix files with mixed line endings");
            if (filesMissingFinalNewline > 0) recs.Add("Add missing final newlines");
            if (inconsistentExtensions.Length > 0) recs.Add("Standardize line endings by file extension");
            recs.Add("Use Convert-ProjectLineEnding to fix issues");
            recommendations = recs.ToArray();
        }

        var message = status switch
        {
            CheckStatus.Pass => $"All {files.Length} files have consistent line endings",
            CheckStatus.Warning => $"Minor line ending issues found: {problemFiles.Count} mixed files, {filesMissingFinalNewline} missing newlines",
            _ => $"Significant line ending issues: {problemFiles.Count} mixed files, {inconsistentExtensions.Length} inconsistent extensions"
        };

        var distribution = lineEndingStats
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ProjectLineEndingDistributionItem(kv.Key, kv.Value))
            .ToArray();

        var extensionMap = extensionStats.Keys
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(ext => new ProjectLineEndingExtensionDistribution(
                ext,
                extensionStats[ext]
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new ProjectLineEndingDistributionItem(kv.Key, kv.Value))
                    .ToArray()))
            .ToArray();

        var summary = new ProjectLineEndingSummary(
            status: status,
            projectPath: enumeration.RootPath,
            projectType: projectType,
            kind: enumeration.Kind,
            totalFiles: files.Length,
            errorFiles: errorFiles,
            mostCommonLineEnding: mostCommon,
            uniqueLineEndings: uniqueLineEndings,
            inconsistentExtensions: inconsistentExtensions,
            problemFiles: problemFiles.Count,
            filesMissingFinalNewline: filesMissingFinalNewline,
            message: message,
            recommendations: recommendations,
            distribution: distribution,
            extensionMap: extensionMap,
            analysisDate: DateTime.Now);

        ProjectLineEndingGroup[]? grouped = null;
        if (groupByLineEnding)
        {
            grouped = uniqueLineEndings
                .Select(le => new ProjectLineEndingGroup(
                    le,
                    fileDetails.Where(f => f.LineEnding == le).ToArray()))
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(exportPath) && fileDetails.Count > 0)
        {
            try
            {
                CsvWriter.Write(
                    exportPath!,
                    headers: new[] { "RelativePath", "FullPath", "Extension", "LineEnding", "HasFinalNewline", "Size", "LastModified", "Directory", "Error" },
                    rows: fileDetails.Select(f => new[]
                    {
                        f.RelativePath,
                        f.FullPath,
                        f.Extension,
                        f.LineEnding.ToString(),
                        f.HasFinalNewline ? "True" : "False",
                        f.Size.ToString(CultureInfo.InvariantCulture),
                        f.LastModified == DateTime.MinValue ? string.Empty : f.LastModified.ToString("o", CultureInfo.InvariantCulture),
                        f.Directory,
                        f.Error ?? string.Empty
                    }));
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to export line ending report to {exportPath}: {ex.Message}");
            }
        }

        return new ProjectLineEndingReport(
            summary: summary,
            files: includeFiles ? fileDetails.ToArray() : null,
            problemFiles: problemFiles.ToArray(),
            groupedByLineEnding: grouped,
            exportPath: exportPath);
    }
}
