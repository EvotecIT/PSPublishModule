using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
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
    /// Executes the line ending analysis and returns a typed report object.
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

        var logger = new CmdletLogger(
            cmdlet: this,
            isVerbose: MyInvocation.BoundParameters.ContainsKey("Verbose"),
            warningsAsVerbose: Internal.IsPresent);

        var analyzer = new ProjectLineEndingAnalyzer(logger);
        var report = analyzer.Analyze(
            enumeration: enumeration,
            projectType: ProjectType,
            includeFiles: ShowFiles.IsPresent,
            groupByLineEnding: GroupByLineEnding.IsPresent,
            checkMixed: CheckMixed.IsPresent,
            exportPath: ExportPath);

        if (report.Summary.TotalFiles == 0)
        {
            if (Internal.IsPresent) WriteVerbose("No files found matching the specified criteria");
            else WriteWarning("No files found matching the specified criteria");
            return;
        }

        var s = report.Summary;

        if (Internal.IsPresent)
        {
            WriteVerbose($"Line Ending Analysis: {s.Status} - {s.Message}");
            if (s.Recommendations.Length > 0)
                WriteVerbose($"Recommendations: {string.Join("; ", s.Recommendations)}");
        }
        else
        {
            HostWriteLineSafe("");
            HostWriteLineSafe("Line Ending Analysis Summary:", ConsoleColor.Cyan);

            var statusColor = s.Status switch
            {
                CheckStatus.Pass => ConsoleColor.Green,
                CheckStatus.Warning => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };
            HostWriteLineSafe($"Status: {s.Status}", statusColor);
            HostWriteLineSafe($"  Total files analyzed: {s.TotalFiles}");
            HostWriteLineSafe($"  Unique line endings found: {s.UniqueLineEndings.Length}");

            var mostCount = s.Distribution.FirstOrDefault(d => d.LineEnding == s.MostCommonLineEnding)?.Count ?? 0;
            HostWriteLineSafe($"  Most common line ending: {s.MostCommonLineEnding} ({mostCount} files)", ConsoleColor.Green);

            if (s.ProblemFiles > 0)
                HostWriteLineSafe($"  Files with mixed line endings: {s.ProblemFiles}", ConsoleColor.Red);

            if (s.FilesMissingFinalNewline > 0)
                HostWriteLineSafe($"  Files without final newline: {s.FilesMissingFinalNewline}", ConsoleColor.Yellow);
            else
                HostWriteLineSafe("  All files end with proper newlines", ConsoleColor.Green);

            if (s.InconsistentExtensions.Length > 0)
                HostWriteLineSafe($"  Extensions with mixed line endings: {string.Join(", ", s.InconsistentExtensions)}", ConsoleColor.Yellow);
            else
                HostWriteLineSafe("  All file extensions have consistent line endings", ConsoleColor.Green);

            if (s.Recommendations.Length > 0)
            {
                HostWriteLineSafe("");
                HostWriteLineSafe("Recommendations:", ConsoleColor.Cyan);
                foreach (var r in s.Recommendations)
                    HostWriteLineSafe($"  - {r}", ConsoleColor.Yellow);
            }

            HostWriteLineSafe("");
            HostWriteLineSafe("Line Ending Distribution:", ConsoleColor.Cyan);
            foreach (var d in s.Distribution)
            {
                var pct = s.TotalFiles > 0 ? Math.Round((double)d.Count / s.TotalFiles * 100, 1) : 0;
                HostWriteLineSafe($"  {d.LineEnding}: {d.Count} files ({pct.ToString("0.0", CultureInfo.InvariantCulture)}%)");
            }
        }

        if (!string.IsNullOrWhiteSpace(ExportPath) && File.Exists(ExportPath!))
        {
            if (Internal.IsPresent) WriteVerbose($"Detailed report exported to: {ExportPath}");
            else
            {
                HostWriteLineSafe("");
                HostWriteLineSafe($"Detailed report exported to: {ExportPath}", ConsoleColor.Green);
            }
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

