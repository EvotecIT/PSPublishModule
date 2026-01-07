using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Provides comprehensive analysis of encoding and line ending consistency across a project.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet combines encoding and line-ending analysis to provide a single “consistency” view of your repository.
/// It is intended to be run before bulk conversions (encoding/line endings) and before packaging a module for release.
/// </para>
/// <para>
/// For fixing issues during builds, use <c>New-ConfigurationFileConsistency</c> with <c>-AutoFix</c> enabled.
/// </para>
/// </remarks>
/// <example>
/// <summary>Analyze a PowerShell project with defaults</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ProjectConsistency -Path 'C:\MyProject' -ProjectType PowerShell</code>
/// <para>Reports encoding and line ending consistency using PowerShell-friendly defaults.</para>
/// </example>
/// <example>
/// <summary>Analyze with explicit recommendations and detailed output</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ProjectConsistency -Path 'C:\MyProject' -ProjectType Mixed -RecommendedEncoding UTF8BOM -RecommendedLineEnding LF -ShowDetails</code>
/// <para>Useful when you want to enforce a policy (e.g. UTF-8 BOM for PS 5.1 compatibility and LF for cross-platform repos).</para>
/// </example>
/// <example>
/// <summary>Export a detailed report to CSV</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ProjectConsistency -Path 'C:\MyProject' -ProjectType CSharp -RecommendedEncoding UTF8 -ExportPath 'C:\Reports\consistency-report.csv'</code>
/// <para>Exports the per-file details so you can review issues outside the console.</para>
/// </example>
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

    /// <summary>The encoding standard you want to achieve (optional).</summary>
    [Parameter]
    public TextEncodingKind? RecommendedEncoding { get; set; }

    /// <summary>The line ending standard you want to achieve (optional).</summary>
    [Parameter]
    public FileConsistencyLineEnding? RecommendedLineEnding { get; set; }

    /// <summary>Include detailed file-by-file analysis in the output.</summary>
    [Parameter]
    public SwitchParameter ShowDetails { get; set; }

    /// <summary>Export the detailed report to a CSV file at the specified path.</summary>
    [Parameter]
    public string? ExportPath { get; set; }

    /// <summary>
    /// Executes the consistency analysis and returns a typed report object.
    /// </summary>
    protected override void ProcessRecord()
    {
        var root = System.IO.Path.GetFullPath(Path.Trim().Trim('"'));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Project path '{root}' not found or is not a directory");

        if (RecommendedEncoding == TextEncodingKind.Any)
            throw new PSArgumentException("RecommendedEncoding cannot be Any for project consistency analysis.");

        var patterns = ResolvePatterns(ProjectType, CustomExtensions);
        WriteVerbose($"Project type: {ProjectType} with patterns: {string.Join(", ", patterns)}");

        HostWriteLineSafe("🔎 Analyzing project consistency...", ConsoleColor.Cyan);
        HostWriteLineSafe($"Project: {root}");
        HostWriteLineSafe($"Type: {ProjectType}");

        var enumeration = new ProjectEnumeration(
            rootPath: root,
            kind: ResolveKind(ProjectType),
            customExtensions: ProjectType.Equals("Custom", StringComparison.OrdinalIgnoreCase) ? patterns : null,
            excludeDirectories: ExcludeDirectories);

        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var analyzer = new ProjectConsistencyAnalyzer(logger);
        var report = analyzer.Analyze(
            enumeration: enumeration,
            projectType: ProjectType,
            recommendedEncoding: RecommendedEncoding,
            recommendedLineEnding: RecommendedLineEnding,
            includeDetails: ShowDetails.IsPresent,
            exportPath: ExportPath,
            encodingOverrides: null,
            lineEndingOverrides: null);

        if (report.Summary.TotalFiles == 0)
        {
            WriteWarning("No files found matching the specified criteria");
            return;
        }

        var s = report.Summary;

        HostWriteLineSafe($"Target encoding: {s.RecommendedEncoding}");
        HostWriteLineSafe($"Target line ending: {s.RecommendedLineEnding}");

        // Display summary (preserve existing UX: always prints to host).
        HostWriteLineSafe("");
        HostWriteLineSafe("Project Consistency Summary:", ConsoleColor.Cyan);
        HostWriteLineSafe($"  Total files analyzed: {s.TotalFiles}");
        HostWriteLineSafe(
            $"  Files compliant with standards: {s.FilesCompliant} ({s.CompliancePercentage.ToString("0.0", CultureInfo.InvariantCulture)}%)",
            s.CompliancePercentage >= 90 ? ConsoleColor.Green : s.CompliancePercentage >= 70 ? ConsoleColor.Yellow : ConsoleColor.Red);
        HostWriteLineSafe($"  Files needing attention: {s.FilesWithIssues}", s.FilesWithIssues == 0 ? ConsoleColor.Green : ConsoleColor.Red);

        HostWriteLineSafe("");
        HostWriteLineSafe("Encoding Issues:", ConsoleColor.Cyan);
        HostWriteLineSafe(
            $"  Files needing encoding conversion: {s.FilesNeedingEncodingConversion}",
            s.FilesNeedingEncodingConversion == 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
        HostWriteLineSafe($"  Target encoding: {s.RecommendedEncoding}");

        HostWriteLineSafe("");
        HostWriteLineSafe("Line Ending Issues:", ConsoleColor.Cyan);
        HostWriteLineSafe(
            $"  Files needing line ending conversion: {s.FilesNeedingLineEndingConversion}",
            s.FilesNeedingLineEndingConversion == 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
        HostWriteLineSafe(
            $"  Files with mixed line endings: {s.FilesWithMixedLineEndings}",
            s.FilesWithMixedLineEndings == 0 ? ConsoleColor.Green : ConsoleColor.Red);
        HostWriteLineSafe(
            $"  Files missing final newline: {s.FilesMissingFinalNewline}",
            s.FilesMissingFinalNewline == 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
        HostWriteLineSafe($"  Target line ending: {s.RecommendedLineEnding}");

        if (s.ExtensionIssues.Length > 0)
        {
            HostWriteLineSafe("");
            HostWriteLineSafe("Extensions with Issues:", ConsoleColor.Yellow);
            foreach (var issue in s.ExtensionIssues.OrderByDescending(i => i.Total))
                HostWriteLineSafe($"  {issue.Extension}: {issue.Total} files");
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
