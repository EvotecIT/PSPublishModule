using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Analyzes PowerShell files and folders to determine compatibility with Windows PowerShell 5.1 and PowerShell 7+.
/// </summary>
public sealed class PowerShellCompatibilityAnalyzer
{
    private sealed class Feature
    {
        internal Feature(string pattern, string name, string description)
        {
            Pattern = pattern;
            Name = name;
            Description = description;
        }

        internal string Pattern { get; }
        internal string Name { get; }
        internal string Description { get; }
    }

    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new analyzer instance.
    /// </summary>
    public PowerShellCompatibilityAnalyzer(ILogger logger) => _logger = logger ?? new NullLogger();

    /// <summary>
    /// Analyzes the specified path and returns a typed compatibility report.
    /// </summary>
    public PowerShellCompatibilityReport Analyze(
        PowerShellCompatibilitySpec spec,
        Action<PowerShellCompatibilityProgress>? progress,
        string? exportPath)
    {
        if (spec == null) throw new ArgumentNullException(nameof(spec));

        if (!File.Exists(spec.Path) && !Directory.Exists(spec.Path))
            throw new FileNotFoundException($"Path not found: {spec.Path}", spec.Path);

        var files = GetFilesToAnalyze(spec.Path, spec.Recurse, spec.ExcludeDirectories);
        var baseDir = Directory.Exists(spec.Path)
            ? spec.Path
            : (System.IO.Path.GetDirectoryName(spec.Path) ?? Directory.GetCurrentDirectory());

        var results = new List<PowerShellCompatibilityFileResult>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            var f = files[i];
            progress?.Invoke(new PowerShellCompatibilityProgress(current: i + 1, total: files.Count, filePath: f));
            results.Add(AnalyzeFile(f, baseDir));
        }

        var summary = BuildSummary(results);

        if (!string.IsNullOrWhiteSpace(exportPath) && results.Count > 0)
        {
            try
            {
                CsvWriter.Write(
                    exportPath!,
                    headers: new[]
                    {
                        "RelativePath", "FullPath", "PowerShell51Compatible", "PowerShell7Compatible",
                        "Encoding", "IssueCount", "IssueTypes", "IssueDescriptions"
                    },
                    rows: results.Select(r => new[]
                    {
                        r.RelativePath,
                        r.FullPath,
                        r.PowerShell51Compatible.ToString(),
                        r.PowerShell7Compatible.ToString(),
                        r.Encoding?.ToString() ?? string.Empty,
                        r.Issues.Length.ToString(CultureInfo.InvariantCulture),
                        string.Join(", ", r.Issues.Select(i => i.Type.ToString())),
                        string.Join("; ", r.Issues.Select(i => i.Description))
                    }));
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to export PowerShell compatibility report to {exportPath}: {ex.Message}");
            }
        }

