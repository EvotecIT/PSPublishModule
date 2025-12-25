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
/// Analyzes line ending consistency across all files in a project directory.
/// </summary>
[Cmdlet(VerbsCommon.Get, "ProjectLineEnding")]
public sealed class GetProjectLineEndingCommand : PSCmdlet
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

    /// <summary>Group results by line ending type.</summary>
    [Parameter]
    public SwitchParameter GroupByLineEnding { get; set; }

    /// <summary>Include individual file details in the report.</summary>
    [Parameter]
    public SwitchParameter ShowFiles { get; set; }

    /// <summary>Additionally check for files with mixed line endings.</summary>
    [Parameter]
    public SwitchParameter CheckMixed { get; set; }

    /// <summary>Export the detailed report to a CSV file at the specified path.</summary>
    [Parameter]
    public string? ExportPath { get; set; }

    /// <summary>Internal mode: avoid host output; use verbose messages instead.</summary>
    [Parameter]
    public SwitchParameter Internal { get; set; }

    /// <summary>
    /// Executes the line ending analysis and returns a report object.
    /// </summary>
    protected override void ProcessRecord()
    {
        var root = System.IO.Path.GetFullPath(Path.Trim().Trim('"'));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Project path '{root}' not found or is not a directory");

        var patterns = ResolvePatterns(ProjectType, CustomExtensions);
        if (Internal.IsPresent)
        {
            WriteVerbose($"Analyzing project line endings for: {root}");
            WriteVerbose($"Project type: {ProjectType} with patterns: {string.Join(", ", patterns)}");
        }
        else
        {
            HostWriteLineSafe("Analyzing project line endings...", ConsoleColor.Cyan);
            WriteVerbose($"Project type: {ProjectType} with patterns: {string.Join(", ", patterns)}");
        }

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
            if (Internal.IsPresent) WriteVerbose("No files found matching the specified criteria");
            else WriteWarning("No files found matching the specified criteria");
            return;
        }

        if (Internal.IsPresent) WriteVerbose($"Analyzing {files.Length} files...");
        else HostWriteLineSafe($"Analyzing {files.Length} files...", ConsoleColor.Green);

        var fileDetails = new List<LineEndingFileDetail>(files.Length);
        var lineEndingStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var extensionStats = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        var problemFiles = new List<LineEndingFileDetail>();
        var filesWithoutFinalNewline = new List<LineEndingFileDetail>();

        var finalNewlineExtensions = new HashSet<string>(new[]
        {
            ".ps1",".psm1",".psd1",".cs",".js",".py",".rb",".java",".cpp",".h",".hpp",".sql",".md",".txt",".yaml",".yml"
        }, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                var ext = info.Extension.ToLowerInvariant();
                var rel = ProjectFileAnalysis.ComputeRelativePath(root, file);

                var le = ProjectFileAnalysis.DetectLineEnding(file);

                var detail = new LineEndingFileDetail(
                    relativePath: rel,
                    fullPath: file,
                    extension: ext,
                    lineEnding: le.LineEnding,
                    hasFinalNewline: le.HasFinalNewline,
                    size: info.Length,
                    lastModified: info.LastWriteTime,
                    directory: info.DirectoryName ?? string.Empty);

                fileDetails.Add(detail);

                if (detail.LineEnding == "Mixed" || (CheckMixed.IsPresent && detail.LineEnding == "Mixed"))
                    problemFiles.Add(detail);

                if (!detail.HasFinalNewline && info.Length > 0 && finalNewlineExtensions.Contains(ext))
                    filesWithoutFinalNewline.Add(detail);

                if (!lineEndingStats.ContainsKey(detail.LineEnding)) lineEndingStats[detail.LineEnding] = 0;
                lineEndingStats[detail.LineEnding]++;

                if (!extensionStats.TryGetValue(ext, out var perExt))
                {
                    perExt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    extensionStats[ext] = perExt;
                }
                if (!perExt.ContainsKey(detail.LineEnding)) perExt[detail.LineEnding] = 0;
                perExt[detail.LineEnding]++;
            }
            catch (Exception ex)
            {
                if (Internal.IsPresent) WriteVerbose($"Failed to analyze {file}: {ex.Message}");
                else WriteWarning($"Failed to analyze {file}: {ex.Message}");
            }
        }

        var totalFiles = fileDetails.Count;
        var uniqueLineEndings = lineEndingStats.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
        var mostCommonLineEnding = lineEndingStats.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? string.Empty;
        var inconsistentExtensions = extensionStats
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hasProblems = problemFiles.Count > 0 || filesWithoutFinalNewline.Count > 0 || inconsistentExtensions.Length > 0;
        var status = !hasProblems
            ? "Pass"
            : (problemFiles.Count == 0 && inconsistentExtensions.Length <= 2 ? "Warning" : "Fail");

        var lineEndingDistribution = new Hashtable();
        foreach (var kv in lineEndingStats.OrderByDescending(kv => kv.Value))
            lineEndingDistribution[kv.Key] = kv.Value;

        var extensionLineEndingMap = new Hashtable();
        foreach (var ext in extensionStats.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var inner = new Hashtable();
            foreach (var kv in extensionStats[ext].OrderByDescending(kv => kv.Value))
                inner[kv.Key] = kv.Value;
            extensionLineEndingMap[ext] = inner;
        }

        var recommendations = new string[0];
        if (hasProblems)
        {
            var recs = new List<string>();
            if (problemFiles.Count > 0) recs.Add("Fix files with mixed line endings");
            if (filesWithoutFinalNewline.Count > 0) recs.Add("Add missing final newlines");
            if (inconsistentExtensions.Length > 0) recs.Add("Standardize line endings by file extension");
            recs.Add("Use Convert-ProjectLineEnding to fix issues");
            recommendations = recs.ToArray();
        }

        var message = status switch
        {
            "Pass" => $"All {totalFiles} files have consistent line endings",
            "Warning" => $"Minor line ending issues found: {problemFiles.Count} mixed files, {filesWithoutFinalNewline.Count} missing newlines",
            _ => $"Significant line ending issues: {problemFiles.Count} mixed files, {inconsistentExtensions.Length} inconsistent extensions"
        };

        var summary = new OrderedDictionary
        {
            ["Status"] = status,
            ["ProjectPath"] = root,
            ["ProjectType"] = ProjectType,
            ["TotalFiles"] = totalFiles,
            ["UniqueLineEndings"] = uniqueLineEndings,
            ["LineEndingCount"] = uniqueLineEndings.Length,
            ["MostCommonLineEnding"] = mostCommonLineEnding,
            ["InconsistentExtensions"] = inconsistentExtensions,
            ["ProblemFiles"] = problemFiles.Count,
            ["FilesWithoutFinalNewline"] = filesWithoutFinalNewline.Count,
            ["LineEndingDistribution"] = lineEndingDistribution,
            ["ExtensionLineEndingMap"] = extensionLineEndingMap,
            ["AnalysisDate"] = DateTime.Now,
            ["Message"] = message,
            ["Recommendations"] = recommendations
        };

        if (Internal.IsPresent)
        {
            WriteVerbose($"Line Ending Analysis: {status} - {message}");
            if (!string.Equals(status, "Pass", StringComparison.OrdinalIgnoreCase) && recommendations.Length > 0)
                WriteVerbose($"Recommendations: {string.Join("; ", recommendations)}");
        }
        else
        {
            HostWriteLineSafe("");
            HostWriteLineSafe("Line Ending Analysis Summary:", ConsoleColor.Cyan);
            HostWriteLineSafe($"Status: {status}", status == "Pass" ? ConsoleColor.Green : status == "Warning" ? ConsoleColor.Yellow : ConsoleColor.Red);
            HostWriteLineSafe($"  Total files analyzed: {totalFiles}");
            HostWriteLineSafe($"  Unique line endings found: {uniqueLineEndings.Length}");
            HostWriteLineSafe($"  Most common line ending: {mostCommonLineEnding} ({(lineEndingStats.TryGetValue(mostCommonLineEnding, out var c) ? c : 0)} files)", ConsoleColor.Green);

            if (problemFiles.Count > 0)
                HostWriteLineSafe($"  ⚠️  Files with mixed line endings: {problemFiles.Count}", ConsoleColor.Red);

            if (filesWithoutFinalNewline.Count > 0)
                HostWriteLineSafe($"  ⚠️  Files without final newline: {filesWithoutFinalNewline.Count}", ConsoleColor.Yellow);
            else
                HostWriteLineSafe("  ✅ All files end with proper newlines", ConsoleColor.Green);

            if (inconsistentExtensions.Length > 0)
                HostWriteLineSafe($"  ⚠️  Extensions with mixed line endings: {string.Join(", ", inconsistentExtensions)}", ConsoleColor.Yellow);
            else
                HostWriteLineSafe("  ✅ All file extensions have consistent line endings", ConsoleColor.Green);

            if (recommendations.Length > 0)
            {
                HostWriteLineSafe("");
                HostWriteLineSafe("💡 Recommendations:", ConsoleColor.Cyan);
                foreach (var r in recommendations) HostWriteLineSafe($"  • {r}", ConsoleColor.Yellow);
            }

            HostWriteLineSafe("");
            HostWriteLineSafe("Line Ending Distribution:", ConsoleColor.Cyan);
            foreach (var kv in lineEndingStats.OrderByDescending(kv => kv.Value))
            {
                var pct = totalFiles > 0 ? Math.Round((double)kv.Value / totalFiles * 100, 1) : 0;
                HostWriteLineSafe($"  {kv.Key}: {kv.Value} files ({pct.ToString("0.0", CultureInfo.InvariantCulture)}%)");
            }
        }

        if (ExportPath is not null && fileDetails.Count > 0)
        {
            try
            {
                WriteCsv(
                    ExportPath,
                    headers: new[] { "RelativePath", "FullPath", "Extension", "LineEnding", "HasFinalNewline", "Size", "LastModified", "Directory" },
                    rows: fileDetails.Select(f => new[]
                    {
                        f.RelativePath,
                        f.FullPath,
                        f.Extension,
                        f.LineEnding,
                        f.HasFinalNewline ? "True" : "False",
                        f.Size.ToString(CultureInfo.InvariantCulture),
                        f.LastModified.ToString("o", CultureInfo.InvariantCulture),
                        f.Directory
                    }));

                if (Internal.IsPresent) WriteVerbose($"Detailed report exported to: {ExportPath}");
                else
                {
                    HostWriteLineSafe("");
                    HostWriteLineSafe($"Detailed report exported to: {ExportPath}", ConsoleColor.Green);
                }
            }
            catch (Exception ex)
            {
                if (Internal.IsPresent) WriteVerbose($"Failed to export report to {ExportPath}: {ex.Message}");
                else WriteWarning($"Failed to export report to {ExportPath}: {ex.Message}");
            }
        }

        object? filesOut = ShowFiles.IsPresent ? fileDetails.ToArray() : null;
        object? groupedOut = GroupByLineEnding.IsPresent ? GroupByLineEndingMap(fileDetails, uniqueLineEndings) : null;

        var report = new OrderedDictionary
        {
            ["Summary"] = summary,
            ["Files"] = filesOut,
            ["ProblemFiles"] = problemFiles.ToArray(),
            ["GroupedByLineEnding"] = groupedOut
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

    private static object GroupByLineEndingMap(IReadOnlyList<LineEndingFileDetail> files, IReadOnlyList<string> lineEndings)
    {
        var grouped = new Hashtable();
        foreach (var le in lineEndings)
        {
            grouped[le] = files.Where(f => string.Equals(f.LineEnding, le, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        return grouped;
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

    private sealed class LineEndingFileDetail
    {
        public string RelativePath { get; }
        public string FullPath { get; }
        public string Extension { get; }
        public string LineEnding { get; }
        public bool HasFinalNewline { get; }
        public long Size { get; }
        public DateTime LastModified { get; }
        public string Directory { get; }

        public LineEndingFileDetail(
            string relativePath,
            string fullPath,
            string extension,
            string lineEnding,
            bool hasFinalNewline,
            long size,
            DateTime lastModified,
            string directory)
        {
            RelativePath = relativePath;
            FullPath = fullPath;
            Extension = extension;
            LineEnding = lineEnding;
            HasFinalNewline = hasFinalNewline;
            Size = size;
            LastModified = lastModified;
            Directory = directory;
        }
    }
}
