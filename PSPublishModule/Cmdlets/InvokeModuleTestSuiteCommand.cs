using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

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

    /// <summary>Return the test results object.</summary>
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

            HostWriteLineSafe(CICD.IsPresent ? "=== CI/CD Module Testing Pipeline ===" : "=== PowerShell Module Test Suite ===",
                ConsoleColor.Magenta);
            HostWriteLineSafe($"Project Path: {projectRoot}", ConsoleColor.Cyan);
            var psVersionTable = SessionState?.PSVariable?.GetValue("PSVersionTable") as Hashtable;
            var psVersion = psVersionTable?["PSVersion"]?.ToString() ?? string.Empty;
            var psEdition = psVersionTable?["PSEdition"]?.ToString() ?? string.Empty;
            HostWriteLineSafe($"PowerShell Version: {psVersion}", ConsoleColor.Cyan);
            HostWriteLineSafe($"PowerShell Edition: {psEdition}", ConsoleColor.Cyan);
            HostWriteLineSafe(string.Empty);

            HostWriteLineSafe("Step 1: Gathering module information...", ConsoleColor.Yellow);
            var moduleInfo = GetModuleInformation(projectRoot);

            var moduleName = GetString(moduleInfo, "ModuleName");
            var moduleVersion = GetString(moduleInfo, "ModuleVersion");
            var manifestPath = GetString(moduleInfo, "ManifestPath");
            var requiredModules = GetEnumerable(moduleInfo, "RequiredModules");

            HostWriteLineSafe($"  Module Name: {moduleName}", ConsoleColor.Green);
            HostWriteLineSafe($"  Module Version: {moduleVersion}", ConsoleColor.Green);
            HostWriteLineSafe($"  Manifest Path: {manifestPath}", ConsoleColor.Green);
            HostWriteLineSafe($"  Required Modules: {requiredModules.Count}", ConsoleColor.Green);
            HostWriteLineSafe(string.Empty);

            if (!SkipDependencies.IsPresent)
            {
                HostWriteLineSafe("Step 2: Checking and installing required modules...", ConsoleColor.Yellow);
                InvokePrivateFunction("Test-RequiredModules",
                    new Dictionary<string, object?>
                    {
                        ["ModuleInformation"] = moduleInfo,
                        ["AdditionalModules"] = AdditionalModules,
                        ["SkipModules"] = SkipModules,
                        ["Force"] = Force.IsPresent
                    });
                HostWriteLineSafe(string.Empty);
            }
            else
            {
                HostWriteLineSafe("Step 2: Skipping dependency check (as requested)", ConsoleColor.Yellow);
                HostWriteLineSafe(string.Empty);
            }

            if (!SkipImport.IsPresent)
            {
                HostWriteLineSafe("Step 3: Importing module under test...", ConsoleColor.Yellow);
                InvokePrivateFunction("Test-ModuleImport",
                    new Dictionary<string, object?>
                    {
                        ["ModuleInformation"] = moduleInfo,
                        ["Force"] = Force.IsPresent,
                        ["ShowInformation"] = true
                    });
                HostWriteLineSafe(string.Empty);
            }
            else
            {
                HostWriteLineSafe("Step 3: Skipping module import (as requested)", ConsoleColor.Yellow);
                HostWriteLineSafe(string.Empty);
            }

            HostWriteLineSafe("Step 4: Module dependency summary...", ConsoleColor.Yellow);
            WriteDependencySummary(requiredModules);
            WriteAdditionalModulesSummary();
            HostWriteLineSafe(string.Empty);

            HostWriteLineSafe("Step 5: Executing module tests...", ConsoleColor.Yellow);

            var effectiveTestPath = ResolveTestPath(projectRoot);
            var enableCoverage = EnableCodeCoverage.IsPresent && string.IsNullOrWhiteSpace(TestPath);
            var testResults = InvokePester(effectiveTestPath, OutputFormat, enableCoverage ? projectRoot : null);

            HostWriteLineSafe(string.Empty);

            var counts = GetCounts(testResults);
            var duration = GetDurationOrNull(testResults);

            if (CICD.IsPresent)
                HostWriteLineSafe("=== CI/CD Pipeline Completed ===", counts.FailedCount > 0 ? ConsoleColor.Red : ConsoleColor.Green);
            else
                HostWriteLineSafe(counts.FailedCount > 0 ? "=== Test Suite Failed ===" : "=== Test Suite Completed Successfully ===",
                    counts.FailedCount > 0 ? ConsoleColor.Red : ConsoleColor.Green);

            HostWriteLineSafe($"Module: {moduleName} v{moduleVersion}", ConsoleColor.Green);
            HostWriteLineSafe($"Tests: {counts.PassedCount}/{counts.TotalCount} passed",
                counts.FailedCount > 0 ? ConsoleColor.Yellow : ConsoleColor.Green);
            if (duration.HasValue)
                HostWriteLineSafe($"Duration: {duration.Value}", ConsoleColor.Green);

            if (counts.FailedCount > 0)
            {
                if (ShowFailureSummary.IsPresent || CICD.IsPresent)
                {
                    HostWriteLineSafe(string.Empty);
                    HostWriteLineSafe("=== Test Failure Analysis ===", ConsoleColor.Yellow);
                    try
                    {
                        InvokeGetModuleTestFailures(testResults, FailureSummaryFormat);
                    }
                    catch (Exception ex)
                    {
                        WriteWarning($"Failed to generate failure summary: {ex.Message}");
                    }
                }

                EmitCiOutputs(counts, testResults, success: false, errorMessage: $"{counts.FailedCount} test{(counts.FailedCount != 1 ? "s" : string.Empty)} failed");

                if (ExitOnFailure.IsPresent)
                    Environment.ExitCode = 1;

                throw new InvalidOperationException($"{counts.FailedCount} test{(counts.FailedCount != 1 ? "s" : string.Empty)} failed");
            }

            EmitCiOutputs(counts, testResults, success: true, errorMessage: null);

            if (PassThru.IsPresent)
                WriteObject(testResults, enumerateCollection: false);
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
        var p = ProjectPath;
        if (p != null && !string.IsNullOrWhiteSpace(p))
            return Path.GetFullPath(p.Trim().Trim('"'));

        return SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Directory.GetCurrentDirectory();
    }

    private Hashtable GetModuleInformation(string projectRoot)
    {
        using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
        ps.AddCommand("Get-ModuleInformation").AddParameter("Path", projectRoot);

        var results = ps.Invoke();
        if (ps.HadErrors)
        {
            var msg = string.Join("; ", ps.Streams.Error.Select(e => e.Exception?.Message ?? e.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(msg)) msg = "Get-ModuleInformation failed";
            throw new InvalidOperationException(msg);
        }

        if (results.Count == 0 || results[0]?.BaseObject is not Hashtable ht)
            throw new InvalidOperationException("Get-ModuleInformation returned no data");

        return ht;
    }

    private void WriteDependencySummary(List<object?> requiredModules)
    {
        if (requiredModules.Count == 0)
        {
            HostWriteLineSafe("  No required modules specified in manifest", ConsoleColor.Gray);
            return;
        }

        HostWriteLineSafe("Required modules:", ConsoleColor.Cyan);
        foreach (var m in requiredModules)
        {
            if (m is IDictionary dict)
            {
                var name = dict.Contains("ModuleName") ? dict["ModuleName"]?.ToString() : null;
                var min = dict.Contains("ModuleVersion") ? dict["ModuleVersion"]?.ToString() : null;
                var req = dict.Contains("RequiredVersion") ? dict["RequiredVersion"]?.ToString() : null;
                var max = dict.Contains("MaximumVersion") ? dict["MaximumVersion"]?.ToString() : null;

                var versionInfo = string.Empty;
                if (!string.IsNullOrWhiteSpace(min)) versionInfo += $" (Min: {min})";
                if (!string.IsNullOrWhiteSpace(req)) versionInfo += $" (Required: {req})";
                if (!string.IsNullOrWhiteSpace(max)) versionInfo += $" (Max: {max})";

                HostWriteLineSafe($"  [>] {name}{versionInfo}", ConsoleColor.Green);
            }
            else
            {
                HostWriteLineSafe($"  [>] {m}", ConsoleColor.Green);
            }
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

            HostWriteLineSafe($"  [+] {m}", ConsoleColor.Green);
        }
    }

    private string ResolveTestPath(string projectRoot)
    {
        var path = string.IsNullOrWhiteSpace(TestPath) ? Path.Combine(projectRoot, "Tests") : TestPath!;
        path = Path.GetFullPath(path.Trim().Trim('"'));
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException($"Test path '{path}' does not exist", path);
        return path;
    }

    private object InvokePester(string testPath, ModuleTestSuiteOutputFormat outputFormat, string? coverageProjectRoot)
    {
        using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
        var script = @"
param(
  [string]$TestPath,
  [string]$OutputFormat,
  [bool]$EnableCodeCoverage,
  [string]$CoverageProjectRoot,
  [bool]$EnableExit
)

Import-Module -Name Pester -Force -ErrorAction Stop
$p = Get-Module -Name Pester
$useV5 = $p.Version.Major -ge 5

if ($useV5) {
  $Configuration = [PesterConfiguration]::Default
  $Configuration.Run.Path = $TestPath
  $Configuration.Run.Exit = $EnableExit
  $Configuration.Run.PassThru = $true
  $Configuration.Should.ErrorAction = 'Continue'
  $Configuration.CodeCoverage.Enabled = $EnableCodeCoverage
  switch ($OutputFormat) {
    'Detailed' { $Configuration.Output.Verbosity = 'Detailed' }
    'Normal'   { $Configuration.Output.Verbosity = 'Normal' }
    'Minimal'  { $Configuration.Output.Verbosity = 'Minimal' }
  }
  Invoke-Pester -Configuration $Configuration
} else {
  $PesterParams = @{
    Script   = $TestPath
    Verbose  = ($OutputFormat -eq 'Detailed')
    PassThru = $true
  }

  if ($OutputFormat -eq 'Detailed') {
    $PesterParams.OutputFormat = 'NUnitXml'
  }

  if ($EnableCodeCoverage -and $CoverageProjectRoot) {
    $ModuleFiles = Get-ChildItem -Path $CoverageProjectRoot -Filter '*.ps1' -Recurse | Where-Object { $_.Directory.Name -in @('Public', 'Private') }
    if ($ModuleFiles) { $PesterParams.CodeCoverage = $ModuleFiles.FullName }
  }

  Invoke-Pester @PesterParams
}
";
        ps.AddScript(script)
            .AddArgument(testPath)
            .AddArgument(outputFormat.ToString())
            .AddArgument(EnableCodeCoverage.IsPresent && !string.IsNullOrWhiteSpace(coverageProjectRoot))
            .AddArgument(coverageProjectRoot ?? string.Empty)
            .AddArgument(ExitOnFailure.IsPresent);

        var results = ps.Invoke();
        if (ps.HadErrors)
        {
            var msg = string.Join("; ", ps.Streams.Error.Select(e => e.Exception?.Message ?? e.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(msg)) msg = "Invoke-Pester failed";
            throw new InvalidOperationException(msg);
        }

        if (results.Count == 0)
            throw new InvalidOperationException("Invoke-Pester returned no results");

        return results[0].BaseObject ?? results[0];
    }

    private void InvokePrivateFunction(string functionName, Dictionary<string, object?> parameters)
    {
        var module = MyInvocation?.MyCommand?.Module;
        if (module is null)
            throw new InvalidOperationException("PSPublishModule module is not loaded. Cannot access internal functions.");

        var sb = ScriptBlock.Create(@"
param($fn, $params)
& $fn @params
");

        var res = module.Invoke(sb, functionName, parameters);

        if (res is IEnumerable enumerable && res is not string)
        {
            foreach (var item in enumerable)
                WriteVerbose(item?.ToString());
        }
    }

    private void InvokeGetModuleTestFailures(object testResults, ModuleTestSuiteFailureSummaryFormat format)
    {
        using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
        ps.AddCommand("Get-ModuleTestFailures")
            .AddParameter("TestResults", testResults)
            .AddParameter("OutputFormat", format.ToString());

        ps.Invoke();
    }

    private void EmitCiOutputs(TestCounts counts, object testResults, bool success, string? errorMessage)
    {
        if (!CICD.IsPresent)
            return;

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
        {
            HostWriteLineSafe($"::set-output name=test-result::{(success ? "true" : "false")}");
            HostWriteLineSafe($"::set-output name=total-tests::{counts.TotalCount}");
            HostWriteLineSafe($"::set-output name=failed-tests::{counts.FailedCount}");
            if (!success && !string.IsNullOrWhiteSpace(errorMessage))
                HostWriteLineSafe($"::set-output name=error-message::{errorMessage}");

            var coverage = GetCoveragePercentOrNull(testResults);
            if (coverage.HasValue)
                HostWriteLineSafe($"::set-output name=code-coverage::{coverage.Value.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TF_BUILD")))
        {
            HostWriteLineSafe($"##vso[task.setvariable variable=TestResult;isOutput=true]{(success ? "true" : "false")}");
            HostWriteLineSafe($"##vso[task.setvariable variable=TotalTests;isOutput=true]{counts.TotalCount}");
            HostWriteLineSafe($"##vso[task.setvariable variable=FailedTests;isOutput=true]{counts.FailedCount}");
            if (!success && !string.IsNullOrWhiteSpace(errorMessage))
                HostWriteLineSafe($"##vso[task.setvariable variable=ErrorMessage;isOutput=true]{errorMessage}");
        }
    }

    private static double? GetCoveragePercentOrNull(object testResults)
    {
        var ps = PSObject.AsPSObject(testResults);
        var cc = ps.Properties["CodeCoverage"]?.Value;
        if (cc is null) return null;

        var psCc = PSObject.AsPSObject(cc);
        var executed = GetNullableDouble(psCc, "NumberOfCommandsExecuted");
        var analyzed = GetNullableDouble(psCc, "NumberOfCommandsAnalyzed");
        if (!executed.HasValue || !analyzed.HasValue || analyzed.Value <= 0)
            return null;

        return Math.Round((executed.Value / analyzed.Value) * 100.0, 2);
    }

    private static double? GetNullableDouble(PSObject o, string name)
    {
        try
        {
            var v = o.Properties[name]?.Value;
            if (v is null) return null;
            return Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static TimeSpan? GetDurationOrNull(object testResults)
    {
        var ps = PSObject.AsPSObject(testResults);
        var v = ps.Properties["Time"]?.Value;
        return v is TimeSpan ts ? ts : null;
    }

    private static TestCounts GetCounts(object testResults)
    {
        var ps = PSObject.AsPSObject(testResults);

        var total = GetNullableInt(ps, "TotalCount");
        var passed = GetNullableInt(ps, "PassedCount");
        var failed = GetNullableInt(ps, "FailedCount");
        var skipped = GetNullableInt(ps, "SkippedCount");

        if (total.HasValue && passed.HasValue && failed.HasValue)
        {
            return new TestCounts(total.Value, passed.Value, failed.Value, skipped ?? 0);
        }

        var tests = ps.Properties["Tests"]?.Value as IEnumerable;
        if (tests is null)
            return new TestCounts(0, 0, 0, 0);

        var t = 0;
        var p = 0;
        var f = 0;
        var s = 0;
        foreach (var it in tests)
        {
            t++;
            var ti = PSObject.AsPSObject(it);
            var res = ti.Properties["Result"]?.Value?.ToString();
            if (string.Equals(res, "Passed", StringComparison.OrdinalIgnoreCase)) p++;
            else if (string.Equals(res, "Failed", StringComparison.OrdinalIgnoreCase)) f++;
            else if (string.Equals(res, "Skipped", StringComparison.OrdinalIgnoreCase)) s++;
        }
        return new TestCounts(t, p, f, s);
    }

    private static int? GetNullableInt(PSObject o, string name)
    {
        try
        {
            var v = o.Properties[name]?.Value;
            if (v is null) return null;
            return Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static string GetString(Hashtable ht, string key)
    {
        try
        {
            return ht.ContainsKey(key) ? (ht[key]?.ToString() ?? string.Empty) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static List<object?> GetEnumerable(Hashtable ht, string key)
    {
        var list = new List<object?>();
        try
        {
            if (!ht.ContainsKey(key))
                return list;

            var v = ht[key];
            if (v is null)
                return list;

            if (v is string)
            {
                list.Add(v);
                return list;
            }

            if (v is IEnumerable e)
            {
                foreach (var it in e) list.Add(it);
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

    private readonly struct TestCounts
    {
        public int TotalCount { get; }
        public int PassedCount { get; }
        public int FailedCount { get; }
        public int SkippedCount { get; }

        public TestCounts(int totalCount, int passedCount, int failedCount, int skippedCount)
        {
            TotalCount = totalCount;
            PassedCount = passedCount;
            FailedCount = failedCount;
            SkippedCount = skippedCount;
        }
    }
}
