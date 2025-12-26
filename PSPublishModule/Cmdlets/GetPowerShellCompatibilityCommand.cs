using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;

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
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
            throw new FileNotFoundException($"Path not found: {inputPath}", inputPath);

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

        var files = GetFilesToAnalyze(inputPath);
        if (files.Count == 0)
        {
            if (!Internal.IsPresent) WriteWarning("No PowerShell files found in the specified path.");
            else WriteVerbose("No PowerShell files found in the specified path.");
            return;
        }

        if (!Internal.IsPresent)
            HostWriteLineSafe($"[i] Found {files.Count} PowerShell files to analyze", ConsoleColor.Yellow);
        else
            WriteVerbose($"Found {files.Count} PowerShell files to analyze");

        var baseDir = Directory.Exists(inputPath)
            ? inputPath
            : (System.IO.Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory());

        var results = new List<object>(capacity: files.Count);

        for (var i = 0; i < files.Count; i++)
        {
            var f = files[i];
            if (!Internal.IsPresent)
            {
                var percent = (int)Math.Round(((i + 1) / (double)files.Count) * 100.0, 0);
                WriteProgress(new ProgressRecord(1, "Analyzing PowerShell Compatibility", $"Processing {System.IO.Path.GetFileName(f)}")
                {
                    PercentComplete = percent
                });
            }

            results.Add(PowerShellCompatibilityAnalyzer.AnalyzeFile(f, baseDir));
        }

        if (!Internal.IsPresent)
            WriteProgress(new ProgressRecord(1, "Analyzing PowerShell Compatibility", "Completed") { RecordType = ProgressRecordType.Completed });

        var totalFiles = results.Count;
        var ps51Compatible = results.Count(r => GetBool(r, "PowerShell51Compatible"));
        var ps7Compatible = results.Count(r => GetBool(r, "PowerShell7Compatible"));
        var crossCompatible = results.Count(r => GetBool(r, "PowerShell51Compatible") && GetBool(r, "PowerShell7Compatible"));
        var filesWithIssues = results.Count(r => GetIssuesCount(r) > 0);

        var crossCompatibilityPercentage = totalFiles == 0 ? 0.0 : Math.Round((crossCompatible / (double)totalFiles) * 100.0, 1);
        var status = filesWithIssues == 0 ? "Pass" : crossCompatibilityPercentage >= 90.0 ? "Warning" : "Fail";

        var message = status switch
        {
            "Pass" => $"All {totalFiles} files are cross-compatible",
            "Warning" => $"{filesWithIssues} files have compatibility issues but {crossCompatibilityPercentage.ToString("0.0", CultureInfo.InvariantCulture)}% are cross-compatible",
            _ => $"{filesWithIssues} files have compatibility issues, only {crossCompatibilityPercentage.ToString("0.0", CultureInfo.InvariantCulture)}% are cross-compatible"
        };

        var recommendations = filesWithIssues > 0
            ? new[]
            {
                "Review files with compatibility issues",
                "Consider using UTF8BOM encoding for Windows PowerShell 5.1 support",
                "Replace deprecated cmdlets with modern alternatives",
                "Test code in both Windows PowerShell 5.1 and PowerShell 7 environments"
            }
            : Array.Empty<string>();

        var summary = NewPsCustomObject();
        summary.Properties.Add(new PSNoteProperty("Status", status));
        summary.Properties.Add(new PSNoteProperty("AnalysisDate", DateTime.Now));
        summary.Properties.Add(new PSNoteProperty("TotalFiles", totalFiles));
        summary.Properties.Add(new PSNoteProperty("PowerShell51Compatible", ps51Compatible));
        summary.Properties.Add(new PSNoteProperty("PowerShell7Compatible", ps7Compatible));
        summary.Properties.Add(new PSNoteProperty("CrossCompatible", crossCompatible));
        summary.Properties.Add(new PSNoteProperty("FilesWithIssues", filesWithIssues));
        summary.Properties.Add(new PSNoteProperty("CrossCompatibilityPercentage", crossCompatibilityPercentage));
        summary.Properties.Add(new PSNoteProperty("Message", message));
        summary.Properties.Add(new PSNoteProperty("Recommendations", recommendations));

        WriteSummaryToHost(summary);
        WriteDetailsToHost(results);
        ExportCsvIfRequested(results);

        var output = NewPsCustomObject();
        output.Properties.Add(new PSNoteProperty("Summary", summary));
        output.Properties.Add(new PSNoteProperty("Files", results.ToArray()));
        WriteObject(output, enumerateCollection: false);
    }

    private List<string> GetFilesToAnalyze(string inputPath)
    {
        var list = new List<string>();
        if (File.Exists(inputPath))
        {
            var ext = System.IO.Path.GetExtension(inputPath) ?? string.Empty;
            if (!string.Equals(ext, ".ps1", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ext, ".psm1", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ext, ".psd1", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("File must be a PowerShell file (.ps1, .psm1, or .psd1)");
            }

            list.Add(inputPath);
            return list;
        }

        if (!Directory.Exists(inputPath))
            return list;

        var searchOption = Recurse.IsPresent ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var pattern in new[] { "*.ps1", "*.psm1", "*.psd1" })
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(inputPath, pattern, searchOption); }
            catch { continue; }

            foreach (var f in files)
            {
                if (IsExcluded(f))
                    continue;
                list.Add(f);
            }
        }

        return list;
    }

    private bool IsExcluded(string filePath)
    {
        var dir = System.IO.Path.GetDirectoryName(filePath) ?? string.Empty;
        foreach (var ex in ExcludeDirectories)
        {
            if (string.IsNullOrWhiteSpace(ex))
                continue;
            if (dir.IndexOf(ex, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private void WriteSummaryToHost(PSObject summary)
    {
        if (Internal.IsPresent)
        {
            WriteVerbose($"PowerShell Compatibility: {summary.Properties["Status"]?.Value} - {summary.Properties["Message"]?.Value}");
            var rec = summary.Properties["Recommendations"]?.Value as IEnumerable;
            if (rec != null)
            {
                var joined = string.Join("; ", rec.Cast<object>().Select(o => o?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(joined))
                    WriteVerbose($"Recommendations: {joined}");
            }
            return;
        }

        HostWriteLineSafe(string.Empty);
        var status = summary.Properties["Status"]?.Value?.ToString() ?? string.Empty;
        var message = summary.Properties["Message"]?.Value?.ToString() ?? string.Empty;

        var color = status switch
        {
            "Pass" => ConsoleColor.Green,
            "Warning" => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };

        HostWriteLineSafe($"[i] Status: {status}", color);
        HostWriteLineSafe($"[i] {message}", ConsoleColor.White);
        HostWriteLineSafe($"[i] PS 5.1 compatible: {summary.Properties["PowerShell51Compatible"]?.Value}/{summary.Properties["TotalFiles"]?.Value}", ConsoleColor.White);
        HostWriteLineSafe($"[i] PS 7 compatible:   {summary.Properties["PowerShell7Compatible"]?.Value}/{summary.Properties["TotalFiles"]?.Value}", ConsoleColor.White);
        HostWriteLineSafe($"[i] Cross-compatible: {summary.Properties["CrossCompatible"]?.Value}/{summary.Properties["TotalFiles"]?.Value} ({summary.Properties["CrossCompatibilityPercentage"]?.Value}%)", ConsoleColor.White);

        var recs = summary.Properties["Recommendations"]?.Value as IEnumerable;
        if (recs != null)
        {
            var list = recs.Cast<object>().Select(o => o?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (list.Length > 0)
            {
                HostWriteLineSafe(string.Empty);
                HostWriteLineSafe("Recommendations:", ConsoleColor.Cyan);
                foreach (var r in list) HostWriteLineSafe($"- {r}", ConsoleColor.White);
            }
        }
    }

    private void WriteDetailsToHost(List<object> results)
    {
        if (!ShowDetails.IsPresent)
            return;

        if (results.Count == 0)
            return;

        if (Internal.IsPresent)
        {
            foreach (var r in results)
            {
                if (GetIssuesCount(r) > 0)
                    WriteVerbose($"Issues in {GetString(r, "RelativePath")}: {GetIssueTypesJoined(r)}");
            }
            return;
        }

        HostWriteLineSafe(string.Empty);
        HostWriteLineSafe("Detailed Analysis:", ConsoleColor.Cyan);

        foreach (var r in results)
        {
            HostWriteLineSafe(string.Empty);
            HostWriteLineSafe($"{GetString(r, "RelativePath")}", ConsoleColor.White);

            var ps51 = GetBool(r, "PowerShell51Compatible");
            var ps7 = GetBool(r, "PowerShell7Compatible");
            HostWriteLineSafe($"  PS 5.1: {(ps51 ? "Compatible" : "Not compatible")}", ps51 ? ConsoleColor.Green : ConsoleColor.Red);
            HostWriteLineSafe($"  PS 7:   {(ps7 ? "Compatible" : "Not compatible")}", ps7 ? ConsoleColor.Green : ConsoleColor.Red);

            var issues = GetIssues(r);
            if (issues.Count == 0)
                continue;

            HostWriteLineSafe("  Issues:", ConsoleColor.Yellow);
            foreach (var issue in issues)
            {
                var issueType = GetString(issue, "Type");
                var desc = GetString(issue, "Description");
                var rec = GetString(issue, "Recommendation");

                HostWriteLineSafe($"    - {issueType}: {desc}", ConsoleColor.Red);
                if (!string.IsNullOrWhiteSpace(rec))
                    HostWriteLineSafe($"      - {rec}", ConsoleColor.Cyan);
            }
        }
    }

    private void ExportCsvIfRequested(List<object> results)
    {
        if (string.IsNullOrWhiteSpace(ExportPath))
            return;

        var path = System.IO.Path.GetFullPath(ExportPath!.Trim().Trim('"'));
        var script = @"
param($results, $exportPath)
$exportData = $results | Select-Object RelativePath, FullPath, PowerShell51Compatible, PowerShell7Compatible, Encoding, @{
    Name = 'IssueCount'; Expression = { $_.Issues.Count }
}, @{
    Name = 'IssueTypes'; Expression = { ($_.Issues.Type -join ', ') }
}, @{
    Name = 'IssueDescriptions'; Expression = { ($_.Issues.Description -join '; ') }
}
$exportData | Export-Csv -Path $exportPath -NoTypeInformation -Encoding UTF8
";
        InvokeCommand.InvokeScript(script, new object[] { results.ToArray(), path });

        if (Internal.IsPresent)
            WriteVerbose($"Detailed report exported to: {path}");
        else
            HostWriteLineSafe($"[i] Detailed report exported to: {path}", ConsoleColor.Green);
    }

    private static bool GetBool(object obj, string name)
    {
        try
        {
            var ps = PSObject.AsPSObject(obj);
            var v = ps.Properties[name]?.Value;
            return v is bool b && b;
        }
        catch
        {
            return false;
        }
    }

    private static int GetIssuesCount(object obj)
    {
        try
        {
            var ps = PSObject.AsPSObject(obj);
            var v = ps.Properties["Issues"]?.Value;
            if (v is ICollection c) return c.Count;
            if (v is IEnumerable e) return e.Cast<object>().Count();
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static List<object> GetIssues(object obj)
    {
        var list = new List<object>();
        try
        {
            var ps = PSObject.AsPSObject(obj);
            var v = ps.Properties["Issues"]?.Value;
            if (v is null) return list;
            if (v is string) return list;
            if (v is IEnumerable e)
            {
                foreach (var it in e) if (it != null) list.Add(it);
                return list;
            }
            list.Add(v);
            return list;
        }
        catch
        {
            return list;
        }
    }

    private static string GetIssueTypesJoined(object obj)
    {
        try
        {
            var issues = GetIssues(obj);
            var types = issues.Select(i => GetString(i, "Type")).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", types);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetString(object obj, string name)
    {
        try
        {
            var ps = PSObject.AsPSObject(obj);
            return ps.Properties[name]?.Value?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static PSObject NewPsCustomObject()
    {
        var t = typeof(PSObject).Assembly.GetType("System.Management.Automation.PSCustomObject");
        if (t is null)
            return new PSObject();

        var inst = Activator.CreateInstance(t, nonPublic: true);
        return inst is PSObject pso ? pso : PSObject.AsPSObject(inst);
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

