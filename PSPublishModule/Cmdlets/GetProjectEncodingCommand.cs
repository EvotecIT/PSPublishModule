using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Analyzes encoding consistency across all files in a project directory.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet is read-only: it does not modify files. Use it to audit a repository before converting encodings.
/// </para>
/// <para>
/// To standardize file encodings after analysis, use <c>Convert-ProjectEncoding</c>.
/// </para>
/// </remarks>
/// <example>
/// <summary>Analyze a PowerShell project</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ProjectEncoding -Path 'C:\MyProject' -ProjectType PowerShell</code>
/// <para>Returns a summary of the encodings used in PowerShell-related files.</para>
/// </example>
/// <example>
/// <summary>Show per-file details grouped by encoding</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ProjectEncoding -Path 'C:\MyProject' -ProjectType Mixed -GroupByEncoding -ShowFiles</code>
/// <para>Useful when you need to identify which files are outliers (e.g. ASCII vs UTF-8 BOM).</para>
/// </example>
/// <example>
/// <summary>Export a detailed report to CSV</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ProjectEncoding -Path 'C:\MyProject' -ProjectType All -ExportPath 'C:\Reports\encoding-report.csv'</code>
/// <para>Creates a CSV report that can be shared or used in CI artifacts.</para>
/// </example>
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
    /// Executes the encoding analysis and returns a typed report object.
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

        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var analyzer = new ProjectEncodingAnalyzer(logger);
        var report = analyzer.Analyze(
            enumeration: enumeration,
            projectType: ProjectType,
            includeFiles: ShowFiles.IsPresent,
            groupByEncoding: GroupByEncoding.IsPresent,
            exportPath: ExportPath);

        if (report.Summary.TotalFiles == 0)
        {
            WriteWarning("No files found matching the specified criteria");
            return;
        }

        var s = report.Summary;

        HostWriteLineSafe("");
        HostWriteLineSafe("Encoding Analysis Summary:", ConsoleColor.Cyan);
        HostWriteLineSafe($"  Total files analyzed: {s.TotalFiles}");
        HostWriteLineSafe($"  Unique encodings found: {s.UniqueEncodings.Length}");
        if (s.ErrorFiles > 0)
            HostWriteLineSafe($"  Files with read errors: {s.ErrorFiles}", ConsoleColor.Yellow);

        if (s.MostCommonEncoding.HasValue)
        {
            var count = s.Distribution.FirstOrDefault(d => d.Encoding == s.MostCommonEncoding.Value)?.Count ?? 0;
            HostWriteLineSafe($"  Most common encoding: {s.MostCommonEncoding.Value} ({count} files)", ConsoleColor.Green);
        }

        if (s.InconsistentExtensions.Length > 0)
            HostWriteLineSafe($"  Extensions with mixed encodings: {string.Join(", ", s.InconsistentExtensions)}", ConsoleColor.Yellow);
        else
            HostWriteLineSafe("  All file extensions have consistent encoding", ConsoleColor.Green);

        HostWriteLineSafe("");
        HostWriteLineSafe("Encoding Distribution:", ConsoleColor.Cyan);
        var denom = Math.Max(1, s.TotalFiles - s.ErrorFiles);
        foreach (var d in s.Distribution)
        {
            var pct = Math.Round((double)d.Count / denom * 100, 1);
            HostWriteLineSafe($"  {d.Encoding}: {d.Count} files ({pct.ToString("0.00", CultureInfo.InvariantCulture)}%)");
        }

        if (!string.IsNullOrWhiteSpace(ExportPath) && File.Exists(ExportPath!))
        {
            HostWriteLineSafe("");
            HostWriteLineSafe($"Detailed report exported to: {ExportPath}", ConsoleColor.Green);
        }

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
}
