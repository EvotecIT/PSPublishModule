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
