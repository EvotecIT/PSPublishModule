using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class ModuleValidationService
{
    private static string CountLabel(int count, string singular, string plural)
        => $"{count} {(count == 1 ? singular : plural)}";

    private static ModuleValidationCheckResult? ValidateStructure(
        ModuleValidationSpec spec,
        ModuleStructureValidationSettings settings)
        => ModuleValidationCoreChecks.ValidateStructure(spec, settings);

    private ModuleValidationCheckResult? ValidateDocumentation(
        ModuleValidationSpec spec,
        DocumentationValidationSettings settings)
    {
        if (settings.Severity == ValidationSeverity.Off) return null;

        var issues = new List<string>();
        var summaryParts = new List<string>(6);

        var manifestPath = spec.ManifestPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            issues.Add("Manifest not found.");
            return BuildResult("Documentation", settings.Severity, issues, "manifest missing");
        }

        DocumentationExtractionPayload payload;
        try
        {
            var engine = new DocumentationEngine(_runner, _logger);
            payload = engine.ExtractHelpPayload(spec.StagingPath, manifestPath, TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));
        }
        catch (Exception ex)
        {
            issues.Add($"Help extraction failed: {ex.Message}");
            return BuildResult("Documentation", settings.Severity, issues, "extraction failed");
        }

        var excluded = new HashSet<string>(settings.ExcludeCommands ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var commands = (payload.Commands ?? new List<DocumentationCommandHelp>())
            .Where(c => !string.IsNullOrWhiteSpace(c?.Name) && !excluded.Contains(c!.Name))
            .ToArray();

        var total = commands.Length;
        if (total == 0)
        {
            issues.Add("No commands discovered for documentation validation.");
            return BuildResult("Documentation", settings.Severity, issues, "no commands");
        }

        int synopsisCount = 0;
        int descriptionCount = 0;
        int exampleCount = 0;
        int minExamplesMissing = 0;
        int parameterDescriptionCount = 0;
        int totalParameterCount = 0;
        int typeDescriptionCount = 0;
        int totalTypeCount = 0;
        var missingParameterDescriptions = new List<string>();
        var missingTypeDescriptions = new List<string>();
        var seenTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cmd in commands)
        {
            if (!string.IsNullOrWhiteSpace(cmd.Synopsis)) synopsisCount++;
            if (!string.IsNullOrWhiteSpace(cmd.Description)) descriptionCount++;

            var examples = cmd.Examples ?? new List<DocumentationExampleHelp>();
            var exampleTotal = examples.Count(e => !string.IsNullOrWhiteSpace(e.Code) || !string.IsNullOrWhiteSpace(e.Remarks));
            if (exampleTotal >= settings.MinExampleCountPerCommand)
                exampleCount++;
            else if (settings.MinExampleCountPerCommand > 0)
                minExamplesMissing++;

            foreach (var parameter in cmd.Parameters ?? new List<DocumentationParameterHelp>())
            {
                if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name)) continue;

                totalParameterCount++;
                if (!string.IsNullOrWhiteSpace(parameter.Description))
                {
                    parameterDescriptionCount++;
                }
                else if (settings.MinParameterDescriptionPercent > 0)
                {
                    missingParameterDescriptions.Add($"{cmd.Name}: parameter '{parameter.Name}' is missing description");
                }
            }

            foreach (var type in EnumerateDocumentableTypes(cmd))
            {
                var key = GetDocumentationTypeKey(type);
                if (string.IsNullOrWhiteSpace(key) || !seenTypes.Add(key)) continue;

                totalTypeCount++;
                if (!string.IsNullOrWhiteSpace(type.Description))
                {
                    typeDescriptionCount++;
                }
                else if (settings.MinTypeDescriptionPercent > 0)
                {
                    missingTypeDescriptions.Add($"{cmd.Name}: type '{GetDocumentationTypeDisplayName(type)}' is missing description");
                }
            }
        }

        var synopsisPercent = Percent(synopsisCount, total);
        var descriptionPercent = Percent(descriptionCount, total);
        var parameterDescriptionPercent = totalParameterCount == 0 ? 100.0 : Percent(parameterDescriptionCount, totalParameterCount);
        var typeDescriptionPercent = totalTypeCount == 0 ? 100.0 : Percent(typeDescriptionCount, totalTypeCount);

        summaryParts.Add($"synopsis {synopsisCount}/{total}");
        summaryParts.Add($"description {descriptionCount}/{total}");
        summaryParts.Add($"examples {exampleCount}/{total}");
        if (totalParameterCount > 0)
            summaryParts.Add($"parameter docs {parameterDescriptionCount}/{totalParameterCount}");
        if (totalTypeCount > 0)
            summaryParts.Add($"type docs {typeDescriptionCount}/{totalTypeCount}");

        if (synopsisPercent < settings.MinSynopsisPercent)
            issues.Add($"Synopsis coverage {synopsisPercent:0.0}% (< {settings.MinSynopsisPercent}%)");
        if (descriptionPercent < settings.MinDescriptionPercent)
            issues.Add($"Description coverage {descriptionPercent:0.0}% (< {settings.MinDescriptionPercent}%)");
        if (settings.MinExampleCountPerCommand > 0 && minExamplesMissing > 0)
            issues.Add($"{minExamplesMissing} command(s) missing required examples");
        if (settings.MinParameterDescriptionPercent > 0 && parameterDescriptionPercent < settings.MinParameterDescriptionPercent)
        {
            issues.Add($"Parameter description coverage {parameterDescriptionPercent:0.0}% (< {settings.MinParameterDescriptionPercent}%)");
            AppendIssues(issues, missingParameterDescriptions);
        }
        if (settings.MinTypeDescriptionPercent > 0 && typeDescriptionPercent < settings.MinTypeDescriptionPercent)
        {
            issues.Add($"Type description coverage {typeDescriptionPercent:0.0}% (< {settings.MinTypeDescriptionPercent}%)");
            AppendIssues(issues, missingTypeDescriptions);
        }

        var summary = string.Join(", ", summaryParts);
        return BuildResult("Documentation", settings.Severity, issues, summary);
    }

    private ModuleValidationCheckResult? ValidateScriptAnalyzer(
        ModuleValidationSpec spec,
        ScriptAnalyzerValidationSettings settings)
    {
        if (settings.Severity == ValidationSeverity.Off || !settings.Enable) return null;

        var issues = new List<string>();
        var moduleRoot = ResolveModuleRoot(spec);
        var scripts = EnumerateFiles(moduleRoot, "*.ps1", settings.ExcludeDirectories).ToArray();
        if (scripts.Length == 0)
            return BuildResult("PSScriptAnalyzer", settings.Severity, issues, "no scripts");

        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "validation");
        Directory.CreateDirectory(tempDir);
        var id = Guid.NewGuid().ToString("N");
        var scriptPath = Path.Combine(tempDir, $"pssa_{id}.ps1");
        var jsonPath = Path.Combine(tempDir, $"pssa_{id}.json");

        if (settings.InstallIfUnavailable)
        {
            try
            {
                var installResults = _ensureRuntimeDependencies(
                    new[] { new ModuleDependency("PSScriptAnalyzer") },
                    new RuntimeToolDependencyOptions
                    {
                        TimeoutPerModule = TimeSpan.FromMinutes(5)
                    });
                var failures = installResults
                    .Where(r => r.Status == ModuleDependencyInstallStatus.Failed)
                    .ToArray();
                if (failures.Length > 0)
                {
                    var messages = failures
                        .Select(f => string.IsNullOrWhiteSpace(f.Message) ? f.Name : $"{f.Name}: {f.Message}")
                        .ToArray();
                    if (!settings.SkipIfUnavailable)
                    {
                        issues.Add($"PSScriptAnalyzer install failed: {string.Join("; ", messages)}");
                        return BuildResult("PSScriptAnalyzer", settings.Severity, issues, "install failed");
                    }

                    _logger.Warn($"PSScriptAnalyzer install failed; continuing with SkipIfUnavailable enabled. {string.Join("; ", messages)}");
                }
            }
            catch (Exception ex)
            {
                if (!settings.SkipIfUnavailable)
                {
                    issues.Add($"PSScriptAnalyzer install failed: {ex.Message}");
                    return BuildResult("PSScriptAnalyzer", settings.Severity, issues, "install failed");
                }

                _logger.Warn($"PSScriptAnalyzer install failed; continuing with SkipIfUnavailable enabled. {ex.Message}");
            }
        }

        File.WriteAllText(scriptPath, BuildScriptAnalyzerScript(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        try
        {
            var args = new[]
            {
                EncodeLines(scripts),
                EncodeLines(settings.ExcludeRules ?? Array.Empty<string>()),
                jsonPath,
                settings.SkipIfUnavailable ? "1" : "0"
            };

            var timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
            var result = _runner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh: true));

            if (result.ExitCode != 0)
            {
                var msg = ExtractMarker(result.StdOut, "PFVALID::ERROR::") ?? result.StdErr;
                issues.Add(string.IsNullOrWhiteSpace(msg) ? "PSScriptAnalyzer failed." : msg.Trim());
                return BuildResult("PSScriptAnalyzer", settings.Severity, issues, "failed");
            }

            // Check PSSA-CONFLICT first: the more specific marker contains PSSA as a substring.
            if (result.StdOut.Contains("PFVALID::SKIP::PSSA-CONFLICT", StringComparison.OrdinalIgnoreCase))
            {
                if (settings.SkipIfUnavailable)
                    return BuildResult("PSScriptAnalyzer", settings.Severity, issues, "skipped (assembly conflict)");

                issues.Add("PSScriptAnalyzer import conflicted with an assembly that was already loaded.");
                return BuildResult("PSScriptAnalyzer", settings.Severity, issues, "assembly conflict");
            }

            if (result.StdOut.Contains("PFVALID::SKIP::PSSA", StringComparison.OrdinalIgnoreCase))
            {
                if (settings.SkipIfUnavailable)
                    return BuildResult("PSScriptAnalyzer", settings.Severity, issues, "skipped (not installed)");

                issues.Add("PSScriptAnalyzer not found.");
                return BuildResult("PSScriptAnalyzer", settings.Severity, issues, "missing");
            }

            if (!File.Exists(jsonPath))
            {
                var msg = ExtractMarker(result.StdOut, "PFVALID::ERROR::") ?? result.StdErr;
                var detailParts = new List<string>(4);
                if (!string.IsNullOrWhiteSpace(msg))
                    detailParts.Add(msg.Trim());
                if (!string.IsNullOrWhiteSpace(result.Executable))
                    detailParts.Add($"runner={Path.GetFileName(result.Executable)}");
                if (!string.IsNullOrWhiteSpace(result.StdOut))
                    detailParts.Add($"stdout={TrimForIssue(result.StdOut)}");
                if (!string.IsNullOrWhiteSpace(result.StdErr))
                    detailParts.Add($"stderr={TrimForIssue(result.StdErr)}");

                issues.Add(detailParts.Count == 0
                    ? "PSScriptAnalyzer runner completed without writing the results file."
                    : $"PSScriptAnalyzer runner completed without writing the results file. {string.Join("; ", detailParts)}");
                return BuildResult("PSScriptAnalyzer", settings.Severity, issues, "no output");
            }

            var json = File.ReadAllText(jsonPath);
            var parsed = JsonSerializer.Deserialize<ScriptAnalyzerIssue[]>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Array.Empty<ScriptAnalyzerIssue>();

            if (parsed.Length == 0)
                return BuildResult("PSScriptAnalyzer", settings.Severity, issues, $"Checked {CountLabel(scripts.Length, "script", "scripts")}, found 0 issues");

            foreach (var i in parsed.Take(25))
            {
                var location = string.IsNullOrWhiteSpace(i.ScriptPath)
                    ? string.Empty
                    : $"{Path.GetFileName(i.ScriptPath)}:{i.Line}:{i.Column}";
                var line = $"{i.RuleName} {i.Severity} {location}".Trim();
                if (!string.IsNullOrWhiteSpace(i.Message))
                    line += $" - {i.Message}";
                issues.Add(line.Trim());
            }

            var summary = $"Checked {CountLabel(scripts.Length, "script", "scripts")}, found {CountLabel(parsed.Length, "issue", "issues")}";
            return BuildResult("PSScriptAnalyzer", settings.Severity, issues, summary);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
            try { File.Delete(jsonPath); } catch { /* ignore */ }
        }
    }

    private static ModuleValidationCheckResult? ValidateFileIntegrity(
        ModuleValidationSpec spec,
        FileIntegrityValidationSettings settings)
    {
        if (settings.Severity == ValidationSeverity.Off) return null;

        var issues = new List<string>();
        var moduleRoot = ResolveModuleRoot(spec);

        var files = EnumerateFiles(moduleRoot, "*.ps1", settings.ExcludeDirectories)
            .Concat(EnumerateFiles(moduleRoot, "*.help.txt", settings.ExcludeDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
            return BuildResult("File integrity", settings.Severity, issues, "no files");

        var banned = new HashSet<string>(settings.BannedCommands ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var allowList = new HashSet<string>(settings.AllowBannedCommandsIn ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var rel = ProjectTextDetector.ComputeRelativePath(moduleRoot, file);

            if (settings.CheckTrailingWhitespace)
            {
                int lineNo = 0;
                foreach (var line in ReadLines(file))
                {
                    lineNo++;
                    if (line.EndsWith(" ", StringComparison.Ordinal) || line.EndsWith("\t", StringComparison.Ordinal))
                    {
                        issues.Add($"[{rel}] trailing whitespace at line {lineNo}");
                        break;
                    }
                }
            }

            if (settings.CheckSyntax && file.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Token[] tokens;
                    ParseError[] errors;
                    Parser.ParseFile(file, out tokens, out errors);
                    if (errors is { Length: > 0 })
                        issues.Add($"[{rel}] syntax errors: {errors.Length}");
                }
                catch (Exception ex)
                {
                    issues.Add($"[{rel}] syntax check failed: {ex.Message}");
                }
            }

            if (banned.Count > 0 && !allowList.Contains(Path.GetFileName(file)))
            {
                try
                {
                    Token[] tokens;
                    ParseError[] errors;
                    Parser.ParseFile(file, out tokens, out errors);
                    foreach (var cmd in banned)
                    {
                        if (tokens.Any(t => string.Equals(t.Text, cmd, StringComparison.OrdinalIgnoreCase)))
                        {
                            issues.Add($"[{rel}] banned command: {cmd}");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    issues.Add($"[{rel}] token scan failed: {ex.Message}");
                }
            }
        }

        var summary = issues.Count == 0
            ? $"Checked {CountLabel(files.Length, "file", "files")}, found 0 issues"
            : $"Checked {CountLabel(files.Length, "file", "files")}, found {CountLabel(issues.Count, "issue", "issues")}";
        return BuildResult("File integrity", settings.Severity, issues, summary);
    }

    private ModuleValidationCheckResult? ValidateTests(
        ModuleValidationSpec spec,
        TestSuiteValidationSettings settings)
    {
        if (settings.Severity == ValidationSeverity.Off || !settings.Enable) return null;

        var issues = new List<string>();
        string summary;

        try
        {
            var service = new ModuleTestSuiteService(_runner, _logger);
            var result = service.Run(new ModuleTestSuiteSpec
            {
                ProjectPath = spec.ProjectRoot,
                TestPath = settings.TestPath,
                AdditionalModules = settings.AdditionalModules ?? Array.Empty<string>(),
                SkipModules = settings.SkipModules ?? Array.Empty<string>(),
                OutputFormat = ModuleTestSuiteOutputFormat.Normal,
                EnableCodeCoverage = false,
                Force = settings.Force,
                SkipDependencies = settings.SkipDependencies,
                SkipImport = settings.SkipImport,
                KeepResultsXml = false,
                PreferPwsh = true,
                TimeoutSeconds = Math.Max(1, settings.TimeoutSeconds)
            });

            summary = $"tests {result.PassedCount}/{result.TotalCount} passed";
            if (result.FailedCount > 0)
                issues.Add($"{result.FailedCount} test(s) failed");
        }
        catch (Exception ex)
        {
            summary = "test run failed";
            issues.Add(ex.Message);
        }

        return BuildResult("Functionality tests", settings.Severity, issues, summary);
    }

    private static ModuleValidationCheckResult? ValidateBinary(
        ModuleValidationSpec spec,
        BinaryModuleValidationSettings settings)
        => ModuleValidationCoreChecks.ValidateBinary(spec, settings);

    private static ModuleValidationCheckResult? ValidateCsproj(
        ModuleValidationSpec spec,
        CsprojValidationSettings settings)
        => ModuleValidationCoreChecks.ValidateCsproj(spec, settings);

}
