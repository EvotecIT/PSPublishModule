using System.Text;
using System.Text.Json;
using PowerForge;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    /// <summary>
    /// Validates imported PowerShell example scripts by parsing them with an out-of-process PowerShell parser.
    /// </summary>
    /// <param name="options">Validation options.</param>
    /// <param name="runner">Optional PowerShell runner override.</param>
    /// <returns>Validation result.</returns>
    public static WebApiDocsPowerShellExampleValidationResult ValidatePowerShellExamples(
        WebApiDocsPowerShellExampleValidationOptions options,
        IPowerShellRunner? runner = null)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var warnings = new List<string>();
        var result = new WebApiDocsPowerShellExampleValidationResult();
        if (string.IsNullOrWhiteSpace(options.HelpPath))
        {
            result.Warnings = new[] { NormalizeWarningCode("API docs PowerShell coverage: helpPath is required for example validation.") };
            return result;
        }

        var resolvedHelpPath = ResolvePowerShellHelpFile(options.HelpPath, warnings);
        result.HelpPath = resolvedHelpPath;
        if (string.IsNullOrWhiteSpace(resolvedHelpPath) || !File.Exists(resolvedHelpPath))
        {
            result.Warnings = warnings
                .Where(static warning => !string.IsNullOrWhiteSpace(warning))
                .Select(NormalizeWarningCode)
                .ToArray();
            return result;
        }

        var moduleName = Path.GetFileNameWithoutExtension(resolvedHelpPath) ?? string.Empty;
        if (moduleName.EndsWith("-help", StringComparison.OrdinalIgnoreCase))
            moduleName = moduleName[..^5];
        if (moduleName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            moduleName = moduleName[..^4];

        var manifestPath = ResolvePowerShellModuleManifestPath(resolvedHelpPath, moduleName, options.PowerShellModuleManifestPath);
        result.ManifestPath = manifestPath;

        var docsOptions = new WebApiDocsOptions
        {
            Type = ApiDocsType.PowerShell,
            HelpPath = options.HelpPath,
            PowerShellModuleManifestPath = manifestPath,
            PowerShellExamplesPath = options.PowerShellExamplesPath,
            GeneratePowerShellFallbackExamples = false
        };
        var apiDoc = ParsePowerShellHelp(options.HelpPath, warnings, docsOptions);
        var knownCommands = apiDoc.Types.Values
            .Where(IsPowerShellCommandType)
            .Select(static type => type.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var knownCommandSet = new HashSet<string>(knownCommands, StringComparer.OrdinalIgnoreCase);
        result.KnownCommands = knownCommands;
        result.KnownCommandCount = knownCommands.Length;

        var files = ResolvePowerShellExampleScriptFiles(options.HelpPath, resolvedHelpPath, manifestPath, docsOptions, warnings);
        result.FileCount = files.Count;
        if (files.Count == 0)
        {
            result.ValidationSucceeded = true;
            result.Warnings = warnings
                .Where(static warning => !string.IsNullOrWhiteSpace(warning))
                .Select(NormalizeWarningCode)
                .ToArray();
            return result;
        }

        var timeoutSeconds = Math.Clamp(options.TimeoutSeconds, 5, 600);
        var runnerToUse = runner ?? new PowerShellRunner();
        Dictionary<string, string[]> parseErrorsByFile;

        try
        {
            var parserResult = RunPowerShellExampleSyntaxValidation(files, TimeSpan.FromSeconds(timeoutSeconds), options.PreferPwsh, runnerToUse);
            result.Executable = parserResult.Executable;
            result.ValidationSucceeded = parserResult.ValidationSucceeded;
            parseErrorsByFile = parserResult.ParseErrorsByFile;

            if (!parserResult.ValidationSucceeded)
            {
                warnings.Add(
                    $"API docs PowerShell coverage: PowerShell example validation did not complete successfully ({parserResult.ErrorMessage}).");
            }
        }
        catch (Exception ex)
        {
            warnings.Add(
                $"API docs PowerShell coverage: failed to validate PowerShell examples ({ex.GetType().Name}: {ex.Message}).");
            result.Warnings = warnings
                .Where(static warning => !string.IsNullOrWhiteSpace(warning))
                .Select(NormalizeWarningCode)
                .ToArray();
            return result;
        }

        var fileResults = new List<WebApiDocsPowerShellExampleFileValidationResult>(files.Count);
        foreach (var file in files)
        {
            parseErrorsByFile.TryGetValue(file, out var parseErrors);
            parseErrors ??= Array.Empty<string>();
            var commands = CollectPowerShellCommandTokensFromFile(file, warnings);
            var matchedCommands = commands
                .Where(knownCommandSet.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static command => command, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            fileResults.Add(new WebApiDocsPowerShellExampleFileValidationResult
            {
                FilePath = file,
                ValidSyntax = parseErrors.Length == 0,
                ParseErrorCount = parseErrors.Length,
                ParseErrors = parseErrors,
                Commands = commands,
                MatchedCommands = matchedCommands
            });
        }

        result.Files = fileResults
            .OrderBy(static file => file.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        result.ValidSyntaxFileCount = result.Files.Count(static file => file.ValidSyntax);
        result.InvalidSyntaxFileCount = result.Files.Length - result.ValidSyntaxFileCount;
        result.MatchedFileCount = result.Files.Count(static file => file.MatchedCommands.Length > 0);
        result.UnmatchedFileCount = result.Files.Length - result.MatchedFileCount;
        result.ParseErrorCount = result.Files.Sum(static file => file.ParseErrorCount);
        result.ExecutionRequested = options.ExecuteMatchedExamples;

        if (options.ExecuteMatchedExamples)
        {
            var executionResult = ExecuteMatchedPowerShellExamples(
                result.Files,
                manifestPath,
                TimeSpan.FromSeconds(Math.Clamp(options.ExecutionTimeoutSeconds, 5, 600)),
                options.PreferPwsh,
                runnerToUse,
                result.Executable);
            result.ExecutionCompleted = executionResult.ExecutionCompleted;
            result.ExecutionExecutable = executionResult.Executable;
            result.ExecutedFileCount = result.Files.Count(static file => file.ExecutionAttempted);
            result.PassedExecutionFileCount = result.Files.Count(static file => file.ExecutionSucceeded == true);
            result.FailedExecutionFileCount = result.Files.Count(static file => file.ExecutionSucceeded == false);
            if (!executionResult.ExecutionCompleted && !string.IsNullOrWhiteSpace(executionResult.ErrorMessage))
            {
                warnings.Add(
                    $"API docs PowerShell coverage: matched example execution did not complete successfully ({executionResult.ErrorMessage}).");
            }
        }

        AppendPowerShellExampleValidationWarnings(result.Files, warnings);

        result.Warnings = warnings
            .Where(static warning => !string.IsNullOrWhiteSpace(warning))
            .Select(NormalizeWarningCode)
            .ToArray();
        return result;
    }

    /// <summary>
    /// Writes a PowerShell example validation report as JSON.
    /// </summary>
    /// <param name="outputPath">API docs output root.</param>
    /// <param name="configuredPath">Optional relative or absolute report path.</param>
    /// <param name="result">Validation result payload.</param>
    /// <returns>Full path to the written report.</returns>
    public static string WritePowerShellExampleValidationReport(
        string outputPath,
        string? configuredPath,
        WebApiDocsPowerShellExampleValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        var resolvedConfiguredPath = string.IsNullOrWhiteSpace(configuredPath)
            ? "powershell-example-validation.json"
            : configuredPath.Trim();
        var reportPath = Path.IsPathRooted(resolvedConfiguredPath)
            ? Path.GetFullPath(resolvedConfiguredPath)
            : Path.Combine(outputPath, resolvedConfiguredPath);
        var parent = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(reportPath, json, Encoding.UTF8);
        return reportPath;
    }

    private static void AppendPowerShellExampleValidationWarnings(
        IReadOnlyList<WebApiDocsPowerShellExampleFileValidationResult> files,
        List<string> warnings)
    {
        if (files is null || warnings is null || files.Count == 0)
            return;

        var invalidFiles = files
            .Where(static file => !file.ValidSyntax)
            .Select(static file => Path.GetFileName(file.FilePath))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (invalidFiles.Length > 0)
        {
            warnings.Add(
                $"API docs PowerShell coverage: {invalidFiles.Length} example script(s) failed syntax validation " +
                $"(samples: {BuildPreviewList(invalidFiles, 4)}).");
        }

        var unmatchedFiles = files
            .Where(static file => file.MatchedCommands.Length == 0)
            .Select(static file => Path.GetFileName(file.FilePath))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unmatchedFiles.Length > 0)
        {
            warnings.Add(
                $"API docs PowerShell coverage: {unmatchedFiles.Length} example script(s) did not reference any documented commands " +
                $"(samples: {BuildPreviewList(unmatchedFiles, 4)}).");
        }

        var failedExecutionFiles = files
            .Where(static file => file.ExecutionAttempted && file.ExecutionSucceeded == false)
            .Select(static file => Path.GetFileName(file.FilePath))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (failedExecutionFiles.Length > 0)
        {
            warnings.Add(
                $"API docs PowerShell coverage: {failedExecutionFiles.Length} example script(s) failed execution " +
                $"(samples: {BuildPreviewList(failedExecutionFiles, 4)}).");
        }
    }

    private static string BuildPreviewList(IReadOnlyList<string> values, int previewCount)
    {
        if (values is null || values.Count == 0)
            return string.Empty;

        var limit = Math.Max(1, previewCount);
        var preview = string.Join(", ", values.Take(limit));
        var remaining = values.Count - Math.Min(values.Count, limit);
        return remaining > 0 ? $"{preview} (+{remaining} more)" : preview;
    }

    private static PowerShellExampleExecutionProcessResult ExecuteMatchedPowerShellExamples(
        IReadOnlyList<WebApiDocsPowerShellExampleFileValidationResult> files,
        string? manifestPath,
        TimeSpan timeout,
        bool preferPwsh,
        IPowerShellRunner runner,
        string? executableOverride)
    {
        if (files is null)
            throw new ArgumentNullException(nameof(files));
        if (runner is null)
            throw new ArgumentNullException(nameof(runner));

        var result = new PowerShellExampleExecutionProcessResult { ExecutionCompleted = true };
        foreach (var file in files)
        {
            if (!file.ValidSyntax)
            {
                file.ExecutionSkippedReason = "Syntax validation failed.";
                continue;
            }

            if (file.MatchedCommands.Length == 0)
            {
                file.ExecutionSkippedReason = "No documented commands matched.";
                continue;
            }

            file.ExecutionAttempted = true;

            try
            {
                var commandText = BuildPowerShellExampleExecutionCommand(file.FilePath, manifestPath);
                var runResult = runner.Run(PowerShellRunRequest.ForCommand(
                    commandText,
                    timeout,
                    preferPwsh: preferPwsh,
                    workingDirectory: Path.GetDirectoryName(file.FilePath),
                    executableOverride: string.IsNullOrWhiteSpace(executableOverride) ? null : executableOverride,
                    captureOutput: true,
                    captureError: true));

                result.Executable ??= string.IsNullOrWhiteSpace(runResult.Executable) ? null : runResult.Executable;
                file.ExecutionExitCode = runResult.ExitCode;
                file.ExecutionSucceeded = runResult.ExitCode == 0;
                file.ExecutionStdOut = NormalizeExecutionCapture(runResult.StdOut);
                file.ExecutionStdErr = NormalizeExecutionCapture(runResult.StdErr);
            }
            catch (Exception ex)
            {
                result.ExecutionCompleted = false;
                result.ErrorMessage ??= $"{ex.GetType().Name}: {ex.Message}";
                file.ExecutionSucceeded = false;
                file.ExecutionExitCode = -1;
                file.ExecutionStdErr = NormalizeExecutionCapture($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        return result;
    }

    private static string BuildPowerShellExampleExecutionCommand(string filePath, string? manifestPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("$ProgressPreference = 'SilentlyContinue'");
        var bootstrapModulePath = ResolvePowerShellExampleExecutionBootstrapModulePath(manifestPath);
        if (!string.IsNullOrWhiteSpace(bootstrapModulePath) && File.Exists(bootstrapModulePath))
        {
            sb.Append("Import-Module -Name ");
            sb.Append(ToPowerShellSingleQuotedLiteral(Path.GetFullPath(bootstrapModulePath)));
            sb.AppendLine(" -Force | Out-Null");
        }
        else if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
        {
            sb.Append("Import-Module -Name ");
            sb.Append(ToPowerShellSingleQuotedLiteral(Path.GetFullPath(manifestPath)));
            sb.AppendLine(" -Force | Out-Null");
        }

        sb.Append("& ");
        sb.Append(ToPowerShellSingleQuotedLiteral(Path.GetFullPath(filePath)));
        return sb.ToString();
    }

    private static string? ResolvePowerShellExampleExecutionBootstrapModulePath(string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return null;

        try
        {
            var text = File.ReadAllText(manifestPath);
            var rootModule = ParseManifestScalarValue(text, "RootModule");
            if (string.IsNullOrWhiteSpace(rootModule))
                return null;

            var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
            var candidate = Path.IsPathRooted(rootModule)
                ? rootModule
                : Path.Combine(manifestDirectory, rootModule);
            return candidate.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate)
                ? candidate
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ToPowerShellSingleQuotedLiteral(string value)
    {
        return "'" + (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string? NormalizeExecutionCapture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        const int maxLength = 16000;
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..maxLength] + "\n...[truncated]";
    }

    private static PowerShellExampleSyntaxValidationProcessResult RunPowerShellExampleSyntaxValidation(
        IReadOnlyList<string> files,
        TimeSpan timeout,
        bool preferPwsh,
        IPowerShellRunner runner)
    {
        if (files is null)
            throw new ArgumentNullException(nameof(files));
        if (runner is null)
            throw new ArgumentNullException(nameof(runner));

        var tempRoot = Path.Combine(Path.GetTempPath(), "pf-web-ps-example-validate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var inputPath = Path.Combine(tempRoot, "input.json");
        var outputPath = Path.Combine(tempRoot, "output.json");
        var scriptPath = Path.Combine(tempRoot, "validate-examples.ps1");

        try
        {
            WriteJson(inputPath, new Dictionary<string, object?>
            {
                ["files"] = files
            });
            File.WriteAllText(scriptPath, PowerShellExampleValidationScript, new UTF8Encoding(false));

            var runResult = runner.Run(new PowerShellRunRequest(
                scriptPath,
                new[] { inputPath, outputPath },
                timeout,
                preferPwsh: preferPwsh));

            var result = new PowerShellExampleSyntaxValidationProcessResult
            {
                Executable = runResult.Executable,
                ValidationSucceeded = runResult.ExitCode == 0
            };

            if (runResult.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(runResult.StdErr) ? runResult.StdOut : runResult.StdErr;
                result.ErrorMessage = string.IsNullOrWhiteSpace(error)
                    ? $"PowerShell parser exited with code {runResult.ExitCode}."
                    : error.Trim();
                return result;
            }

            if (!File.Exists(outputPath))
            {
                result.ErrorMessage = "PowerShell parser did not write a validation report.";
                result.ValidationSucceeded = false;
                return result;
            }

            using var json = JsonDocument.Parse(File.ReadAllText(outputPath));
            if (!json.RootElement.TryGetProperty("files", out var filesElement) ||
                (filesElement.ValueKind != JsonValueKind.Array && filesElement.ValueKind != JsonValueKind.Object))
            {
                result.ErrorMessage = "PowerShell parser returned an invalid validation payload.";
                result.ValidationSucceeded = false;
                return result;
            }

            var fileEntries = filesElement.ValueKind == JsonValueKind.Array
                ? filesElement.EnumerateArray().ToArray()
                : new[] { filesElement };

            foreach (var fileElement in fileEntries)
            {
                var filePath = fileElement.TryGetProperty("filePath", out var filePathElement)
                    ? filePathElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(filePath))
                    continue;

                var errors = fileElement.TryGetProperty("parseErrors", out var errorsElement) &&
                             errorsElement.ValueKind == JsonValueKind.Array
                    ? errorsElement.EnumerateArray()
                        .Select(static element => element.GetString())
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Select(static value => value!)
                        .ToArray()
                    : Array.Empty<string>();
                result.ParseErrorsByFile[filePath] = errors;
            }

            return result;
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    private sealed class PowerShellExampleSyntaxValidationProcessResult
    {
        public string? Executable { get; set; }
        public bool ValidationSucceeded { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, string[]> ParseErrorsByFile { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PowerShellExampleExecutionProcessResult
    {
        public string? Executable { get; set; }
        public bool ExecutionCompleted { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private const string PowerShellExampleValidationScript =
        """
        param(
            [string]$InputPath,
            [string]$OutputPath
        )

        $payload = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
        $files = @($payload.files)
        $results = foreach ($file in $files) {
            $tokens = $null
            $parseErrors = $null
            [System.Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$parseErrors) | Out-Null
            [ordered]@{
                filePath = $file
                parseErrors = @($parseErrors | ForEach-Object {
                    if ($null -eq $_) {
                        return
                    }

                    if ($_.Extent -and $_.Extent.StartLineNumber) {
                        "$($_.Extent.StartLineNumber): $($_.Message)"
                    } else {
                        "$($_.Message)"
                    }
                })
            }
        }

        [ordered]@{
            files = @($results)
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
        """;
}
