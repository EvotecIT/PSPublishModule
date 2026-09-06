using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectBuildVersionBindingPublicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_binding_release_can_plan_then_build_and_publish_to_local_feed(bool executeBuild)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-binding-publish-" + Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Example.Tool"));
            var projectPath = Path.Combine(project.FullName, "Example.Tool.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Version>1.2.3</Version>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """);
            var bindingPath = Path.Combine(root.FullName, "tool.txt");
            File.WriteAllText(bindingPath, "Example.Tool@1.2.3");
            var feed = Directory.CreateDirectory(Path.Combine(root.FullName, "feed")).FullName;
            var spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersion = "1.2.4",
                UpdateVersions = true,
                Pack = true,
                Publish = true,
                PublishApiKey = "local-test",
                PublishSource = feed,
                VersionSources = new[] { feed },
                OutputPath = Path.Combine(root.FullName, "packages"),
                VersionBindings = new[]
                {
                    new ProjectVersionBinding
                    {
                        Path = "tool.txt",
                        Project = "Example.Tool",
                        Pattern = @"(?<=Example\.Tool@)\d+\.\d+\.\d+"
                    }
                }
            };
            var result = new ProjectBuildWorkflowService(new NullLogger()).Execute(
                new ProjectBuildConfiguration(),
                root.FullName,
                new ProjectBuildPreparedContext
                {
                    RootPath = root.FullName,
                    Spec = spec,
                    PublishNuget = true,
                    PublishApiKey = "local-test"
                },
                executeBuild: executeBuild).Result;

            Assert.True(result.Success, result.ErrorMessage);
            var release = Assert.IsType<DotNetRepositoryReleaseResult>(result.Release);
            Assert.Equal(!executeBuild, release.IsPlan);
            Assert.Equal(!executeBuild, release.PublishOrderDeferred);
            Assert.Equal(executeBuild ? "Example.Tool@1.2.4" : "Example.Tool@1.2.3", File.ReadAllText(bindingPath));
            Assert.Equal(executeBuild, Directory.EnumerateFiles(feed, "*.nupkg", SearchOption.AllDirectories).Any());
            if (executeBuild)
                Assert.Single(release.PublishedPackages);
            else
            {
                Assert.Empty(release.PublishedPackages);
                Assert.Contains("<Version>1.2.3</Version>", File.ReadAllText(projectPath));
                var summary = new DotNetRepositoryReleaseSummaryService().CreateSummary(release);
                var display = new DotNetRepositoryReleaseDisplayService().CreateDisplay(summary, isPlan: true);
                Assert.Contains(display.Totals, row => row.Label == "Publish order" && row.Value.Contains("Deferred"));
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Preview_summary_reports_planned_projects_and_overall_failure(bool success)
    {
        var result = new DotNetRepositoryReleaseResult { IsPlan = true, Success = success };
        result.Projects.Add(new DotNetRepositoryProjectResult
        {
            ProjectName = "Example.Tool",
            IsPackable = true,
            Packages = new() { "Example.Tool.1.2.4.nupkg" }
        });
        var summary = new DotNetRepositoryReleaseSummaryService().CreateSummary(result);
        var display = new DotNetRepositoryReleaseDisplayService().CreateDisplay(summary, isPlan: false);
        Assert.Equal(success, display.Success);
        Assert.True(display.IsPlan);
        Assert.Equal(success ? "Plan" : "Plan failed", display.Title);
        Assert.Equal("Planned", Assert.Single(display.Projects).StatusText);
        Assert.Contains(display.Totals, row => row.Label == "Planned packages" && row.Value == "1");
    }
}
