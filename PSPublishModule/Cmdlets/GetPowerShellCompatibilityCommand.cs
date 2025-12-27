using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Analyzes PowerShell files and folders to determine compatibility with Windows PowerShell 5.1 and PowerShell 7+.
/// </summary>
[Cmdlet(VerbsCommon.Get, "PowerShellCompatibility")]
public sealed class GetPowerShellCompatibilityCommand : PSCmdlet
{
    /// <summary>Path to the file or directory to analyze for PowerShell compatibility.</summary>
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>When analyzing a directory, recursively analyze all subdirectories.</summary>
    [Parameter]
    public SwitchParameter Recurse { get; set; }

    /// <summary>Directory names to exclude from analysis.</summary>
    [Parameter]
    public string[] ExcludeDirectories { get; set; } = new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode", "Artefacts", "Ignore" };

    /// <summary>Include detailed analysis of each file with specific compatibility issues found.</summary>
    [Parameter]
    public SwitchParameter ShowDetails { get; set; }

    /// <summary>Export the detailed report to a CSV file at the specified path.</summary>
    [Parameter]
    public string? ExportPath { get; set; }

    /// <summary>Internal mode used by build pipelines to suppress host output.</summary>
    [Parameter(DontShow = true)]
    public SwitchParameter Internal { get; set; }

    /// <summary>Runs the compatibility analysis.</summary>
    protected override void ProcessRecord()
    {
        var inputPath = System.IO.Path.GetFullPath(Path.Trim().Trim('"'));
        var exportPath = string.IsNullOrWhiteSpace(ExportPath)
            ? null
            : System.IO.Path.GetFullPath(ExportPath!.Trim().Trim('"'));

        if (!Internal.IsPresent)
        {
            HostWriteLineSafe("[i] Analyzing PowerShell compatibility...", ConsoleColor.Cyan);
            HostWriteLineSafe($"[i] Path: {inputPath}", ConsoleColor.White);

            var psVersionTable = SessionState?.PSVariable?.GetValue("PSVersionTable") as Hashtable;
            var psVersion = psVersionTable?["PSVersion"]?.ToString() ?? string.Empty;
            var psEdition = psVersionTable?["PSEdition"]?.ToString() ?? string.Empty;
            HostWriteLineSafe($"[i] Current PowerShell: {psEdition} {psVersion}", ConsoleColor.White);
        }
        else
        {
            WriteVerbose($"Analyzing PowerShell compatibility for: {inputPath}");
        }

        var logger = new CmdletLogger(
            this,
            isVerbose: MyInvocation.BoundParameters.ContainsKey("Verbose"),
            warningsAsVerbose: Internal.IsPresent);

        var analyzer = new PowerShellCompatibilityAnalyzer(logger);
        var spec = new PowerShellCompatibilitySpec(inputPath, Recurse.IsPresent, ExcludeDirectories);

        var report = analyzer.Analyze(
            spec,
            progress: !Internal.IsPresent
                ? p =>
                {
                    var percent = p.Total == 0
                        ? 0
                        : (int)Math.Round((p.Current / (double)p.Total) * 100.0, 0);

                    WriteProgress(new ProgressRecord(1, "Analyzing PowerShell Compatibility", $"Processing {System.IO.Path.GetFileName(p.FilePath)}")
                    {
                        PercentComplete = percent
                    });
                }
                : null,
            exportPath: exportPath);

        if (!Internal.IsPresent && report.Files.Length > 0)
            WriteProgress(new ProgressRecord(1, "Analyzing PowerShell Compatibility", "Completed") { RecordType = ProgressRecordType.Completed });

        if (report.Files.Length == 0)
        {
            if (!Internal.IsPresent) WriteWarning("No PowerShell files found in the specified path.");
            else WriteVerbose("No PowerShell files found in the specified path.");
            return;
        }

        if (!Internal.IsPresent)
            HostWriteLineSafe($"[i] Found {report.Files.Length} PowerShell files to analyze", ConsoleColor.Yellow);
        else
            WriteVerbose($"Found {report.Files.Length} PowerShell files to analyze");

        WriteSummaryToHost(report.Summary);
        WriteDetailsToHost(report.Files);
        WriteExportToHostIfRequested(exportPath);

        WriteObject(report, enumerateCollection: false);
    }

