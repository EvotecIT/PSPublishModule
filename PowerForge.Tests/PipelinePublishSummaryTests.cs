using PowerForge.ConsoleShared;

namespace PowerForge.Tests;

public sealed class PipelinePublishSummaryTests
{
    [Fact]
    public void Build_combines_nuget_package_github_psgallery_and_module_github_results()
    {
        const string corePackage = @"C:\artifacts\DbaClientX.Core.0.12.0.nupkg";
        const string sqlitePackage = @"C:\artifacts\DbaClientX.SQLite.0.11.0.nupkg";
        var release = new DotNetRepositoryReleaseResult
        {
            Success = true,
            PublishSource = "https://api.nuget.org/v3/index.json"
        };
        release.Projects.Add(CreateProject("DbaClientX.Core", "DbaClientX.Core", "0.12.0", corePackage, @"C:\artifacts\DbaClientX.Core.0.12.0.zip"));
        release.Projects.Add(CreateProject("DbaClientX.SQLite", "DbaClientX.SQLite", "0.11.0", sqlitePackage, @"C:\artifacts\DbaClientX.SQLite.0.11.0.zip"));
        release.PublishedPackages.Add(corePackage);
        release.SkippedDuplicatePackages.Add(sqlitePackage);

        var packageBuild = new ProjectBuildHostExecutionResult
        {
            Success = true,
            Result = new ProjectBuildResult
            {
                Success = true,
                Release = release
            }
        };
        packageBuild.Result.GitHub.Add(CreateProjectGitHub("DbaClientX.Core"));
        packageBuild.Result.GitHub.Add(CreateProjectGitHub("DbaClientX.SQLite"));

        var modulePublishes = new[]
        {
            new ModulePublishResult(
                PublishDestination.PowerShellGallery,
                "PSGallery",
                null,
                null,
                "1.0.2",
                false,
                Array.Empty<string>(),
                null,
                true,
                null,
                PublishTool.ManagedModule),
            new ModulePublishResult(
                PublishDestination.GitHub,
                "DbaClientX",
                "EvotecIT",
                "DbaClientX-PowerShellModule.v1.0.2",
                "1.0.2",
                false,
                new[] { "module.zip", corePackage, sqlitePackage },
                "https://github.com/EvotecIT/DbaClientX/releases/tag/DbaClientX-PowerShellModule.v1.0.2",
                true,
                null)
        };

        var summary = PipelinePublishSummaryBuilder.Build(
            "DbaClientX",
            modulePublishes,
            new[] { packageBuild });

        Assert.Equal(5, summary.Rows.Count);
        Assert.Equal(3, summary.ChannelCount);
        Assert.Equal(3, summary.Channels.Count);
        Assert.Contains(summary.Channels, channel => channel.Channel == "NuGet" && channel.Label == "2 packages, 1 published, 1 already existed, nuget.org, via dotnet nuget push");
        Assert.Contains(summary.Channels, channel => channel.Channel == "PowerShell Gallery" && channel.Label == "1 module, 1 published, PSGallery, via ManagedModule");
        Assert.Contains(summary.Channels, channel => channel.Channel == "GitHub" && channel.Label == "2 releases, 2 published, EvotecIT/DbaClientX, via GitHub API");
        Assert.Equal(2, summary.Rows.Count(row => row.Channel == "NuGet"));
        Assert.Equal(2, summary.Rows.Count(row => row.Channel == "GitHub"));
        Assert.Contains(summary.Rows, row => row.Result == "Published DbaClientX.Core 0.12.0" && row.Reference.EndsWith("/DbaClientX.Core/0.12.0", StringComparison.Ordinal));
        Assert.Contains(summary.Rows, row => row.Result == "Already existed DbaClientX.SQLite 0.11.0" && row.Reference.EndsWith("/DbaClientX.SQLite/0.11.0", StringComparison.Ordinal));
        Assert.Contains(summary.Rows, row => row.Channel == "PowerShell Gallery" && row.Method == "ManagedModule" && row.Result == "Published DbaClientX 1.0.2");
        Assert.Single(summary.Rows, row => row.Result == "Published DbaClientX-v20260712082246 (2 assets)");
        Assert.Contains(summary.Rows, row => row.Result == "Published DbaClientX-PowerShellModule.v1.0.2 (3 assets)");
    }

    private static DotNetRepositoryProjectResult CreateProject(
        string projectName,
        string packageId,
        string version,
        string package,
        string releaseZip)
    {
        var result = new DotNetRepositoryProjectResult
        {
            ProjectName = projectName,
            PackageId = packageId,
            IsPackable = true,
            NewVersion = version,
            ReleaseZipPath = releaseZip
        };
        result.Packages.Add(package);
        return result;
    }

    private static ProjectBuildGitHubResult CreateProjectGitHub(string projectName)
        => new()
        {
            ProjectName = projectName,
            Success = true,
            TagName = "DbaClientX-v20260712082246",
            ReleaseUrl = "https://github.com/EvotecIT/DbaClientX/releases/tag/DbaClientX-v20260712082246"
        };
}
