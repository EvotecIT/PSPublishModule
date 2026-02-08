using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class ModuleValidationService
{
    private static ModuleValidationCheckResult? ValidateStructure(
        ModuleValidationSpec spec,
        ModuleStructureValidationSettings settings)
    {
        if (settings.Severity == ValidationSeverity.Off) return null;

        var issues = new List<string>();
        var summaryParts = new List<string>(4);

        var manifestPath = spec.ManifestPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            issues.Add("Manifest not found.");
            return BuildResult("Module structure", settings.Severity, issues, "manifest missing");
        }

        var moduleRoot = string.IsNullOrWhiteSpace(spec.StagingPath)
            ? Path.GetDirectoryName(manifestPath) ?? string.Empty
            : spec.StagingPath;

        var publicFunctions = DiscoverFunctionFileNames(moduleRoot, settings.PublicFunctionPaths);
        if (publicFunctions.Count > 0)
            summaryParts.Add($"public functions {publicFunctions.Count}");

        var internalFunctions = DiscoverFunctionFileNames(moduleRoot, settings.InternalFunctionPaths);
        if (internalFunctions.Count > 0)
            summaryParts.Add($"internal functions {internalFunctions.Count}");

        if (settings.ValidateExports)
        {
            var (exportedFunctions, wildcardExport) = GetManifestStringArray(manifestPath, "FunctionsToExport");
            if (wildcardExport && !settings.AllowWildcardExports)
            {
                issues.Add("FunctionsToExport uses wildcard; cannot validate exports.");
            }
            else if (exportedFunctions is { Length: > 0 } && !wildcardExport)
            {
                var exportedSet = new HashSet<string>(exportedFunctions, StringComparer.OrdinalIgnoreCase);
                summaryParts.Add($"exports {exportedSet.Count}");

                var missing = publicFunctions.Where(f => !exportedSet.Contains(f)).ToArray();
                var extra = exportedSet.Where(f => !publicFunctions.Contains(f)).ToArray();

                if (missing.Length > 0)
                    issues.Add($"Public functions not exported: {FormatList(missing)}");
                if (extra.Length > 0)
                    issues.Add($"Exports not found in public folder: {FormatList(extra)}");
            }
        }

        if (settings.ValidateInternalNotExported && internalFunctions.Count > 0)
        {
            var (exportedFunctions, wildcardExport) = GetManifestStringArray(manifestPath, "FunctionsToExport");
            if (exportedFunctions is { Length: > 0 } && !wildcardExport)
            {
                var exportedSet = new HashSet<string>(exportedFunctions, StringComparer.OrdinalIgnoreCase);
                var leaked = internalFunctions.Where(f => exportedSet.Contains(f)).ToArray();
                if (leaked.Length > 0)
                    issues.Add($"Internal functions exported: {FormatList(leaked)}");
            }
        }

        if (settings.ValidateManifestFiles)
        {
            if (ManifestEditor.TryGetTopLevelString(manifestPath, "RootModule", out var root) &&
                !string.IsNullOrWhiteSpace(root))
            {
                if (!File.Exists(Path.Combine(moduleRoot, root)))
                    issues.Add($"RootModule missing: {root}");
            }

            if (ManifestEditor.TryGetTopLevelStringArray(manifestPath, "FormatsToProcess", out var formats) &&
                formats is { Length: > 0 })
            {
                foreach (var f in formats)
                {
                    if (string.IsNullOrWhiteSpace(f)) continue;
                    if (!File.Exists(Path.Combine(moduleRoot, f)))
                        issues.Add($"Format file missing: {f}");
                }
            }

            if (ManifestEditor.TryGetTopLevelStringArray(manifestPath, "TypesToProcess", out var types) &&
                types is { Length: > 0 })
            {
                foreach (var t in types)
                {
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    if (!File.Exists(Path.Combine(moduleRoot, t)))
                        issues.Add($"Type file missing: {t}");
                }
            }

            if (ManifestEditor.TryGetTopLevelStringArray(manifestPath, "RequiredAssemblies", out var assemblies) &&
                assemblies is { Length: > 0 })
            {
                foreach (var a in assemblies)
                {
                    if (string.IsNullOrWhiteSpace(a)) continue;
                    if (a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!File.Exists(Path.Combine(moduleRoot, a)))
                            issues.Add($"Required assembly missing: {a}");
                    }
                    else
                    {
                        if (!TryLoadAssembly(a, out var error))
                            issues.Add($"Required assembly failed to load: {a} ({error})");
                    }
                }
            }
        }

        var summary = summaryParts.Count == 0 ? "ok" : string.Join(", ", summaryParts);
        return BuildResult("Module structure", settings.Severity, issues, summary);
    }

    private ModuleValidationCheckResult? ValidateDocumentation(
        ModuleValidationSpec spec,
        DocumentationValidationSettings settings)
    {
        if (settings.Severity == ValidationSeverity.Off) return null;

        var issues = new List<string>();
        var summaryParts = new List<string>(4);

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
        }

        var synopsisPercent = Percent(synopsisCount, total);
        var descriptionPercent = Percent(descriptionCount, total);
        var examplesPercent = Percent(exampleCount, total);

        summaryParts.Add($"synopsis {synopsisCount}/{total}");
        summaryParts.Add($"description {descriptionCount}/{total}");
        summaryParts.Add($"examples {exampleCount}/{total}");

        if (synopsisPercent < settings.MinSynopsisPercent)
            issues.Add($"Synopsis coverage {synopsisPercent:0.0}% (< {settings.MinSynopsisPercent}%)");
        if (descriptionPercent < settings.MinDescriptionPercent)
            issues.Add($"Description coverage {descriptionPercent:0.0}% (< {settings.MinDescriptionPercent}%)");
        if (settings.MinExampleCountPerCommand > 0 && minExamplesMissing > 0)
            issues.Add($"{minExamplesMissing} command(s) missing required examples");

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
                if (string.IsNullOrWhiteSpace(msg))
                {
                    issues.Add("PSScriptAnalyzer produced no output.");
                }
                else
                {
                    issues.Add($"PSScriptAnalyzer produced no output. {msg.Trim()}");
                }
                return BuildResult("PSScriptAnalyzer", settings.Severity, issues, "no output");
            }

            var json = File.ReadAllText(jsonPath);
            var parsed = JsonSerializer.Deserialize<ScriptAnalyzerIssue[]>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Array.Empty<ScriptAnalyzerIssue>();

            if (parsed.Length == 0)
                return BuildResult("PSScriptAnalyzer", settings.Severity, issues, "ok");

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

            var summary = $"{parsed.Length} issue(s)";
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

        var summary = issues.Count == 0 ? "ok" : $"{issues.Count} issue(s)";
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
    {
        if (settings.Severity == ValidationSeverity.Off) return null;

        var issues = new List<string>();
        var summaryParts = new List<string>(3);
        var manifestPath = spec.ManifestPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            issues.Add("Manifest not found.");
            return BuildResult("Binary exports", settings.Severity, issues, "manifest missing");
        }

        var moduleRoot = string.IsNullOrWhiteSpace(spec.StagingPath)
            ? Path.GetDirectoryName(manifestPath) ?? string.Empty
            : spec.StagingPath;

        var assemblies = ResolveManifestAssemblies(manifestPath);
        var assemblyPaths = assemblies
            .Where(a => !string.IsNullOrWhiteSpace(a) && a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(a => Path.Combine(moduleRoot, a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (assemblyPaths.Length == 0)
            return BuildResult("Binary exports", settings.Severity, issues, "no binaries");

        if (settings.ValidateAssembliesExist)
        {
            foreach (var asm in assemblyPaths)
            {
                if (!File.Exists(asm))
                    issues.Add($"Assembly missing: {Path.GetFileName(asm)}");
            }
        }

        var existingAssemblies = assemblyPaths.Where(File.Exists).ToArray();
        if (existingAssemblies.Length == 0)
            return BuildResult("Binary exports", settings.Severity, issues, "no binaries");

        if (settings.ValidateManifestExports)
        {
            var detectedCmdlets = ExportDetector.DetectBinaryCmdlets(existingAssemblies);
            var detectedAliases = ExportDetector.DetectBinaryAliases(existingAssemblies);

            var (manifestCmdlets, cmdletWildcard) = GetManifestStringArray(manifestPath, "CmdletsToExport");
            var (manifestAliases, aliasWildcard) = GetManifestStringArray(manifestPath, "AliasesToExport");

            if (cmdletWildcard && !settings.AllowWildcardExports)
                issues.Add("CmdletsToExport uses wildcard; cannot validate binary exports.");
            else if (manifestCmdlets is { Length: > 0 } && !cmdletWildcard)
            {
                var cmdletSet = new HashSet<string>(manifestCmdlets, StringComparer.OrdinalIgnoreCase);
                var missing = detectedCmdlets.Where(c => !cmdletSet.Contains(c)).ToArray();
                var extra = cmdletSet.Where(c => !detectedCmdlets.Contains(c)).ToArray();
                if (missing.Length > 0)
                    issues.Add($"Binary cmdlets not exported: {FormatList(missing)}");
                if (extra.Length > 0)
                    issues.Add($"Manifest cmdlets missing from binaries: {FormatList(extra)}");
            }

            if (aliasWildcard && !settings.AllowWildcardExports)
                issues.Add("AliasesToExport uses wildcard; cannot validate binary exports.");
            else if (manifestAliases is { Length: > 0 } && !aliasWildcard)
            {
                var aliasSet = new HashSet<string>(manifestAliases, StringComparer.OrdinalIgnoreCase);
                var missing = detectedAliases.Where(a => !aliasSet.Contains(a)).ToArray();
                var extra = aliasSet.Where(a => !detectedAliases.Contains(a)).ToArray();
                if (missing.Length > 0)
                    issues.Add($"Binary aliases not exported: {FormatList(missing)}");
                if (extra.Length > 0)
                    issues.Add($"Manifest aliases missing from binaries: {FormatList(extra)}");
            }

            summaryParts.Add($"cmdlets {detectedCmdlets.Count}");
            summaryParts.Add($"aliases {detectedAliases.Count}");
        }

        var summary = summaryParts.Count == 0 ? "ok" : string.Join(", ", summaryParts);
        return BuildResult("Binary exports", settings.Severity, issues, summary);
    }

    private static ModuleValidationCheckResult? ValidateCsproj(
        ModuleValidationSpec spec,
        CsprojValidationSettings settings)
    {
        if (settings.Severity == ValidationSeverity.Off) return null;

        var issues = new List<string>();
        var summaryParts = new List<string>(2);

        var csproj = spec.BuildSpec?.CsprojPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(csproj))
        {
            issues.Add("CsprojPath is not configured.");
            return BuildResult("Csproj", settings.Severity, issues, "missing csproj");
        }

        var csprojPath = Path.IsPathRooted(csproj)
            ? csproj
            : Path.GetFullPath(Path.Combine(spec.ProjectRoot ?? string.Empty, csproj));

        if (!File.Exists(csprojPath))
        {
            issues.Add($"Csproj not found: {csproj}");
            return BuildResult("Csproj", settings.Severity, issues, "missing csproj");
        }

        try
        {
            var doc = XDocument.Load(csprojPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            string? targetFramework = doc.Descendants(ns + "TargetFramework").Select(e => e.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            string? targetFrameworks = doc.Descendants(ns + "TargetFrameworks").Select(e => e.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            string? outputType = doc.Descendants(ns + "OutputType").Select(e => e.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            if (settings.RequireTargetFramework && string.IsNullOrWhiteSpace(targetFramework) && string.IsNullOrWhiteSpace(targetFrameworks))
                issues.Add("TargetFramework/TargetFrameworks not set.");
            else
                summaryParts.Add($"tfm {(targetFramework ?? targetFrameworks)}");

            if (settings.RequireLibraryOutput && !string.IsNullOrWhiteSpace(outputType) &&
                !string.Equals(outputType, "Library", StringComparison.OrdinalIgnoreCase))
                issues.Add($"OutputType is '{outputType}' (expected Library).");
            else if (!string.IsNullOrWhiteSpace(outputType))
                summaryParts.Add($"output {outputType}");
        }
        catch (Exception ex)
        {
            issues.Add($"Csproj parse failed: {ex.Message}");
        }

        var summary = summaryParts.Count == 0 ? "ok" : string.Join(", ", summaryParts);
        return BuildResult("Csproj", settings.Severity, issues, summary);
    }

}
