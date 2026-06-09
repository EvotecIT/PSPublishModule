using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Analyzes PowerShell files and folders to determine compatibility with Windows PowerShell 5.1 and PowerShell 7+.
/// </summary>
/// <remarks>
/// <para>
/// Scans PowerShell files to detect patterns and constructs that can cause cross-version issues
/// (Windows PowerShell 5.1 vs PowerShell 7+), and outputs a compatibility report.
/// </para>
/// <para>
/// Use this as part of CI to keep modules compatible across editions, and pair it with encoding/line-ending checks
/// when supporting Windows PowerShell 5.1.
/// </para>
/// </remarks>
/// <example>
/// <summary>Analyze a module folder</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-PowerShellCompatibility -Path 'C:\MyModule'</code>
/// <para>Analyzes PowerShell files in the folder and returns a compatibility report.</para>
/// </example>
/// <example>
/// <summary>Recursively analyze and include detailed findings</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-PowerShellCompatibility -Path 'C:\MyModule' -Recurse -ShowDetails</code>
/// <para>Useful when investigating why a module behaves differently in PS 5.1 vs PS 7+.</para>
/// </example>
/// <example>
/// <summary>Export compatibility findings to CSV</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-PowerShellCompatibility -Path 'C:\MyModule' -ExportPath 'C:\Reports\compatibility.csv'</code>
/// <para>Creates a report that can be attached to CI artifacts.</para>
/// </example>
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
        var display = new PowerShellCompatibilityDisplayService();

        if (!Internal.IsPresent)
        {
            var psVersionTable = SessionState?.PSVariable?.GetValue("PSVersionTable") as Hashtable;
            var psVersion = psVersionTable?["PSVersion"]?.ToString() ?? string.Empty;
            var psEdition = psVersionTable?["PSEdition"]?.ToString() ?? string.Empty;
            WriteDisplayLines(display.CreateHeader(inputPath, psEdition, psVersion));
        }
        else
        {
            WriteVerbose($"Analyzing PowerShell compatibility for: {inputPath}");
        }

        var logger = new CmdletLogger(
            this,
            isVerbose: MyInvocation.BoundParameters.ContainsKey("Verbose"),
            warningsAsVerbose: Internal.IsPresent);

        var workflow = new PowerShellCompatibilityWorkflowService(new PowerShellCompatibilityAnalyzer(logger));
        var result = workflow.Execute(
            new PowerShellCompatibilityWorkflowRequest
            {
                InputPath = inputPath,
                ExportPath = exportPath,
                Recurse = Recurse.IsPresent,
                ExcludeDirectories = ExcludeDirectories,
                Internal = Internal.IsPresent
            },
            progress: !Internal.IsPresent
                ? p =>
                {
                    WriteProgress(new ProgressRecord(1, "Analyzing PowerShell Compatibility", $"Processing {System.IO.Path.GetFileName(p.FilePath)}")
                    {
                        PercentComplete = PowerShellCompatibilityDisplayService.CalculatePercent(p)
                    });
                }
                : null);
        var report = result.Report;

        if (!Internal.IsPresent && report.Files.Length > 0)
            WriteProgress(new ProgressRecord(1, "Analyzing PowerShell Compatibility", "Completed") { RecordType = ProgressRecordType.Completed });

        if (report.Files.Length == 0)
        {
            if (!Internal.IsPresent) WriteWarning("No PowerShell files found in the specified path.");
            else WriteVerbose("No PowerShell files found in the specified path.");
            return;
        }

        if (!Internal.IsPresent)
            HostWriteLineSafe($"📄 Found {report.Files.Length} PowerShell files to analyze", ConsoleColor.Yellow);
        else
            foreach (var message in display.CreateInternalSummaryMessages(report, ShowDetails.IsPresent, exportPath))
                WriteVerbose(message);

        if (!Internal.IsPresent)
        {
            WriteDisplayLines(display.CreateSummary(report.Summary));
            if (ShowDetails.IsPresent)
                WriteDisplayLines(display.CreateDetails(report.Files));
            if (!string.IsNullOrWhiteSpace(exportPath))
            {
                var exportStatus = display.CreateExportStatus(exportPath!, File.Exists(exportPath));
                HostWriteLineSafe(exportStatus.Text, exportStatus.Color);
            }
        }

        WriteObject(report, enumerateCollection: false);
    }

    private void WriteDisplayLines(IReadOnlyList<PowerShellCompatibilityDisplayLine> lines)
    {
        foreach (var line in lines)
            HostWriteLineSafe(line.Text, line.Color);
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
