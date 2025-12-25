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
/// Analyzes encoding consistency across all files in a project directory.
/// </summary>
[Cmdlet(VerbsCommon.Get, "ProjectEncoding")]
public sealed class GetProjectEncodingCommand : PSCmdlet
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

    /// <summary>Group results by encoding type.</summary>
    [Parameter]
    public SwitchParameter GroupByEncoding { get; set; }

    /// <summary>Include individual file details in the report.</summary>
    [Parameter]
    public SwitchParameter ShowFiles { get; set; }

    /// <summary>Export the detailed report to a CSV file at the specified path.</summary>
    [Parameter]
    public string? ExportPath { get; set; }

    /// <summary>
    /// Executes the encoding analysis and returns a report object.
    /// </summary>
    protected override void ProcessRecord()
    {
        var root = System.IO.Path.GetFullPath(Path.Trim().Trim('"'));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Project path '{root}' not found or is not a directory");

        var patterns = ResolvePatterns(ProjectType, CustomExtensions);
        WriteVerbose($"Project type: {ProjectType} with patterns: {string.Join(", ", patterns)}");

        HostWriteLineSafe("Analyzing project encoding...", ConsoleColor.Cyan);

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

        HostWriteLineSafe($"Analyzing {files.Length} files...", ConsoleColor.Green);

        var fileDetails = new List<EncodingFileDetail>(files.Length);
        var encodingStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var extensionStats = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            try
            {
                var encName = ProjectFileAnalysis.DetectEncodingName(file);
                var info = new FileInfo(file);
                var ext = info.Extension.ToLowerInvariant();
                var rel = ProjectFileAnalysis.ComputeRelativePath(root, file);

                var detail = new EncodingFileDetail(
                    relativePath: rel,
                    fullPath: file,
                    extension: ext,
                    encoding: encName,
                    size: info.Length,
                    lastModified: info.LastWriteTime,
                    directory: info.DirectoryName ?? string.Empty);

                fileDetails.Add(detail);

                if (!encodingStats.ContainsKey(encName)) encodingStats[encName] = 0;
                encodingStats[encName]++;

                if (!extensionStats.TryGetValue(ext, out var perExt))
                {
                    perExt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    extensionStats[ext] = perExt;
                }
                if (!perExt.ContainsKey(encName)) perExt[encName] = 0;
                perExt[encName]++;
            }
            catch (Exception ex)
            {
                WriteWarning($"Failed to analyze {file}: {ex.Message}");
            }
        }

        var totalFiles = fileDetails.Count;
        if (totalFiles == 0)
        {
            WriteWarning("No files found for analysis");
            return;
        }

        var uniqueEncodings = encodingStats.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
        var mostCommonEncoding = encodingStats.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? string.Empty;
        var inconsistentExtensions = extensionStats
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var encodingDistribution = new Hashtable();
        foreach (var kv in encodingStats.OrderByDescending(kv => kv.Value))
            encodingDistribution[kv.Key] = kv.Value;

        var extensionEncodingMap = new Hashtable();
        foreach (var ext in extensionStats.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var inner = new Hashtable();
            foreach (var kv in extensionStats[ext].OrderByDescending(kv => kv.Value))
                inner[kv.Key] = kv.Value;
            extensionEncodingMap[ext] = inner;
        }

        var summary = new OrderedDictionary
        {
            ["ProjectPath"] = root,
            ["ProjectType"] = ProjectType,
            ["TotalFiles"] = totalFiles,
            ["UniqueEncodings"] = uniqueEncodings,
            ["EncodingCount"] = uniqueEncodings.Length,
            ["MostCommonEncoding"] = mostCommonEncoding,
            ["InconsistentExtensions"] = inconsistentExtensions,
            ["EncodingDistribution"] = encodingDistribution,
            ["ExtensionEncodingMap"] = extensionEncodingMap,
            ["AnalysisDate"] = DateTime.Now
        };

        // Display summary (preserve existing UX: always prints to host).
        HostWriteLineSafe("");
        HostWriteLineSafe("Encoding Analysis Summary:", ConsoleColor.Cyan);
        HostWriteLineSafe($"  Total files analyzed: {totalFiles}");
        HostWriteLineSafe($"  Unique encodings found: {uniqueEncodings.Length}");
        if (!string.IsNullOrWhiteSpace(mostCommonEncoding))
        {
            HostWriteLineSafe($"  Most common encoding: {mostCommonEncoding} ({encodingStats[mostCommonEncoding]} files)", ConsoleColor.Green);
        }

        if (inconsistentExtensions.Length > 0)
        {
            HostWriteLineSafe($"  ⚠️  Extensions with mixed encodings: {string.Join(", ", inconsistentExtensions)}", ConsoleColor.Yellow);
        }
        else
        {
            HostWriteLineSafe("  ✅ All file extensions have consistent encodings", ConsoleColor.Green);
        }

        HostWriteLineSafe("");
        HostWriteLineSafe("Encoding Distribution:", ConsoleColor.Cyan);
        foreach (var kv in encodingStats.OrderByDescending(kv => kv.Value))
        {
            var pct = totalFiles > 0 ? Math.Round((double)kv.Value / totalFiles * 100, 1) : 0;
            HostWriteLineSafe($"  {kv.Key}: {kv.Value} files ({pct.ToString("0.0", CultureInfo.InvariantCulture)}%)");
        }

        if (ExportPath is not null && fileDetails.Count > 0)
        {
            try
            {
                WriteCsv(
                    ExportPath,
                    headers: new[] { "RelativePath", "FullPath", "Extension", "Encoding", "Size", "LastModified", "Directory" },
                    rows: fileDetails.Select(f => new[]
                    {
                        f.RelativePath,
                        f.FullPath,
                        f.Extension,
                        f.Encoding,
                        f.Size.ToString(CultureInfo.InvariantCulture),
                        f.LastModified.ToString("o", CultureInfo.InvariantCulture),
                        f.Directory
                    }));
                HostWriteLineSafe($"");
                HostWriteLineSafe($"Detailed report exported to: {ExportPath}", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                WriteWarning($"Failed to export report to {ExportPath}: {ex.Message}");
            }
        }

        object? filesOut = ShowFiles.IsPresent ? fileDetails.ToArray() : null;
        object? groupedOut = GroupByEncoding.IsPresent ? GroupByEncodingMap(fileDetails, uniqueEncodings) : null;

        var report = new OrderedDictionary
        {
            ["Summary"] = summary,
            ["Files"] = filesOut,
            ["GroupedByEncoding"] = groupedOut
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

    private static object GroupByEncodingMap(IReadOnlyList<EncodingFileDetail> files, IReadOnlyList<string> encodings)
    {
        var grouped = new Hashtable();
        foreach (var enc in encodings)
        {
            grouped[enc] = files.Where(f => string.Equals(f.Encoding, enc, StringComparison.OrdinalIgnoreCase)).ToArray();
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

    private sealed class EncodingFileDetail
    {
        public string RelativePath { get; }
        public string FullPath { get; }
        public string Extension { get; }
        public string Encoding { get; }
        public long Size { get; }
        public DateTime LastModified { get; }
        public string Directory { get; }

        public EncodingFileDetail(
            string relativePath,
            string fullPath,
            string extension,
            string encoding,
            long size,
            DateTime lastModified,
            string directory)
        {
            RelativePath = relativePath;
            FullPath = fullPath;
            Extension = extension;
            Encoding = encoding;
            Size = size;
            LastModified = lastModified;
            Directory = directory;
        }
    }
}
