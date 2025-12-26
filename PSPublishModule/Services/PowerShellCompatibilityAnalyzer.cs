using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;

namespace PSPublishModule;

internal static class PowerShellCompatibilityAnalyzer
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

    internal static PSObject AnalyzeFile(string filePath, string? baseDirectory)
    {
        var fullPath = System.IO.Path.GetFullPath(filePath.Trim().Trim('"'));
        var rel = ComputeRelativeOrFileName(baseDirectory, fullPath);

        var issues = new List<PSObject>();
        var ps51Compatible = true;
        var ps7Compatible = true;

        try
        {
            if (!File.Exists(fullPath))
            {
                AddIssue(
                    issues,
                    type: "Error",
                    description: $"File not found: {fullPath}",
                    recommendation: "Verify path and file existence",
                    severity: "High");
                ps51Compatible = false;
                ps7Compatible = false;
            }
            else
            {
                var encodingName = ProjectFileAnalysis.DetectEncodingName(fullPath);
                AddEncodingIssues(fullPath, encodingName, issues);

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
                                type: "PowerShell7Feature",
                                description: $"{feature.Name}: {feature.Description}",
                                recommendation: "Consider using alternative syntax for PowerShell 5.1 compatibility",
                                severity: "High");
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
                                type: "PowerShell51Feature",
                                description: $"{feature.Name}: {feature.Description}",
                                recommendation: "Consider updating to PowerShell 7 compatible alternatives",
                                severity: "High");
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
                                type: "PlatformSpecific",
                                description: $"{feature.Name}: {feature.Description}",
                                recommendation: "Consider cross-platform alternatives or add platform checks",
                                severity: "Medium");
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
                                type: "DotNetFramework",
                                description: $"{assembly} assembly may not be available in PowerShell 7",
                                recommendation: "Verify assembly availability or find .NET Core/.NET 5+ alternatives",
                                severity: "Medium");
                        }
                    }

                    // Class inheritance differences (low severity)
                    if (RegexIsMatch(content, @"(?m)^class\s+\w+"))
                    {
                        if (RegexIsMatch(content, @"(?m)^class\s+\w+\s*:\s*System\."))
                        {
                            AddIssue(
                                issues,
                                type: "ClassInheritance",
                                description: "Class inheritance from System types may behave differently between versions",
                                recommendation: "Test class behavior across PowerShell versions",
                                severity: "Low");
                        }
                    }

                    // Workflows (Windows PowerShell only)
                    if (RegexIsMatch(content, @"(?m)^workflow\s+\w+"))
                    {
                        ps7Compatible = false;
                        AddIssue(
                            issues,
                            type: "Workflow",
                            description: "PowerShell workflows are not supported in PowerShell 7",
                            recommendation: "Convert workflow to functions or use Windows PowerShell 5.1",
                            severity: "High");
                    }

                    // ISE-only features
                    if (content.IndexOf("$psISE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        content.IndexOf("Microsoft.PowerShell.Host.ISE", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ps7Compatible = false;
                        AddIssue(
                            issues,
                            type: "ISE",
                            description: "PowerShell ISE is not available in PowerShell 7",
                            recommendation: "Use Visual Studio Code or other editors for PowerShell 7",
                            severity: "Medium");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            issues.Clear();
            AddIssue(
                issues,
                type: "Error",
                description: $"Error analyzing file: {ex.Message}",
                recommendation: "Check file permissions and format",
                severity: "High");
            ps51Compatible = false;
            ps7Compatible = false;
        }

        var output = NewPsCustomObject();
        output.Properties.Add(new PSNoteProperty("FullPath", fullPath));
        output.Properties.Add(new PSNoteProperty("RelativePath", rel));
        output.Properties.Add(new PSNoteProperty("PowerShell51Compatible", ps51Compatible));
        output.Properties.Add(new PSNoteProperty("PowerShell7Compatible", ps7Compatible));
        output.Properties.Add(new PSNoteProperty("Encoding", SafeDetectEncodingName(fullPath)));
        output.Properties.Add(new PSNoteProperty("Issues", issues.ToArray()));
        return output;
    }

    private static string SafeDetectEncodingName(string path)
    {
        try
        {
            if (File.Exists(path))
                return ProjectFileAnalysis.DetectEncodingName(path);
        }
        catch { }
        return string.Empty;
    }

    private static string ComputeRelativeOrFileName(string? baseDirectory, string fullPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(baseDirectory))
                return ProjectFileAnalysis.ComputeRelativePath(baseDirectory!, fullPath);
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

    private static void AddEncodingIssues(string fullPath, string encodingName, List<PSObject> issues)
    {
        if (!string.Equals(encodingName, "UTF8", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(encodingName, "ASCII", StringComparison.OrdinalIgnoreCase))
            return;

        if (!HasNonAsciiBytes(fullPath))
            return;

        if (string.Equals(encodingName, "UTF8", StringComparison.OrdinalIgnoreCase))
        {
            AddIssue(
                issues,
                type: "Encoding",
                description: "UTF8 without BOM may cause issues in PowerShell 5.1 with special characters",
                recommendation: "Consider using UTF8BOM encoding for cross-version compatibility",
                severity: "Medium");
        }
        else if (string.Equals(encodingName, "ASCII", StringComparison.OrdinalIgnoreCase))
        {
            AddIssue(
                issues,
                type: "Encoding",
                description: "ASCII encoding with special characters will cause issues in PowerShell 5.1",
                recommendation: "Convert to UTF8BOM encoding to properly handle special characters",
                severity: "High");
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

    private static void AddIssue(List<PSObject> issues, string type, string description, string recommendation, string severity)
    {
        // Avoid duplicates when multiple patterns overlap.
        if (issues.Any(i =>
                string.Equals(i.Properties["Type"]?.Value?.ToString(), type, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(i.Properties["Description"]?.Value?.ToString(), description, StringComparison.OrdinalIgnoreCase)))
            return;

        var issue = NewPsCustomObject();
        issue.Properties.Add(new PSNoteProperty("Type", type));
        issue.Properties.Add(new PSNoteProperty("Description", description));
        issue.Properties.Add(new PSNoteProperty("Recommendation", recommendation));
        issue.Properties.Add(new PSNoteProperty("Severity", severity));
        issues.Add(issue);
    }

    private static PSObject NewPsCustomObject()
    {
        var t = typeof(PSObject).Assembly.GetType("System.Management.Automation.PSCustomObject");
        if (t is null)
            return new PSObject();

        var inst = Activator.CreateInstance(t, nonPublic: true);
        return inst is PSObject pso ? pso : PSObject.AsPSObject(inst);
    }
}
