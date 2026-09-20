using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class StudioDeclarativePlanTests
{
    [Fact]
    public void DirectBuildContractWinsOverNestedContractAndSiblingJsonWinsOverScript()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Build"));
        Directory.CreateDirectory(Path.Combine(root, "child", "Build"));
        try
        {
            var script = Path.Combine(root, "Build", "Build-Project.ps1");
            var json = Path.Combine(root, "Build", "project.build.json");
            File.WriteAllText(script, "# Test fixture");
            File.WriteAllText(Path.Combine(root, "child", "Build", "project.build.json"), "{}");
            var scanner = new RepositoryCatalogScanner();
            Assert.Equal(script, scanner.InspectRepository(root).ProjectBuildScriptPath);
            File.WriteAllText(json, "{}");
            Assert.Equal(json, scanner.InspectRepository(root).ProjectBuildScriptPath);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ProjectJsonWithoutPowerShellWrapperIsDiscoveredAndInvalidPlanFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "studio-plan-" + Guid.NewGuid().ToString("N"));
        var build = Directory.CreateDirectory(Path.Combine(root, "Build")).FullName;
        var config = Path.Combine(build, "project.build.json");
        try
        {
            await File.WriteAllTextAsync(config, "not valid JSON");
            var repository = new RepositoryCatalogScanner().InspectRepository(root);
            Assert.True(repository.IsReleaseManaged);
            Assert.Equal(config, repository.ProjectBuildScriptPath);
            var planner = new RepositoryPlanPreviewService();
            var results = await planner.PlanRepositoryAsync(repository);
            Assert.Equal(RepositoryPlanStatus.Failed, Assert.Single(results).Status);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => planner.PlanRepositoryAsync(repository, cancellation.Token));
            Assert.Equal("not valid JSON", await File.ReadAllTextAsync(config));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            var plans = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PowerForgeStudio", "plans", Path.GetFileName(root));
            if (Directory.Exists(plans)) Directory.Delete(plans, recursive: true);
        }
    }
}
