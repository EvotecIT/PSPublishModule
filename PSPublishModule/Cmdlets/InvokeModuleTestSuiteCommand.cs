using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
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
            ApplyCiCdDefaults();

            var projectRoot = ResolveProjectPath();
            if (!Directory.Exists(projectRoot))
                throw new DirectoryNotFoundException($"Path '{projectRoot}' does not exist or is not a directory");

            HostWriteLineSafe(CICD.IsPresent ? "=== CI/CD Module Testing Pipeline ===" : "=== PowerShell Module Test Suite ===", ConsoleColor.Magenta);
            HostWriteLineSafe($"Project Path: {projectRoot}", ConsoleColor.Cyan);

            var psVersionTable = SessionState?.PSVariable?.GetValue("PSVersionTable") as Hashtable;
            var psVersion = psVersionTable?["PSVersion"]?.ToString() ?? string.Empty;
            var psEdition = psVersionTable?["PSEdition"]?.ToString() ?? string.Empty;
            HostWriteLineSafe($"PowerShell Version: {psVersion}", ConsoleColor.Cyan);
            HostWriteLineSafe($"PowerShell Edition: {psEdition}", ConsoleColor.Cyan);
            HostWriteLineSafe(string.Empty);

            HostWriteLineSafe("Step 1: Gathering module information...", ConsoleColor.Yellow);
            var moduleInfo = new ModuleInformationReader().Read(projectRoot);
            HostWriteLineSafe($"  Module Name: {moduleInfo.ModuleName}", ConsoleColor.Green);
            HostWriteLineSafe($"  Module Version: {moduleInfo.ModuleVersion ?? string.Empty}", ConsoleColor.Green);
            HostWriteLineSafe($"  Manifest Path: {moduleInfo.ManifestPath}", ConsoleColor.Green);
            HostWriteLineSafe($"  Required Modules: {(moduleInfo.RequiredModules ?? Array.Empty<ManifestEditor.RequiredModule>()).Length}", ConsoleColor.Green);
            HostWriteLineSafe(string.Empty);

            HostWriteLineSafe("Step 2: Executing test suite (out-of-process)...", ConsoleColor.Yellow);
            var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"), warningsAsVerbose: true);

            var service = new ModuleTestSuiteService(new PowerShellRunner(), logger);
            var result = service.Run(new ModuleTestSuiteSpec
            {
                ProjectPath = projectRoot,
                TestPath = TestPath,
                AdditionalModules = AdditionalModules ?? Array.Empty<string>(),
                SkipModules = SkipModules ?? Array.Empty<string>(),
                OutputFormat = MapOutputFormat(OutputFormat),
                EnableCodeCoverage = EnableCodeCoverage.IsPresent,
                Force = Force.IsPresent,
                SkipDependencies = SkipDependencies.IsPresent,
                SkipImport = SkipImport.IsPresent,
                KeepResultsXml = false,
                PreferPwsh = true
            });

            // Emit captured Pester output if requested.
            if (OutputFormat != ModuleTestSuiteOutputFormat.Minimal && !string.IsNullOrWhiteSpace(result.StdOut))
            {
                HostWriteLineSafe(result.StdOut);
                HostWriteLineSafe(string.Empty);
            }

            HostWriteLineSafe("Step 3: Dependency summary...", ConsoleColor.Yellow);
            WriteDependencySummary(result.RequiredModules);
            WriteAdditionalModulesSummary();
            HostWriteLineSafe(string.Empty);

            if (!SkipDependencies.IsPresent)
            {
                HostWriteLineSafe("Step 4: Dependency installation results...", ConsoleColor.Yellow);
                WriteDependencyInstallResults(result.DependencyResults);
                HostWriteLineSafe(string.Empty);
            }

            var successColor = result.FailedCount > 0 ? ConsoleColor.Red : ConsoleColor.Green;
            HostWriteLineSafe(result.FailedCount > 0 ? "=== Test Suite Failed ===" : "=== Test Suite Completed Successfully ===", successColor);
            HostWriteLineSafe($"Module: {result.ModuleName} v{result.ModuleVersion ?? string.Empty}", ConsoleColor.Green);
            HostWriteLineSafe($"Tests: {result.PassedCount}/{result.TotalCount} passed", result.FailedCount > 0 ? ConsoleColor.Yellow : ConsoleColor.Green);
            if (result.Duration.HasValue)
                HostWriteLineSafe($"Duration: {result.Duration.Value}", ConsoleColor.Green);
            HostWriteLineSafe(string.Empty);

            if (result.FailedCount > 0)
            {
                if (ShowFailureSummary.IsPresent || CICD.IsPresent)
                {
                    HostWriteLineSafe("=== Test Failure Analysis ===", ConsoleColor.Yellow);
                    WriteFailureSummary(result.FailureAnalysis, FailureSummaryFormat);
                    HostWriteLineSafe(string.Empty);
                }

                EmitCiOutputs(result, success: false, errorMessage: $"{result.FailedCount} test{(result.FailedCount != 1 ? "s" : string.Empty)} failed");

                if (ExitOnFailure.IsPresent)
                    Environment.ExitCode = 1;

                if (PassThru.IsPresent)
                    WriteObject(result, enumerateCollection: false);

                throw new InvalidOperationException($"{result.FailedCount} test{(result.FailedCount != 1 ? "s" : string.Empty)} failed");
            }

            EmitCiOutputs(result, success: true, errorMessage: null);

            if (PassThru.IsPresent)
                WriteObject(result, enumerateCollection: false);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "InvokeModuleTestSuiteFailed", ErrorCategory.NotSpecified, null));
            throw;
        }
    }

    private void ApplyCiCdDefaults()
    {
        if (!CICD.IsPresent)
            return;

        OutputFormat = ModuleTestSuiteOutputFormat.Minimal;
        ExitOnFailure = true;
        PassThru = true;
    }

    private string ResolveProjectPath()
    {
        if (!string.IsNullOrWhiteSpace(ProjectPath))
            return Path.GetFullPath(ProjectPath!.Trim().Trim('"'));

        return SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Directory.GetCurrentDirectory();
    }

    private void WriteDependencySummary(ManifestEditor.RequiredModule[] requiredModules)
    {
        if (requiredModules.Length == 0)
        {
            HostWriteLineSafe("  No required modules specified in manifest", ConsoleColor.Gray);
            return;
        }

        HostWriteLineSafe("Required modules:", ConsoleColor.Cyan);
        foreach (var m in requiredModules)
        {
            var name = m.ModuleName;
            var min = m.ModuleVersion;
            var req = m.RequiredVersion;
            var max = m.MaximumVersion;

            var versionInfo = string.Empty;
            if (!string.IsNullOrWhiteSpace(min)) versionInfo += $" (Min: {min})";
            if (!string.IsNullOrWhiteSpace(req)) versionInfo += $" (Required: {req})";
            if (!string.IsNullOrWhiteSpace(max)) versionInfo += $" (Max: {max})";

            HostWriteLineSafe($"  📦 {name}{versionInfo}", ConsoleColor.Green);
        }
    }

    private void WriteAdditionalModulesSummary()
    {
        if (AdditionalModules.Length == 0)
            return;

        HostWriteLineSafe("Additional modules:", ConsoleColor.Cyan);
        foreach (var m in AdditionalModules)
        {
            if (SkipModules.Contains(m, StringComparer.OrdinalIgnoreCase))
                continue;

            HostWriteLineSafe($"  ✅ {m}", ConsoleColor.Green);
        }
    }

    private void WriteDependencyInstallResults(ModuleDependencyInstallResult[] results)
    {
        if (results.Length == 0)
        {
            HostWriteLineSafe("  (no dependency install actions)", ConsoleColor.Gray);
            return;
        }

        foreach (var r in results)
        {
            switch (r.Status)
            {
                case ModuleDependencyInstallStatus.Skipped:
                    HostWriteLineSafe($"  ⏭️ Skipping: {r.Name}", ConsoleColor.Gray);
                    break;
                case ModuleDependencyInstallStatus.Satisfied:
                    HostWriteLineSafe($"  ✅ {r.Name} OK (installed: {r.InstalledVersion ?? "unknown"})", ConsoleColor.Green);
                    break;
                case ModuleDependencyInstallStatus.Installed:
                case ModuleDependencyInstallStatus.Updated:
                {
                    var icon = r.Status == ModuleDependencyInstallStatus.Updated ? "🔄" : "📥";
                    HostWriteLineSafe($"  {icon} {r.Name} {r.Status} via {r.Installer ?? "installer"} (resolved: {r.ResolvedVersion ?? "unknown"})", ConsoleColor.Green);
                    break;
                }
                case ModuleDependencyInstallStatus.Failed:
                    HostWriteLineSafe($"  ❌ {r.Name}: {r.Message}", ConsoleColor.Red);
                    break;
            }
        }
    }

    private void WriteFailureSummary(ModuleTestFailureAnalysis? analysis, ModuleTestSuiteFailureSummaryFormat format)
    {
        if (analysis is null)
        {
            HostWriteLineSafe("No failure analysis available.", ConsoleColor.Yellow);
            return;
        }

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
            if (format == ModuleTestSuiteFailureSummaryFormat.Detailed &&
                !string.IsNullOrWhiteSpace(f.ErrorMessage) &&
                !string.Equals(f.ErrorMessage, "No error message available", StringComparison.Ordinal))
            {
                foreach (var line in f.ErrorMessage.Split(new[] { '\n' }, StringSplitOptions.None))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0)
                        HostWriteLineSafe($"   {trimmed}", ConsoleColor.Yellow);
                }
            }

            if (format == ModuleTestSuiteFailureSummaryFormat.Detailed && f.Duration.HasValue)
                HostWriteLineSafe($"   Duration: {f.Duration.Value}", ConsoleColor.DarkGray);

            HostWriteLineSafe(string.Empty);
        }
    }

    private void EmitCiOutputs(ModuleTestSuiteResult result, bool success, string? errorMessage)
    {
        if (!CICD.IsPresent)
            return;

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
        {
            HostWriteLineSafe($"::set-output name=test-result::{(success ? "true" : "false")}");
            HostWriteLineSafe($"::set-output name=total-tests::{result.TotalCount}");
            HostWriteLineSafe($"::set-output name=failed-tests::{result.FailedCount}");
            if (!success && !string.IsNullOrWhiteSpace(errorMessage))
                HostWriteLineSafe($"::set-output name=error-message::{errorMessage}");
            if (result.CoveragePercent.HasValue)
                HostWriteLineSafe($"::set-output name=code-coverage::{result.CoveragePercent.Value.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TF_BUILD")))
        {
            HostWriteLineSafe($"##vso[task.setvariable variable=TestResult;isOutput=true]{(success ? "true" : "false")}");
            HostWriteLineSafe($"##vso[task.setvariable variable=TotalTests;isOutput=true]{result.TotalCount}");
            HostWriteLineSafe($"##vso[task.setvariable variable=FailedTests;isOutput=true]{result.FailedCount}");
            if (!success && !string.IsNullOrWhiteSpace(errorMessage))
                HostWriteLineSafe($"##vso[task.setvariable variable=ErrorMessage;isOutput=true]{errorMessage}");
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
