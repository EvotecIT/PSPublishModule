using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Runs consumer-owned validation against the complete staged release.
/// </summary>
internal sealed class PowerForgeReleaseValidationService
{
    private static readonly JsonSerializerOptions ContextJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ILogger _logger;

    internal PowerForgeReleaseValidationService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal PowerForgeReleaseValidationResult Run(
        PowerForgeReleaseValidationAction action,
        PowerForgeReleaseValidationContext context,
        string configurationDirectory,
        CancellationToken cancellationToken)
    {
        if (action is null)
            throw new ArgumentNullException(nameof(action));
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            throw new ArgumentException("Configuration directory is required.", nameof(configurationDirectory));
        if (string.IsNullOrWhiteSpace(action.FilePath))
            throw new InvalidOperationException("A staged-release validation action requires FilePath.");
        if (action.TimeoutSeconds <= 0)
            throw new InvalidOperationException("A staged-release validation action TimeoutSeconds must be greater than zero.");

        var scriptPath = ResolvePath(configurationDirectory, action.FilePath);
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"Staged-release validation script was not found: {scriptPath}", scriptPath);

        var workingDirectory = string.IsNullOrWhiteSpace(action.WorkingDirectory)
            ? configurationDirectory
            : ResolvePath(configurationDirectory, action.WorkingDirectory!);
        if (!Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"Staged-release validation working directory was not found: {workingDirectory}");

        var actionName = string.IsNullOrWhiteSpace(action.Name)
            ? Path.GetFileNameWithoutExtension(scriptPath)
            : action.Name!.Trim();
        var contextDirectory = Path.Combine(
            Path.GetTempPath(),
            "PowerForge",
            "release-validation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contextDirectory);
        var contextPath = Path.Combine(contextDirectory, "context.json");
        context.ActionName = actionName;
        context.ContextPath = contextPath;
        File.WriteAllText(contextPath, JsonSerializer.Serialize(context, ContextJsonOptions), new UTF8Encoding(false));

        try
        {
            var executable = ResolvePowerShellExecutable(action.PreferWindowsPowerShell);
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
#if NET472
            startInfo.Arguments = $"-NoLogo -NoProfile -NonInteractive -File \"{scriptPath}\"";
#else
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", scriptPath })
                startInfo.ArgumentList.Add(argument);
#endif
            ProcessStartInfoEncoding.TryApplyUtf8(startInfo);
            foreach (var entry in action.Environment ?? new Dictionary<string, string?>())
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                    continue;
                if (entry.Value is null)
                    startInfo.EnvironmentVariables.Remove(entry.Key.Trim());
                else
                    startInfo.EnvironmentVariables[entry.Key.Trim()] = entry.Value;
            }

            startInfo.EnvironmentVariables["POWERFORGE_CONTEXT"] = contextPath;
            startInfo.EnvironmentVariables["POWERFORGE_RELEASE_STAGE"] = context.Stage;
            startInfo.EnvironmentVariables["POWERFORGE_RELEASE_VERSION"] = context.ResolvedVersion;
            SetEnvironmentValue(startInfo, "POWERFORGE_RELEASE_MANIFEST", context.ReleaseManifestPath);
            SetEnvironmentValue(startInfo, "POWERFORGE_RELEASE_CHECKSUMS", context.ReleaseChecksumsPath);
            SetEnvironmentValue(startInfo, "POWERFORGE_RELEASE_STAGING_ROOT", context.StagingRoot);
            SetEnvironmentValue(startInfo, "POWERFORGE_MODULE_STAGING_PATH", context.ModuleStagingPath);

            _logger.Info($"Running staged-release validation '{actionName}'.");
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Unable to start staged-release validation '{actionName}'.");
            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try { KillProcessTree(process); }
                catch { }
            });
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            var deadline = DateTime.UtcNow.AddSeconds(action.TimeoutSeconds);
            var timedOut = false;
            while (!process.WaitForExit(100))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow < deadline)
                    continue;

                timedOut = true;
                try { KillProcessTree(process); } catch { }
                process.WaitForExit();
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var stdout = standardOutput.GetAwaiter().GetResult();
            var stderr = standardError.GetAwaiter().GetResult();
            var result = new PowerForgeReleaseValidationResult
            {
                Name = actionName,
                Succeeded = !timedOut && process.ExitCode == 0,
                ExitCode = timedOut ? -1 : process.ExitCode,
                Executable = executable,
                FilePath = scriptPath,
                WorkingDirectory = workingDirectory,
                StdOut = stdout,
                StdErr = stderr,
                TimedOut = timedOut
            };
            if (result.Succeeded)
                _logger.Success($"Staged-release validation '{actionName}' completed successfully.");
            return result;
        }
        finally
        {
            try { Directory.Delete(contextDirectory, recursive: true); } catch { }
        }
    }

    private static void SetEnvironmentValue(ProcessStartInfo startInfo, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            startInfo.EnvironmentVariables.Remove(name);
        else
            startInfo.EnvironmentVariables[name] = value;
    }

    private static void KillProcessTree(Process process)
    {
        if (process.HasExited)
            return;

#if NET472
        if (Path.DirectorySeparatorChar == '\\')
        {
            using var taskKill = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = $"/PID {process.Id} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            taskKill?.WaitForExit();
            return;
        }

        process.Kill();
#else
        process.Kill(entireProcessTree: true);
#endif
    }

    private static string ResolvePowerShellExecutable(bool preferWindowsPowerShell)
    {
        if (Path.DirectorySeparatorChar == '\\' && preferWindowsPowerShell)
        {
            var windowsPowerShell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            if (File.Exists(windowsPowerShell))
                return windowsPowerShell;
        }

        return "pwsh";
    }

    private static string ResolvePath(string baseDirectory, string path)
        => Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(baseDirectory, path));

}
