using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Provides comprehensive analysis of encoding and line ending consistency across a project.
/// </summary>
[Cmdlet(VerbsCommon.Get, "ProjectConsistency")]
public sealed class GetProjectConsistencyCommand : PSCmdlet
{
    /// <summary>Path to the project directory to analyze.</summary>
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>Type of project to analyze. Determines which file extensions are included.</summary>
    [Parameter]
    [ValidateSet("PowerShell", "CSharp", "Mixed", "All", "Custom")]
    public string ProjectType { get; set; } = "Mixed";

    /// <summary>Custom file extensions to analyze when ProjectType is Custom (e.g., *.ps1, *.cs).</summary>
    [Parameter]
    public string[]? CustomExtensions { get; set; }

    /// <summary>Directory names to exclude from analysis (e.g., .git, bin, obj).</summary>
    [Parameter]
    public string[] ExcludeDirectories { get; set; } = new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode" };

    /// <summary>The encoding standard you want to achieve.</summary>
    [Parameter]
    [ValidateSet("Ascii", "BigEndianUnicode", "Unicode", "UTF7", "UTF8", "UTF8BOM", "UTF32", "Default", "OEM")]
    public string? RecommendedEncoding { get; set; }

    /// <summary>The line ending standard you want to achieve.</summary>
    [Parameter]
    [ValidateSet("CRLF", "LF")]
    public string? RecommendedLineEnding { get; set; }

    /// <summary>Include detailed file-by-file analysis in the output.</summary>
    [Parameter]
    public SwitchParameter ShowDetails { get; set; }

    /// <summary>Export the detailed report to a CSV file at the specified path.</summary>
    [Parameter]
    public string? ExportPath { get; set; }

    /// <summary>
    /// Executes the consistency analysis and returns a report object.
    /// </summary>
    protected override void ProcessRecord()
    {
        var root = System.IO.Path.GetFullPath(Path.Trim().Trim('"'));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Project path '{root}' not found or is not a directory");

        var patterns = ResolvePatterns(ProjectType, CustomExtensions);
        WriteVerbose($"Project type: {ProjectType} with patterns: {string.Join(", ", patterns)}");

        var recommendedEncoding = ResolveRecommendedEncoding(ProjectType, RecommendedEncoding);
        var recommendedLineEnding = ResolveRecommendedLineEnding(RecommendedLineEnding);

        HostWriteLineSafe("[i] Analyzing project consistency...", ConsoleColor.Cyan);
        HostWriteLineSafe($"Project: {root}");
        HostWriteLineSafe($"Type: {ProjectType}");
        HostWriteLineSafe($"Target encoding: {recommendedEncoding}");
        HostWriteLineSafe($"Target line ending: {recommendedLineEnding}");

        var enumeration = new ProjectEnumeration(
            rootPath: root,
            kind: ResolveKind(ProjectType),
            customExtensions: ProjectType.Equals("Custom", StringComparison.OrdinalIgnoreCase) ? patterns : null,
            excludeDirectories: ExcludeDirectories);

        var files = ProjectFileEnumerator.Enumerate(enumeration)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            WriteWarning("No files found matching the specified criteria");
            return;
        }

        HostWriteLineSafe("");
        HostWriteLineSafe("[i] Analyzing file encodings...", ConsoleColor.Yellow);
        HostWriteLineSafe("[i] Analyzing line endings...", ConsoleColor.Yellow);
        HostWriteLineSafe("[i] Combining analysis...", ConsoleColor.Yellow);

        var finalNewlineExtensions = new HashSet<string>(new[]
        {
            ".ps1",".psm1",".psd1",".cs",".js",".py",".rb",".java",".cpp",".h",".hpp",".sql",".md",".txt",".yaml",".yml"
        }, StringComparer.OrdinalIgnoreCase);

        var encodingStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lineEndingStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var extensionEncodingStats = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        var extensionLineEndingStats = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        var encodingFiles = new List<ProjectFileDetail>(files.Length);
        var lineEndingFiles = new List<ProjectFileDetail>(files.Length);
        var allFiles = new List<ProjectConsistencyFileDetail>(files.Length);

        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                var ext = info.Extension.ToLowerInvariant();
                var rel = ProjectFileAnalysis.ComputeRelativePath(root, file);

