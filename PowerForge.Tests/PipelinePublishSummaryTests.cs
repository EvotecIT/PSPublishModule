using PowerForge.ConsoleShared;

namespace PowerForge.Tests;

public sealed class PipelinePublishSummaryTests
{
    [Fact]
    public void Build_combines_nuget_package_github_psgallery_and_module_github_results()
    {
        const string corePackage = @"C:\artifacts\DBAClientX.Core.0.12.0.nupkg";
        const string sqlServerPackage = @"C:\artifacts\DBAClientX.SqlServer.0.11.0.nupkg";
        const string postgreSqlPackage = @"C:\artifacts\DBAClientX.PostgreSql.0.11.0.nupkg";
        const string mySqlPackage = @"C:\artifacts\DBAClientX.MySql.0.11.0.nupkg";
        const string sqlitePackage = @"C:\artifacts\DBAClientX.SQLite.0.11.0.nupkg";
        const string oraclePackage = @"C:\artifacts\DBAClientX.Oracle.0.11.0.nupkg";
        var release = new DotNetRepositoryReleaseResult
        {
            Success = true,
            PublishSource = "https://api.nuget.org/v3/index.json"
        };
        release.Projects.Add(CreateProject("DbaClientX.Core", "DBAClientX.Core", "0.12.0", corePackage, @"C:\artifacts\DBAClientX.Core.0.12.0.zip"));
        release.Projects.Add(CreateProject("DbaClientX.SqlServer", "DBAClientX.SqlServer", "0.11.0", sqlServerPackage, @"C:\artifacts\DBAClientX.SqlServer.0.11.0.zip"));
        release.Projects.Add(CreateProject("DbaClientX.PostgreSql", "DBAClientX.PostgreSql", "0.11.0", postgreSqlPackage, @"C:\artifacts\DBAClientX.PostgreSql.0.11.0.zip"));
        release.Projects.Add(CreateProject("DbaClientX.MySql", "DBAClientX.MySql", "0.11.0", mySqlPackage, @"C:\artifacts\DBAClientX.MySql.0.11.0.zip"));
        release.Projects.Add(CreateProject("DbaClientX.SQLite", "DBAClientX.SQLite", "0.11.0", sqlitePackage, @"C:\artifacts\DBAClientX.SQLite.0.11.0.zip"));
        release.Projects.Add(CreateProject("DbaClientX.Oracle", "DBAClientX.Oracle", "0.11.0", oraclePackage, @"C:\artifacts\DBAClientX.Oracle.0.11.0.zip"));
        release.PublishedPackages.Add(corePackage);
        release.PublishedPackages.Add(sqlServerPackage);
        release.PublishedPackages.Add(postgreSqlPackage);
        release.PublishedPackages.Add(mySqlPackage);
        release.PublishedPackages.Add(oraclePackage);
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
        packageBuild.Result.GitHub.Add(CreateProjectGitHub("DbaClientX.SqlServer"));
        packageBuild.Result.GitHub.Add(CreateProjectGitHub("DbaClientX.PostgreSql"));
        packageBuild.Result.GitHub.Add(CreateProjectGitHub("DbaClientX.MySql"));
        packageBuild.Result.GitHub.Add(CreateProjectGitHub("DbaClientX.SQLite"));
        packageBuild.Result.GitHub.Add(CreateProjectGitHub("DbaClientX.Oracle"));

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
                new[]
                {
                    "module.zip",
                    corePackage,
                    sqlServerPackage,
                    postgreSqlPackage,
                    mySqlPackage,
                    sqlitePackage,
                    oraclePackage
                },
                "https://github.com/EvotecIT/DbaClientX/releases/tag/DbaClientX-PowerShellModule.v1.0.2",
                true,
                null)
        };

        var summary = PipelinePublishSummaryBuilder.Build(
            "DbaClientX",
            modulePublishes,
            new[] { packageBuild });

        Assert.Equal(9, summary.Rows.Count);
        Assert.Equal(3, summary.ChannelCount);
        Assert.Equal(3, summary.Channels.Count);
        Assert.Contains(summary.Channels, channel => channel.Channel == "NuGet" && channel.Label == "6 packages, 5 published, 1 already existed, nuget.org, via dotnet nuget push");
        Assert.Contains(summary.Channels, channel => channel.Channel == "PowerShell Gallery" && channel.Label == "1 module, 1 published, PSGallery, via ManagedModule");
        Assert.Contains(summary.Channels, channel => channel.Channel == "GitHub" && channel.Label == "2 releases, 2 published, EvotecIT/DbaClientX, via GitHub API");
        Assert.Equal(6, summary.Rows.Count(row => row.Channel == "NuGet"));
        Assert.Equal(2, summary.Rows.Count(row => row.Channel == "GitHub"));
        Assert.Contains(summary.Rows, row => row.Result == "Published DBAClientX.Core 0.12.0" && row.Reference.EndsWith("/DBAClientX.Core/0.12.0", StringComparison.Ordinal));
        Assert.Contains(summary.Rows, row => row.Result == "Published DBAClientX.SqlServer 0.11.0" && row.Reference.EndsWith("/DBAClientX.SqlServer/0.11.0", StringComparison.Ordinal));
        Assert.Contains(summary.Rows, row => row.Result == "Published DBAClientX.PostgreSql 0.11.0" && row.Reference.EndsWith("/DBAClientX.PostgreSql/0.11.0", StringComparison.Ordinal));
        Assert.Contains(summary.Rows, row => row.Result == "Published DBAClientX.MySql 0.11.0" && row.Reference.EndsWith("/DBAClientX.MySql/0.11.0", StringComparison.Ordinal));
        Assert.Contains(summary.Rows, row => row.Result == "Already existed DBAClientX.SQLite 0.11.0" && row.Reference.EndsWith("/DBAClientX.SQLite/0.11.0", StringComparison.Ordinal));
        Assert.Contains(summary.Rows, row => row.Result == "Published DBAClientX.Oracle 0.11.0" && row.Reference.EndsWith("/DBAClientX.Oracle/0.11.0", StringComparison.Ordinal));
        Assert.Contains(summary.Rows, row => row.Channel == "PowerShell Gallery" && row.Method == "ManagedModule" && row.Result == "Published DbaClientX 1.0.2");
        Assert.Single(summary.Rows, row => row.Result == "Published DbaClientX-v20260712082246 (6 assets)");
        Assert.Contains(summary.Rows, row => row.Result == "Published DbaClientX-PowerShellModule.v1.0.2 (7 assets)");
    }

    [Fact]
    public void Build_redacts_private_feed_credentials_from_targets_and_references()
    {
        const string package = @"C:\artifacts\Company.Tools.1.0.0.nupkg";
        var release = new DotNetRepositoryReleaseResult
        {
            Success = true,
            PublishSource = "https://user:token@feed.example/v3/index.json"
        };
        release.Projects.Add(CreateProject("Company.Tools", "Company.Tools", "1.0.0", package, @"C:\artifacts\Company.Tools.1.0.0.zip"));
        release.PublishedPackages.Add(package);
        var build = new ProjectBuildHostExecutionResult
        {
            Success = true,
            Result = new ProjectBuildResult
            {
                Success = true,
                Release = release
            }
        };

        var summary = PipelinePublishSummaryBuilder.Build(
            "Company.Tools",
            Array.Empty<ModulePublishResult>(),
            new[] { build });

        var row = Assert.Single(summary.Rows);
        Assert.Equal("https://feed.example/v3/index.json", row.Target);
        Assert.Equal("https://feed.example/v3/index.json", row.Reference);
        Assert.DoesNotContain("user", summary.Channels[0].Label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", summary.Channels[0].Label, StringComparison.OrdinalIgnoreCase);
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
