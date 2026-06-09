using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteHook(
        JsonElement step,
        string label,
        string baseDir,
        string effectiveMode,
        WebPipelineStepResult stepResult)
    {
        var eventName = GetString(step, "event") ?? GetString(step, "hook") ?? GetString(step, "name");
        var command = GetString(step, "command") ?? GetString(step, "cmd") ?? GetString(step, "file");
        var args = GetString(step, "args") ?? GetString(step, "arguments");
        var argsList = GetArrayOfStrings(step, "argsList") ?? GetArrayOfStrings(step, "args-list") ?? GetArrayOfStrings(step, "argumentsList") ?? GetArrayOfStrings(step, "arguments-list");
        var allowFailure = GetBool(step, "allowFailure") ?? GetBool(step, "continueOnError") ?? false;
        var timeoutSeconds = GetInt(step, "timeoutSeconds") ?? GetInt(step, "timeout-seconds") ?? 600;
        var stepId = GetString(step, "id");

        var workingDirectoryValue = GetString(step, "workingDirectory") ??
                                    GetString(step, "workingDir") ??
                                    GetString(step, "cwd") ??
                                    baseDir;
        var workingDirectory = ResolvePath(baseDir, workingDirectoryValue) ?? baseDir;

        if (string.IsNullOrWhiteSpace(eventName))
            throw new InvalidOperationException("hook requires event.");
        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("hook requires command.");
        if (timeoutSeconds <= 0)
            timeoutSeconds = 600;
        if (!Directory.Exists(workingDirectory))
            throw new InvalidOperationException($"hook working directory not found: {workingDirectory}");

        var contextPath = WriteHookContext(step, eventName, label, stepId, baseDir, workingDirectory, effectiveMode);
        var stdoutPath = ResolveHookPath(step, baseDir, "stdoutPath", "stdout-path", "stdout");
        var stderrPath = ResolveHookPath(step, baseDir, "stderrPath", "stderr-path", "stderr");
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

        startInfo.Environment["POWERFORGE_HOOK_EVENT"] = eventName;
        startInfo.Environment["POWERFORGE_HOOK_LABEL"] = label;
        startInfo.Environment["POWERFORGE_HOOK_MODE"] = effectiveMode;
        startInfo.Environment["POWERFORGE_HOOK_WORKDIR"] = workingDirectory;
        startInfo.Environment["POWERFORGE_HOOK_BASEDIR"] = Path.GetFullPath(baseDir);
        if (!string.IsNullOrWhiteSpace(stepId))
            startInfo.Environment["POWERFORGE_HOOK_ID"] = stepId;
        if (!string.IsNullOrWhiteSpace(contextPath))
            startInfo.Environment["POWERFORGE_HOOK_CONTEXT"] = contextPath;

        var environment = ReadHookEnvironment(step);
        foreach (var pair in environment)
            startInfo.Environment[pair.Key] = pair.Value;

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"hook failed to start '{command}': {ex.Message}", ex);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

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

            throw new InvalidOperationException($"hook timed out after {timeoutSeconds}s: {command}");
        }

        var outputText = stdoutTask.GetAwaiter().GetResult().Trim();
        var errorText = stderrTask.GetAwaiter().GetResult().Trim();
        WriteHookOutput(stdoutPath, outputText);
        WriteHookOutput(stderrPath, errorText);

        var exitCode = process.ExitCode;
        var preview = FirstNonEmptyLine(errorText, outputText);
        if (exitCode != 0 && !allowFailure)
        {
            var message = string.IsNullOrWhiteSpace(preview)
                ? $"hook '{eventName}' failed with exit code {exitCode}: {command}"
                : $"hook '{eventName}' failed with exit code {exitCode}: {command} ({preview})";
            throw new InvalidOperationException(message);
        }

        if (exitCode != 0)
        {
            stepResult.Success = true;
            stepResult.Message = string.IsNullOrWhiteSpace(preview)
                ? $"hook '{eventName}' allowed failure (exit {exitCode}): {command}"
                : $"hook '{eventName}' allowed failure (exit {exitCode}): {command} ({preview})";
            return;
        }

        stepResult.Success = true;
        var messageOk = string.IsNullOrWhiteSpace(preview)
            ? $"hook ok: {eventName} ({command})"
            : $"hook ok: {eventName} ({command}) ({preview})";
        if (!string.IsNullOrWhiteSpace(contextPath))
            messageOk += $" context={contextPath}";
        stepResult.Message = messageOk;
    }

    private static string? WriteHookContext(
        JsonElement step,
        string eventName,
        string label,
        string? stepId,
        string baseDir,
        string workingDirectory,
        string effectiveMode)
    {
        var contextPathValue = GetString(step, "contextPath") ?? GetString(step, "context-path") ?? GetString(step, "context");
        if (string.IsNullOrWhiteSpace(contextPathValue))
            return null;

        var resolved = ResolvePath(baseDir, contextPathValue);
        if (string.IsNullOrWhiteSpace(resolved))
            throw new InvalidOperationException($"hook has invalid contextPath: {contextPathValue}");

        var payload = new HookContextPayload
        {
            Event = eventName,
            Label = label,
            StepId = stepId,
            Mode = effectiveMode,
            BaseDirectory = Path.GetFullPath(baseDir),
            WorkingDirectory = Path.GetFullPath(workingDirectory),
            Utc = DateTimeOffset.UtcNow.ToString("O")
        };

        var directory = Path.GetDirectoryName(resolved);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(resolved, json);
        return Path.GetFullPath(resolved);
    }

    private static string? ResolveHookPath(JsonElement step, string baseDir, params string[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var value = GetString(step, name);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var resolved = ResolvePath(baseDir, value);
            if (string.IsNullOrWhiteSpace(resolved))
                throw new InvalidOperationException($"hook has invalid path for '{name}': {value}");
            return resolved;
        }

        return null;
    }

    private static void WriteHookOutput(string? path, string content)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, content ?? string.Empty);
    }

    private static Dictionary<string, string> ReadHookEnvironment(JsonElement step)
    {
        if (step.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        JsonElement environmentElement;
        if (!TryGetObjectProperty(step, out environmentElement, "env", "environment"))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in environmentElement.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
                continue;
            var value = ToStringValue(property.Value);
            if (value is null)
                continue;
            result[property.Name] = value;
        }

        return result;
    }

    private static bool TryGetObjectProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (!element.TryGetProperty(name, out var candidate))
                continue;
            if (candidate.ValueKind != JsonValueKind.Object)
                continue;
            value = candidate;
            return true;
        }

        value = default;
        return false;
    }

    private static string? ToStringValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private sealed class HookContextPayload
    {
        public string Event { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string? StepId { get; set; }
        public string Mode { get; set; } = string.Empty;
        public string BaseDirectory { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public string Utc { get; set; } = string.Empty;
    }
}
