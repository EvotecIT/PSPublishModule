using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Xml;

namespace PSPublishModule;

/// <summary>
/// Output format for <c>Get-ModuleTestFailures</c>.
/// </summary>
public enum ModuleTestFailureOutputFormat
{
    /// <summary>Write a concise summary to the host.</summary>
    Summary,
    /// <summary>Write detailed failures (including messages) to the host.</summary>
    Detailed,
    /// <summary>Write the analysis object as JSON to the pipeline.</summary>
    Json
}

/// <summary>
/// Analyzes and summarizes failed Pester tests from either a Pester results object or an NUnit XML result file.
/// </summary>
[Cmdlet(VerbsCommon.Get, "ModuleTestFailures", DefaultParameterSetName = ParameterSetPath)]
public sealed class GetModuleTestFailuresCommand : PSCmdlet
{
    private const string ParameterSetPath = "Path";
    private const string ParameterSetTestResults = "TestResults";

    /// <summary>
    /// Path to the NUnit XML test results file. If not specified, searches for <c>TestResults.xml</c> under ProjectPath.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetPath)]
    public string? Path { get; set; }

    /// <summary>Pester test results object from <c>Invoke-Pester</c> or <c>Invoke-ModuleTestSuite</c>.</summary>
    [Parameter(ParameterSetName = ParameterSetTestResults, Mandatory = true)]
    public object? TestResults { get; set; }

    /// <summary>
    /// Path to the project directory used to locate test results when Path is not specified.
    /// </summary>
    [Parameter]
    public string? ProjectPath { get; set; }

    /// <summary>Format for displaying test failures.</summary>
    [Parameter]
    public ModuleTestFailureOutputFormat OutputFormat { get; set; } = ModuleTestFailureOutputFormat.Detailed;

    /// <summary>Include successful tests in the output (only applies to Summary format).</summary>
    [Parameter]
    public SwitchParameter ShowSuccessful { get; set; }

