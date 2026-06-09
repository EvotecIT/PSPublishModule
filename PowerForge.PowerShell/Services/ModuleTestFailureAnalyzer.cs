using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Xml;

namespace PowerForge;

/// <summary>
/// Analyzes and summarizes failed Pester tests from either a Pester results object or an NUnit XML result file.
/// </summary>
public sealed class ModuleTestFailureAnalyzer
{
    /// <summary>
    /// Creates a failure analysis from a Pester results object (Pester v5+ or legacy v4 style).
    /// </summary>
    /// <param name="testResults">Pester results object returned by <c>Invoke-Pester</c>.</param>
    public ModuleTestFailureAnalysis AnalyzeFromPesterResults(object? testResults)
    {
        var analysis = new ModuleTestFailureAnalysis
        {
            Source = "PesterResults",
            Timestamp = DateTime.Now,
            FailedTests = Array.Empty<ModuleTestFailureInfo>()
        };

        PopulateFromPesterResults(testResults, analysis);
        return analysis;
    }

    /// <summary>
    /// Creates a failure analysis from an NUnit XML test results file.
    /// </summary>
    /// <param name="path">Path to the NUnit XML test results file.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the results file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the XML structure is not recognized.</exception>
    public ModuleTestFailureAnalysis AnalyzeFromXmlFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Test results file not found: {fullPath}", fullPath);

        var analysis = new ModuleTestFailureAnalysis
        {
            Source = fullPath,
            Timestamp = DateTime.Now,
            FailedTests = Array.Empty<ModuleTestFailureInfo>()
        };

        PopulateFromXmlFile(fullPath, analysis);
        return analysis;
    }

    private static void PopulateFromPesterResults(object? testResults, ModuleTestFailureAnalysis analysis)
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

        var failures = new List<ModuleTestFailureInfo>(failed.Length);
        foreach (var t in failed)
            failures.Add(ExtractFailureInfo(t));

        analysis.FailedTests = failures.ToArray();
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

        name = string.IsNullOrWhiteSpace(name) ? "Unknown Test" : name!;

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
            return msg!;

        var failureMsg = GetString(test, "FailureMessage");
        if (!string.IsNullOrWhiteSpace(failureMsg))
            return failureMsg!;

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

    private static void PopulateFromXmlFile(string path, ModuleTestFailureAnalysis analysis)
    {
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.Load(path);

        var root = doc.SelectSingleNode("/test-results");
        if (root is null)
            throw new InvalidDataException($"Invalid XML structure in test results file '{path}'");

        analysis.TotalCount = GetXmlIntAttr(root, "total");
        var failuresCount = GetXmlIntAttr(root, "failures");
        var errorsCount = GetXmlIntAttr(root, "errors");
        var notRunCount = GetXmlIntAttr(root, "not-run");
        var inconclusiveCount = GetXmlIntAttr(root, "inconclusive");
        var ignoredCount = GetXmlIntAttr(root, "ignored");
        var skippedCount = GetXmlIntAttr(root, "skipped");
        var invalidCount = GetXmlIntAttr(root, "invalid");

        analysis.FailedCount = failuresCount + errorsCount;
        analysis.SkippedCount = skippedCount + ignoredCount + inconclusiveCount + notRunCount + invalidCount;
        analysis.PassedCount = Math.Max(0, analysis.TotalCount - analysis.FailedCount - analysis.SkippedCount);

        var nodes = doc.SelectNodes("/test-results//test-case");
        if (nodes is null)
        {
            analysis.FailedTests = Array.Empty<ModuleTestFailureInfo>();        
            return;
        }

        var failures = new List<ModuleTestFailureInfo>();
        foreach (XmlNode tc in nodes)
        {
            var result = tc.Attributes?["result"]?.Value;
            var success = tc.Attributes?["success"]?.Value;
            var executed = tc.Attributes?["executed"]?.Value;

            var isSuccess = string.Equals(result, "Success", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(success, "True", StringComparison.OrdinalIgnoreCase);
            if (isSuccess)
                continue;

            var isSkipped = string.Equals(result, "Skipped", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result, "Ignored", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result, "Inconclusive", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(executed, "False", StringComparison.OrdinalIgnoreCase);
            if (isSkipped)
                continue;

            var name = tc.Attributes?["name"]?.Value ?? "Unknown Test";
            var errorMessage = ExtractXmlFailureMessage(tc) ?? "No error message available";
            var stack = tc.SelectSingleNode("failure/stack-trace")?.InnerText;  
            var duration = ExtractXmlDuration(tc.Attributes?["time"]?.Value);   

            failures.Add(new ModuleTestFailureInfo
            {
                Name = name,
                ErrorMessage = errorMessage,
                StackTrace = string.IsNullOrWhiteSpace(stack) ? null : stack,
                Duration = duration
            });
        }

        if (analysis.TotalCount == 0 && nodes.Count > 0)
        {
            analysis.TotalCount = nodes.Count;
            analysis.PassedCount = Math.Max(0, analysis.TotalCount - analysis.FailedCount - analysis.SkippedCount);
        }

        if (analysis.FailedCount == 0 && failures.Count > 0)
        {
            analysis.FailedCount = failures.Count;
            analysis.PassedCount = Math.Max(0, analysis.TotalCount - analysis.FailedCount - analysis.SkippedCount);
        }

        analysis.FailedTests = failures.ToArray();
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
        try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
        catch { return null; }
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
}
