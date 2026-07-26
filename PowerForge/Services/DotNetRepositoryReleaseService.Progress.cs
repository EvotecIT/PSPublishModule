using System.Diagnostics;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static Dictionary<DotNetRepositoryProjectResult, ProjectBuildProgressItem> CreatePackageProgressItems(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        IProjectBuildProgressReporterV2? progress)
    {
        var items = projects
            .Select((project, index) => new
            {
                Project = project,
                Item = new ProjectBuildProgressItem
                {
                    Phase = ProjectBuildProgressPhase.PackageBuild,
                    Key = $"package:{project.ProjectName}",
                    Title = project.ProjectName,
                    Kind = nameof(ProjectBuildProgressPhase.PackageBuild),
                    Position = index + 1,
                    Total = projects.Count
                }
            })
            .ToDictionary(entry => entry.Project, entry => entry.Item);
        progress?.ItemsPlanned(
            ProjectBuildProgressPhase.PackageBuild,
            items.Values.OrderBy(item => item.Position).ToArray());
        return items;
    }

    private static void CompleteProjectPackageProgress(
        DotNetRepositoryProjectResult project,
        ProjectBuildProgressItem item,
        TimeSpan duration,
        bool whatIf,
        bool cancelled,
        IProjectBuildProgressReporterV2? detailedProgress,
        IProjectBuildProgressReporter? progress,
        int completed,
        int total)
    {
        project.PackageBuildDuration = duration;
        item.Duration = duration;
        var state = cancelled
            ? ProjectBuildProgressItemState.Skipped
            : string.IsNullOrWhiteSpace(project.ErrorMessage)
                ? ProjectBuildProgressItemState.Completed
                : ProjectBuildProgressItemState.Failed;
        detailedProgress?.ItemUpdated(
            item,
            state,
            BuildProjectPackageProgressDetail(project, whatIf, cancelled));
        progress?.PhaseUpdated(
            ProjectBuildProgressPhase.PackageBuild,
            completed,
            total,
            project.ProjectName);
    }

    private static Stopwatch StartProjectPackageProgress(
        DotNetRepositoryProjectResult project,
        ProjectBuildProgressItem item,
        IProjectBuildProgressReporterV2? detailedProgress,
        IProjectBuildProgressReporter? progress,
        int completed,
        int total,
        string detail = "building packages and archives")
    {
        progress?.PhaseUpdated(
            ProjectBuildProgressPhase.PackageBuild,
            completed,
            total,
            $"Current: {project.ProjectName}");
        detailedProgress?.ItemUpdated(
            item,
            ProjectBuildProgressItemState.Started,
            detail);
        return Stopwatch.StartNew();
    }

    private static void StartMsBuildBatchProgress(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        IReadOnlyDictionary<DotNetRepositoryProjectResult, ProjectBuildProgressItem> items,
        IDictionary<DotNetRepositoryProjectResult, Stopwatch> watches,
        IProjectBuildProgressReporterV2? detailedProgress,
        IProjectBuildProgressReporter? progress,
        int total)
    {
        progress?.PhaseUpdated(
            ProjectBuildProgressPhase.PackageBuild,
            0,
            total,
            $"MSBuild batch building {projects.Count} project(s)");
        foreach (var project in projects)
        {
            watches[project] = StartProjectPackageProgress(
                project,
                items[project],
                detailedProgress,
                progress: null,
                completed: 0,
                total,
                detail: "building in MSBuild batch");
        }
    }

    private static void CompleteFailedMsBuildBatchProgress(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        IReadOnlyDictionary<DotNetRepositoryProjectResult, ProjectBuildProgressItem> items,
        IReadOnlyDictionary<DotNetRepositoryProjectResult, Stopwatch> watches,
        bool whatIf,
        IProjectBuildProgressReporterV2? detailedProgress,
        IProjectBuildProgressReporter? progress,
        int total)
    {
        var completed = 0;
        foreach (var project in projects)
        {
            completed++;
            var watch = watches[project];
            watch.Stop();
            CompleteProjectPackageProgress(
                project,
                items[project],
                watch.Elapsed,
                whatIf,
                cancelled: false,
                detailedProgress,
                progress,
                completed,
                total);
        }
    }

    private static void PauseMsBuildBatchProgress(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        IReadOnlyDictionary<DotNetRepositoryProjectResult, ProjectBuildProgressItem> items,
        IReadOnlyDictionary<DotNetRepositoryProjectResult, Stopwatch> watches,
        IProjectBuildProgressReporterV2? detailedProgress)
    {
        foreach (var project in projects)
        {
            var watch = watches[project];
            watch.Stop();
            project.PackageBuildDuration = watch.Elapsed;
            var item = items[project];
            item.Duration = watch.Elapsed;
            detailedProgress?.ItemUpdated(
                item,
                ProjectBuildProgressItemState.Started,
                "MSBuild batch complete; awaiting package collection");
        }
    }

    private static Stopwatch GetProjectPackageProgressWatch(
        DotNetRepositoryProjectResult project,
        ProjectBuildProgressItem item,
        IReadOnlyDictionary<DotNetRepositoryProjectResult, Stopwatch> watches,
        IProjectBuildProgressReporterV2? detailedProgress,
        IProjectBuildProgressReporter? progress,
        int completed,
        int total)
    {
        if (!watches.TryGetValue(project, out var watch))
            return StartProjectPackageProgress(project, item, detailedProgress, progress, completed, total);

        watch.Start();
        progress?.PhaseUpdated(
            ProjectBuildProgressPhase.PackageBuild,
            completed,
            total,
            $"Collecting: {project.ProjectName}");
        return watch;
    }

    private static string BuildProjectPackageProgressDetail(
        DotNetRepositoryProjectResult project,
        bool whatIf,
        bool cancelled)
    {
        if (cancelled)
            return "cancelled";
        if (!string.IsNullOrWhiteSpace(project.ErrorMessage))
            return project.ErrorMessage!;

        var outputs = new List<string>
        {
            $"{project.Packages.Count} package(s)"
        };
        if (project.SymbolPackages.Count > 0)
            outputs.Add($"{project.SymbolPackages.Count} symbol package(s)");
        if (!string.IsNullOrWhiteSpace(project.ReleaseZipPath))
            outputs.Add("1 archive");
        if (whatIf)
            outputs.Add("planned");
        return string.Join(", ", outputs);
    }
}