                var encName = ProjectFileAnalysis.DetectEncodingName(file);
                var le = ProjectFileAnalysis.DetectLineEnding(file);

                var encodingDetail = new ProjectFileDetail(
                    relativePath: rel,
                    fullPath: file,
                    extension: ext,
                    encoding: encName,
                    lineEnding: le.LineEnding,
                    hasFinalNewline: le.HasFinalNewline,
                    size: info.Length,
                    lastModified: info.LastWriteTime,
                    directory: info.DirectoryName ?? string.Empty);

                // Keep separate lists to mirror legacy output shape.
                encodingFiles.Add(encodingDetail);
                lineEndingFiles.Add(encodingDetail);

                if (!encodingStats.ContainsKey(encName)) encodingStats[encName] = 0;
                encodingStats[encName]++;

                if (!lineEndingStats.ContainsKey(le.LineEnding)) lineEndingStats[le.LineEnding] = 0;
                lineEndingStats[le.LineEnding]++;

                if (!extensionEncodingStats.TryGetValue(ext, out var perExtEnc))
                {
                    perExtEnc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    extensionEncodingStats[ext] = perExtEnc;
                }
                if (!perExtEnc.ContainsKey(encName)) perExtEnc[encName] = 0;
                perExtEnc[encName]++;

                if (!extensionLineEndingStats.TryGetValue(ext, out var perExtLe))
                {
                    perExtLe = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    extensionLineEndingStats[ext] = perExtLe;
                }
                if (!perExtLe.ContainsKey(le.LineEnding)) perExtLe[le.LineEnding] = 0;
                perExtLe[le.LineEnding]++;

