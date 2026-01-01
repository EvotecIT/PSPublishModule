using System;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using PowerForge;

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
/// <remarks>
/// <para>
/// This cmdlet is designed to make CI output and local troubleshooting easier by summarizing failures from:
/// </para>
/// <list type="bullet">
/// <item><description>An NUnit XML results file (commonly <c>TestResults.xml</c>)</description></item>
/// <item><description>A Pester results object (from <c>Invoke-Pester</c>)</description></item>
/// <item><description>A <see cref="ModuleTestSuiteResult"/> (from <c>Invoke-ModuleTestSuite</c>)</description></item>
/// </list>
/// <para>
/// Use <c>-OutputFormat</c> to control whether the cmdlet writes a concise host summary, detailed messages,
/// or emits JSON to the pipeline.
/// </para>
/// </remarks>
/// <example>
/// <summary>Analyze test failures from the default results location</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleTestFailures</code>
/// <para>Searches for <c>TestResults.xml</c> under the project and prints a detailed failure report.</para>
/// </example>
/// <example>
/// <summary>Analyze failures from a specific NUnit XML file</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleTestFailures -Path 'Tests\TestResults.xml' -OutputFormat Summary</code>
/// <para>Writes a compact summary that is suitable for CI logs.</para>
/// </example>
/// <example>
/// <summary>Pipe results from Invoke-ModuleTestSuite</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleTestSuite -ProjectPath 'C:\Git\MyModule' | Get-ModuleTestFailures -OutputFormat Detailed -PassThru</code>
/// <para>Uses the in-memory results and returns the analysis object for further processing.</para>
/// </example>
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

    /// <summary>
    /// Pester test results object from <c>Invoke-Pester</c>, a <see cref="ModuleTestSuiteResult"/> from <c>Invoke-ModuleTestSuite</c>,
    /// or a precomputed <see cref="ModuleTestFailureAnalysis"/>.
    /// </summary>
    [Parameter(ParameterSetName = ParameterSetTestResults, Mandatory = true, ValueFromPipeline = true)]
    public object? TestResults { get; set; }

    /// <summary>Path to the project directory used to locate test results when Path is not specified.</summary>
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
            var analyzer = new ModuleTestFailureAnalyzer();
            ModuleTestFailureAnalysis analysis;

            if (ParameterSetName == ParameterSetTestResults)
            {
                analysis = AnalyzeTestResults(analyzer, TestResults);
            }
            else
            {
                var projectPath = ResolveProjectPath();
                var resultsPath = ResolveResultsPath(Path, projectPath);        
                if (resultsPath is null)
                    return;

                if (!File.Exists(resultsPath))
                {
                    // Missing results file should be non-fatal (for example: first run, partial pipelines).
                    // Use a warning rather than an error to avoid turning this into a terminating exception
                    // when ErrorActionPreference is set to Stop (common in CI/test runners).
                    WriteWarning($"Test results file not found: {resultsPath}");
                    return;
                }

                analysis = analyzer.AnalyzeFromXmlFile(resultsPath);
            }

            switch (OutputFormat)
            {
                case ModuleTestFailureOutputFormat.Json:
                    WriteObject(ConvertToJson(analysis), enumerateCollection: false);
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
                WriteObject(analysis, enumerateCollection: false);
            }
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "GetModuleTestFailuresFailed", ErrorCategory.NotSpecified, null));
            throw;
        }
    }

    private static ModuleTestFailureAnalysis AnalyzeTestResults(
        ModuleTestFailureAnalyzer analyzer,
        object? testResults)
    {
        if (testResults is ModuleTestFailureAnalysis analysis)
            return analysis;

        if (testResults is ModuleTestSuiteResult suite)
        {
            if (suite.FailureAnalysis is not null)
                return suite.FailureAnalysis;

            var xmlPath = suite.ResultsXmlPath;
            if (xmlPath is not null && File.Exists(xmlPath))
                return analyzer.AnalyzeFromXmlFile(xmlPath);

            return new ModuleTestFailureAnalysis
            {
                Source = "ModuleTestSuiteResult",
                Timestamp = DateTime.Now,
                TotalCount = suite.TotalCount,
                PassedCount = suite.PassedCount,
                FailedCount = suite.FailedCount,
                SkippedCount = suite.SkippedCount,
                FailedTests = Array.Empty<ModuleTestFailureInfo>()
            };
        }

        return analyzer.AnalyzeFromPesterResults(testResults);
    }

    private string ResolveProjectPath()
    {
        if (!string.IsNullOrWhiteSpace(ProjectPath))
            return System.IO.Path.GetFullPath(ProjectPath!.Trim().Trim('"'));

        try
        {
            var moduleBase = MyInvocation?.MyCommand?.Module?.ModuleBase;
            if (!string.IsNullOrWhiteSpace(moduleBase))
                return moduleBase!;
        }
        catch
        {
            // ignore
        }

        return SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Directory.GetCurrentDirectory();
    }

    private string? ResolveResultsPath(string? explicitPath, string projectPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return System.IO.Path.GetFullPath(explicitPath!.Trim().Trim('"'));

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

    private string ConvertToJson(object analysis)
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

        HostWriteLineSafe("Test Statistics:", ConsoleColor.Yellow);
        HostWriteLineSafe($"   Total Tests: {analysis.TotalCount}");
        HostWriteLineSafe($"   Passed: {analysis.PassedCount}", ConsoleColor.Green);
        HostWriteLineSafe($"   Failed: {analysis.FailedCount}", analysis.FailedCount > 0 ? ConsoleColor.Red : ConsoleColor.Green);
        if (analysis.SkippedCount > 0)
            HostWriteLineSafe($"   Skipped: {analysis.SkippedCount}", ConsoleColor.Yellow);

        if (analysis.TotalCount > 0)
        {
            var rate = Math.Round((double)analysis.PassedCount / analysis.TotalCount * 100, 1);
            var color = rate == 100 ? ConsoleColor.Green : (rate >= 80 ? ConsoleColor.Yellow : ConsoleColor.Red);
            HostWriteLineSafe($"   Success Rate: {rate.ToString("0.0", CultureInfo.InvariantCulture)}%", color);
        }

        HostWriteLineSafe(string.Empty);
        if (analysis.FailedCount > 0)
        {
            HostWriteLineSafe("Failed Tests:", ConsoleColor.Red);
            foreach (var f in analysis.FailedTests)
                HostWriteLineSafe($"   - {f.Name}", ConsoleColor.Red);
            HostWriteLineSafe(string.Empty);
        }
        else if (showSuccessful && analysis.PassedCount > 0)
        {
            HostWriteLineSafe("All tests passed successfully!", ConsoleColor.Green);
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
            HostWriteLineSafe("No test results found", ConsoleColor.Yellow);
            return;
        }

        var color = analysis.FailedCount == 0 ? ConsoleColor.Green : ConsoleColor.Yellow;
        HostWriteLineSafe($"Summary: {analysis.PassedCount}/{analysis.TotalCount} tests passed", color);
        HostWriteLineSafe(string.Empty);

        if (analysis.FailedCount == 0)
        {
            HostWriteLineSafe("All tests passed successfully!", ConsoleColor.Green);
            return;
        }

        HostWriteLineSafe($"Failed Tests ({analysis.FailedCount}):", ConsoleColor.Red);
        HostWriteLineSafe(string.Empty);

        foreach (var f in analysis.FailedTests)
        {
            HostWriteLineSafe($"- {f.Name}", ConsoleColor.Red);
            if (!string.IsNullOrWhiteSpace(f.ErrorMessage) && !string.Equals(f.ErrorMessage, "No error message available", StringComparison.Ordinal))
            {
                foreach (var line in f.ErrorMessage.Split(new[] { '\n' }, StringSplitOptions.None))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0)
                        HostWriteLineSafe($"   {trimmed}", ConsoleColor.Yellow);
                }
            }

            if (f.Duration.HasValue)
                HostWriteLineSafe($"   Duration: {f.Duration.Value}", ConsoleColor.DarkGray);
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
}
