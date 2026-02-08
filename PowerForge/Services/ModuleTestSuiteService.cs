using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

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

        var importModulesB64 = EncodeImportModules(spec.ImportModules);
        var args = new List<string>(11)
        {
            testPath,
            spec.OutputFormat.ToString(),
            spec.EnableCodeCoverage ? "1" : "0",
            coverageRoot,
            xmlPath,
            moduleName,
            manifestPath,
            spec.SkipImport ? "1" : "0",
            spec.Force ? "1" : "0",
            importModulesB64,
            spec.ImportModulesVerbose ? "1" : "0"
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

    private static string EncodeImportModules(IEnumerable<ModuleDependency>? modules)
    {
        var list = new List<ImportModuleEntry>();
        foreach (var m in modules ?? Array.Empty<ModuleDependency>())
        {
            if (m is null || string.IsNullOrWhiteSpace(m.Name)) continue;
            var minVersion = m.MinimumVersion;
            var reqVersion = m.RequiredVersion;
            list.Add(new ImportModuleEntry
            {
                Name = m.Name.Trim(),
                MinimumVersion = string.IsNullOrWhiteSpace(minVersion) ? null : minVersion!.Trim(),
                RequiredVersion = string.IsNullOrWhiteSpace(reqVersion) ? null : reqVersion!.Trim()
            });
        }

        if (list.Count == 0) return string.Empty;

        var json = JsonSerializer.Serialize(list);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private sealed class ImportModuleEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? MinimumVersion { get; set; }
        public string? RequiredVersion { get; set; }
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
        return EmbeddedScripts.Load("Scripts/Tests/Invoke-TestSuite.ps1");
    }
}
