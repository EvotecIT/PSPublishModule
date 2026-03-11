using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Output verbosity for <c>Invoke-ModuleTestSuite</c>.
/// </summary>
public enum ModuleTestSuiteOutputFormat
{
    /// <summary>Detailed output.</summary>
    Detailed,
    /// <summary>Normal output.</summary>
    Normal,
    /// <summary>Minimal output.</summary>
    Minimal
}

/// <summary>
/// Failure summary format used by <c>Invoke-ModuleTestSuite</c>.
/// </summary>
public enum ModuleTestSuiteFailureSummaryFormat
{
    /// <summary>Write a concise summary.</summary>
    Summary,
    /// <summary>Write detailed failures.</summary>
    Detailed
}

/// <summary>
/// Complete module testing suite that handles dependencies, imports, and test execution.
/// </summary>
/// <remarks>
/// <para>
/// Executes module tests out-of-process, installs required dependencies, and provides a summary that is suitable for both
/// local development and CI pipelines.
/// </para>
/// <para>
/// For post-processing failures (e.g. emitting JSON summaries), combine it with <c>Get-ModuleTestFailures</c>.
/// </para>
/// </remarks>
/// <example>
/// <summary>Run the full test suite for a module</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleTestSuite -ProjectPath 'C:\Git\MyModule'</code>
/// <para>Runs tests under the module project folder, installs dependencies, and prints a summary.</para>
/// </example>
/// <example>
/// <summary>Run in CI mode and return the result object</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleTestSuite -ProjectPath 'C:\Git\MyModule' -CICD -PassThru</code>
/// <para>Optimizes output for CI and returns a structured result object.</para>
/// </example>
/// <example>
/// <summary>Pipe results into Get-ModuleTestFailures</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Invoke-ModuleTestSuite -ProjectPath 'C:\Git\MyModule' -PassThru | Get-ModuleTestFailures -OutputFormat Summary</code>
/// <para>Produces a concise failure summary that can be used in CI logs.</para>
/// </example>
[Cmdlet(VerbsLifecycle.Invoke, "ModuleTestSuite")]
public sealed class InvokeModuleTestSuiteCommand : PSCmdlet
{
    /// <summary>Path to the PowerShell module project directory.</summary>
    [Parameter]
    public string? ProjectPath { get; set; }

    /// <summary>Additional modules to install beyond those specified in the manifest.</summary>
    [Parameter]
    public string[] AdditionalModules { get; set; } = new[] { "Pester", "PSWriteColor" };

    /// <summary>Array of module names to skip during installation.</summary>
    [Parameter]
    public string[] SkipModules { get; set; } = Array.Empty<string>();

    /// <summary>Custom path to test files (defaults to Tests folder in project).</summary>
    [Parameter]
    public string? TestPath { get; set; }

    /// <summary>Test output format.</summary>
    [Parameter]
    public ModuleTestSuiteOutputFormat OutputFormat { get; set; } = ModuleTestSuiteOutputFormat.Detailed;

    /// <summary>Timeout for the out-of-process test execution, in seconds.</summary>
    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>Enable code coverage analysis during tests.</summary>
    [Parameter]
    public SwitchParameter EnableCodeCoverage { get; set; }

    /// <summary>Force reinstall of modules and reimport of the target module.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Set a non-zero process exit code when tests fail.</summary>
    [Parameter]
    public SwitchParameter ExitOnFailure { get; set; }

    /// <summary>Skip dependency checking and installation.</summary>
    [Parameter]
    public SwitchParameter SkipDependencies { get; set; }

    /// <summary>Skip module import step.</summary>
    [Parameter]
    public SwitchParameter SkipImport { get; set; }

    /// <summary>Return the test suite result object.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Enable CI/CD mode with optimized settings and output.</summary>
    [Parameter]
    public SwitchParameter CICD { get; set; }

    /// <summary>Display detailed failure analysis when tests fail.</summary>
    [Parameter]
    public SwitchParameter ShowFailureSummary { get; set; }

    /// <summary>Format for failure summary display.</summary>
    [Parameter]
    public ModuleTestSuiteFailureSummaryFormat FailureSummaryFormat { get; set; } = ModuleTestSuiteFailureSummaryFormat.Summary;