                var needsEncodingConversion = !string.Equals(encName, recommendedEncoding, StringComparison.OrdinalIgnoreCase);
                var needsLineEndingConversion =
                    !string.Equals(le.LineEnding, recommendedLineEnding, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(le.LineEnding, "None", StringComparison.OrdinalIgnoreCase);
                var hasMixedLineEndings = string.Equals(le.LineEnding, "Mixed", StringComparison.OrdinalIgnoreCase);
                var missingFinalNewline = !le.HasFinalNewline && info.Length > 0 && finalNewlineExtensions.Contains(ext);

                var detail = new ProjectConsistencyFileDetail(
                    relativePath: rel,
                    fullPath: file,
                    extension: ext,
                    currentEncoding: encName,
                    currentLineEnding: le.LineEnding,
                    recommendedEncoding: recommendedEncoding,
                    recommendedLineEnding: recommendedLineEnding,
                    needsEncodingConversion: needsEncodingConversion,
                    needsLineEndingConversion: needsLineEndingConversion,
                    hasMixedLineEndings: hasMixedLineEndings,
                    missingFinalNewline: missingFinalNewline,
                    size: info.Length,
                    lastModified: info.LastWriteTime,
                    directory: info.DirectoryName ?? string.Empty);

                allFiles.Add(detail);
            }
            catch (Exception ex)
            {
                WriteWarning($"Failed to analyze {file}: {ex.Message}");
            }
        }

        var totalFiles = allFiles.Count;
        if (totalFiles == 0)
        {
            WriteWarning("No files could be analyzed");
            return;
        }

        var filesNeedingEncodingConversion = allFiles.Count(f => f.NeedsEncodingConversion);
        var filesNeedingLineEndingConversion = allFiles.Count(f => f.NeedsLineEndingConversion);
        var filesWithMixedLineEndings = allFiles.Count(f => f.HasMixedLineEndings);
        var filesMissingFinalNewline = allFiles.Count(f => f.MissingFinalNewline);
        var filesWithIssues = allFiles.Count(f => f.HasIssues);
        var filesCompliant = totalFiles - filesWithIssues;
        var compliancePercentage = totalFiles > 0
            ? Math.Round((double)filesCompliant / totalFiles * 100, 1)
            : 0;

        var encodingDistribution = new Hashtable();
        foreach (var kv in encodingStats.OrderByDescending(kv => kv.Value))
            encodingDistribution[kv.Key] = kv.Value;

        var lineEndingDistribution = new Hashtable();
        foreach (var kv in lineEndingStats.OrderByDescending(kv => kv.Value))
            lineEndingDistribution[kv.Key] = kv.Value;

        var extensionIssues = BuildExtensionIssues(allFiles);

        var summary = new OrderedDictionary
        {
            ["ProjectPath"] = root,
            ["ProjectType"] = ProjectType,
            ["AnalysisDate"] = DateTime.Now,

            ["TotalFiles"] = totalFiles,
            ["FilesCompliant"] = filesCompliant,
            ["FilesWithIssues"] = filesWithIssues,
            ["CompliancePercentage"] = compliancePercentage,

            ["CurrentEncodingDistribution"] = encodingDistribution,
            ["FilesNeedingEncodingConversion"] = filesNeedingEncodingConversion,
            ["RecommendedEncoding"] = recommendedEncoding,

            ["CurrentLineEndingDistribution"] = lineEndingDistribution,
            ["FilesNeedingLineEndingConversion"] = filesNeedingLineEndingConversion,
            ["FilesWithMixedLineEndings"] = filesWithMixedLineEndings,
            ["FilesMissingFinalNewline"] = filesMissingFinalNewline,
            ["RecommendedLineEnding"] = recommendedLineEnding,

            ["ExtensionIssues"] = extensionIssues
        };

        // Display summary (preserve existing UX: always prints to host).
        HostWriteLineSafe("");
        HostWriteLineSafe("Project Consistency Summary:", ConsoleColor.Cyan);
        HostWriteLineSafe($"  Total files analyzed: {totalFiles}");
        HostWriteLineSafe($"  Files compliant with standards: {filesCompliant} ({compliancePercentage.ToString("0.0", CultureInfo.InvariantCulture)}%)",
            compliancePercentage >= 90 ? ConsoleColor.Green : compliancePercentage >= 70 ? ConsoleColor.Yellow : ConsoleColor.Red);
        HostWriteLineSafe($"  Files needing attention: {filesWithIssues}", filesWithIssues == 0 ? ConsoleColor.Green : ConsoleColor.Red);

        HostWriteLineSafe("");
        HostWriteLineSafe("Encoding Issues:", ConsoleColor.Cyan);
        HostWriteLineSafe($"  Files needing encoding conversion: {filesNeedingEncodingConversion}",
            filesNeedingEncodingConversion == 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
        HostWriteLineSafe($"  Target encoding: {recommendedEncoding}");

        HostWriteLineSafe("");
        HostWriteLineSafe("Line Ending Issues:", ConsoleColor.Cyan);
        HostWriteLineSafe($"  Files needing line ending conversion: {filesNeedingLineEndingConversion}",
            filesNeedingLineEndingConversion == 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
        HostWriteLineSafe($"  Files with mixed line endings: {filesWithMixedLineEndings}",
            filesWithMixedLineEndings == 0 ? ConsoleColor.Green : ConsoleColor.Red);
        HostWriteLineSafe($"  Files missing final newline: {filesMissingFinalNewline}",
            filesMissingFinalNewline == 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
        HostWriteLineSafe($"  Target line ending: {recommendedLineEnding}");

        if (extensionIssues.Count > 0)
        {
            HostWriteLineSafe("");
            HostWriteLineSafe("Extensions with Issues:", ConsoleColor.Yellow);
            var orderedIssues = extensionIssues
                .Cast<DictionaryEntry>()
                .Select(de => new { Ext = de.Key as string, Stats = de.Value as Hashtable })
                .Where(x => !string.IsNullOrWhiteSpace(x.Ext) && x.Stats is not null)
                .OrderByDescending(x => GetInt(x.Stats!, "Total"))
                .ToArray();

            foreach (var item in orderedIssues)
            {
                HostWriteLineSafe($"  {item.Ext}: {GetInt(item.Stats!, "Total")} files");
            }
        }

        if (ExportPath is not null && allFiles.Count > 0)
        {
            try
            {
                WriteCsv(
                    ExportPath,
                    headers: new[]
                    {
                        "RelativePath","FullPath","Extension",
                        "CurrentEncoding","CurrentLineEnding",
                        "RecommendedEncoding","RecommendedLineEnding",
                        "NeedsEncodingConversion","NeedsLineEndingConversion","HasMixedLineEndings","MissingFinalNewline","HasIssues",
                        "Size","LastModified","Directory"
                    },
                    rows: allFiles.Select(f => new[]
                    {
                        f.RelativePath,
                        f.FullPath,
                        f.Extension,
                        f.CurrentEncoding,
                        f.CurrentLineEnding,
                        f.RecommendedEncoding,
                        f.RecommendedLineEnding,
                        f.NeedsEncodingConversion.ToString(CultureInfo.InvariantCulture),
                        f.NeedsLineEndingConversion.ToString(CultureInfo.InvariantCulture),
                        f.HasMixedLineEndings.ToString(CultureInfo.InvariantCulture),
                        f.MissingFinalNewline.ToString(CultureInfo.InvariantCulture),
                        f.HasIssues.ToString(CultureInfo.InvariantCulture),
                        f.Size.ToString(CultureInfo.InvariantCulture),
                        f.LastModified.ToString("o", CultureInfo.InvariantCulture),
                        f.Directory
                    }));
                HostWriteLineSafe("");
                HostWriteLineSafe($"Detailed report exported to: {ExportPath}", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                WriteWarning($"Failed to export report to {ExportPath}: {ex.Message}");
            }
        }

        var encodingReport = BuildEncodingReport(root, ProjectType, encodingStats, extensionEncodingStats, encodingFiles);
        var lineEndingReport = BuildLineEndingReport(root, ProjectType, lineEndingStats, extensionLineEndingStats, lineEndingFiles);

        object? filesOut = ShowDetails.IsPresent ? allFiles.ToArray() : null;
        var problematic = allFiles.Where(f => f.HasIssues).ToArray();

        var report = new OrderedDictionary
        {
            ["Summary"] = summary,
            ["EncodingReport"] = encodingReport,
            ["LineEndingReport"] = lineEndingReport,
            ["Files"] = filesOut,
            ["ProblematicFiles"] = problematic
        };

        WriteObject(report);
    }

    private static string[] ResolvePatterns(string projectType, string[]? custom)
    {
        if (projectType.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            return (custom is null || custom.Length == 0) ? Array.Empty<string>() : custom;

        return projectType switch
        {
            "PowerShell" => new[] { "*.ps1", "*.psm1", "*.psd1", "*.ps1xml" },
            "CSharp" => new[] { "*.cs", "*.csx", "*.csproj", "*.sln", "*.config", "*.json", "*.xml", "*.resx" },
            "Mixed" => new[] { "*.ps1", "*.psm1", "*.psd1", "*.ps1xml", "*.cs", "*.csx", "*.csproj", "*.sln", "*.config", "*.json", "*.xml" },
            "All" => new[] { "*.ps1", "*.psm1", "*.psd1", "*.ps1xml", "*.cs", "*.csx", "*.csproj", "*.sln", "*.config", "*.json", "*.xml", "*.js", "*.ts", "*.py", "*.rb", "*.java", "*.cpp", "*.h", "*.hpp", "*.sql", "*.md", "*.txt", "*.yaml", "*.yml" },
            _ => new[] { "*.ps1", "*.psm1", "*.psd1", "*.ps1xml", "*.cs", "*.csx", "*.csproj", "*.sln", "*.config", "*.json", "*.xml" }
        };
    }

    private static ProjectKind ResolveKind(string projectType)
    {
        return projectType switch
        {
            "PowerShell" => ProjectKind.PowerShell,
            "CSharp" => ProjectKind.CSharp,
            "All" => ProjectKind.All,
            _ => ProjectKind.Mixed
        };
    }

    private static string ResolveRecommendedEncoding(string projectType, string? userValue)
    {
        if (!string.IsNullOrWhiteSpace(userValue)) return userValue!;

        return projectType switch
        {
            "PowerShell" => "UTF8BOM",
            "Mixed" => "UTF8BOM",
            _ => "UTF8"
        };
    }

    private static string ResolveRecommendedLineEnding(string? userValue)
    {
        if (!string.IsNullOrWhiteSpace(userValue)) return userValue!;
        return Environment.NewLine == "\r\n" ? "CRLF" : "LF";
    }

    private static Hashtable BuildExtensionIssues(IEnumerable<ProjectConsistencyFileDetail> allFiles)
    {
        var extensionIssues = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var file in allFiles.Where(f => f.HasIssues))
        {
            if (!extensionIssues.ContainsKey(file.Extension))
            {
                extensionIssues[file.Extension] = new Hashtable
                {
                    ["Total"] = 0,
                    ["EncodingIssues"] = 0,
                    ["LineEndingIssues"] = 0,
                    ["MixedLineEndings"] = 0
                };
            }

            var stats = (Hashtable)extensionIssues[file.Extension]!;
            stats["Total"] = GetInt(stats, "Total") + 1;
            if (file.NeedsEncodingConversion) stats["EncodingIssues"] = GetInt(stats, "EncodingIssues") + 1;
            if (file.NeedsLineEndingConversion) stats["LineEndingIssues"] = GetInt(stats, "LineEndingIssues") + 1;
            if (file.HasMixedLineEndings) stats["MixedLineEndings"] = GetInt(stats, "MixedLineEndings") + 1;
        }
        return extensionIssues;
    }

    private static int GetInt(Hashtable table, string key)
        => table[key] is int v ? v : 0;

    private static OrderedDictionary BuildEncodingReport(
        string root,
        string projectType,
        IReadOnlyDictionary<string, int> encodingStats,
        IReadOnlyDictionary<string, Dictionary<string, int>> extensionEncodingStats,
        IReadOnlyList<ProjectFileDetail> files)
    {
        var totalFiles = files.Count;
        var uniqueEncodings = encodingStats.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
        var mostCommonEncoding = encodingStats.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? string.Empty;
        var inconsistentExtensions = extensionEncodingStats
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var encodingDistribution = new Hashtable();
        foreach (var kv in encodingStats.OrderByDescending(kv => kv.Value))
            encodingDistribution[kv.Key] = kv.Value;

        var extensionEncodingMap = new Hashtable();
        foreach (var ext in extensionEncodingStats.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var inner = new Hashtable();
            foreach (var kv in extensionEncodingStats[ext].OrderByDescending(kv => kv.Value))
                inner[kv.Key] = kv.Value;
            extensionEncodingMap[ext] = inner;
        }

        var summary = new OrderedDictionary
        {
            ["ProjectPath"] = root,
            ["ProjectType"] = projectType,
            ["TotalFiles"] = totalFiles,
            ["UniqueEncodings"] = uniqueEncodings,
            ["EncodingCount"] = uniqueEncodings.Length,
            ["MostCommonEncoding"] = mostCommonEncoding,
            ["InconsistentExtensions"] = inconsistentExtensions,
            ["EncodingDistribution"] = encodingDistribution,
            ["ExtensionEncodingMap"] = extensionEncodingMap,
            ["AnalysisDate"] = DateTime.Now
        };

        return new OrderedDictionary
        {
            ["Summary"] = summary,
            ["Files"] = files.ToArray(),
            ["GroupedByEncoding"] = null
        };
    }

    private static OrderedDictionary BuildLineEndingReport(
        string root,
        string projectType,
        IReadOnlyDictionary<string, int> lineEndingStats,
        IReadOnlyDictionary<string, Dictionary<string, int>> extensionLineEndingStats,
        IReadOnlyList<ProjectFileDetail> files)
    {
        var totalFiles = files.Count;
        var uniqueLineEndings = lineEndingStats.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
        var mostCommonLineEnding = lineEndingStats.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? string.Empty;
        var inconsistentExtensions = extensionLineEndingStats
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lineEndingDistribution = new Hashtable();
        foreach (var kv in lineEndingStats.OrderByDescending(kv => kv.Value))
            lineEndingDistribution[kv.Key] = kv.Value;

        var extensionLineEndingMap = new Hashtable();
        foreach (var ext in extensionLineEndingStats.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var inner = new Hashtable();
            foreach (var kv in extensionLineEndingStats[ext].OrderByDescending(kv => kv.Value))
                inner[kv.Key] = kv.Value;
            extensionLineEndingMap[ext] = inner;
        }

        var problemFiles = files.Where(f => string.Equals(f.LineEnding, "Mixed", StringComparison.OrdinalIgnoreCase)).ToArray();

        var summary = new OrderedDictionary
        {
            ["ProjectPath"] = root,
            ["ProjectType"] = projectType,
            ["TotalFiles"] = totalFiles,
            ["UniqueLineEndings"] = uniqueLineEndings,
            ["LineEndingCount"] = uniqueLineEndings.Length,
            ["MostCommonLineEnding"] = mostCommonLineEnding,
            ["InconsistentExtensions"] = inconsistentExtensions,
            ["ProblemFiles"] = problemFiles.Length,
            ["LineEndingDistribution"] = lineEndingDistribution,
            ["ExtensionLineEndingMap"] = extensionLineEndingMap,
            ["AnalysisDate"] = DateTime.Now
        };

        return new OrderedDictionary
        {
            ["Summary"] = summary,
            ["Files"] = files.ToArray(),
            ["ProblemFiles"] = problemFiles,
            ["GroupedByLineEnding"] = null
        };
    }

    private static void WriteCsv(string path, IEnumerable<string> headers, IEnumerable<string[]> rows)
    {
        using var sw = new StreamWriter(path, append: false, new UTF8Encoding(true));
        sw.WriteLine(string.Join(",", headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            sw.WriteLine(string.Join(",", row.Select(EscapeCsv)));
        }
    }

    private static string EscapeCsv(string? value)
    {
        value ??= string.Empty;
        bool mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!mustQuote) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private void HostWriteLineSafe(string text, ConsoleColor? fg = null)
    {
        try
        {
            if (fg.HasValue)
            {
                var bg = Host?.UI?.RawUI?.BackgroundColor ?? ConsoleColor.Black;
                Host?.UI?.WriteLine(fg.Value, bg, text);
            }
            else
            {
                Host?.UI?.WriteLine(text);
            }
        }
        catch
        {
            // ignore host limitations
        }
    }

    private sealed class ProjectFileDetail
    {
        public string RelativePath { get; }
        public string FullPath { get; }
        public string Extension { get; }
        public string Encoding { get; }
        public string LineEnding { get; }
        public bool HasFinalNewline { get; }
        public long Size { get; }
        public DateTime LastModified { get; }
        public string Directory { get; }

        public ProjectFileDetail(
            string relativePath,
            string fullPath,
            string extension,
            string encoding,
            string lineEnding,
            bool hasFinalNewline,
            long size,
            DateTime lastModified,
            string directory)
        {
            RelativePath = relativePath;
            FullPath = fullPath;
            Extension = extension;
            Encoding = encoding;
            LineEnding = lineEnding;
            HasFinalNewline = hasFinalNewline;
            Size = size;
            LastModified = lastModified;
            Directory = directory;
        }
    }

    private sealed class ProjectConsistencyFileDetail
    {
        public string RelativePath { get; }
        public string FullPath { get; }
        public string Extension { get; }
        public string CurrentEncoding { get; }
        public string CurrentLineEnding { get; }
        public string RecommendedEncoding { get; }
        public string RecommendedLineEnding { get; }
        public bool NeedsEncodingConversion { get; }
        public bool NeedsLineEndingConversion { get; }
        public bool HasMixedLineEndings { get; }
        public bool MissingFinalNewline { get; }
        public bool HasIssues { get; }
        public long Size { get; }
        public DateTime LastModified { get; }
        public string Directory { get; }

        public ProjectConsistencyFileDetail(
            string relativePath,
            string fullPath,
            string extension,
            string currentEncoding,
            string currentLineEnding,
            string recommendedEncoding,
            string recommendedLineEnding,
            bool needsEncodingConversion,
            bool needsLineEndingConversion,
            bool hasMixedLineEndings,
            bool missingFinalNewline,
            long size,
            DateTime lastModified,
            string directory)
        {
            RelativePath = relativePath;
            FullPath = fullPath;
            Extension = extension;
            CurrentEncoding = currentEncoding;
            CurrentLineEnding = currentLineEnding;
            RecommendedEncoding = recommendedEncoding;
            RecommendedLineEnding = recommendedLineEnding;
            NeedsEncodingConversion = needsEncodingConversion;
            NeedsLineEndingConversion = needsLineEndingConversion;
            HasMixedLineEndings = hasMixedLineEndings;
            MissingFinalNewline = missingFinalNewline;
            HasIssues = needsEncodingConversion || needsLineEndingConversion || hasMixedLineEndings || missingFinalNewline;
            Size = size;
            LastModified = lastModified;
            Directory = directory;
        }
    }
}
