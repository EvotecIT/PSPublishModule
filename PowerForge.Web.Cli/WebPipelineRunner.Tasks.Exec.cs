using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteExec(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var command = GetString(step, "command") ?? GetString(step, "cmd") ?? GetString(step, "file");
        var args = GetString(step, "args") ?? GetString(step, "arguments");
        var argsList = GetArrayOfStrings(step, "argsList") ?? GetArrayOfStrings(step, "args-list") ?? GetArrayOfStrings(step, "argumentsList") ?? GetArrayOfStrings(step, "arguments-list");
        var allowFailure = GetBool(step, "allowFailure") ?? GetBool(step, "continueOnError") ?? false;
        var timeoutSeconds = GetInt(step, "timeoutSeconds") ?? GetInt(step, "timeout-seconds") ?? 600;

        var workingDirectoryValue = GetString(step, "workingDirectory") ??
                                    GetString(step, "workingDir") ??
                                    GetString(step, "cwd") ??
                                    baseDir;
        var workingDirectory = ResolvePath(baseDir, workingDirectoryValue) ?? baseDir;

        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("exec requires command.");

        if (timeoutSeconds <= 0)
            timeoutSeconds = 600;

        if (!Directory.Exists(workingDirectory))
            throw new InvalidOperationException($"exec working directory not found: {workingDirectory}");

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (argsList is { Length: > 0 })
        {
            foreach (var argument in argsList)
            {
                if (!string.IsNullOrWhiteSpace(argument))
                    startInfo.ArgumentList.Add(argument);
            }
        }
        else if (!string.IsNullOrWhiteSpace(args))
        {
            startInfo.Arguments = args;
        }

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"exec failed to start '{command}': {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

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

            throw new InvalidOperationException($"exec timed out after {timeoutSeconds}s: {command}");
        }

        var outputText = stdout.ToString().Trim();
        var errorText = stderr.ToString().Trim();
        var exitCode = process.ExitCode;
        var preview = FirstNonEmptyLine(errorText, outputText);

        if (exitCode != 0 && !allowFailure)
        {
            var message = string.IsNullOrWhiteSpace(preview)
                ? $"exec failed with exit code {exitCode}: {command}"
                : $"exec failed with exit code {exitCode}: {command} ({preview})";
            throw new InvalidOperationException(message);
        }

        if (exitCode != 0)
        {
            stepResult.Success = true;
            stepResult.Message = string.IsNullOrWhiteSpace(preview)
                ? $"exec allowed failure (exit {exitCode}): {command}"
                : $"exec allowed failure (exit {exitCode}): {command} ({preview})";
            return;
        }

        stepResult.Success = true;
        stepResult.Message = string.IsNullOrWhiteSpace(preview)
            ? $"exec ok: {command}"
            : $"exec ok: {command} ({preview})";
    }

    private static string? FirstNonEmptyLine(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            using var reader = new StringReader(candidate);
            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    return line.Trim();
            }
        }

        return null;
    }
}