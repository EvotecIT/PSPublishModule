using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Projects;

namespace PowerForgeStudio.Tests;

public sealed class ProjectOverviewServiceTests : IDisposable
{
    private readonly string _fixture = Path.Combine(Path.GetTempPath(), "studio-overview-owner-" + Guid.NewGuid().ToString("N"));

    public ProjectOverviewServiceTests() => Directory.CreateDirectory(_fixture);
    public void Dispose() => Directory.Delete(_fixture, recursive: true);

    [Fact]
    public async Task InspectionMapsEntrypointsToWorkingCopyAndNeverExecutesThem()
    {
        var primary = Directory.CreateDirectory(Path.Combine(_fixture, "Primary")).FullName;
        var workingCopy = Directory.CreateDirectory(Path.Combine(_fixture, "WorkingCopy")).FullName;
        Directory.CreateDirectory(Path.Combine(primary, "Build"));
        Directory.CreateDirectory(Path.Combine(workingCopy, "Build"));
        Directory.CreateDirectory(Path.Combine(workingCopy, ".git"));
        Directory.CreateDirectory(Path.Combine(workingCopy, "src"));
        for (var index = 0; index < 400; index++)
            await File.WriteAllTextAsync(Path.Combine(workingCopy, ".git", $"metadata-{index:D3}"), "ignored");
        var primaryConfig = Path.Combine(primary, "Build", "project.build.json");
        await File.WriteAllTextAsync(primaryConfig, "{}");
        await File.WriteAllTextAsync(Path.Combine(workingCopy, "Build", "project.build.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(workingCopy, "README.md"), "# Sample\n\nA bounded project overview for release work.\n");
        await File.WriteAllTextAsync(Path.Combine(workingCopy, "Sample.slnx"), "<Solution />");
        await File.WriteAllTextAsync(Path.Combine(workingCopy, "src", "Sample.csproj"), "<Project />");
        await File.WriteAllTextAsync(Path.Combine(workingCopy, "Build", "Build-Project.ps1"),
            "Set-Content -LiteralPath ../overview-should-not-run.txt -Value executed");
        var repository = new RepositoryCatalogEntry("Sample", primary, ReleaseRepositoryKind.Library,
            ReleaseWorkspaceKind.PrimaryRepository, null, primaryConfig, false, false);
        var git = Git(workingCopy);

        var snapshot = await new ProjectOverviewService().InspectAsync(repository, workingCopy, git);

        Assert.Equal("A bounded project overview for release work.", snapshot.Purpose);
        Assert.Equal("Worktree", snapshot.WorkspaceKind);
        Assert.Equal(Path.Combine(workingCopy, "README.md"), snapshot.ReadmePath);
        Assert.Contains(snapshot.EntryPoints, item => item.Name == "Project build" &&
            item.SourcePath == Path.Combine(workingCopy, "Build", "project.build.json"));
        Assert.Contains(snapshot.EntryPoints, item => item.Name == ".NET solution");
        Assert.Contains(snapshot.Products, item => item.Name == ".NET / package");
        Assert.Contains(snapshot.Products, item => item.Name == ".NET projects" && item.Detail.StartsWith("1 ", StringComparison.Ordinal));
        Assert.Contains(snapshot.Prerequisites, item => item.Name == "PowerForge");
        Assert.Contains(snapshot.Prerequisites, item => item.Name == ".NET SDK");
        Assert.False(File.Exists(Path.Combine(workingCopy, "overview-should-not-run.txt")));
    }

    [Fact]
    public async Task OversizedReadmeAndMissingEntrypointRemainExplicit()
    {
        var root = Directory.CreateDirectory(Path.Combine(_fixture, "Unmanaged")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(root, "README.md"), new byte[257 * 1024]);
        var repository = new RepositoryCatalogEntry("Unmanaged", root, ReleaseRepositoryKind.Unknown,
            ReleaseWorkspaceKind.PrimaryRepository, null, null, false, false);

        var snapshot = await new ProjectOverviewService().InspectAsync(repository, root, Git(root));

        Assert.Contains("too large", snapshot.Purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(snapshot.Products, item => item.Name == "Unclassified");
        Assert.Empty(snapshot.EntryPoints);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("256 KiB", StringComparison.Ordinal));
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("No supported build entrypoint", StringComparison.Ordinal));
    }

    private static ProjectGitStatus Git(string root) => new(true, "feature/overview", "origin/main", 2, 1,
        0, 1, 0, [], [], [], ["feature/overview", "main"], [new(root, "feature/overview", false, false)]);
}