    /// <summary>Return the failure analysis object for further processing.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Executes test failure analysis.</summary>
    protected override void ProcessRecord()
    {
        try
        {
            var analysis = new ModuleTestFailureAnalysis
            {
                Source = string.Empty,
                TotalCount = 0,
                PassedCount = 0,
                FailedCount = 0,
                SkippedCount = 0,
                FailedTests = new List<ModuleTestFailureInfo>(),
                Timestamp = DateTime.Now
            };

            if (ParameterSetName == ParameterSetTestResults)
            {
                analysis.Source = "PesterResults";
                PopulateFromPesterResults(TestResults, analysis);
            }
            else
            {
                var projectPath = ResolveProjectPath();
                var resultsPath = ResolveResultsPath(Path, projectPath);
                if (resultsPath is null)
                    return;

                if (!File.Exists(resultsPath))
                {
                    WriteError(new ErrorRecord(
                        new FileNotFoundException($"Test results file not found: {resultsPath}", resultsPath),
                        "TestResultsFileNotFound",
                        ErrorCategory.ObjectNotFound,
                        resultsPath));
                    return;
                }

                analysis.Source = resultsPath;
                PopulateFromXmlFile(resultsPath, analysis);
            }

            var psAnalysis = ToPsCustomObject(analysis);

            switch (OutputFormat)
            {
                case ModuleTestFailureOutputFormat.Json:
                    WriteObject(ConvertToJson(psAnalysis), enumerateCollection: false);
                    break;
                case ModuleTestFailureOutputFormat.Summary:
                    WriteSummary(analysis, ShowSuccessful.IsPresent);
                    break;
                case ModuleTestFailureOutputFormat.Detailed:
                    WriteDetails(analysis);
                    break;
            }

            if (PassThru.IsPresent)
            {
                WriteObject(psAnalysis, enumerateCollection: false);
            }
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "GetModuleTestFailuresFailed", ErrorCategory.NotSpecified, null));
            throw;
        }
    }

    private string ResolveProjectPath()
    {
        if (!string.IsNullOrWhiteSpace(ProjectPath))
            return System.IO.Path.GetFullPath(ProjectPath.Trim().Trim('"'));

        try
        {
            var moduleBase = MyInvocation?.MyCommand?.Module?.ModuleBase;
            if (!string.IsNullOrWhiteSpace(moduleBase))
                return moduleBase;
        }
        catch
        {
            // ignore
        }

        return SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Directory.GetCurrentDirectory();
    }

    private void PopulateFromPesterResults(object? testResults, ModuleTestFailureAnalysis analysis)
    {
        var ps = PSObject.AsPSObject(testResults ?? new Hashtable());

        analysis.TotalCount = GetInt(ps, "TotalCount") ?? CountTests(ps);
        analysis.PassedCount = GetInt(ps, "PassedCount") ?? CountTestsByResult(ps, "Passed");
        analysis.FailedCount = GetInt(ps, "FailedCount") ?? CountTestsByResult(ps, "Failed");
        analysis.SkippedCount = GetInt(ps, "SkippedCount") ?? CountTestsByResult(ps, "Skipped");

        var failed = GetTests(ps, "Tests")
            .Where(t => string.Equals(GetString(t, "Result"), "Failed", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (failed.Length == 0)
        {
            failed = GetTests(ps, "TestResult")
                .Where(t => string.Equals(GetString(t, "Result"), "Failed", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        foreach (var t in failed)
        {
            analysis.FailedTests.Add(ExtractFailureInfo(t));
        }
    }

    private static int CountTests(PSObject ps)
    {
        return GetTests(ps, "Tests").Count();
    }

    private static int CountTestsByResult(PSObject ps, string result)
    {
        return GetTests(ps, "Tests").Count(t => string.Equals(GetString(t, "Result"), result, StringComparison.OrdinalIgnoreCase));
    }

    private static ModuleTestFailureInfo ExtractFailureInfo(PSObject test)
    {
        var name = GetString(test, "Name");
        if (string.IsNullOrWhiteSpace(name))
        {
            var describe = GetString(test, "Describe");
            var context = GetString(test, "Context");
            var it = GetString(test, "It");
            if (!string.IsNullOrWhiteSpace(describe) && !string.IsNullOrWhiteSpace(context) && !string.IsNullOrWhiteSpace(it))
                name = $"{describe} {context} {it}";
        }
        name = string.IsNullOrWhiteSpace(name) ? "Unknown Test" : name;

        var errorMessage = GetErrorMessage(test);
        var stack = GetStackTrace(test);
        var duration = GetDuration(test);

        return new ModuleTestFailureInfo
        {
            Name = name,
            ErrorMessage = errorMessage,
            StackTrace = stack,
            Duration = duration
        };
    }

    private static string GetErrorMessage(PSObject test)
    {
        var err = GetPsObject(test, "ErrorRecord");
        var ex = err is not null ? GetPsObject(err, "Exception") : null;
        var msg = ex is not null ? GetString(ex, "Message") : null;
        if (!string.IsNullOrWhiteSpace(msg))
            return msg;

        var failureMsg = GetString(test, "FailureMessage");
        if (!string.IsNullOrWhiteSpace(failureMsg))
            return failureMsg;

        return "No error message available";
    }

    private static string? GetStackTrace(PSObject test)
    {
        var err = GetPsObject(test, "ErrorRecord");
        var stack = err is not null ? GetString(err, "ScriptStackTrace") : null;
        return string.IsNullOrWhiteSpace(stack) ? null : stack;
    }

    private static TimeSpan? GetDuration(PSObject test)
    {
        var time = test.Properties["Time"]?.Value ?? test.Properties["Duration"]?.Value;
        if (time is null) return null;

        if (time is TimeSpan ts) return ts;

        if (time is string s && TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        try
        {
            var seconds = Convert.ToDouble(time, CultureInfo.InvariantCulture);
            return TimeSpan.FromSeconds(seconds);
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveResultsPath(string? explicitPath, string projectPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return System.IO.Path.GetFullPath(explicitPath.Trim().Trim('"'));

        var candidates = new[]
        {
            System.IO.Path.Combine(projectPath, "TestResults.xml"),
            System.IO.Path.Combine(projectPath, "Tests", "TestResults.xml"),
            System.IO.Path.Combine(projectPath, "Test", "TestResults.xml"),
            System.IO.Path.Combine(projectPath, "Tests", "Results", "TestResults.xml")
        };

        foreach (var p in candidates)
        {
            if (File.Exists(p))
                return p;
        }

        WriteWarning("No test results file found. Searched in:");
        foreach (var p in candidates)
            WriteWarning($"  {p}");

        return null;
    }

    private static void PopulateFromXmlFile(string path, ModuleTestFailureAnalysis analysis)
    {
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.Load(path);

        var suite = doc.SelectSingleNode("/test-results/test-suite");
        var results = suite?.SelectSingleNode("results");
        if (suite is null || results is null)
            throw new InvalidDataException($"Invalid XML structure in test results file '{path}'");

        analysis.TotalCount = GetXmlIntAttr(suite, "total");
        analysis.FailedCount = GetXmlIntAttr(suite, "failures");
        analysis.PassedCount = Math.Max(0, analysis.TotalCount - analysis.FailedCount);
        analysis.SkippedCount = GetXmlIntAttr(suite, "inconclusive");

        var nodes = results.SelectNodes("test-case");
        if (nodes is null) return;

        foreach (XmlNode tc in nodes)
        {
            var result = tc.Attributes?["result"]?.Value;
            if (string.Equals(result, "Success", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = tc.Attributes?["name"]?.Value ?? "Unknown Test";
            var errorMessage = ExtractXmlFailureMessage(tc) ?? "No error message available";
            var stack = tc.SelectSingleNode("failure/stack-trace")?.InnerText;
            var duration = ExtractXmlDuration(tc.Attributes?["time"]?.Value);

            analysis.FailedTests.Add(new ModuleTestFailureInfo
            {
                Name = name,
                ErrorMessage = errorMessage,
                StackTrace = string.IsNullOrWhiteSpace(stack) ? null : stack,
                Duration = duration
            });
        }
    }

    private static string? ExtractXmlFailureMessage(XmlNode testCase)
    {
        var msgNode = testCase.SelectSingleNode("failure/message");
        var text = msgNode?.InnerText;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static TimeSpan? ExtractXmlDuration(string? secondsText)
    {
        if (string.IsNullOrWhiteSpace(secondsText)) return null;
        if (!double.TryParse(secondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return null;
        return TimeSpan.FromSeconds(seconds);
    }

    private static int GetXmlIntAttr(XmlNode node, string attr)
    {
        var v = node.Attributes?[attr]?.Value;
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0;
    }

    private static IEnumerable<PSObject> GetTests(PSObject ps, string propertyName)
    {
        var v = ps.Properties[propertyName]?.Value;
        if (v is null) yield break;

        if (v is IEnumerable en && v is not string)
        {
            foreach (var item in en)
                yield return PSObject.AsPSObject(item);
        }
    }

    private static int? GetInt(PSObject ps, string propertyName)
    {
        var v = ps.Properties[propertyName]?.Value;
        if (v is null) return null;
        try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return null; }
    }

    private static string? GetString(PSObject ps, string propertyName)
    {
        var v = ps.Properties[propertyName]?.Value;
        return v?.ToString();
    }

    private static PSObject? GetPsObject(PSObject ps, string propertyName)
    {
        var v = ps.Properties[propertyName]?.Value;
        return v is null ? null : PSObject.AsPSObject(v);
    }

    private string ConvertToJson(PSObject analysis)
    {
        var script = "param($o) $o | ConvertTo-Json -Depth 3";
        var res = InvokeCommand.InvokeScript(script, analysis);
        return res.Count > 0 ? (res[0]?.BaseObject?.ToString() ?? string.Empty) : string.Empty;
    }

    private void WriteSummary(ModuleTestFailureAnalysis analysis, bool showSuccessful)
    {
        HostWriteLineSafe("=== Module Test Results Summary ===", ConsoleColor.Cyan);
        HostWriteLineSafe($"Source: {analysis.Source}", ConsoleColor.DarkGray);
        HostWriteLineSafe(string.Empty);

        HostWriteLineSafe("📊 Test Statistics:", ConsoleColor.Yellow);
        HostWriteLineSafe($"   Total Tests: {analysis.TotalCount}");
        HostWriteLineSafe($"   ✅ Passed: {analysis.PassedCount}", ConsoleColor.Green);
        HostWriteLineSafe($"   ❌ Failed: {analysis.FailedCount}", analysis.FailedCount > 0 ? ConsoleColor.Red : ConsoleColor.Green);
        if (analysis.SkippedCount > 0)
            HostWriteLineSafe($"   ⏭️  Skipped: {analysis.SkippedCount}", ConsoleColor.Yellow);

        if (analysis.TotalCount > 0)
        {
            var rate = Math.Round((double)analysis.PassedCount / analysis.TotalCount * 100, 1);
            var color = rate == 100 ? ConsoleColor.Green : (rate >= 80 ? ConsoleColor.Yellow : ConsoleColor.Red);
            HostWriteLineSafe($"   📈 Success Rate: {rate.ToString("0.0", CultureInfo.InvariantCulture)}%", color);
        }

        HostWriteLineSafe(string.Empty);
        if (analysis.FailedCount > 0)
        {
            HostWriteLineSafe("❌ Failed Tests:", ConsoleColor.Red);
            foreach (var f in analysis.FailedTests)
                HostWriteLineSafe($"   • {f.Name}", ConsoleColor.Red);
            HostWriteLineSafe(string.Empty);
        }
        else if (showSuccessful && analysis.PassedCount > 0)
        {
            HostWriteLineSafe("✅ All tests passed successfully!", ConsoleColor.Green);
        }
    }

    private void WriteDetails(ModuleTestFailureAnalysis analysis)
    {
        HostWriteLineSafe("=== Module Test Failure Analysis ===", ConsoleColor.Cyan);
        HostWriteLineSafe($"Source: {analysis.Source}", ConsoleColor.DarkGray);
        HostWriteLineSafe($"Analysis Time: {analysis.Timestamp}", ConsoleColor.DarkGray);
        HostWriteLineSafe(string.Empty);

        if (analysis.TotalCount == 0)
        {
            HostWriteLineSafe("⚠️  No test results found", ConsoleColor.Yellow);
            return;
        }

        var color = analysis.FailedCount == 0 ? ConsoleColor.Green : ConsoleColor.Yellow;
        HostWriteLineSafe($"📊 Summary: {analysis.PassedCount}/{analysis.TotalCount} tests passed", color);
        HostWriteLineSafe(string.Empty);

        if (analysis.FailedCount == 0)
        {
            HostWriteLineSafe("🎉 All tests passed successfully!", ConsoleColor.Green);
            return;
        }

        HostWriteLineSafe($"❌ Failed Tests ({analysis.FailedCount}):", ConsoleColor.Red);
        HostWriteLineSafe(string.Empty);

        foreach (var f in analysis.FailedTests)
        {
            HostWriteLineSafe($"🔴 {f.Name}", ConsoleColor.Red);
            if (!string.IsNullOrWhiteSpace(f.ErrorMessage) && !string.Equals(f.ErrorMessage, "No error message available", StringComparison.Ordinal))
            {
                foreach (var line in f.ErrorMessage.Split(new[] { '\n' }, StringSplitOptions.None))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0)
                        HostWriteLineSafe($"   💬 {trimmed}", ConsoleColor.Yellow);
                }
            }
            if (f.Duration.HasValue)
                HostWriteLineSafe($"   ⏱️  Duration: {f.Duration.Value}", ConsoleColor.DarkGray);
            HostWriteLineSafe(string.Empty);
        }

        HostWriteLineSafe($"=== Summary: {analysis.FailedCount} test{(analysis.FailedCount != 1 ? "s" : string.Empty)} failed ===", ConsoleColor.Red);
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

    private static PSObject ToPsCustomObject(ModuleTestFailureAnalysis analysis)
    {
        var o = NewPsCustomObject();
        o.Properties.Add(new PSNoteProperty("Source", analysis.Source));
        o.Properties.Add(new PSNoteProperty("TotalCount", analysis.TotalCount));
        o.Properties.Add(new PSNoteProperty("PassedCount", analysis.PassedCount));
        o.Properties.Add(new PSNoteProperty("FailedCount", analysis.FailedCount));
        o.Properties.Add(new PSNoteProperty("SkippedCount", analysis.SkippedCount));
        o.Properties.Add(new PSNoteProperty("FailedTests", analysis.FailedTests.Select(ToPsCustomObject).ToArray()));
        o.Properties.Add(new PSNoteProperty("Timestamp", analysis.Timestamp));
        return o;
    }

    private static PSObject ToPsCustomObject(ModuleTestFailureInfo failure)
    {
        var o = NewPsCustomObject();
        o.Properties.Add(new PSNoteProperty("Name", failure.Name));
        o.Properties.Add(new PSNoteProperty("ErrorMessage", failure.ErrorMessage));
        o.Properties.Add(new PSNoteProperty("StackTrace", failure.StackTrace));
        o.Properties.Add(new PSNoteProperty("Duration", failure.Duration));
        return o;
    }

    private static PSObject NewPsCustomObject()
    {
        // PSCustomObject has a non-public parameterless ctor; use it to preserve legacy output type without relying on
        // PSCustomObject members in the compile-time reference assembly.
        var t = typeof(PSObject).Assembly.GetType("System.Management.Automation.PSCustomObject");
        if (t is null)
            return new PSObject();

        var inst = Activator.CreateInstance(t, nonPublic: true);
        return inst is PSObject pso ? pso : PSObject.AsPSObject(inst);
    }

    private sealed class ModuleTestFailureAnalysis
    {
        public string Source { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public int PassedCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public List<ModuleTestFailureInfo> FailedTests { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    private sealed class ModuleTestFailureInfo
    {
        public string Name { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
        public TimeSpan? Duration { get; set; }
    }
}
