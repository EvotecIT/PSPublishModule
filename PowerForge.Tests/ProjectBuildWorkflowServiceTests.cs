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

    [Fact]
    public void Execute_reports_version_progress_while_preparing_the_plan()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample"));
            File.WriteAllText(
                Path.Combine(projectDirectory.FullName, "Sample.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Version>1.0.0</Version><IsPackable>true</IsPackable></PropertyGroup></Project>");
            var progress = new RecordingProjectBuildProgress();
            var workflow = new ProjectBuildWorkflowService(new NullLogger()).Execute(
                new ProjectBuildConfiguration(),
                root.FullName,
                new ProjectBuildPreparedContext
                {
                    PlanOnly = false,
                    RootPath = root.FullName,
                    Spec = new DotNetRepositoryReleaseSpec
                    {
                        RootPath = root.FullName,
                        Pack = false,
                        Publish = false,
                        UpdateVersions = false
                    }
                },
                executeBuild: false,
                progress: progress);

            Assert.True(workflow.Result.Success, workflow.Result.ErrorMessage);
            Assert.Contains("start:Versioning:1", progress.Events);
            Assert.Contains("update:Versioning:1/1", progress.Events);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_keeps_parallel_planning_callbacks_on_the_calling_thread_and_plan_progress_monotonic()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample"));
            File.WriteAllText(
                Path.Combine(projectDirectory.FullName, "Sample.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Version>1.0.0</Version><IsPackable>true</IsPackable></PropertyGroup></Project>");
            var ownerThreadId = Environment.CurrentManagedThreadId;
            var progress = new ThreadAffineProjectBuildProgress(ownerThreadId);
            var workflow = new ProjectBuildWorkflowService(new ThreadAffineLogger(ownerThreadId)).Execute(
                new ProjectBuildConfiguration(),
                root.FullName,
                new ProjectBuildPreparedContext
                {
                    RootPath = root.FullName,
                    Spec = new DotNetRepositoryReleaseSpec
                    {
                        RootPath = root.FullName,
                        Pack = false,
                        Publish = true,
                        PublishApiKey = "test-only",
                        PublishSource = Path.Combine(root.FullName, "feed"),
                        UpdateVersions = false
                    }
                },
                executeBuild: false,
                progress: progress);

            Assert.False(workflow.Result.Success);
            Assert.Contains("has no packages to publish", workflow.Result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True(progress.PlanUpdates.Count >= 2);
            for (var index = 1; index < progress.PlanUpdates.Count; index++)
            {
                var previous = progress.PlanUpdates[index - 1];
                var current = progress.PlanUpdates[index];
                Assert.True(
                    (long)previous.Completed * current.Total <= (long)current.Completed * previous.Total,
                    $"Plan progress moved backwards from {previous.Completed}/{previous.Total} to {current.Completed}/{current.Total}.");
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_does_not_complete_execution_phases_during_release_preflight()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample"));
            File.WriteAllText(
                Path.Combine(projectDirectory.FullName, "Sample.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Version>1.0.0</Version><IsPackable>true</IsPackable></PropertyGroup></Project>");
            var progress = new RecordingProjectBuildProgress();
            var workflow = new ProjectBuildWorkflowService(new NullLogger()).Execute(
                new ProjectBuildConfiguration(),
                root.FullName,
                new ProjectBuildPreparedContext
                {
                    RootPath = root.FullName,
                    Spec = new DotNetRepositoryReleaseSpec
                    {
                        RootPath = root.FullName,
                        Pack = false,
                        Publish = false,
                        UpdateVersions = false
                    }
                },
                executeBuild: true,
                progress: progress);

            Assert.True(workflow.Result.Success, workflow.Result.ErrorMessage);
            Assert.Equal(1, progress.Events.Count(entry => entry == "start:Versioning:1"));
            Assert.Equal(1, progress.Events.Count(entry => entry == "complete:Versioning"));
            Assert.True(
                progress.Events.IndexOf("complete:Plan") < progress.Events.IndexOf("start:Versioning:1"),
                "The release execution phase started before plan preflight completed.");
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class RecordingProjectBuildProgress : IProjectBuildProgressReporterV2
    {
        public List<string> Events { get; } = new();
        public List<ProjectBuildProgressItem> Items { get; } = new();

        public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null)
            => Events.Add($"start:{phase}:{totalItems}");

        public void PhaseUpdated(ProjectBuildProgressPhase phase, int completedItems, int totalItems, string? detail = null)
            => Events.Add($"update:{phase}:{completedItems}/{totalItems}");

        public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null)
            => Events.Add($"complete:{phase}");

        public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null)
            => Events.Add($"fail:{phase}");

        public void ItemsPlanned(ProjectBuildProgressPhase phase, IReadOnlyList<ProjectBuildProgressItem> items)
            => Items.AddRange(items);

        public void ItemUpdated(ProjectBuildProgressItem item, ProjectBuildProgressItemState state, string? detail = null)
            => Events.Add($"item:{item.Phase}:{item.Title}:{state}");
    }

    private sealed class ThreadAffineProjectBuildProgress : IProjectBuildProgressReporterV2
    {
        private readonly int _ownerThreadId;

        internal ThreadAffineProjectBuildProgress(int ownerThreadId)
        {
            _ownerThreadId = ownerThreadId;
        }

        internal List<(int Completed, int Total)> PlanUpdates { get; } = new();

        public void PhaseStarted(ProjectBuildProgressPhase phase, int totalItems, string? detail = null)
            => AssertOwnerThread();

        public void PhaseUpdated(ProjectBuildProgressPhase phase, int completedItems, int totalItems, string? detail = null)
        {
            AssertOwnerThread();
            if (phase == ProjectBuildProgressPhase.Plan)
                PlanUpdates.Add((completedItems, totalItems));
        }

        public void PhaseCompleted(ProjectBuildProgressPhase phase, string? detail = null)
            => AssertOwnerThread();

        public void PhaseFailed(ProjectBuildProgressPhase phase, string? detail = null)
            => AssertOwnerThread();

        public void ItemsPlanned(ProjectBuildProgressPhase phase, IReadOnlyList<ProjectBuildProgressItem> items)
            => AssertOwnerThread();

        public void ItemUpdated(ProjectBuildProgressItem item, ProjectBuildProgressItemState state, string? detail = null)
            => AssertOwnerThread();

        private void AssertOwnerThread()
            => Assert.Equal(_ownerThreadId, Environment.CurrentManagedThreadId);
    }

    private sealed class ThreadAffineLogger : ILogger
    {
        private readonly int _ownerThreadId;

        internal ThreadAffineLogger(int ownerThreadId)
        {
            _ownerThreadId = ownerThreadId;
        }

        public bool IsVerbose
        {
            get
            {
                AssertOwnerThread();
                return true;
            }
        }

        public void Info(string message) => AssertOwnerThread();

        public void Success(string message) => AssertOwnerThread();

        public void Warn(string message) => AssertOwnerThread();

        public void Error(string message) => AssertOwnerThread();

        public void Verbose(string message) => AssertOwnerThread();

        private void AssertOwnerThread()
            => Assert.Equal(_ownerThreadId, Environment.CurrentManagedThreadId);
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
        var progress = new RecordingProjectBuildProgress();
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
                Assert.NotNull(spec.PlannedVersionsByProject);
                Assert.Equal("1.2.3", spec.PlannedVersionsByProject!["ProjectA"]);
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
                var detailed = Assert.IsAssignableFrom<IProjectBuildProgressReporterV2>(request.Progress);
                var asset = new ProjectBuildProgressItem
                {
                    Phase = ProjectBuildProgressPhase.GitHubPublish,
                    Key = "github:1:ProjectA.1.2.3.zip",
                    Title = "ProjectA.1.2.3.zip",
                    Kind = "GitHubAsset",
                    Position = 1,
                    Total = 1
                };
                detailed.ItemsPlanned(ProjectBuildProgressPhase.GitHubPublish, [asset]);
                detailed.ItemUpdated(asset, ProjectBuildProgressItemState.Completed, "uploaded");
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
            remotePublishAttempted: () => remotePublishAttempts++,
            progress: progress);

        Assert.Equal(2, callIndex);
        Assert.Equal(1, remotePublishAttempts);
        Assert.True(remoteAttemptRecordedBeforePublish);
        Assert.True(workflow.Result.Success);
        Assert.Single(workflow.Result.GitHub);
        Assert.NotNull(workflow.GitHubPublishSummary);
        Assert.Equal("v1.2.3", workflow.GitHubPublishSummary!.SummaryTag);
        var asset = Assert.Single(progress.Items, item => item.Kind == "GitHubAsset");
        Assert.Equal("ProjectA.1.2.3.zip", asset.Title);
        Assert.Contains("item:GitHubPublish:ProjectA.1.2.3.zip:Completed", progress.Events);
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
        Assert.Contains(logger.ErrorMessages, message =>
            message.Contains("ProjectA: package provenance mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_reuses_planned_central_version_without_inserting_project_version()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <VersionPrefix>1.2.3</VersionPrefix>
                  </PropertyGroup>
                </Project>
                """);

            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.CentralVersion"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.CentralVersion.csproj");
            const string projectSource = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Sample.CentralVersion</PackageId>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """;
            File.WriteAllText(projectPath, projectSource);

            var workflow = new ProjectBuildWorkflowService(new NullLogger()).Execute(
                new ProjectBuildConfiguration(),
                root.FullName,
                new ProjectBuildPreparedContext
                {
                    RootPath = root.FullName,
                    UpdateVersions = true,
                    Spec = new DotNetRepositoryReleaseSpec
                    {
                        RootPath = root.FullName,
                        Pack = false,
                        Publish = false,
                        UpdateVersions = true,
                        SignAssemblies = false,
                        SignPackages = false
                    }
                },
                executeBuild: true);

            Assert.True(workflow.Result.Success, workflow.Result.ErrorMessage);
            Assert.Equal("1.2.3", workflow.Result.Release!.ResolvedVersionsByProject["Sample.CentralVersion"]);
            Assert.Equal(projectSource, File.ReadAllText(projectPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Execute_reuses_evaluated_package_version_instead_of_literal_project_version()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <PackageVersion>2.3.4</PackageVersion>
                  </PropertyGroup>
                </Project>
                """);

            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.EffectiveVersion"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.EffectiveVersion.csproj");
            const string projectSource = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Sample.EffectiveVersion</PackageId>
                    <VersionPrefix>1.0.0</VersionPrefix>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """;
            File.WriteAllText(projectPath, projectSource);

            var workflow = new ProjectBuildWorkflowService(new NullLogger()).Execute(
                new ProjectBuildConfiguration(),
                root.FullName,
                new ProjectBuildPreparedContext
                {
                    RootPath = root.FullName,
                    Spec = new DotNetRepositoryReleaseSpec
                    {
                        RootPath = root.FullName,
                        Pack = false,
                        Publish = false,
                        UpdateVersions = false,
                        SignAssemblies = false,
                        SignPackages = false
                    }
                },
                executeBuild: true);

            Assert.True(workflow.Result.Success, workflow.Result.ErrorMessage);
            Assert.Equal("2.3.4", workflow.Result.Release!.ResolvedVersionsByProject["Sample.EffectiveVersion"]);
            Assert.Equal(projectSource, File.ReadAllText(projectPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
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
