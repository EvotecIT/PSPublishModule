using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Produces a typed encoding report for a project.
/// </summary>
public sealed class ProjectEncodingAnalyzer
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new analyzer instance.
    /// </summary>
    public ProjectEncodingAnalyzer(ILogger logger) => _logger = logger;

    /// <summary>
    /// Analyzes the project for file encoding consistency and distribution.
    /// </summary>
    public ProjectEncodingReport Analyze(
        ProjectEnumeration enumeration,
        string projectType,
        bool includeFiles,
        bool groupByEncoding,
        string? exportPath)
    {
        var files = ProjectFileEnumerator.Enumerate(enumeration)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var fileDetails = new List<ProjectEncodingFileDetail>(files.Length);
        var encodingStats = new Dictionary<TextEncodingKind, int>();
        var extensionStats = new Dictionary<string, Dictionary<TextEncodingKind, int>>(StringComparer.OrdinalIgnoreCase);
        int errorFiles = 0;

        foreach (var file in files)
        {
            TextEncodingKind? encoding = null;
            string? error = null;

            FileInfo info;
            try { info = new FileInfo(file); }
            catch (Exception ex)
            {
                errorFiles++;
                error = ex.Message;
                fileDetails.Add(new ProjectEncodingFileDetail(
                    relativePath: ProjectTextDetector.ComputeRelativePath(enumeration.RootPath, file),
                    fullPath: file,
                    extension: string.Empty,
                    encoding: null,
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
                encoding = ProjectTextDetector.DetectEncodingKind(file);
            }
            catch (Exception ex)
            {
                errorFiles++;
                error = ex.Message;
                _logger.Warn($"Failed to detect encoding for {file}: {ex.Message}");
            }

            fileDetails.Add(new ProjectEncodingFileDetail(
                relativePath: rel,
                fullPath: file,
                extension: ext,
                encoding: encoding,
                size: info.Exists ? info.Length : 0,
                lastModified: info.Exists ? info.LastWriteTime : DateTime.MinValue,
                directory: info.DirectoryName ?? string.Empty,
                error: error));

            if (!encoding.HasValue) continue;

            if (!encodingStats.ContainsKey(encoding.Value)) encodingStats[encoding.Value] = 0;
            encodingStats[encoding.Value]++;

            if (!extensionStats.TryGetValue(ext, out var perExt))
            {
                perExt = new Dictionary<TextEncodingKind, int>();
                extensionStats[ext] = perExt;
            }
            if (!perExt.ContainsKey(encoding.Value)) perExt[encoding.Value] = 0;
            perExt[encoding.Value]++;
        }

        var uniqueEncodings = encodingStats.Keys
            .OrderBy(k => k.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        TextEncodingKind? mostCommon = encodingStats.Count == 0
            ? null
            : encodingStats.OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
                .First().Key;

        var inconsistentExtensions = extensionStats
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var distribution = encodingStats
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ProjectEncodingDistributionItem(kv.Key, kv.Value))
            .ToArray();

        var extensionMap = extensionStats.Keys
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(ext => new ProjectEncodingExtensionDistribution(
                ext,
                extensionStats[ext]
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key.ToString(), StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new ProjectEncodingDistributionItem(kv.Key, kv.Value))
                    .ToArray()))
            .ToArray();

        var summary = new ProjectEncodingSummary(
            projectPath: enumeration.RootPath,
            projectType: projectType,
            kind: enumeration.Kind,
            totalFiles: files.Length,
            errorFiles: errorFiles,
            mostCommonEncoding: mostCommon,
            uniqueEncodings: uniqueEncodings,
            inconsistentExtensions: inconsistentExtensions,
            distribution: distribution,
            extensionMap: extensionMap,
            analysisDate: DateTime.Now);

        ProjectEncodingGroup[]? grouped = null;
        if (groupByEncoding)
        {
            grouped = uniqueEncodings
                .Select(enc => new ProjectEncodingGroup(
                    enc,
                    fileDetails.Where(f => f.Encoding == enc).ToArray()))
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(exportPath) && fileDetails.Count > 0)
        {
            try
            {
                CsvWriter.Write(
                    exportPath!,
                    headers: new[] { "RelativePath", "FullPath", "Extension", "Encoding", "Size", "LastModified", "Directory", "Error" },
                    rows: fileDetails.Select(f => new[]
                    {
                        f.RelativePath,
                        f.FullPath,
                        f.Extension,
                        f.Encoding?.ToString() ?? string.Empty,
                        f.Size.ToString(CultureInfo.InvariantCulture),
                        f.LastModified == DateTime.MinValue ? string.Empty : f.LastModified.ToString("o", CultureInfo.InvariantCulture),
                        f.Directory,
                        f.Error ?? string.Empty
                    }));
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to export encoding report to {exportPath}: {ex.Message}");
            }
        }

        return new ProjectEncodingReport(
            summary: summary,
            files: includeFiles ? fileDetails.ToArray() : null,
            groupedByEncoding: grouped,
            exportPath: exportPath);
    }
}
