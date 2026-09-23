using PowerForge;
using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Tests;

public sealed class WorkspaceProjectDiscoveryTests
{
    [Fact]
    public async Task DiscoverAsync_ListsBuildEnabledLocalFolderBesideGitRepository()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "studio-local-project-discovery-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var local = Directory.CreateDirectory(Path.Combine(workspace, "LocalBuild")).FullName;
            var build = Directory.CreateDirectory(Path.Combine(local, "Build")).FullName;
            await File.WriteAllTextAsync(Path.Combine(build, "project.build.json"), "{}");
            var git = Directory.CreateDirectory(Path.Combine(workspace, "GitProject")).FullName;
            Assert.True((await new GitClient().RunRawAsync(git, ["init", "-b", "main"])).Succeeded);
            var gitNestedBuild = Directory.CreateDirectory(Path.Combine(git, "Library", "Build")).FullName;
            await File.WriteAllTextAsync(Path.Combine(gitNestedBuild, "project.build.json"), "{}");
            Directory.CreateDirectory(Path.Combine(workspace, "UnrelatedFolder"));
            var containerBuild = Directory.CreateDirectory(Path.Combine(workspace, "_worktrees", "NestedBuild", "Build")).FullName;
            await File.WriteAllTextAsync(Path.Combine(containerBuild, "project.build.json"), "{}");
            var unrelatedNestedBuild = Directory.CreateDirectory(Path.Combine(workspace, "Collection", "NestedBuild", "Build")).FullName;
            await File.WriteAllTextAsync(Path.Combine(unrelatedNestedBuild, "project.build.json"), "{}");

            var source = new WorkspaceRepositorySource();
            var entries = await source.DiscoverAsync(workspace);

            Assert.Equal(new[] { "GitProject", "LocalBuild" }, entries.Select(static entry => entry.Name));
            Assert.True(Assert.Single(entries, entry => entry.Name == "GitProject").IsReleaseManaged);
            Assert.True(Assert.Single(entries, entry => entry.Name == "LocalBuild").IsReleaseManaged);
            Assert.Equal(local, Assert.Single(await source.DiscoverAsync(local)).RootPath);
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }
}
