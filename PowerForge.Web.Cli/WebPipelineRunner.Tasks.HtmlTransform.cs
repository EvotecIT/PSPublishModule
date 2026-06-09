using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteHtmlTransform(
        JsonElement step,
        string label,
        string baseDir,
        WebPipelineStepResult stepResult)
    {
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
        if (string.IsNullOrWhiteSpace(siteRoot))
            throw new InvalidOperationException("html-transform requires siteRoot.");
        if (!Directory.Exists(siteRoot))
            throw new InvalidOperationException($"html-transform siteRoot not found: {siteRoot}");

        var command = GetString(step, "command") ?? GetString(step, "cmd") ?? GetString(step, "file");
        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("html-transform requires command.");

        var args = GetString(step, "args") ?? GetString(step, "arguments");
        var argsList = GetArrayOfStrings(step, "argsList") ??
                       GetArrayOfStrings(step, "args-list") ??
                       GetArrayOfStrings(step, "argumentsList") ??
                       GetArrayOfStrings(step, "arguments-list");
        var allowFailure = GetBool(step, "allowFailure") ?? GetBool(step, "continueOnError") ?? false;
        var timeoutSeconds = GetInt(step, "timeoutSeconds") ?? GetInt(step, "timeout-seconds") ?? 120;
        if (timeoutSeconds <= 0)
            timeoutSeconds = 120;

        var workingDirectoryValue = GetString(step, "workingDirectory") ??
                                    GetString(step, "workingDir") ??
                                    GetString(step, "cwd") ??
                                    siteRoot;
        var workingDirectory = ResolvePath(baseDir, workingDirectoryValue) ?? siteRoot;
        if (!Directory.Exists(workingDirectory))
            throw new InvalidOperationException($"html-transform working directory not found: {workingDirectory}");

        var includePatterns = ReadTransformStringList(
            step,
            "include",
            "includes",
            "includePatterns",
            "include-patterns");
        var excludePatterns = ReadTransformStringList(
            step,
            "exclude",
            "excludes",
            "excludePatterns",
            "exclude-patterns");
        var extensions = ReadTransformStringList(
            step,
            "extensions",
            "ext",
            "fileExtensions",
            "file-extensions");
        if (extensions.Length == 0)
            extensions = new[] { ".html", ".htm" };
        var extensionSet = new HashSet<string>(
            extensions
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.StartsWith(".", StringComparison.Ordinal) ? value : "." + value)
                .Select(static value => value.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        var maxFiles = GetInt(step, "maxFiles") ?? GetInt(step, "max-files") ?? 0;
        var writeMode = NormalizeHtmlTransformWriteMode(GetString(step, "writeMode") ?? GetString(step, "write-mode"));
        var useStdin = GetBool(step, "stdin") ??
                       GetBool(step, "passContentToStdin") ??
                       GetBool(step, "pass-content-to-stdin") ??
                       GetBool(step, "passToStdin") ??
                       false;
        var requireOutput = GetBool(step, "requireOutput") ?? GetBool(step, "require-output") ?? writeMode == "stdout";
        var reportPath = ResolvePath(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path"));
        var environmentTemplate = ReadHookEnvironment(step);

        var files = GetHtmlTransformFiles(siteRoot, includePatterns, excludePatterns, extensionSet, maxFiles);
        var report = new HtmlTransformReport
        {
            SiteRoot = Path.GetFullPath(siteRoot),
            WriteMode = writeMode
        };

        if (files.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(reportPath))
                WriteHtmlTransformReport(reportPath, report);
            stepResult.Success = true;
            stepResult.Message = "html-transform ok: no matching files.";
            return;
        }

        var changed = 0;
        var failures = 0;
        var allowedFailureMessages = new List<string>();

        for (var index = 0; index < files.Length; index++)
        {
            var filePath = files[index];
            var relativePath = Path.GetRelativePath(siteRoot, filePath).Replace('\\', '/');
            var originalContent = File.ReadAllText(filePath);
            var fileEntry = new HtmlTransformReportEntry
            {
                Path = relativePath
            };

            HtmlTransformExecutionResult execution;
            try
            {
                execution = RunHtmlTransformCommand(
                    command!,
                    args,
                    argsList,
                    workingDirectory,
                    originalContent,
                    useStdin,
                    timeoutSeconds,
                    environmentTemplate,
                    filePath,
                    relativePath,
                    siteRoot,
                    index);
            }
            catch (Exception ex)
            {
                if (!allowFailure)
                    throw;

                failures++;
                fileEntry.Error = ex.Message;
                if (allowedFailureMessages.Count < 3)
                    allowedFailureMessages.Add($"{relativePath}: {TruncateForLog(ex.Message, 140)}");
                report.Files.Add(fileEntry);
                continue;
            }

            fileEntry.ExitCode = execution.ExitCode;
            if (execution.ExitCode != 0)
            {
                var preview = FirstNonEmptyLine(execution.Stderr, execution.Stdout);
                var errorMessage = string.IsNullOrWhiteSpace(preview)
                    ? $"html-transform failed (exit {execution.ExitCode}) for '{relativePath}'."
                    : $"html-transform failed (exit {execution.ExitCode}) for '{relativePath}': {preview}";

                if (!allowFailure)
                    throw new InvalidOperationException(errorMessage);

                failures++;
                fileEntry.Error = errorMessage;
                if (allowedFailureMessages.Count < 3)
                    allowedFailureMessages.Add($"{relativePath}: {TruncateForLog(preview ?? $"exit {execution.ExitCode}", 140)}");
                report.Files.Add(fileEntry);
                continue;
            }

            if (writeMode == "stdout")
            {
                if (requireOutput && string.IsNullOrWhiteSpace(execution.Stdout))
                    throw new InvalidOperationException($"html-transform stdout mode produced empty output for '{relativePath}'.");

                var transformed = execution.Stdout ?? string.Empty;
                if (!string.Equals(originalContent, transformed, StringComparison.Ordinal))
                {
                    File.WriteAllText(filePath, transformed);
                    changed++;
                    fileEntry.Changed = true;
                }
            }
            else
            {
                if (!File.Exists(filePath))
                    throw new InvalidOperationException($"html-transform in-place mode removed file '{relativePath}'.");

                var transformed = File.ReadAllText(filePath);
                if (!string.Equals(originalContent, transformed, StringComparison.Ordinal))
                {
                    changed++;
                    fileEntry.Changed = true;
                }
            }

            report.Files.Add(fileEntry);
        }

        report.ProcessedCount = files.Length;
        report.ChangedCount = changed;
        report.FailedCount = failures;

        if (!string.IsNullOrWhiteSpace(reportPath))
            WriteHtmlTransformReport(reportPath, report);

        stepResult.Success = true;
        var message = $"html-transform ok: processed {files.Length}, changed {changed}.";
        if (failures > 0)
        {
            var sample = allowedFailureMessages.Count > 0 ? $" Sample: {string.Join(" | ", allowedFailureMessages)}" : string.Empty;
            message += $" allowed failures {failures}.{sample}";
        }
        stepResult.Message = message;
    }

    private static HtmlTransformExecutionResult RunHtmlTransformCommand(
        string command,
        string? args,
        string[]? argsList,
        string workingDirectory,
        string originalContent,
        bool useStdin,
        int timeoutSeconds,
        Dictionary<string, string> environmentTemplate,
        string filePath,
        string relativePath,
        string siteRoot,
        int index)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = useStdin,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (argsList is { Length: > 0 })
        {
            foreach (var argument in argsList)
            {
                if (string.IsNullOrWhiteSpace(argument))
                    continue;
                startInfo.ArgumentList.Add(ExpandHtmlTransformTokens(argument, filePath, relativePath, siteRoot, index));
            }
        }
        else if (!string.IsNullOrWhiteSpace(args))
        {
            startInfo.Arguments = ExpandHtmlTransformTokens(args, filePath, relativePath, siteRoot, index);
        }

        startInfo.Environment["POWERFORGE_TRANSFORM_FILE"] = filePath;
        startInfo.Environment["POWERFORGE_TRANSFORM_RELATIVE"] = relativePath;
        startInfo.Environment["POWERFORGE_TRANSFORM_SITE_ROOT"] = siteRoot;
        startInfo.Environment["POWERFORGE_TRANSFORM_INDEX"] = index.ToString();

        foreach (var pair in environmentTemplate)
        {
            startInfo.Environment[pair.Key] = ExpandHtmlTransformTokens(pair.Value, filePath, relativePath, siteRoot, index);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"html-transform failed to start '{command}': {ex.Message}", ex);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        if (useStdin)
        {
            process.StandardInput.Write(originalContent);
            process.StandardInput.Close();
        }

        var finished = process.WaitForExit(Math.Max(1, timeoutSeconds) * 1000);
        if (!finished)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // best-effort cleanup
            }

            throw new InvalidOperationException($"html-transform timed out after {timeoutSeconds}s for '{relativePath}'.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        return new HtmlTransformExecutionResult
        {
            ExitCode = process.ExitCode,
            Stdout = stdout,
            Stderr = stderr
        };
    }

    private static string[] GetHtmlTransformFiles(
        string siteRoot,
        string[] includePatterns,
        string[] excludePatterns,
        HashSet<string> extensionSet,
        int maxFiles)
    {
        var includes = NormalizeHtmlTransformPatterns(includePatterns);
        var excludes = NormalizeHtmlTransformPatterns(excludePatterns);

        var matches = new List<string>();
        foreach (var file in Directory.EnumerateFiles(siteRoot, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extensionSet.Count > 0 && !extensionSet.Contains(extension))
                continue;

            var relativePath = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
            if (excludes.Length > 0 && MatchesHtmlTransformPattern(excludes, relativePath))
                continue;
            if (includes.Length > 0 && !MatchesHtmlTransformPattern(includes, relativePath))
                continue;

            matches.Add(Path.GetFullPath(file));
        }

        var ordered = matches
            .OrderBy(path => Path.GetRelativePath(siteRoot, path), StringComparer.OrdinalIgnoreCase);
        return maxFiles > 0
            ? ordered.Take(maxFiles).ToArray()
            : ordered.ToArray();
    }

    private static string[] NormalizeHtmlTransformPatterns(string[] patterns)
    {
        return patterns
            .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(static pattern => pattern.Trim().Replace('\\', '/'))
            .ToArray();
    }

    private static bool MatchesHtmlTransformPattern(string[] patterns, string value)
    {
        foreach (var pattern in patterns)
        {
            if (GlobMatchHtmlTransform(pattern, value))
                return true;
        }

        return false;
    }

    private static bool GlobMatchHtmlTransform(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
    }

    private static string NormalizeHtmlTransformWriteMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "inplace";

        return value.Trim().ToLowerInvariant() switch
        {
            "inplace" => "inplace",
            "file" => "inplace",
            "stdout" => "stdout",
            _ => throw new InvalidOperationException($"html-transform has unsupported writeMode '{value}'. Supported values: inplace, stdout.")
        };
    }

    private static string ExpandHtmlTransformTokens(
        string value,
        string filePath,
        string relativePath,
        string siteRoot,
        int index)
    {
        return value
            .Replace("{file}", filePath, StringComparison.OrdinalIgnoreCase)
            .Replace("{relative}", relativePath, StringComparison.OrdinalIgnoreCase)
            .Replace("{siteRoot}", siteRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{index}", index.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ReadTransformStringList(JsonElement step, params string[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var array = GetArrayOfStrings(step, name);
            if (array is { Length: > 0 })
                return array;

            var value = GetString(step, name);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            return value
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static item => item.Trim())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static void WriteHtmlTransformReport(string reportPath, HtmlTransformReport report)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(reportPath, json);
    }

    private sealed class HtmlTransformExecutionResult
    {
        public int ExitCode { get; set; }
        public string Stdout { get; set; } = string.Empty;
        public string Stderr { get; set; } = string.Empty;
    }

    private sealed class HtmlTransformReport
    {
        public string SiteRoot { get; set; } = string.Empty;
        public string WriteMode { get; set; } = "inplace";
        public int ProcessedCount { get; set; }
        public int ChangedCount { get; set; }
        public int FailedCount { get; set; }
        public List<HtmlTransformReportEntry> Files { get; } = new();
    }

    private sealed class HtmlTransformReportEntry
    {
        public string Path { get; set; } = string.Empty;
        public bool Changed { get; set; }
        public int ExitCode { get; set; }
        public string? Error { get; set; }
    }
}
