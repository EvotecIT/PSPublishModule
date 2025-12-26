using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>
/// Runs a full module test suite (dependency installation + optional import + Pester execution) in a child PowerShell process.
/// </summary>
public sealed class ModuleTestSuiteService
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new service using the provided runner and logger.
    /// </summary>
    public ModuleTestSuiteService(IPowerShellRunner runner, ILogger logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the test suite according to <paramref name="spec"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when required values are missing.</exception>
    /// <exception cref="InvalidOperationException">Thrown when Pester cannot be executed.</exception>
    public ModuleTestSuiteResult Run(ModuleTestSuiteSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.ProjectPath))
            throw new ArgumentException("ProjectPath is required.", nameof(spec));

        var projectRoot = Path.GetFullPath(spec.ProjectPath.Trim().Trim('"'));

        var reader = new ModuleInformationReader();
        var moduleInfo = reader.Read(projectRoot);

        var moduleName = moduleInfo.ModuleName;
        var moduleVersion = moduleInfo.ModuleVersion;
        var manifestPath = moduleInfo.ManifestPath;
        var requiredModules = moduleInfo.RequiredModules ?? Array.Empty<ManifestEditor.RequiredModule>();

        var testPath = ResolveTestPath(projectRoot, spec.TestPath);

        var dependencyResults = Array.Empty<ModuleDependencyInstallResult>();
        if (!spec.SkipDependencies)
        {
            dependencyResults = EnsureDependenciesInstalled(spec, requiredModules);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "testsuite");
        Directory.CreateDirectory(tempDir);

        var runId = Guid.NewGuid().ToString("N");
        var scriptPath = Path.Combine(tempDir, $"testsuite_{runId}.ps1");
        var xmlPath = Path.Combine(tempDir, $"pester_{runId}.xml");

        var script = BuildTestSuiteScript();
        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var timeout = TimeSpan.FromSeconds(Math.Max(1, spec.TimeoutSeconds));
        var coverageRoot = spec.EnableCodeCoverage && string.IsNullOrWhiteSpace(spec.TestPath)
            ? projectRoot
            : string.Empty;

        var args = new List<string>(9)
        {
            testPath,
            spec.OutputFormat.ToString(),
            spec.EnableCodeCoverage ? "1" : "0",
            coverageRoot,
            xmlPath,
            moduleName,
            manifestPath,
            spec.SkipImport ? "1" : "0",
            spec.Force ? "1" : "0"
        };

        PowerShellRunResult runResult;
        try
        {
            runResult = _runner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh: spec.PreferPwsh));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
        }

        var markers = ParseMarkers(runResult.StdOut);
        var sanitizedStdOut = StripMarkerLines(runResult.StdOut);

        ModuleTestFailureAnalysis? failureAnalysis = null;
        if (File.Exists(xmlPath))
        {
            try
            {
                failureAnalysis = new ModuleTestFailureAnalyzer().AnalyzeFromXmlFile(xmlPath);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to parse NUnit XML test results: {ex.Message}");
            }
        }

        if (failureAnalysis is not null && !spec.KeepResultsXml)
        {
            failureAnalysis.Source = "NUnitXml (temp)";
        }

        var total = markers.TotalCount ?? failureAnalysis?.TotalCount ?? 0;
        var passed = markers.PassedCount ?? failureAnalysis?.PassedCount ?? 0;
        var failed = markers.FailedCount ?? failureAnalysis?.FailedCount ?? 0;
        var skipped = markers.SkippedCount ?? failureAnalysis?.SkippedCount ?? 0;

        if (runResult.ExitCode != 0 && total == 0 && failureAnalysis is null)
        {
            var msg = markers.ErrorMessage;
            if (string.IsNullOrWhiteSpace(msg)) msg = runResult.StdErr;
            if (string.IsNullOrWhiteSpace(msg)) msg = "Invoke-Pester failed";
            throw new InvalidOperationException(msg!.Trim());
        }

        string? keptXmlPath = null;
        if (spec.KeepResultsXml)
        {
            keptXmlPath = File.Exists(xmlPath) ? xmlPath : null;
        }
        else
        {
            try { File.Delete(xmlPath); } catch { /* ignore */ }
        }

        return new ModuleTestSuiteResult(
            projectPath: projectRoot,
            testPath: testPath,
            moduleName: moduleName,
            moduleVersion: moduleVersion,
            manifestPath: manifestPath,
            requiredModules: requiredModules,
            dependencyResults: dependencyResults,
            moduleImported: markers.ModuleImported,
            exportedFunctionCount: markers.ExportedFunctions,
            exportedCmdletCount: markers.ExportedCmdlets,
            exportedAliasCount: markers.ExportedAliases,
            pesterVersion: markers.PesterVersion,
            totalCount: total,
            passedCount: passed,
            failedCount: failed,
            skippedCount: skipped,
            duration: markers.Duration,
            coveragePercent: markers.CoveragePercent,
            failureAnalysis: failureAnalysis,
            exitCode: runResult.ExitCode,
            stdOut: sanitizedStdOut,
            stdErr: runResult.StdErr,
            resultsXmlPath: keptXmlPath);
    }

    private ModuleDependencyInstallResult[] EnsureDependenciesInstalled(
        ModuleTestSuiteSpec spec,
        ManifestEditor.RequiredModule[] requiredModules)
    {
        var deps = new List<ModuleDependency>();

        foreach (var name in spec.AdditionalModules ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(name))
                deps.Add(new ModuleDependency(name.Trim()));
        }

        foreach (var m in requiredModules ?? Array.Empty<ManifestEditor.RequiredModule>())
        {
            if (string.IsNullOrWhiteSpace(m.ModuleName))
                continue;

            deps.Add(new ModuleDependency(
                name: m.ModuleName.Trim(),
                requiredVersion: m.RequiredVersion,
                minimumVersion: m.ModuleVersion,
                maximumVersion: m.MaximumVersion));
        }

        if (deps.Count == 0)
            return Array.Empty<ModuleDependencyInstallResult>();

        var installer = new ModuleDependencyInstaller(_runner, _logger);
        var results = installer.EnsureInstalled(
            dependencies: deps,
            skipModules: spec.SkipModules,
            force: spec.Force);

        var failures = results.Where(r => r.Status == ModuleDependencyInstallStatus.Failed).ToArray();
        if (failures.Length > 0)
            throw new InvalidOperationException($"Dependency installation failed for {failures.Length} module{(failures.Length == 1 ? string.Empty : "s")}.");

        return results.ToArray();
    }

    private static string ResolveTestPath(string projectRoot, string? testPath)
    {
        var p = string.IsNullOrWhiteSpace(testPath) ? Path.Combine(projectRoot, "Tests") : testPath!;
        p = Path.GetFullPath(p.Trim().Trim('"'));
        if (!File.Exists(p) && !Directory.Exists(p))
            throw new FileNotFoundException($"Test path '{p}' does not exist", p);
        return p;
    }

    private sealed class MarkerBag
    {
        public string? PesterVersion { get; set; }
        public int? TotalCount { get; set; }
        public int? PassedCount { get; set; }
        public int? FailedCount { get; set; }
        public int? SkippedCount { get; set; }
        public TimeSpan? Duration { get; set; }
        public double? CoveragePercent { get; set; }
        public bool ModuleImported { get; set; }
        public int? ExportedFunctions { get; set; }
        public int? ExportedCmdlets { get; set; }
        public int? ExportedAliases { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private static MarkerBag ParseMarkers(string stdout)
    {
        string? pesterVersion = null;
        int? total = null;
        int? passed = null;
        int? failed = null;
        int? skipped = null;
        TimeSpan? duration = null;
        double? coverage = null;
        var imported = false;
        int? expFunc = null;
        int? expCmdlet = null;
        int? expAlias = null;
        string? error = null;

        foreach (var line in SplitLines(stdout))
        {
            if (line.StartsWith("PFTEST::PESTER::", StringComparison.Ordinal))
            {
                pesterVersion = line.Substring("PFTEST::PESTER::".Length);
                continue;
            }

            if (line.StartsWith("PFTEST::COUNTS::", StringComparison.Ordinal))
            {
                var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
                if (parts.Length >= 6)
                {
                    total = TryParseInt(parts[2]);
                    passed = TryParseInt(parts[3]);
                    failed = TryParseInt(parts[4]);
                    skipped = TryParseInt(parts[5]);
                }
                continue;
            }

            if (line.StartsWith("PFTEST::DURATION::", StringComparison.Ordinal))
            {
                var t = line.Substring("PFTEST::DURATION::".Length);
                if (TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out var ts))
                    duration = ts;
                continue;
            }

            if (line.StartsWith("PFTEST::COVERAGE::", StringComparison.Ordinal))
            {
                var v = line.Substring("PFTEST::COVERAGE::".Length);
                if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    coverage = d;
                continue;
            }

            if (line.Equals("PFTEST::IMPORT::OK", StringComparison.Ordinal))
            {
                imported = true;
                continue;
            }

            if (line.StartsWith("PFTEST::EXPORTS::", StringComparison.Ordinal))
            {
                var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
                if (parts.Length >= 5)
                {
                    expFunc = TryParseInt(parts[2]);
                    expCmdlet = TryParseInt(parts[3]);
                    expAlias = TryParseInt(parts[4]);
                }
                continue;
            }

            if (line.StartsWith("PFTEST::ERROR::", StringComparison.Ordinal))
            {
                var b64 = line.Substring("PFTEST::ERROR::".Length);
                var msg = Decode(b64);
                error = string.IsNullOrWhiteSpace(msg) ? null : msg;
            }
        }

        return new MarkerBag
        {
            PesterVersion = pesterVersion,
            TotalCount = total,
            PassedCount = passed,
            FailedCount = failed,
            SkippedCount = skipped,
            Duration = duration,
            CoveragePercent = coverage,
            ModuleImported = imported,
            ExportedFunctions = expFunc,
            ExportedCmdlets = expCmdlet,
            ExportedAliases = expAlias,
            ErrorMessage = error
        };
    }

    private static string StripMarkerLines(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var line in SplitLinesPreserveEmpty(stdout))
        {
            if (line.StartsWith("PFTEST::", StringComparison.Ordinal))
                continue;

            sb.AppendLine(line);
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<string> SplitLinesPreserveEmpty(string text)
    {
        if (text is null) yield break;
        using var sr = new StringReader(text);
        while (true)
        {
            var line = sr.ReadLine();
            if (line is null) yield break;
            yield return line;
        }
    }

    private static int? TryParseInt(string? s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static string Decode(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return string.Empty;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return string.Empty; }
    }

    private static string BuildTestSuiteScript()
    {
        return @"
param(
  [string]$TestPath,
  [string]$OutputFormat,
  [string]$EnableCodeCoverage,
  [string]$CoverageProjectRoot,
  [string]$ResultsPath,
  [string]$ModuleName,
  [string]$ModuleImportPath,
  [string]$SkipImport,
  [string]$ForceImport
)

function Encode([string]$s) {
  if ([string]::IsNullOrWhiteSpace($s)) { return '' }
  return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s))
}

try {
  Import-Module -Name Pester -Force -ErrorAction Stop
  $p = Get-Module -Name Pester
  if ($p -and $p.Version) {
    Write-Output ('PFTEST::PESTER::' + $p.Version.ToString())
  }

  $doImport = ($SkipImport -ne '1') -and (-not [string]::IsNullOrWhiteSpace($ModuleImportPath))
  if ($doImport) {
    Import-Module -Name $ModuleImportPath -Force:($ForceImport -eq '1') -ErrorAction Stop | Out-Null
    Write-Output 'PFTEST::IMPORT::OK'
    try {
      $m = Get-Module -Name $ModuleName | Select-Object -First 1
      if ($m) {
        $funcCount = 0
        $cmdletCount = 0
        $aliasCount = 0
        try { $funcCount = ($m.ExportedFunctions.Keys | Measure-Object).Count } catch { }
        try { $cmdletCount = ($m.ExportedCmdlets.Keys | Measure-Object).Count } catch { }
        try { $aliasCount = ($m.ExportedAliases.Keys | Measure-Object).Count } catch { }
        Write-Output ('PFTEST::EXPORTS::' + $funcCount + '::' + $cmdletCount + '::' + $aliasCount)
      }
    } catch { }
  }

  $enableCoverage = ($EnableCodeCoverage -eq '1')
  $useCoveragePath = $enableCoverage -and (-not [string]::IsNullOrWhiteSpace($CoverageProjectRoot))

  $r = $null
  if ($p -and $p.Version -and $p.Version.Major -ge 5) {
    $Configuration = [PesterConfiguration]::Default
    $Configuration.Run.Path = $TestPath
    $Configuration.Run.Exit = $false
    $Configuration.Run.PassThru = $true
    $Configuration.Should.ErrorAction = 'Continue'

    $Configuration.TestResult.Enabled = $true
    $Configuration.TestResult.OutputPath = $ResultsPath
    $Configuration.TestResult.OutputFormat = 'NUnitXml'

    $Configuration.CodeCoverage.Enabled = $enableCoverage
    if ($useCoveragePath) {
      $files = Get-ChildItem -Path $CoverageProjectRoot -Filter '*.ps1' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -in @('Public', 'Private') }
      if ($files) { $Configuration.CodeCoverage.Path = $files.FullName }
    }

    switch ($OutputFormat) {
      'Detailed' { $Configuration.Output.Verbosity = 'Detailed' }
      'Normal'   { $Configuration.Output.Verbosity = 'Normal' }
      'Minimal'  { $Configuration.Output.Verbosity = 'Minimal' }
    }

    $r = Invoke-Pester -Configuration $Configuration
  } else {
    $PesterParams = @{
      Script      = $TestPath
      PassThru    = $true
      OutputFormat = 'NUnitXml'
      OutputFile  = $ResultsPath
      Verbose     = ($OutputFormat -eq 'Detailed')
    }

    if ($useCoveragePath) {
      $files = Get-ChildItem -Path $CoverageProjectRoot -Filter '*.ps1' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -in @('Public', 'Private') }
      if ($files) { $PesterParams.CodeCoverage = $files.FullName }
    }

    $r = Invoke-Pester @PesterParams
  }

  if (-not $r) { throw 'Invoke-Pester returned no results' }

  $total = $r.TotalCount
  $passed = $r.PassedCount
  $failed = $r.FailedCount
  $skipped = $r.SkippedCount

  if ($null -eq $total -or $null -eq $passed -or $null -eq $failed) {
    $total = 0; $passed = 0; $failed = 0; $skipped = 0
    foreach ($t in $r.Tests) {
      $total++
      $res = $t.Result
      if ($res -eq 'Passed') { $passed++ }
      elseif ($res -eq 'Failed') { $failed++ }
      elseif ($res -eq 'Skipped') { $skipped++ }
    }
  }

  Write-Output ('PFTEST::COUNTS::' + $total + '::' + $passed + '::' + $failed + '::' + $skipped)

  try {
    if ($r.Time) { Write-Output ('PFTEST::DURATION::' + $r.Time.ToString()) }
  } catch { }

  try {
    if ($r.CodeCoverage) {
      $exec = [double]$r.CodeCoverage.NumberOfCommandsExecuted
      $an = [double]$r.CodeCoverage.NumberOfCommandsAnalyzed
      if ($an -gt 0) {
        $pct = [Math]::Round(($exec / $an) * 100.0, 2)
        Write-Output ('PFTEST::COVERAGE::' + $pct.ToString('0.00', [CultureInfo]::InvariantCulture))
      }
    }
  } catch { }

  exit 0
} catch {
  $m = $_.Exception.Message
  if ([string]::IsNullOrWhiteSpace($m)) { $m = $_.ToString() }
  Write-Output ('PFTEST::ERROR::' + (Encode $m))
  exit 2
}
";
    }
}
