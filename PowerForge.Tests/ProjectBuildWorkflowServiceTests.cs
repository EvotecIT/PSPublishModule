using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectBuildWorkflowServiceTests
{
    [Fact]
    public void Execute_returns_plan_result_when_execution_is_skipped()
    {
        var plan = new DotNetRepositoryReleaseResult { Success = true };
        plan.Projects.Add(new DotNetRepositoryProjectResult { ProjectName = "ProjectA", IsPackable = true });
        var executeCalls = 0;
        var progress = new RecordingProjectBuildProgress();
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
            executeBuild: false,
            progress: progress);

        Assert.Equal(1, executeCalls);
        Assert.True(workflow.Result.Success);
        Assert.Same(plan, workflow.Result.Release);
        Assert.Null(workflow.GitHubPublishSummary);
        Assert.Equal(
            new[] { "start:Plan:1", "complete:Plan" },
            progress.Events);
    }

    private sealed class RecordingProjectBuildProgress : IProjectBuildProgressReporter
    {
        public List<string> Events { get; } = new();

        public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null)
            => Events.Add($"start:{phase}:{totalItems}");

        public void PhaseUpdated(ProjectBuildProgressPhase phase, int completedItems, int totalItems, string? detail = null)
            => Events.Add($"update:{phase}:{completedItems}/{totalItems}");

        public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null)
            => Events.Add($"complete:{phase}");

        public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null)
            => Events.Add($"fail:{phase}");
    }

    [Fact]
    public void Execute_returns_preflight_failure_before_release_execution()
    {
        var executeCalls = 0;
        var remotePublishAttempts = 0;
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
            executeBuild: true,
            remotePublishAttempted: () => remotePublishAttempts++,
            coordinatedReleaseCheckpointActive: true);

        Assert.Equal(1, executeCalls);
        Assert.Equal(0, remotePublishAttempts);
        Assert.False(workflow.Result.Success);
        Assert.Contains("GitHubAccessToken", workflow.Result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_runs_release_and_github_publish_when_requested()
    {
        var callIndex = 0;
        var remotePublishAttempts = 0;
        var remoteAttemptRecordedBeforePublish = false;
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
                remoteAttemptRecordedBeforePublish = remotePublishAttempts == 1;
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
            executeBuild: true,
            remotePublishAttempted: () => remotePublishAttempts++);

        Assert.Equal(2, callIndex);
        Assert.Equal(1, remotePublishAttempts);
        Assert.True(remoteAttemptRecordedBeforePublish);
        Assert.True(workflow.Result.Success);
        Assert.Single(workflow.Result.GitHub);
        Assert.NotNull(workflow.GitHubPublishSummary);
        Assert.Equal("v1.2.3", workflow.GitHubPublishSummary!.SummaryTag);
        Assert.Contains(logger.SuccessMessages, message => message.Contains("Project build plan prepared in", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logger.SuccessMessages, message => message.Contains("Project build release execution completed in", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logger.SuccessMessages, message => message.Contains("GitHub publish completed in", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("PerProject", "Reuse", null, null, "GitHubReleaseMode 'Single'")]
    [InlineData("Single", "AppendUtcTimestamp", null, null, "AppendUtcTimestamp")]
    [InlineData("Single", "Fail", null, null, "GitHubTagConflictPolicy 'Reuse'")]
    [InlineData("Single", "Reuse", null, "{Repo}-v{UtcTimestamp}", "stable GitHub tag")]
    [InlineData("Single", "Reuse", null, "{Repo}-{Date}", "stable GitHub tag")]
    public void Execute_rejects_nonreplayable_coordinated_github_settings_before_release_execution(
        string releaseMode,
        string conflictPolicy,
        string? tagName,
        string? tagTemplate,
        string expectedMessage)
    {
        var executeCalls = 0;
        var publishCalls = 0;
        var remotePublishAttempts = 0;
        var service = new ProjectBuildWorkflowService(
            new NullLogger(),
            executeRelease: spec =>
            {
                executeCalls++;
                Assert.True(spec.WhatIf);
                return new DotNetRepositoryReleaseResult { Success = true };
            },
            publishGitHub: _ =>
            {
                publishCalls++;
                return new ProjectBuildGitHubPublishSummary { Success = true };
            },
            validateGitHubPreflight: (_, _, _) => throw new InvalidOperationException("Unsafe settings must fail before GitHub preflight."));

        var workflow = service.Execute(
            new ProjectBuildConfiguration
            {
                PublishGitHub = true,
                GitHubAccessToken = "token",
                GitHubUsername = "EvotecIT",
                GitHubRepositoryName = "PSPublishModule",
                GitHubReleaseMode = releaseMode,
                GitHubTagConflictPolicy = conflictPolicy,
                GitHubTagName = tagName,
                GitHubTagTemplate = tagTemplate
            },
            Directory.GetCurrentDirectory(),
            new ProjectBuildPreparedContext
            {
                PublishGitHub = true,
                CreateReleaseZip = true,
                GitHubToken = "token",
                RootPath = Directory.GetCurrentDirectory(),
                Spec = new DotNetRepositoryReleaseSpec { RootPath = Directory.GetCurrentDirectory() }
            },
            executeBuild: true,
            remotePublishAttempted: () => remotePublishAttempts++,
            coordinatedReleaseCheckpointActive: true);

        Assert.Equal(1, executeCalls);
        Assert.Equal(0, publishCalls);
        Assert.Equal(0, remotePublishAttempts);
        Assert.False(workflow.Result.Success);
        Assert.Contains(expectedMessage, workflow.Result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_allows_nonreplayable_github_settings_without_coordinated_checkpoint()
    {
        var executeCalls = 0;
        var publishCalls = 0;
        var remotePublishAttempts = 0;
        var service = new ProjectBuildWorkflowService(
            new NullLogger(),
            executeRelease: spec =>
            {
                executeCalls++;
                if (spec.WhatIf)
                    return new DotNetRepositoryReleaseResult { Success = true };

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
            publishGitHub: _ =>
            {
                publishCalls++;
                return new ProjectBuildGitHubPublishSummary { Success = true };
            },
            validateGitHubPreflight: (_, _, _) => null);

        var workflow = service.Execute(
            new ProjectBuildConfiguration
            {
                PublishGitHub = true,
                GitHubAccessToken = "token",
                GitHubUsername = "EvotecIT",
                GitHubRepositoryName = "PSPublishModule",
                GitHubReleaseMode = "PerProject",
                GitHubTagConflictPolicy = "AppendUtcTimestamp"
            },
            Directory.GetCurrentDirectory(),
            new ProjectBuildPreparedContext
            {
                PublishGitHub = true,
                CreateReleaseZip = true,
                GitHubToken = "token",
                RootPath = Directory.GetCurrentDirectory(),
                Spec = new DotNetRepositoryReleaseSpec { RootPath = Directory.GetCurrentDirectory() }
            },
            executeBuild: true,
            remotePublishAttempted: () => remotePublishAttempts++,
            coordinatedReleaseCheckpointActive: false);

        Assert.Equal(2, executeCalls);
        Assert.Equal(1, publishCalls);
        Assert.Equal(1, remotePublishAttempts);
        Assert.True(workflow.Result.Success);
    }

    [Fact]
    public void Coordinated_github_retry_safety_allows_explicit_stable_tag()
    {
        var error = ProjectBuildGitHubRetrySafety.Validate(
            new ProjectBuildConfiguration
            {
                GitHubReleaseMode = "Single",
                GitHubTagConflictPolicy = "Reuse",
                GitHubTagName = "v1.2.3",
                GitHubTagTemplate = "{Repo}-v{UtcTimestamp}"
            },
            CreateMixedVersionRelease());

        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{Repo}-v{Version}")]
    [InlineData("{Repo}-v{PrimaryVersion}")]
    public void Coordinated_github_retry_safety_rejects_implicit_date_fallback_for_mixed_versions(
        string? tagTemplate)
    {
        var error = ProjectBuildGitHubRetrySafety.Validate(
            new ProjectBuildConfiguration
            {
                GitHubReleaseMode = "Single",
                GitHubTagConflictPolicy = "Reuse",
                GitHubTagTemplate = tagTemplate
            },
            CreateMixedVersionRelease());

        Assert.Contains("no single base version", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Coordinated_github_retry_safety_allows_stable_template_without_base_version()
    {
        var error = ProjectBuildGitHubRetrySafety.Validate(
            new ProjectBuildConfiguration
            {
                GitHubReleaseMode = "Single",
                GitHubTagConflictPolicy = "Reuse",
                GitHubTagTemplate = "{Repo}-coordinated"
            },
            CreateMixedVersionRelease());

        Assert.Null(error);
    }

    [Fact]
    public void Coordinated_github_retry_safety_allows_matching_primary_project_version()
    {
        var error = ProjectBuildGitHubRetrySafety.Validate(
            new ProjectBuildConfiguration
            {
                GitHubReleaseMode = "Single",
                GitHubTagConflictPolicy = "Reuse",
                GitHubPrimaryProject = "ProjectA",
                GitHubTagTemplate = "{Repo}-v{PrimaryVersion}"
            },
            CreateMixedVersionRelease());

        Assert.Null(error);
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

    private static DotNetRepositoryReleaseResult CreateMixedVersionRelease()
        => new()
        {
            Success = true,
            Projects =
            {
                new DotNetRepositoryProjectResult
                {
                    ProjectName = "ProjectA",
                    IsPackable = true,
                    NewVersion = "1.2.3"
                },
                new DotNetRepositoryProjectResult
                {
                    ProjectName = "ProjectB",
                    IsPackable = true,
                    NewVersion = "2.0.0"
                }
            }
        };

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
