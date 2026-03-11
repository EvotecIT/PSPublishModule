using System;
using System.Collections.Generic;
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
            var serializer = new ModuleTestFailureSerializationService();
            var display = new ModuleTestFailureDisplayService();
            var workflow = new ModuleTestFailureWorkflowService();
            var workflowResult = workflow.Execute(new ModuleTestFailureWorkflowRequest
            {
                UseTestResultsInput = ParameterSetName == ParameterSetTestResults,
                TestResults = TestResults,
                ExplicitPath = Path,
                ProjectPath = ProjectPath,
                ModuleBasePath = MyInvocation?.MyCommand?.Module?.ModuleBase,
                CurrentDirectory = SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Directory.GetCurrentDirectory()
            });

            foreach (var warning in workflowResult.WarningMessages)
                WriteWarning(warning);

            if (workflowResult.Analysis is null)
                return;

            var analysis = workflowResult.Analysis;

            switch (OutputFormat)
            {
                case ModuleTestFailureOutputFormat.Json:
                    WriteObject(serializer.ToJson(analysis), enumerateCollection: false);
                    break;
                case ModuleTestFailureOutputFormat.Summary:
                    WriteDisplayLines(display.CreateSummary(analysis, ShowSuccessful.IsPresent));
                    break;
                case ModuleTestFailureOutputFormat.Detailed:
                    WriteDisplayLines(display.CreateDetailed(analysis));
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

    private void WriteDisplayLines(IReadOnlyList<ModuleTestFailureDisplayLine> lines)
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
