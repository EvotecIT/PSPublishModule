using System.Diagnostics;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal void RunCommandHook(DotNetPublishPlan plan, DotNetPublishStep step)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (step is null) throw new ArgumentNullException(nameof(step));
        if (string.IsNullOrWhiteSpace(step.HookCommand))
            throw new InvalidOperationException($"Hook step '{step.Key}' is missing HookCommand.");

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hook"] = step.HookId ?? string.Empty,
            ["phase"] = step.HookPhase?.ToString() ?? string.Empty,
            ["target"] = step.TargetName ?? string.Empty,
            ["rid"] = step.Runtime ?? string.Empty,
            ["framework"] = step.Framework ?? string.Empty,
            ["style"] = step.Style?.ToString() ?? string.Empty,
            ["bundle"] = step.BundleId ?? string.Empty,
            ["configuration"] = plan.Configuration,
            ["projectRoot"] = plan.ProjectRoot
        };

        var command = ApplyTemplate(step.HookCommand!, tokens);
        var commandPath = ResolveHookCommandPath(plan.ProjectRoot, command);
        var args = (step.HookArguments ?? Array.Empty<string>())
            .Select(argument => ApplyTemplate(argument ?? string.Empty, tokens))
            .ToArray();
        var workingDirectory = string.IsNullOrWhiteSpace(step.HookWorkingDirectory)
            ? plan.ProjectRoot
            : ResolvePath(plan.ProjectRoot, ApplyTemplate(step.HookWorkingDirectory!, tokens));
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in step.HookEnvironment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entry.Key)) continue;
            environment[entry.Key.Trim()] = ApplyTemplate(entry.Value ?? string.Empty, tokens);
        }
        string[] generatedOutputs = ResolveHookGeneratedOutputs(plan, step, tokens);
        step.HookGeneratedOutputsValidated = false;
        EnsureHookGeneratedOutputsAbsent(plan.ProjectRoot, step, generatedOutputs);

        _logger.Info($"Hook {step.HookPhase}: {step.HookId}");
        var result = RunHookProcess(
            commandPath,
            workingDirectory,
            args,
            environment,
            TimeSpan.FromSeconds(Math.Max(1, step.HookTimeoutSeconds)));

        if (result.ExitCode == 0)
        {
            EnsureHookGeneratedOutputsPresent(plan.ProjectRoot, step, generatedOutputs);
            step.HookGeneratedOutputsValidated = true;
            if (_logger.IsVerbose)
            {
                if (!string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.TrimEnd());
                if (!string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.TrimEnd());
            }

            return;
        }

        var stderrTail = TailLines(result.StdErr, maxLines: 40, maxChars: 4000);
        var stdoutTail = TailLines(result.StdOut, maxLines: 40, maxChars: 4000);
        var message = result.TimedOut
            ? $"Hook '{step.HookId}' timed out after {Math.Max(1, step.HookTimeoutSeconds)} seconds."
            : ExtractBestFailureLine(!string.IsNullOrWhiteSpace(stderrTail) ? stderrTail : stdoutTail);
        if (string.IsNullOrWhiteSpace(message))
            message = $"Hook '{step.HookId}' failed with exit code {result.ExitCode}.";

        string? partialOutput = generatedOutputs.FirstOrDefault(PathEntryExists);
        if (!string.IsNullOrWhiteSpace(partialOutput))
        {
            message += $" The failed hook left a declared generated output: {partialOutput}";
        }

        if (!step.HookRequired && string.IsNullOrWhiteSpace(partialOutput))
        {
            _logger.Warn(message);
            return;
        }

        throw new DotNetPublishCommandException(
            message,
            result.Executable,
            string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            args,
            result.ExitCode,
            result.StdOut,
            result.StdErr);
    }

    private static string[] ResolveHookGeneratedOutputs(
        DotNetPublishPlan plan,
        DotNetPublishStep step,
        IReadOnlyDictionary<string, string> tokens)
    {
        var comparison = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return (step.HookGeneratedOutputs ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolvePath(plan.ProjectRoot, ApplyTemplate(path, tokens)))
            .Distinct(comparison)
            .ToArray();
    }

    private static void EnsureHookGeneratedOutputsAbsent(
        string projectRoot,
        DotNetPublishStep step,
        IEnumerable<string> generatedOutputs)
    {
        foreach (string output in generatedOutputs)
        {
            EnsureHookGeneratedOutputPath(projectRoot, step, output);
            if (File.Exists(output) || Directory.Exists(output))
            {
                throw new InvalidOperationException(
                    $"Hook '{step.HookId}' generated output must be absent before execution: {output}");
            }
        }
    }

    private static void EnsureHookGeneratedOutputsPresent(
        string projectRoot,
        DotNetPublishStep step,
        IEnumerable<string> generatedOutputs)
    {
        foreach (string output in generatedOutputs)
        {
            EnsureHookGeneratedOutputPath(projectRoot, step, output);
            if (!File.Exists(output) && !Directory.Exists(output))
            {
                throw new InvalidOperationException(
                    $"Hook '{step.HookId}' did not produce its declared output: {output}");
            }
            EnsureHookGeneratedOutputTreeIsSafe(projectRoot, step, output);
        }
    }

    internal static void EnsureHookGeneratedOutputTreeIsSafe(
        string projectRoot,
        DotNetPublishStep step,
        string output)
    {
        if (HasReparsePointBelowRoot(output, projectRoot))
        {
            throw new InvalidOperationException(
                $"Hook '{step.HookId}' generated output traverses a reparse point: {output}");
        }

        var pending = new Stack<string>();
        pending.Push(output);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Hook '{step.HookId}' generated output could not be inspected safely: {current}",
                    ex);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Hook '{step.HookId}' generated output contains a reparse point: {current}");
            }
            if ((attributes & FileAttributes.Directory) == 0)
                continue;

            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(
                             current,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    pending.Push(entry);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Hook '{step.HookId}' generated output could not be enumerated safely: {current}",
                    ex);
            }
        }
    }

    private static bool PathEntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void EnsureHookGeneratedOutputPath(
        string projectRoot,
        DotNetPublishStep step,
        string output)
    {
        EnsurePathWithinRoot(projectRoot, output, $"Hook '{step.HookId}' generated output");
        if (PathsEqual(projectRoot, output))
        {
            throw new InvalidOperationException(
                $"Hook '{step.HookId}' generated output cannot be the project root.");
        }
    }

    private static string ResolveHookCommandPath(string projectRoot, string command)
    {
        var raw = TrimMatchingQuotes((command ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(raw))
            return raw;
        if (Path.IsPathRooted(raw))
            return Path.GetFullPath(raw);
        if (raw.Contains(Path.DirectorySeparatorChar) || raw.Contains(Path.AltDirectorySeparatorChar))
            return ResolvePath(projectRoot, raw);
        return raw;
    }

    private static string TrimMatchingQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            return value.Substring(1, value.Length - 2);

        return value;
    }

    private (int ExitCode, string StdOut, string StdErr, string Executable, bool TimedOut) RunHookProcess(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

        foreach (var entry in environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            psi.Environment[entry.Key] = entry.Value ?? string.Empty;

#if NET472
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        foreach (var arg in args ?? Array.Empty<string>())
            psi.ArgumentList.Add(arg);
#endif

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start hook command: {fileName}");
        var cancellationToken = _cancellationToken.Value;
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
#if NET472
                process.Kill();
#else
                process.Kill(entireProcessTree: true);
#endif
            }
            catch (Exception ex)
            {
                _logger.Verbose($"Hook cancellation kill failed for '{fileName}': {ex.Message}");
            }
        });
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var timeoutMs = (int)Math.Min(Math.Max(1, timeout.TotalMilliseconds), int.MaxValue);

        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
#if NET472
                process.Kill();
#else
                process.Kill(entireProcessTree: true);
#endif
            }
            catch (Exception ex)
            {
                _logger.Verbose($"Hook timeout kill failed for '{fileName}': {ex.Message}");
            }

            try
            {
                // Bound stream draining after kill so timeout handling cannot hang on net472 pipe reads.
                Task.WhenAll(stdoutTask, stderrTask).Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Best effort after timeout; return whatever stream data completed.
            }

            var stdout = TryGetCompletedOutput(stdoutTask);
            var stderr = TryGetCompletedOutput(stderrTask);
            return (-1, stdout, stderr, fileName, TimedOut: true);
        }

#if NET472
        process.WaitForExit(30_000);
#else
        process.WaitForExit();
#endif
        cancellationToken.ThrowIfCancellationRequested();
        return (
            process.ExitCode,
            stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult(),
            fileName,
            TimedOut: false);
    }

    private static string TryGetCompletedOutput(Task<string> task)
    {
        return task.IsCompleted && !task.IsFaulted && !task.IsCanceled
            ? task.GetAwaiter().GetResult()
            : string.Empty;
    }
}
