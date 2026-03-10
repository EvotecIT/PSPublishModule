using System;
using System.Collections.Generic;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ProjectBuildGitHubPreflightTests
{
    [Fact]
    public void BuildGitHubSingleReleaseReuseAdvisory_ReturnsMessage_WhenMixedVersionsWouldReuseDifferentAssets()
    {
        var projects = CreateProjects();
        var advisory = InvokeProjectBuildCommand.BuildGitHubSingleReleaseReuseAdvisory(
            tag: "DbaClientX-v0.2.0",
            projects: projects,
            plannedAssetNames: new[]
            {
                "DbaClientX.Core.0.2.0.zip",
                "DbaClientX.MySql.0.1.0.zip",
                "DbaClientX.Oracle.0.1.0.zip",
                "DbaClientX.PostgreSql.0.1.0.zip",
                "DbaClientX.SQLite.0.1.1.zip",
                "DbaClientX.SqlServer.0.1.0.zip"
            },
            existingAssetNames: new[]
            {
                "DbaClientX.Core.0.2.0.zip",
                "DbaClientX.MySql.0.1.0.zip",
                "DbaClientX.Oracle.0.1.0.zip",
                "DbaClientX.PostgreSql.0.1.0.zip",
                "DbaClientX.SQLite.0.1.0.zip",
                "DbaClientX.SqlServer.0.1.0.zip"
            });

        Assert.NotNull(advisory);
        Assert.Contains("GitHub preflight blocked publish", advisory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DbaClientX.SQLite=0.1.1", advisory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DbaClientX.SQLite.0.1.1.zip", advisory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GitHubReleaseMode to 'PerProject'", advisory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildGitHubSingleReleaseReuseAdvisory_ReturnsNull_WhenExistingAssetsAlreadyMatchPlan()
    {
        var plannedAssets = new[]
        {
            "DbaClientX.Core.0.2.0.zip",
            "DbaClientX.MySql.0.1.0.zip",
            "DbaClientX.Oracle.0.1.0.zip",
            "DbaClientX.PostgreSql.0.1.0.zip",
            "DbaClientX.SQLite.0.1.1.zip",
            "DbaClientX.SqlServer.0.1.0.zip"
        };

        var advisory = InvokeProjectBuildCommand.BuildGitHubSingleReleaseReuseAdvisory(
            tag: "DbaClientX-v0.2.0",
            projects: CreateProjects(),
            plannedAssetNames: plannedAssets,
            existingAssetNames: plannedAssets);

        Assert.Null(advisory);
    }

    private static List<DotNetRepositoryProjectResult> CreateProjects() =>
        new()
        {
            new DotNetRepositoryProjectResult { ProjectName = "DbaClientX.Core", IsPackable = true, NewVersion = "0.2.0" },
            new DotNetRepositoryProjectResult { ProjectName = "DbaClientX.MySql", IsPackable = true, NewVersion = "0.1.0" },
            new DotNetRepositoryProjectResult { ProjectName = "DbaClientX.Oracle", IsPackable = true, NewVersion = "0.1.0" },
            new DotNetRepositoryProjectResult { ProjectName = "DbaClientX.PostgreSql", IsPackable = true, NewVersion = "0.1.0" },
            new DotNetRepositoryProjectResult { ProjectName = "DbaClientX.SQLite", IsPackable = true, NewVersion = "0.1.1" },
            new DotNetRepositoryProjectResult { ProjectName = "DbaClientX.SqlServer", IsPackable = true, NewVersion = "0.1.0" }
        };
}