    /// <summary>Runs the full test suite.</summary>
    protected override void ProcessRecord()
    {
        try
        {
            var display = new ModuleTestSuiteDisplayService();
            var preparation = new ModuleTestSuitePreparationService().Prepare(new ModuleTestSuitePreparationRequest
            {
                CurrentPath = SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Environment.CurrentDirectory,
                ProjectPath = ProjectPath,
                AdditionalModules = AdditionalModules,
                SkipModules = SkipModules,
                TestPath = TestPath,
                OutputFormat = MapOutputFormat(OutputFormat),
                TimeoutSeconds = TimeoutSeconds,
                EnableCodeCoverage = EnableCodeCoverage.IsPresent,
                Force = Force.IsPresent,
                ExitOnFailure = ExitOnFailure.IsPresent,
                SkipDependencies = SkipDependencies.IsPresent,
                SkipImport = SkipImport.IsPresent,
                PassThru = PassThru.IsPresent,
                CICD = CICD.IsPresent
            });
            var projectRoot = preparation.ProjectRoot;

            var psVersionTable = SessionState?.PSVariable?.GetValue("PSVersionTable") as Hashtable;
            var psVersion = psVersionTable?["PSVersion"]?.ToString() ?? string.Empty;
            var psEdition = psVersionTable?["PSEdition"]?.ToString() ?? string.Empty;
            WriteDisplayLines(display.CreateHeader(projectRoot, psVersion, psEdition, CICD.IsPresent));

            WriteDisplayLines(display.CreateModuleInfoHeader());
            var moduleInfo = new ModuleInformationReader().Read(projectRoot);
            WriteDisplayLines(display.CreateModuleInfoDetails(moduleInfo));

            WriteDisplayLines(display.CreateExecutionHeader());
            var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"), warningsAsVerbose: true);
            var workflow = new ModuleTestSuiteWorkflowService(logger).Execute(preparation);
            var result = workflow.Result;

            // Emit captured Pester output if requested.
            if (preparation.Spec.OutputFormat != PowerForge.ModuleTestSuiteOutputFormat.Minimal && !string.IsNullOrWhiteSpace(result.StdOut))
            {
                HostWriteLineSafe(result.StdOut);
                HostWriteLineSafe(string.Empty);
            }

            WriteDisplayLines(display.CreateDependencySummary(result.RequiredModules, AdditionalModules, SkipModules));

            if (!SkipDependencies.IsPresent)
                WriteDisplayLines(display.CreateDependencyInstallResults(result.DependencyResults));

            WriteDisplayLines(display.CreateCompletionSummary(result));

            if (result.FailedCount > 0)
            {
                if (ShowFailureSummary.IsPresent || CICD.IsPresent)
                {
                    WriteDisplayLines(display.CreateFailureSummary(
                        result.FailureAnalysis,
                        detailed: FailureSummaryFormat == ModuleTestSuiteFailureSummaryFormat.Detailed));
                    HostWriteLineSafe(string.Empty);
                }

                foreach (var line in workflow.CiOutputLines)
                    HostWriteLineSafe(line);

                if (preparation.ExitOnFailure)
                    Environment.ExitCode = 1;

                if (preparation.PassThru)
                    WriteObject(result, enumerateCollection: false);

                throw new InvalidOperationException(workflow.FailureMessage);
            }

            foreach (var line in workflow.CiOutputLines)
                HostWriteLineSafe(line);

            if (preparation.PassThru)
                WriteObject(result, enumerateCollection: false);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "InvokeModuleTestSuiteFailed", ErrorCategory.NotSpecified, null));
            throw;
        }
    }

    private static PowerForge.ModuleTestSuiteOutputFormat MapOutputFormat(ModuleTestSuiteOutputFormat format)
    {
        return format switch
        {
            ModuleTestSuiteOutputFormat.Detailed => PowerForge.ModuleTestSuiteOutputFormat.Detailed,
            ModuleTestSuiteOutputFormat.Normal => PowerForge.ModuleTestSuiteOutputFormat.Normal,
            ModuleTestSuiteOutputFormat.Minimal => PowerForge.ModuleTestSuiteOutputFormat.Minimal,
            _ => PowerForge.ModuleTestSuiteOutputFormat.Detailed
        };
    }

    private void WriteDisplayLines(IReadOnlyList<ModuleTestSuiteDisplayLine> lines)
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
