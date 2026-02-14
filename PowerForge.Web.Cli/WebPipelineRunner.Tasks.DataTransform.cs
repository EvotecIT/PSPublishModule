using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteDataTransform(
        JsonElement step,
        string baseDir,
        WebPipelineStepResult stepResult)
    {
        var inputPath = ResolvePath(baseDir,
            GetString(step, "input") ??
            GetString(step, "inputPath") ??
            GetString(step, "input-path") ??
            GetString(step, "source") ??
            GetString(step, "sourcePath") ??
            GetString(step, "source-path"));
        var outputPath = ResolvePath(baseDir,
            GetString(step, "out") ??
            GetString(step, "output") ??
            GetString(step, "outputPath") ??
            GetString(step, "output-path") ??
            GetString(step, "destination") ??
            GetString(step, "dest"));
        var command = GetString(step, "command") ?? GetString(step, "cmd") ?? GetString(step, "file");
        var args = GetString(step, "args") ?? GetString(step, "arguments");
        var argsList = GetArrayOfStrings(step, "argsList") ??
                       GetArrayOfStrings(step, "args-list") ??
                       GetArrayOfStrings(step, "argumentsList") ??
                       GetArrayOfStrings(step, "arguments-list");
        var mode = NormalizeDataTransformMode(
            GetString(step, "inputMode") ??
            GetString(step, "input-mode") ??
            GetString(step, "transformMode") ??
            GetString(step, "transform-mode"));
        var writeMode = NormalizeDataTransformWriteMode(GetString(step, "writeMode") ?? GetString(step, "write-mode"));
        var allowFailure = GetBool(step, "allowFailure") ?? GetBool(step, "continueOnError") ?? false;
        var timeoutSeconds = GetInt(step, "timeoutSeconds") ?? GetInt(step, "timeout-seconds") ?? 120;
        var requireOutput = GetBool(step, "requireOutput") ?? GetBool(step, "require-output") ?? true;
        var reportPath = ResolvePath(baseDir, GetString(step, "reportPath") ?? GetString(step, "report-path"));

        var workingDirectoryValue = GetString(step, "workingDirectory") ??
                                    GetString(step, "workingDir") ??
                                    GetString(step, "cwd") ??
                                    baseDir;
        var workingDirectory = ResolvePath(baseDir, workingDirectoryValue) ?? baseDir;

        if (string.IsNullOrWhiteSpace(inputPath))
            throw new InvalidOperationException("data-transform requires input.");
        if (!File.Exists(inputPath))
            throw new InvalidOperationException($"data-transform input file not found: {inputPath}");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("data-transform requires out/output path.");
        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("data-transform requires command.");
        if (!Directory.Exists(workingDirectory))
            throw new InvalidOperationException($"data-transform working directory not found: {workingDirectory}");
        if (timeoutSeconds <= 0)
            timeoutSeconds = 120;

        var inputContent = File.ReadAllText(inputPath);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);
        var beforeOutput = File.Exists(outputPath) ? File.ReadAllText(outputPath) : null;

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = mode == "stdin",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (argsList is { Length: > 0 })
        {
            foreach (var argument in argsList)
            {
                if (string.IsNullOrWhiteSpace(argument))
                    continue;
                startInfo.ArgumentList.Add(ExpandDataTransformTokens(argument, inputPath, outputPath, baseDir));
            }
        }
        else if (!string.IsNullOrWhiteSpace(args))
        {
            startInfo.Arguments = ExpandDataTransformTokens(args, inputPath, outputPath, baseDir);
        }

        startInfo.Environment["POWERFORGE_DATA_INPUT"] = inputPath;
        startInfo.Environment["POWERFORGE_DATA_OUTPUT"] = outputPath;
        startInfo.Environment["POWERFORGE_DATA_BASEDIR"] = Path.GetFullPath(baseDir);
        startInfo.Environment["POWERFORGE_DATA_MODE"] = mode;
        startInfo.Environment["POWERFORGE_DATA_WRITE_MODE"] = writeMode;

        var environment = ReadHookEnvironment(step);
        foreach (var pair in environment)
            startInfo.Environment[pair.Key] = ExpandDataTransformTokens(pair.Value, inputPath, outputPath, baseDir);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"data-transform failed to start '{command}': {ex.Message}", ex);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        if (mode == "stdin")
        {
            process.StandardInput.Write(inputContent);
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

            throw new InvalidOperationException($"data-transform timed out after {timeoutSeconds}s.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        var preview = FirstNonEmptyLine(stderr, stdout);

        if (process.ExitCode != 0 && !allowFailure)
        {
            var message = string.IsNullOrWhiteSpace(preview)
                ? $"data-transform failed with exit code {process.ExitCode}: {command}"
                : $"data-transform failed with exit code {process.ExitCode}: {command} ({preview})";
            throw new InvalidOperationException(message);
        }

        if (process.ExitCode == 0 && writeMode == "stdout")
        {
            if (requireOutput && string.IsNullOrWhiteSpace(stdout))
                throw new InvalidOperationException("data-transform produced empty stdout output.");
            File.WriteAllText(outputPath, stdout ?? string.Empty);
        }

        if (process.ExitCode == 0 && writeMode == "passthrough" && requireOutput && !File.Exists(outputPath))
            throw new InvalidOperationException($"data-transform passthrough mode did not produce output: {outputPath}");

        var afterOutput = File.Exists(outputPath) ? File.ReadAllText(outputPath) : null;
        var changed = !string.Equals(beforeOutput, afterOutput, StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(reportPath))
            WriteDataTransformReport(reportPath, new DataTransformReport
            {
                Input = Path.GetFullPath(inputPath),
                Output = Path.GetFullPath(outputPath),
                Mode = mode,
                WriteMode = writeMode,
                ExitCode = process.ExitCode,
                Changed = changed,
                AllowedFailure = allowFailure && process.ExitCode != 0,
                ErrorPreview = process.ExitCode == 0 ? null : preview,
                Utc = DateTimeOffset.UtcNow.ToString("O")
            });

        if (process.ExitCode != 0)
        {
            stepResult.Success = true;
            stepResult.Message = string.IsNullOrWhiteSpace(preview)
                ? $"data-transform allowed failure (exit {process.ExitCode}): {command}"
                : $"data-transform allowed failure (exit {process.ExitCode}): {command} ({preview})";
            return;
        }

        stepResult.Success = true;
        var messageOk = changed
            ? $"data-transform ok: updated '{outputPath}'."
            : $"data-transform ok: no output changes for '{outputPath}'.";
        if (!string.IsNullOrWhiteSpace(preview))
            messageOk += $" ({preview})";
        stepResult.Message = messageOk;
    }

    private static string NormalizeDataTransformMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "stdin";

        return value.Trim().ToLowerInvariant() switch
        {
            "stdin" => "stdin",
            "file" => "file",
            _ => throw new InvalidOperationException($"data-transform has unsupported mode '{value}'. Supported values: stdin, file.")
        };
    }

    private static string NormalizeDataTransformWriteMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "stdout";

        return value.Trim().ToLowerInvariant() switch
        {
            "stdout" => "stdout",
            "passthrough" => "passthrough",
            "file" => "passthrough",
            _ => throw new InvalidOperationException($"data-transform has unsupported writeMode '{value}'. Supported values: stdout, passthrough.")
        };
    }

    private static string ExpandDataTransformTokens(string value, string inputPath, string outputPath, string baseDir)
    {
        return value
            .Replace("{input}", inputPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{output}", outputPath, StringComparison.OrdinalIgnoreCase)
            .Replace("{baseDir}", Path.GetFullPath(baseDir), StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteDataTransformReport(string reportPath, DataTransformReport report)
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

    private sealed class DataTransformReport
    {
        public string Input { get; set; } = string.Empty;
        public string Output { get; set; } = string.Empty;
        public string Mode { get; set; } = "stdin";
        public string WriteMode { get; set; } = "stdout";
        public int ExitCode { get; set; }
        public bool Changed { get; set; }
        public bool AllowedFailure { get; set; }
        public string? ErrorPreview { get; set; }
        public string Utc { get; set; } = string.Empty;
    }
}