    private void WriteSummaryToHost(PowerShellCompatibilitySummary summary)
    {
        if (Internal.IsPresent)
        {
            WriteVerbose($"PowerShell Compatibility: {summary.Status} - {summary.Message}");
            if (summary.Recommendations.Length > 0)
            {
                var joined = string.Join("; ", summary.Recommendations.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(joined))
                    WriteVerbose($"Recommendations: {joined}");
            }
            return;
        }

        HostWriteLineSafe(string.Empty);

        var color = summary.Status switch
        {
            CheckStatus.Pass => ConsoleColor.Green,
            CheckStatus.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };

        HostWriteLineSafe($"[i] Status: {summary.Status}", color);
        HostWriteLineSafe($"[i] {summary.Message}", ConsoleColor.White);
        HostWriteLineSafe($"[i] PS 5.1 compatible: {summary.PowerShell51Compatible}/{summary.TotalFiles}", ConsoleColor.White);
        HostWriteLineSafe($"[i] PS 7 compatible:   {summary.PowerShell7Compatible}/{summary.TotalFiles}", ConsoleColor.White);
        HostWriteLineSafe($"[i] Cross-compatible: {summary.CrossCompatible}/{summary.TotalFiles} ({summary.CrossCompatibilityPercentage.ToString("0.0", CultureInfo.InvariantCulture)}%)", ConsoleColor.White);

        if (summary.Recommendations.Length > 0)
        {
            HostWriteLineSafe(string.Empty);
            HostWriteLineSafe("Recommendations:", ConsoleColor.Cyan);
            foreach (var r in summary.Recommendations.Where(s => !string.IsNullOrWhiteSpace(s)))
                HostWriteLineSafe($"- {r}", ConsoleColor.White);
        }
    }

    private void WriteDetailsToHost(PowerShellCompatibilityFileResult[] results)
    {
        if (!ShowDetails.IsPresent)
            return;

        if (results.Length == 0)
            return;

        if (Internal.IsPresent)
        {
            foreach (var r in results)
            {
                if (r.Issues.Length > 0)
                {
                    var issueTypes = string.Join(
                        ", ",
                        r.Issues.Select(i => i.Type.ToString())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Distinct(StringComparer.OrdinalIgnoreCase));

                    WriteVerbose($"Issues in {r.RelativePath}: {issueTypes}");
                }
            }
            return;
        }

        HostWriteLineSafe(string.Empty);
        HostWriteLineSafe("Detailed Analysis:", ConsoleColor.Cyan);

        foreach (var r in results)
        {
            HostWriteLineSafe(string.Empty);
            HostWriteLineSafe($"{r.RelativePath}", ConsoleColor.White);

            HostWriteLineSafe($"  PS 5.1: {(r.PowerShell51Compatible ? "Compatible" : "Not compatible")}", r.PowerShell51Compatible ? ConsoleColor.Green : ConsoleColor.Red);
            HostWriteLineSafe($"  PS 7:   {(r.PowerShell7Compatible ? "Compatible" : "Not compatible")}", r.PowerShell7Compatible ? ConsoleColor.Green : ConsoleColor.Red);

            if (r.Issues.Length == 0)
                continue;

            HostWriteLineSafe("  Issues:", ConsoleColor.Yellow);
            foreach (var issue in r.Issues)
            {
                HostWriteLineSafe($"    - {issue.Type}: {issue.Description}", ConsoleColor.Red);
                if (!string.IsNullOrWhiteSpace(issue.Recommendation))
                    HostWriteLineSafe($"      - {issue.Recommendation}", ConsoleColor.Cyan);
            }
        }
    }

    private void WriteExportToHostIfRequested(string? exportPath)
    {
        if (string.IsNullOrWhiteSpace(exportPath))
            return;

        if (!File.Exists(exportPath))
        {
            if (Internal.IsPresent)
                WriteVerbose($"Failed to export detailed report to: {exportPath}");
            else
                HostWriteLineSafe($"[e] Failed to export detailed report to: {exportPath}", ConsoleColor.Red);
            return;
        }

        if (Internal.IsPresent)
            WriteVerbose($"Detailed report exported to: {exportPath}");
        else
            HostWriteLineSafe($"[i] Detailed report exported to: {exportPath}", ConsoleColor.Green);
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
