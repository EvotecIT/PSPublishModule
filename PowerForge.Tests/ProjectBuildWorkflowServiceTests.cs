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
        var logger = new RecordingLogger();
        var service = new ProjectBuildWorkflowService(
            logger,
            executeRelease: spec =>
            {
                callIndex++;
                if (callIndex == 1)
                {
                    Assert.True(spec.WhatIf);
                    var plan = new DotNetRepositoryReleaseResult { Success = true };
                    plan.ResolvedVersionsByProject["ProjectA"] = "1.2.3";
                    return plan;
                }

                Assert.False(spec.WhatIf);
                Assert.Null(spec.ExpectedVersion);
                Assert.NotNull(spec.ExpectedVersionsByProject);
                Assert.Equal("1.2.3", spec.ExpectedVersionsByProject!["ProjectA"]);
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
        Assert.Contains(logger.SuccessMessages, message => message.Contains("Project build plan prepared in", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logger.SuccessMessages, message => message.Contains("Project build release execution completed in", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logger.SuccessMessages, message => message.Contains("GitHub publish completed in", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_reports_all_project_failures_and_uses_error_severity()
    {
        var callIndex = 0;
        var logger = new RecordingLogger();
        var service = new ProjectBuildWorkflowService(
            logger,
            executeRelease: _ =>
            {
                callIndex++;
                if (callIndex == 1)
                    return new DotNetRepositoryReleaseResult { Success = true };

                var release = new DotNetRepositoryReleaseResult { Success = false };
                release.Projects.Add(new DotNetRepositoryProjectResult
                {
                    ProjectName = "ProjectA",
                    IsPackable = true,
                    ErrorMessage = "package provenance mismatch"
                });
                release.Projects.Add(new DotNetRepositoryProjectResult
                {
                    ProjectName = "ProjectB",
                    IsPackable = true,
                    ErrorMessage = "signing failed"
                });
                return release;
            });

        var workflow = service.Execute(
            new ProjectBuildConfiguration(),
            Directory.GetCurrentDirectory(),
            new ProjectBuildPreparedContext
            {
                RootPath = Directory.GetCurrentDirectory(),
                Spec = new DotNetRepositoryReleaseSpec { RootPath = Directory.GetCurrentDirectory() }
            },
            executeBuild: true);

        Assert.False(workflow.Result.Success);
        Assert.Contains("2 of 2 project(s) failed", workflow.Result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("Detail: ProjectA: package provenance mismatch", workflow.Result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("Detail: ProjectB: signing failed", workflow.Result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(logger.ErrorMessages, message =>
            message.Contains("Project build release execution failed after", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> SuccessMessages { get; } = new();
        public List<string> ErrorMessages { get; } = new();

        public bool IsVerbose => false;

        public void Info(string message) { }

        public void Success(string message) => SuccessMessages.Add(message);

        public void Warn(string message) { }

        public void Error(string message) => ErrorMessages.Add(message);

        public void Verbose(string message) { }
    }
}
