using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectBuildWorkflowServiceTests
{
    [Fact]
    public void Execute_returns_plan_result_when_execution_is_skipped()
    {
        var plan = new DotNetRepositoryReleaseResult { Success = true };
        var executeCalls = 0;
        var service = new ProjectBuildWorkflowService(
            new NullLogger(),
            executeRelease: spec =>
            {
                executeCalls++;
                Assert.True(spec.WhatIf);
                return plan;
            });

        var workflow = service.Execute(
            new ProjectBuildConfiguration(),
            Directory.GetCurrentDirectory(),
            new ProjectBuildPreparedContext
            {
                PlanOnly = false,
                RootPath = Directory.GetCurrentDirectory(),
                Spec = new DotNetRepositoryReleaseSpec { RootPath = Directory.GetCurrentDirectory() }
            },
            executeBuild: false);

        Assert.Equal(1, executeCalls);
        Assert.True(workflow.Result.Success);
        Assert.Same(plan, workflow.Result.Release);
        Assert.Null(workflow.GitHubPublishSummary);
    }

    [Fact]
    public void Execute_returns_preflight_failure_before_release_execution()
    {
        var executeCalls = 0;
        var service = new ProjectBuildWorkflowService(
            new NullLogger(),
            executeRelease: spec =>
            {
                executeCalls++;
                return new DotNetRepositoryReleaseResult { Success = true };
            });

        var workflow = service.Execute(
            new ProjectBuildConfiguration
            {
                PublishGitHub = true,
                GitHubUsername = "EvotecIT",
                GitHubRepositoryName = "PSPublishModule"
            },
            Directory.GetCurrentDirectory(),
            new ProjectBuildPreparedContext
            {
                PublishGitHub = true,
                CreateReleaseZip = true,
                RootPath = Directory.GetCurrentDirectory(),
                Spec = new DotNetRepositoryReleaseSpec { RootPath = Directory.GetCurrentDirectory() }
            },
            executeBuild: true);

        Assert.Equal(1, executeCalls);
        Assert.False(workflow.Result.Success);
        Assert.Contains("GitHubAccessToken", workflow.Result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_runs_release_and_github_publish_when_requested()
    {
        var callIndex = 0;
        var service = new ProjectBuildWorkflowService(
            new NullLogger(),
            executeRelease: spec =>
            {
                callIndex++;
                if (callIndex == 1)
                {
                    Assert.True(spec.WhatIf);
                    return new DotNetRepositoryReleaseResult { Success = true };
                }

                Assert.False(spec.WhatIf);
                return new DotNetRepositoryReleaseResult
                {
                    Success = true,
                    Projects =
                    {
                        new DotNetRepositoryProjectResult
                        {
                            ProjectName = "ProjectA",
                            IsPackable = true,
                            NewVersion = "1.2.3",
                            ReleaseZipPath = "ProjectA.1.2.3.zip"
                        }
                    }
                };
            },
            publishGitHub: request =>
            {
                Assert.Equal("EvotecIT", request.Owner);
                Assert.Equal("PSPublishModule", request.Repository);
                Assert.Equal("token", request.Token);
                return new ProjectBuildGitHubPublishSummary
                {
                    Success = true,
                    SummaryTag = "v1.2.3",
                    Results =
                    {
                        new ProjectBuildGitHubResult
                        {
                            ProjectName = "ProjectA",
                            Success = true,
                            TagName = "v1.2.3",
                            ReleaseUrl = "https://example.test/v1.2.3"
                        }
                    }
                };
            },
            validateGitHubPreflight: (_, _, _) => null);

        var workflow = service.Execute(
            new ProjectBuildConfiguration
            {
                PublishGitHub = true,
                GitHubAccessToken = "token",
                GitHubUsername = "EvotecIT",
                GitHubRepositoryName = "PSPublishModule"
            },
            Directory.GetCurrentDirectory(),
            new ProjectBuildPreparedContext
            {
                PublishGitHub = true,
                CreateReleaseZip = true,
                GitHubToken = "token",
                RootPath = Directory.GetCurrentDirectory(),
                Spec = new DotNetRepositoryReleaseSpec { RootPath = Directory.GetCurrentDirectory(), PublishFailFast = true }
            },
            executeBuild: true);

        Assert.Equal(2, callIndex);
        Assert.True(workflow.Result.Success);
        Assert.Single(workflow.Result.GitHub);
        Assert.NotNull(workflow.GitHubPublishSummary);
        Assert.Equal("v1.2.3", workflow.GitHubPublishSummary!.SummaryTag);
    }
}
