using System.Diagnostics;
using PowerForge;
using PowerForgeStudio.Domain.Hub;

using HubProjectBuildResult = PowerForgeStudio.Domain.Hub.ProjectBuildResult;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed class ProjectBuildService
{
    private readonly IProcessRunner _processRunner;

    public ProjectBuildService()
        : this(new ProcessRunner())
    {
    }

    public ProjectBuildService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<HubProjectBuildResult> RunBuildAsync(
        ProjectEntry project,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var (executable, arguments) = ResolveBuildCommand(project);
            progress?.Report($"Starting build: {executable} {string.Join(' ', arguments)}");

            var result = await _processRunner.RunAsync(
                new ProcessRunRequest(executable, project.RootPath, arguments, TimeSpan.FromMinutes(10)),
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            progress?.Report(result.Succeeded ? "Build completed successfully." : "Build failed.");

            return new HubProjectBuildResult(
                ProjectName: project.Name,
                ScriptKind: project.BuildScriptKind,
                ScriptPath: project.PrimaryBuildScriptPath ?? project.RootPath,
                Succeeded: result.Succeeded,
                Output: result.StdOut,
                Error: result.StdErr,
                DurationSeconds: stopwatch.Elapsed.TotalSeconds,
                CompletedAtUtc: DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new HubProjectBuildResult(
                ProjectName: project.Name,
                ScriptKind: project.BuildScriptKind,
                ScriptPath: project.PrimaryBuildScriptPath ?? project.RootPath,
                Succeeded: false,
                Output: string.Empty,
                Error: exception.Message,
                DurationSeconds: stopwatch.Elapsed.TotalSeconds,
                CompletedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    public async Task<HubProjectBuildResult> RunBuildStreamingAsync(
        ProjectEntry project,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var (executable, arguments) = ResolveBuildCommand(project);
        var outputBuilder = new System.Text.StringBuilder();

        onOutputLine($"> {executable} {string.Join(' ', arguments)}");

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = project.RootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var tcs = new TaskCompletionSource<int>();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    outputBuilder.AppendLine(e.Data);
                    onOutputLine(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    outputBuilder.AppendLine(e.Data);
                    onOutputLine(e.Data);
                }
            };

            process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var reg = cancellationToken.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                tcs.TrySetCanceled();
            });

            var exitCode = await tcs.Task.ConfigureAwait(false);
            stopwatch.Stop();
            var succeeded = exitCode == 0;

            onOutputLine(succeeded ? $"\nBuild succeeded ({stopwatch.Elapsed.TotalSeconds:F1}s)" : $"\nBuild failed with exit code {exitCode}");

            return new HubProjectBuildResult(
                ProjectName: project.Name,
                ScriptKind: project.BuildScriptKind,
                ScriptPath: project.PrimaryBuildScriptPath ?? project.RootPath,
                Succeeded: succeeded,
                Output: outputBuilder.ToString(),
                Error: succeeded ? null : $"Exit code: {exitCode}",
                DurationSeconds: stopwatch.Elapsed.TotalSeconds,
                CompletedAtUtc: DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            onOutputLine($"\nBuild error: {exception.Message}");
            return new HubProjectBuildResult(
                ProjectName: project.Name,
                ScriptKind: project.BuildScriptKind,
                ScriptPath: project.PrimaryBuildScriptPath ?? project.RootPath,
                Succeeded: false,
                Output: outputBuilder.ToString(),
                Error: exception.Message,
                DurationSeconds: stopwatch.Elapsed.TotalSeconds,
                CompletedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    private static (string Executable, IReadOnlyList<string> Arguments) ResolveBuildCommand(ProjectEntry project)
    {
        if (!string.IsNullOrWhiteSpace(project.PrimaryBuildScriptPath))
        {
            return ("pwsh", ["-NoProfile", "-NonInteractive", "-File", project.PrimaryBuildScriptPath]);
        }

        if (project.HasPowerForgeJson)
        {
            return ("powerforge", ["run", "--config", Path.Combine(project.RootPath, "powerforge.json")]);
        }

        if (project.HasSolution)
        {
            return ("dotnet", ["build", project.RootPath, "-c", "Release"]);
        }

        throw new InvalidOperationException($"No build script detected for project '{project.Name}'.");
    }
}