        return new PowerShellCompatibilityReport(
            summary: summary,
            files: results.ToArray(),
            exportPath: exportPath);
    }

    private static PowerShellCompatibilitySummary BuildSummary(IReadOnlyList<PowerShellCompatibilityFileResult> results)
    {
        var totalFiles = results.Count;
        var ps51Compatible = results.Count(r => r.PowerShell51Compatible);
        var ps7Compatible = results.Count(r => r.PowerShell7Compatible);
        var crossCompatible = results.Count(r => r.PowerShell51Compatible && r.PowerShell7Compatible);
        var filesWithIssues = results.Count(r => r.Issues.Length > 0);

        var crossCompatibilityPercentage = totalFiles == 0
            ? 0.0
            : Math.Round((crossCompatible / (double)totalFiles) * 100.0, 1);

        var status = filesWithIssues == 0
            ? CheckStatus.Pass
            : crossCompatibilityPercentage >= 90.0
                ? CheckStatus.Warning
                : CheckStatus.Fail;

        var percentageText = crossCompatibilityPercentage.ToString("0.0", CultureInfo.InvariantCulture);
        var message = status switch
        {
            CheckStatus.Pass => $"All {totalFiles} files are cross-compatible",
            CheckStatus.Warning => $"{filesWithIssues} files have compatibility issues but {percentageText}% are cross-compatible",
            _ => $"{filesWithIssues} files have compatibility issues, only {percentageText}% are cross-compatible"
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

        return new PowerShellCompatibilitySummary(
            status: status,
            analysisDate: DateTime.Now,
            totalFiles: totalFiles,
            powerShell51Compatible: ps51Compatible,
            powerShell7Compatible: ps7Compatible,
            crossCompatible: crossCompatible,
            filesWithIssues: filesWithIssues,
            crossCompatibilityPercentage: crossCompatibilityPercentage,
            message: message,
            recommendations: recommendations);
    }

    private static List<string> GetFilesToAnalyze(string inputPath, bool recurse, IReadOnlyList<string> excludeDirectories)
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

        var searchOption = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var pattern in new[] { "*.ps1", "*.psm1", "*.psd1" })
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(inputPath, pattern, searchOption); }
            catch { continue; }

            foreach (var f in files)
            {
                if (IsExcluded(f, excludeDirectories))
                    continue;
                list.Add(f);
            }
        }

        return list
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsExcluded(string filePath, IReadOnlyList<string> excludeDirectories)
    {
        var dir = System.IO.Path.GetDirectoryName(filePath) ?? string.Empty;
        foreach (var ex in excludeDirectories)
        {
            if (string.IsNullOrWhiteSpace(ex))
                continue;
            if (dir.IndexOf(ex, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static PowerShellCompatibilityFileResult AnalyzeFile(string filePath, string? baseDirectory)
    {
        var fullPath = System.IO.Path.GetFullPath(filePath.Trim().Trim('"'));
        var rel = ComputeRelativeOrFileName(baseDirectory, fullPath);

        var issues = new List<PowerShellCompatibilityIssue>();
        var ps51Compatible = true;
        var ps7Compatible = true;
        TextEncodingKind? encoding = null;

        try
        {
            if (!File.Exists(fullPath))
            {
                AddIssue(
                    issues,
                    type: PowerShellCompatibilityIssueType.Error,
                    description: $"File not found: {fullPath}",
                    recommendation: "Verify path and file existence",
                    severity: PowerShellCompatibilitySeverity.High);
                ps51Compatible = false;
                ps7Compatible = false;
            }
            else
            {
                try { encoding = ProjectTextInspection.DetectEncodingKind(fullPath); }
                catch { encoding = null; }

                AddEncodingIssues(fullPath, encoding, issues);

                var content = ReadTextBestEffort(fullPath);
                if (!string.IsNullOrEmpty(content))
                {
                    // PowerShell 7+ features (not supported in Windows PowerShell 5.1)
                    var ps7Only = new[]
                    {
                        new Feature(@"(?m)^using\s+namespace\s+", "Using Namespace", "Using namespace directive is not supported in PowerShell 5.1"),
                        new Feature(@"\?\?(?!\=)", "Null Coalescing", "Null coalescing operator (??) is PowerShell 7+ only"),
                        new Feature(@"\?\.|\?\[", "Null Conditional", "Null conditional operators (?. and ?[) are PowerShell 7+ only"),
                        new Feature(@"\?\?=", "Null Coalescing Assignment", "Null coalescing assignment operator (??=) is PowerShell 7+ only"),
                        new Feature(@"\bGet-Error\b", "Get-Error Cmdlet", "Get-Error cmdlet is PowerShell 7+ only"),
                        new Feature(@"\bConvertTo-Json\b.*-AsArray", "ConvertTo-Json -AsArray", "ConvertTo-Json -AsArray parameter is PowerShell 7+ only"),
                        new Feature(@"\bTest-Json\b", "Test-Json Cmdlet", "Test-Json cmdlet is PowerShell 6+ only"),
                        new Feature(@"\bGet-Content\b.*-AsByteStream", "Get-Content -AsByteStream", "Get-Content -AsByteStream parameter is PowerShell 7+ only (use -Encoding Byte in PS 5.1)"),
                        new Feature(@"\bInvoke-RestMethod\b.*-Resume", "Invoke-RestMethod -Resume", "Invoke-RestMethod -Resume parameter is PowerShell 7+ only"),
                    };

                    foreach (var feature in ps7Only)
                    {
                        if (RegexIsMatch(content, feature.Pattern))
                        {
                            ps51Compatible = false;
                            AddIssue(
                                issues,
                                type: PowerShellCompatibilityIssueType.PowerShell7Feature,
                                description: $"{feature.Name}: {feature.Description}",
                                recommendation: "Consider using alternative syntax for PowerShell 5.1 compatibility",
                                severity: PowerShellCompatibilitySeverity.High);
                        }
                    }

                    // PowerShell 5.1 specific / deprecated in PowerShell 7
                    var ps51Only = new[]
                    {
                        new Feature(@"\bAdd-PSSnapin\b", "Add-PSSnapin", "Add-PSSnapin is deprecated in PowerShell 7 (use modules instead)"),
                        new Feature(@"\bGet-WmiObject\b", "Get-WmiObject", "Get-WmiObject is deprecated in PowerShell 7 (use Get-CimInstance instead)"),
                        new Feature(@"\bSet-WmiInstance\b", "Set-WmiInstance", "Set-WmiInstance is deprecated in PowerShell 7 (use Set-CimInstance instead)"),
                        new Feature(@"\bRemove-WmiObject\b", "Remove-WmiObject", "Remove-WmiObject is deprecated in PowerShell 7 (use Remove-CimInstance instead)"),
                        new Feature(@"\bInvoke-WmiMethod\b", "Invoke-WmiMethod", "Invoke-WmiMethod is deprecated in PowerShell 7 (use Invoke-CimMethod instead)"),
                        new Feature(@"\bGet-Content\b.*-Encoding\\s+Byte", "Get-Content -Encoding Byte", "Get-Content -Encoding Byte is deprecated in PowerShell 7 (use -AsByteStream instead)"),
                        new Feature(@"\[\\s*System\\.Web\\.HttpUtility\\s*\\]", "System.Web.HttpUtility", "System.Web.HttpUtility requires .NET Framework (not available in PowerShell 7 by default)"),
                        new Feature(@"\$PSVersionTable\.PSEdition\s*-eq\s*['""]Desktop['""]", "Desktop Edition Check", "Desktop edition is Windows PowerShell 5.1 only"),
                    };

                    foreach (var feature in ps51Only)
                    {
                        if (RegexIsMatch(content, feature.Pattern))
                        {
                            ps7Compatible = false;
                            AddIssue(
                                issues,
                                type: PowerShellCompatibilityIssueType.PowerShell51Feature,
                                description: $"{feature.Name}: {feature.Description}",
                                recommendation: "Consider updating to PowerShell 7 compatible alternatives",
                                severity: PowerShellCompatibilitySeverity.High);
                        }
                    }

                    // Platform-specific/behavior differences
                    var platform = new[]
                    {
                        new Feature(@"\bGet-EventLog\b", "Get-EventLog", "Get-EventLog is Windows-only and not available in PowerShell 7 on other platforms"),
                        new Feature(@"\bGet-Counter\b", "Get-Counter", "Get-Counter is Windows-only"),
                        new Feature(@"\bGet-Service\b", "Get-Service", "Get-Service works differently across platforms in PowerShell 7"),
                        new Feature(@"\bGet-Process\b.*-ComputerName", "Get-Process -ComputerName", "Get-Process -ComputerName is not available in PowerShell 7"),
                        new Feature(@"\bRegister-ObjectEvent\b", "Register-ObjectEvent", "Register-ObjectEvent may not work consistently across platforms"),
                    };

                    foreach (var feature in platform)
                    {
                        if (RegexIsMatch(content, feature.Pattern))
                        {
                            AddIssue(
                                issues,
                                type: PowerShellCompatibilityIssueType.PlatformSpecific,
                                description: $"{feature.Name}: {feature.Description}",
                                recommendation: "Consider cross-platform alternatives or add platform checks",
                                severity: PowerShellCompatibilitySeverity.Medium);
                        }
                    }

                    // .NET Framework assemblies
                    foreach (var assembly in new[]
                    {
                        "System.Web",
                        "System.Web.Extensions",
                        "System.Configuration",
                        "System.ServiceProcess",
                        "System.Management.Automation.dll"
                    })
                    {
                        if (content.IndexOf(assembly, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            AddIssue(
                                issues,
                                type: PowerShellCompatibilityIssueType.DotNetFramework,
                                description: $"{assembly} assembly may not be available in PowerShell 7",
                                recommendation: "Verify assembly availability or find .NET Core/.NET 5+ alternatives",
                                severity: PowerShellCompatibilitySeverity.Medium);
                        }
                    }

                    // Class inheritance differences (low severity)
                    if (RegexIsMatch(content, @"(?m)^class\s+\w+"))
                    {
                        if (RegexIsMatch(content, @"(?m)^class\s+\w+\s*:\s*System\."))
                        {
                            AddIssue(
                                issues,
                                type: PowerShellCompatibilityIssueType.ClassInheritance,
                                description: "Class inheritance from System types may behave differently between versions",
                                recommendation: "Test class behavior across PowerShell versions",
                                severity: PowerShellCompatibilitySeverity.Low);
                        }
                    }

                    // Workflows (Windows PowerShell only)
                    if (RegexIsMatch(content, @"(?m)^workflow\s+\w+"))
                    {
                        ps7Compatible = false;
                        AddIssue(
                            issues,
                            type: PowerShellCompatibilityIssueType.Workflow,
                            description: "PowerShell workflows are not supported in PowerShell 7",
                            recommendation: "Convert workflow to functions or use Windows PowerShell 5.1",
                            severity: PowerShellCompatibilitySeverity.High);
                    }

                    // ISE-only features
                    if (content.IndexOf("$psISE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        content.IndexOf("Microsoft.PowerShell.Host.ISE", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ps7Compatible = false;
                        AddIssue(
                            issues,
                            type: PowerShellCompatibilityIssueType.ISE,
                            description: "PowerShell ISE is not available in PowerShell 7",
                            recommendation: "Use Visual Studio Code or other editors for PowerShell 7",
                            severity: PowerShellCompatibilitySeverity.Medium);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            issues.Clear();
            AddIssue(
                issues,
                type: PowerShellCompatibilityIssueType.Error,
                description: $"Error analyzing file: {ex.Message}",
                recommendation: "Check file permissions and format",
                severity: PowerShellCompatibilitySeverity.High);
            ps51Compatible = false;
            ps7Compatible = false;
        }

        return new PowerShellCompatibilityFileResult(
            fullPath: fullPath,
            relativePath: rel,
            powerShell51Compatible: ps51Compatible,
            powerShell7Compatible: ps7Compatible,
            encoding: encoding,
            issues: issues.ToArray());
    }

    private static string ComputeRelativeOrFileName(string? baseDirectory, string fullPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(baseDirectory))
                return ProjectTextInspection.ComputeRelativePath(baseDirectory!, fullPath);
        }
        catch { }

        try { return System.IO.Path.GetFileName(fullPath) ?? fullPath; }
        catch { return fullPath; }
    }

    private static string ReadTextBestEffort(string fullPath)
    {
        try
        {
            using var sr = new StreamReader(fullPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return sr.ReadToEnd();
        }
        catch
        {
            try { return File.ReadAllText(fullPath); } catch { return string.Empty; }
        }
    }

    private static bool RegexIsMatch(string content, string pattern)
    {
        try
        {
            return Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch
        {
            return false;
        }
    }

    private static void AddEncodingIssues(string fullPath, TextEncodingKind? encoding, List<PowerShellCompatibilityIssue> issues)
    {
        if (!encoding.HasValue) return;
        if (encoding.Value != TextEncodingKind.UTF8 && encoding.Value != TextEncodingKind.Ascii)
            return;

        if (!HasNonAsciiBytes(fullPath))
            return;

        if (encoding.Value == TextEncodingKind.UTF8)
        {
            AddIssue(
                issues,
                type: PowerShellCompatibilityIssueType.Encoding,
                description: "UTF8 without BOM may cause issues in PowerShell 5.1 with special characters",
                recommendation: "Consider using UTF8BOM encoding for cross-version compatibility",
                severity: PowerShellCompatibilitySeverity.Medium);
        }
        else if (encoding.Value == TextEncodingKind.Ascii)
        {
            AddIssue(
                issues,
                type: PowerShellCompatibilityIssueType.Encoding,
                description: "ASCII encoding with special characters will cause issues in PowerShell 5.1",
                recommendation: "Convert to UTF8BOM encoding to properly handle special characters",
                severity: PowerShellCompatibilitySeverity.High);
        }
    }

    private static bool HasNonAsciiBytes(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[4096];
            int read;
            while ((read = fs.Read(buf, 0, buf.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    if (buf[i] > 0x7F) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static void AddIssue(
        List<PowerShellCompatibilityIssue> issues,
        PowerShellCompatibilityIssueType type,
        string description,
        string recommendation,
        PowerShellCompatibilitySeverity severity)
    {
        // Avoid duplicates when multiple patterns overlap.
        if (issues.Any(i =>
                i.Type == type &&
                string.Equals(i.Description, description, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        issues.Add(new PowerShellCompatibilityIssue(
            type: type,
            description: description,
            recommendation: recommendation,
            severity: severity));
    }
}

