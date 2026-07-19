using System.Diagnostics;

namespace PowerForge;

/// <summary>
/// Generates Xcode projects from a checked-in XcodeGen project.yml.
/// </summary>
internal sealed class AppleProjectGenerationService
{
    private readonly Func<ProcessStartInfo, int, AppleProjectGenerationProcessResult> _runProcess;

    internal AppleProjectGenerationService(
        Func<ProcessStartInfo, int, AppleProjectGenerationProcessResult>? runProcess = null)
    {
        _runProcess = runProcess ?? RunProcess;
    }

    internal bool Generate(PowerForgeAppleAppReleaseTargetPlan app)
    {
        if (app is null)
            throw new ArgumentNullException(nameof(app));
        if (!app.GenerateProjectIfMissing && !app.RegenerateProject)
            return false;
        if (!app.RegenerateProject && (Directory.Exists(app.ProjectPath) || File.Exists(app.ProjectPath)))
            return false;

        var projectDirectory = Path.GetDirectoryName(app.ProjectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
            throw new InvalidOperationException($"Unable to resolve project generation directory for '{app.Name}'.");
        var specPath = Path.Combine(projectDirectory, "project.yml");
        if (!File.Exists(specPath))
            throw new FileNotFoundException($"XcodeGen project specification was not found for '{app.Name}': {specPath}", specPath);
        if (string.IsNullOrWhiteSpace(app.XcodeGenExecutable))
            throw new InvalidOperationException($"XcodeGen executable is required for '{app.Name}'.");
        if (app.ProjectGenerationTimeoutSeconds <= 0)
            throw new InvalidOperationException($"Project generation timeout must be greater than zero for '{app.Name}'.");

        var startInfo = new ProcessStartInfo
        {
            FileName = app.XcodeGenExecutable,
            Arguments = "generate",
            WorkingDirectory = projectDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(startInfo);

        var result = _runProcess(startInfo, app.ProjectGenerationTimeoutSeconds);
        if (result.TimedOut)
            throw new TimeoutException($"XcodeGen timed out for '{app.Name}' after {app.ProjectGenerationTimeoutSeconds} seconds.");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"XcodeGen failed for '{app.Name}' with exit code {result.ExitCode}: " +
                $"{result.StandardError.Trim()} {result.StandardOutput.Trim()}".Trim());
        }
        if (!Directory.Exists(app.ProjectPath) && !File.Exists(app.ProjectPath))
            throw new InvalidOperationException($"XcodeGen completed but did not create expected project '{app.ProjectPath}'.");
        return true;
    }

    private static AppleProjectGenerationProcessResult RunProcess(ProcessStartInfo startInfo, int timeoutSeconds)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start XcodeGen executable '{startInfo.FileName}'.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // The timeout remains the actionable failure.
            }
            return new AppleProjectGenerationProcessResult
            {
                TimedOut = true
            };
        }

        Task.WaitAll(standardOutput, standardError);
        return new AppleProjectGenerationProcessResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = standardOutput.Result,
            StandardError = standardError.Result
        };
    }
}

internal sealed class AppleProjectGenerationProcessResult
{
    public int ExitCode { get; set; }

    public string StandardOutput { get; set; } = string.Empty;

    public string StandardError { get; set; } = string.Empty;

    public bool TimedOut { get; set; }
}
