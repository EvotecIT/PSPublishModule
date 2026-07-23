using PowerForge;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed class HomeAssistantReleaseTests {
    [Fact]
    public void Policy_DocumentationAndWorkflowOnlyChangesDoNotRelease() {
        var increment = HomeAssistantReleasePolicy.Resolve(
            Array.Empty<string>(),
            new[] { "README.md", ".github/workflows/validate.yml", "tests/test_client.py" },
            explicitIncrement: null);

        Assert.Equal(HomeAssistantVersionIncrement.None, increment);
    }

    [Fact]
    public void Policy_ProductChangeDefaultsToPatchAndLabelCanOverride() {
        Assert.Equal(
            HomeAssistantVersionIncrement.Patch,
            HomeAssistantReleasePolicy.Resolve(Array.Empty<string>(), new[] { "custom_components/example/sensor.py" }, null));
        Assert.Equal(
            HomeAssistantVersionIncrement.Minor,
            HomeAssistantReleasePolicy.Resolve(new[] { "release:minor" }, new[] { "README.md" }, null));
    }

    [Fact]
    public void Policy_ConflictingReleaseLabelsFail() {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            HomeAssistantReleasePolicy.Resolve(
                new[] { "release:patch", "release:major" },
                new[] { "custom_components/example/sensor.py" },
                null));

        Assert.Contains("conflicting", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SemanticVersion_UsesThreePartPublicVersions() {
        var current = HomeAssistantSemanticVersion.Parse("v1.2.3");

        Assert.Equal("1.2.4", current.Increment(HomeAssistantVersionIncrement.Patch).ToString());
        Assert.Equal("1.3.0", current.Increment(HomeAssistantVersionIncrement.Minor).ToString());
        Assert.Equal("2.0.0", current.Increment(HomeAssistantVersionIncrement.Major).ToString());
        Assert.Throws<InvalidOperationException>(() => HomeAssistantSemanticVersion.Parse("1.2.3.4"));
    }

    [Fact]
    public void ReleaseMetadata_RecordsProvenanceWithoutVisiblePlaceholder() {
        const string releaseCommit = "cccccccccccccccccccccccccccccccccccccccc";
        var notes = HomeAssistantReleasePolicy.BuildReleaseMetadata(
            HomeAssistantReleasePolicy.BuildMarker(42, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            releaseCommit,
            requiredAsset: null);

        Assert.Equal(releaseCommit, HomeAssistantReleasePolicy.ReadReleaseCommit(notes));
        Assert.Null(HomeAssistantReleasePolicy.ReadRequiredAsset(notes));
        Assert.DoesNotContain("Release triggered by", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryService_SynchronizesIntegrationMetadata() {
        using var fixture = HomeAssistantFixture.CreateIntegration("0.2.6");
        var service = new HomeAssistantRepositoryService();
        var snapshot = service.Inspect(fixture.Root);

        var changed = service.UpdateVersion(snapshot, fixture.Root, "0.2.7");

        Assert.Equal(HomeAssistantRepositoryKind.Integration, snapshot.Kind);
        Assert.Equal(2, changed.Count);
        Assert.Equal("0.2.7", ReadJsonVersion(Path.Combine(fixture.Root, "custom_components", "example", "manifest.json")));
        var pyProject = File.ReadAllText(Path.Combine(fixture.Root, "pyproject.toml"));
        Assert.True(pyProject.Contains("version = \"0.2.7\"", StringComparison.Ordinal), pyProject.Replace("\n", "\\n"));
    }

    [Fact]
    public void RepositoryService_PreservesIntegrationManifestFormatting() {
        using var fixture = HomeAssistantFixture.CreateIntegration("0.2.6");
        var path = Path.Combine(fixture.Root, "custom_components", "example", "manifest.json");
        const string before = "{\r\n  \"domain\": \"example\",\r\n  \"codeowners\": [\"@EvotecIT\", \"@Example\"],\r\n  \"version\": \"0.2.6\"\r\n}\r\n";
        File.WriteAllText(path, before, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var service = new HomeAssistantRepositoryService();

        service.UpdateVersion(service.Inspect(fixture.Root), fixture.Root, "0.2.10");

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
        Assert.Equal(before.Replace("\"0.2.6\"", "\"0.2.10\"", StringComparison.Ordinal), Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public void RepositoryService_SynchronizesPluginPackageAndLockMetadata() {
        using var fixture = HomeAssistantFixture.CreatePlugin("0.1.10");
        var service = new HomeAssistantRepositoryService();
        var snapshot = service.Inspect(fixture.Root);

        var changed = service.UpdateVersion(snapshot, fixture.Root, "0.1.11");

        Assert.Equal(HomeAssistantRepositoryKind.LovelacePlugin, snapshot.Kind);
        Assert.Equal(2, changed.Count);
        Assert.Equal("0.1.11", ReadJsonVersion(Path.Combine(fixture.Root, "package.json")));
        var packageLock = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Root, "package-lock.json")))!.AsObject();
        Assert.Equal("0.1.11", packageLock["version"]!.GetValue<string>());
        Assert.Equal("0.1.11", packageLock["packages"]![""]!["version"]!.GetValue<string>());
    }

    [Fact]
    public void RepositoryService_PreservesUnrelatedPluginJsonText() {
        using var fixture = HomeAssistantFixture.CreatePlugin("0.9.9");
        var packagePath = Path.Combine(fixture.Root, "package.json");
        var lockPath = Path.Combine(fixture.Root, "package-lock.json");
        const string packageBefore = "{\n  \"name\": \"example\",\n  \"version\": \"0.9.9\",\n  \"scripts\": { \"pack\": \"npm run build && node pack.mjs\" },\n  \"repository\": \"git+https://example.test/repo.git\"\n}\n";
        const string lockBefore = "{\n  \"name\": \"example\",\n  \"version\": \"0.9.9\",\n  \"lockfileVersion\": 3,\n  \"packages\": {\n    \"\": { \"name\": \"example\", \"version\": \"0.9.9\" },\n    \"node_modules/tool\": { \"version\": \"1.0.0\", \"integrity\": \"sha512-a+b/c==\", \"engines\": { \"node\": \">=18\" } }\n  }\n}\n";
        File.WriteAllText(packagePath, packageBefore);
        File.WriteAllText(lockPath, lockBefore);
        var service = new HomeAssistantRepositoryService();

        service.UpdateVersion(service.Inspect(fixture.Root), fixture.Root, "0.10.0");

        Assert.Equal(packageBefore.Replace("\"0.9.9\"", "\"0.10.0\"", StringComparison.Ordinal), File.ReadAllText(packagePath));
        Assert.Equal(lockBefore.Replace("\"0.9.9\"", "\"0.10.0\"", StringComparison.Ordinal), File.ReadAllText(lockPath));
    }

    [Fact]
    public void RepositoryService_RequiresPluginLockfileForReproducibleBuilds() {
        using var fixture = HomeAssistantFixture.CreatePlugin("0.1.10");
        File.Delete(Path.Combine(fixture.Root, "package-lock.json"));

        var exception = Assert.Throws<InvalidOperationException>(() => new HomeAssistantRepositoryService().Inspect(fixture.Root));

        Assert.Contains("require package-lock.json", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryService_RejectsPluginLockfileVersionDrift() {
        using var fixture = HomeAssistantFixture.CreatePlugin("0.1.10");
        var path = Path.Combine(fixture.Root, "package-lock.json");
        var packageLock = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        packageLock["packages"]![""]!["version"] = "0.1.9";
        File.WriteAllText(path, packageLock.ToJsonString());

        var exception = Assert.Throws<InvalidOperationException>(() => new HomeAssistantRepositoryService().Inspect(fixture.Root));

        Assert.Contains("Version drift", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryService_DoesNotReadVersionFromALaterTomlTable() {
        using var fixture = HomeAssistantFixture.CreateIntegration("0.2.6");
        var pyProject = Path.Combine(fixture.Root, "pyproject.toml");
        File.WriteAllText(pyProject, "[project]\nname = \"example\"\n\n[tool.example]\nversion = \"9.9.9\"\n");

        var exception = Assert.Throws<InvalidOperationException>(() => new HomeAssistantRepositoryService().Inspect(fixture.Root));

        Assert.Contains("[project] table", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("version = \"9.9.9\"", File.ReadAllText(pyProject), StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryService_PrioritizesZipIntegrationLayoutWhenNpmToolingAlsoExists() {
        using var fixture = HomeAssistantFixture.CreateZipIntegration("0.2.6", includePackageJson: true);

        var snapshot = new HomeAssistantRepositoryService().Inspect(fixture.Root);

        Assert.Equal(HomeAssistantRepositoryKind.Integration, snapshot.Kind);
        Assert.True(snapshot.ZipRelease);
        Assert.Equal("example.zip", snapshot.HacsFileName);
        Assert.EndsWith(Path.Combine("custom_components", "example"), snapshot.IntegrationDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseService_ReusesVerifiedSourceMarkerWithoutPublishingAgain() {
        using var fixture = HomeAssistantFixture.CreateIntegration("0.2.7");
        const string mergeSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var marker = HomeAssistantReleasePolicy.BuildMarker(42, mergeSha);
        var github = new FakeGitHubClient {
            PullRequest = new HomeAssistantPullRequest {
                Number = 42,
                Merged = true,
                HeadSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                MergeCommitSha = mergeSha
            },
            MarkerRelease = new HomeAssistantGitHubRelease {
                TagName = "v0.2.7",
                Body = marker + "\n<!-- powerforge-homeassistant release-commit:cccccccccccccccccccccccccccccccccccccccc -->\n<!-- powerforge-homeassistant required-asset: -->",
                HtmlUrl = "https://github.example/releases/v0.2.7"
            },
            TagCommitSha = "cccccccccccccccccccccccccccccccccccccccc"
        };
        var publisher = new RecordingPublisher();
        var service = new HomeAssistantReleaseService(
            new NullLogger(),
            new HomeAssistantRepositoryService(),
            new HomeAssistantReleaseGitService(),
            github,
            publisher);

        var result = service.Prepare(new HomeAssistantReleasePrepareSpec {
            RepositoryRoot = fixture.Root,
            Owner = "EvotecIT",
            Repository = "example",
            Token = "token",
            PullRequestNumber = 42,
            MergeCommitSha = mergeSha,
            Apply = true
        });

        Assert.Equal(HomeAssistantReleaseAction.Reused, result.Action);
        Assert.Equal("0.2.7", result.ReleaseVersion);
        Assert.Equal("v0.2.7", result.TagName);
        Assert.Equal(0, publisher.PublishCalls);
    }

    [Fact]
    public void ReleaseService_QueuedPullRequestPreservesItsIncrementAfterANewerMerge() {
        using var fixture = HomeAssistantFixture.CreateIntegration("0.2.6");
        RunGit(fixture.Root, "init", "-b", "main");
        RunGit(fixture.Root, "config", "user.name", "PowerForge Tests");
        RunGit(fixture.Root, "config", "user.email", "powerforge-tests@example.invalid");
        RunGit(fixture.Root, "add", ".");
        RunGit(fixture.Root, "commit", "-m", "Merge source change");
        var sourceMergeSha = RunGit(fixture.Root, "rev-parse", "HEAD").StdOut.Trim();
        File.WriteAllText(Path.Combine(fixture.Root, "custom_components", "example", "sensor.py"), "VALUE = 1\n");
        RunGit(fixture.Root, "add", ".");
        RunGit(fixture.Root, "commit", "-m", "Newer merged change");

        var github = new FakeGitHubClient {
            PullRequest = new HomeAssistantPullRequest {
                Number = 42,
                Merged = true,
                HeadSha = sourceMergeSha,
                MergeCommitSha = sourceMergeSha
            },
            LatestRelease = new HomeAssistantGitHubRelease { TagName = "v0.2.6" }
        };
        github.PullRequest.ChangedFiles.Add("custom_components/example/sensor.py");
        var service = new HomeAssistantReleaseService(
            new NullLogger(),
            new HomeAssistantRepositoryService(),
            new HomeAssistantReleaseGitService(),
            github,
            new RecordingPublisher());

        var result = service.Prepare(new HomeAssistantReleasePrepareSpec {
            RepositoryRoot = fixture.Root,
            Owner = "EvotecIT",
            Repository = "example",
            Token = "token",
            PullRequestNumber = 42,
            MergeCommitSha = sourceMergeSha,
            WorkflowRunId = 29561117925L
        });

        Assert.Equal(HomeAssistantReleaseAction.Planned, result.Action);
        Assert.Equal(HomeAssistantVersionIncrement.Patch, result.Increment);
        Assert.Equal("0.2.7", result.ReleaseVersion);
        Assert.Equal(29561117925L, github.ExcludedWorkflowRunId);
    }

    [Fact]
    public void PrepareStage_DoesNotExecuteRepositoryBuildCommands() {
        using var fixture = HomeAssistantFixture.CreatePlugin("0.1.10");
        RunGit(fixture.Root, "init", "-b", "main");
        RunGit(fixture.Root, "config", "user.name", "PowerForge Tests");
        RunGit(fixture.Root, "config", "user.email", "powerforge-tests@example.invalid");
        RunGit(fixture.Root, "add", ".");
        RunGit(fixture.Root, "commit", "-m", "Merge plugin change");
        var mergeSha = RunGit(fixture.Root, "rev-parse", "HEAD").StdOut.Trim();
        var github = new FakeGitHubClient {
            PullRequest = new HomeAssistantPullRequest {
                Number = 42,
                Merged = true,
                HeadSha = mergeSha,
                MergeCommitSha = mergeSha
            },
            LatestRelease = new HomeAssistantGitHubRelease { TagName = "v0.1.10" }
        };
        github.PullRequest.ChangedFiles.Add("src/example.ts");
        var runner = new RecordingProcessRunner(mutateTrackedFile: false);
        var service = new HomeAssistantReleaseService(
            new NullLogger(),
            new HomeAssistantRepositoryService(runner),
            new HomeAssistantReleaseGitService(),
            github,
            new RecordingPublisher());

        var result = service.Prepare(new HomeAssistantReleasePrepareSpec {
            RepositoryRoot = fixture.Root,
            Owner = "EvotecIT",
            Repository = "example",
            Token = "write-token",
            PullRequestNumber = 42,
            MergeCommitSha = mergeSha
        });

        Assert.Equal(HomeAssistantReleaseAction.Planned, result.Action);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void PrepareStage_PreflightsForeignTargetTagBeforeVersionMutation() {
        using var fixture = HomeAssistantFixture.CreateIntegration("0.2.6");
        RunGit(fixture.Root, "init", "-b", "main");
        RunGit(fixture.Root, "config", "user.name", "PowerForge Tests");
        RunGit(fixture.Root, "config", "user.email", "powerforge-tests@example.invalid");
        RunGit(fixture.Root, "add", ".");
        RunGit(fixture.Root, "commit", "-m", "Merge integration change");
        var mergeSha = RunGit(fixture.Root, "rev-parse", "HEAD").StdOut.Trim();
        var github = new FakeGitHubClient {
            PullRequest = new HomeAssistantPullRequest {
                Number = 42,
                Merged = true,
                HeadSha = mergeSha,
                MergeCommitSha = mergeSha
            },
            LatestRelease = new HomeAssistantGitHubRelease { TagName = "v0.2.6" },
            TagRelease = new HomeAssistantGitHubRelease {
                Id = 99,
                TagName = "v0.2.7",
                Body = "unrelated release"
            },
            TagCommitSha = "dddddddddddddddddddddddddddddddddddddddd"
        };
        github.PullRequest.ChangedFiles.Add("custom_components/example/sensor.py");
        var service = new HomeAssistantReleaseService(
            new NullLogger(),
            new HomeAssistantRepositoryService(),
            new HomeAssistantReleaseGitService(),
            github,
            new RecordingPublisher());

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare(new HomeAssistantReleasePrepareSpec {
            RepositoryRoot = fixture.Root,
            Owner = "EvotecIT",
            Repository = "example",
            Token = "write-token",
            PullRequestNumber = 42,
            MergeCommitSha = mergeSha,
            Apply = true
        }));

        Assert.Contains("source marker", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0.2.6", ReadJsonVersion(Path.Combine(fixture.Root, "custom_components", "example", "manifest.json")));
        Assert.Empty(RunGit(fixture.Root, "status", "--porcelain").StdOut);
    }

    [Fact]
    public void ReleaseService_AheadVersionMustBelongToTheRequestedPullRequest() {
        using var fixture = HomeAssistantFixture.CreateIntegration("0.2.7");
        RunGit(fixture.Root, "init", "-b", "main");
        RunGit(fixture.Root, "config", "user.name", "PowerForge Tests");
        RunGit(fixture.Root, "config", "user.email", "powerforge-tests@example.invalid");
        RunGit(fixture.Root, "add", ".");
        RunGit(fixture.Root, "commit", "-m", "Merge unrelated source change");
        var mergeSha = RunGit(fixture.Root, "rev-parse", "HEAD").StdOut.Trim();
        var github = new FakeGitHubClient {
            PullRequest = new HomeAssistantPullRequest {
                Number = 42,
                Merged = true,
                HeadSha = mergeSha,
                MergeCommitSha = mergeSha
            },
            LatestRelease = new HomeAssistantGitHubRelease { TagName = "v0.2.6" }
        };
        github.PullRequest.ChangedFiles.Add("custom_components/example/sensor.py");
        var service = new HomeAssistantReleaseService(
            new NullLogger(),
            new HomeAssistantRepositoryService(),
            new HomeAssistantReleaseGitService(),
            github,
            new RecordingPublisher());

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare(new HomeAssistantReleasePrepareSpec {
            RepositoryRoot = fixture.Root,
            Owner = "EvotecIT",
            Repository = "example",
            Token = "token",
            PullRequestNumber = 42,
            MergeCommitSha = mergeSha
        }));

        Assert.Contains("no PowerForge release commit belongs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseService_AheadVersionRejectsAReceiverCommitThatClaimsTheSourcePullRequest() {
        using var fixture = HomeAssistantFixture.CreateIntegration("0.2.7");
        RunGit(fixture.Root, "init", "-b", "main");
        RunGit(fixture.Root, "config", "user.name", "PowerForge Tests");
        RunGit(fixture.Root, "config", "user.email", "powerforge-tests@example.invalid");
        RunGit(fixture.Root, "add", ".");
        RunGit(fixture.Root, "commit", "-m", "Merge source change");
        var mergeSha = RunGit(fixture.Root, "rev-parse", "HEAD").StdOut.Trim();
        File.WriteAllText(Path.Combine(fixture.Root, "custom_components", "example", "sensor.py"), "VALUE = 1\n");
        RunGit(fixture.Root, "add", ".");
        RunGit(
            fixture.Root,
            "commit",
            "-m",
            $"Receiver-controlled change\n\nSource-PR: #42\nSource-Merge: {mergeSha}");

        var github = new FakeGitHubClient {
            PullRequest = new HomeAssistantPullRequest {
                Number = 42,
                Merged = true,
                HeadSha = mergeSha,
                MergeCommitSha = mergeSha
            },
            LatestRelease = new HomeAssistantGitHubRelease { TagName = "v0.2.6" }
        };
        github.PullRequest.ChangedFiles.Add("custom_components/example/sensor.py");
        var service = new HomeAssistantReleaseService(
            new NullLogger(),
            new HomeAssistantRepositoryService(),
            new HomeAssistantReleaseGitService(),
            github,
            new RecordingPublisher());

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare(new HomeAssistantReleasePrepareSpec {
            RepositoryRoot = fixture.Root,
            Owner = "EvotecIT",
            Repository = "example",
            Token = "token",
            PullRequestNumber = 42,
            MergeCommitSha = mergeSha
        }));

        Assert.Contains("no PowerForge release commit belongs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseService_AheadVersionResumesOnlyThePreparedMetadataCommit() {
        using var fixture = HomeAssistantFixture.CreateIntegration("0.2.6");
        RunGit(fixture.Root, "init", "-b", "main");
        RunGit(fixture.Root, "config", "user.name", "PowerForge Tests");
        RunGit(fixture.Root, "config", "user.email", "powerforge-tests@example.invalid");
        RunGit(fixture.Root, "add", ".");
        RunGit(fixture.Root, "commit", "-m", "Merge source change");
        var mergeSha = RunGit(fixture.Root, "rev-parse", "HEAD").StdOut.Trim();
        var repository = new HomeAssistantRepositoryService();
        var snapshot = repository.Inspect(fixture.Root);
        var changedFiles = repository.UpdateVersion(snapshot, fixture.Root, "0.2.7");
        var preparedCommit = new HomeAssistantReleaseGitService().CommitRelease(
            fixture.Root,
            changedFiles,
            "0.2.7",
            42,
            mergeSha);

        var github = new FakeGitHubClient {
            PullRequest = new HomeAssistantPullRequest {
                Number = 42,
                Merged = true,
                HeadSha = mergeSha,
                MergeCommitSha = mergeSha
            },
            LatestRelease = new HomeAssistantGitHubRelease { TagName = "v0.2.6" }
        };
        github.PullRequest.ChangedFiles.Add("custom_components/example/sensor.py");
        var service = new HomeAssistantReleaseService(
            new NullLogger(),
            repository,
            new HomeAssistantReleaseGitService(),
            github,
            new RecordingPublisher());

        var result = service.Prepare(new HomeAssistantReleasePrepareSpec {
            RepositoryRoot = fixture.Root,
            Owner = "EvotecIT",
            Repository = "example",
            Token = "token",
            PullRequestNumber = 42,
            MergeCommitSha = mergeSha,
            Apply = true
        });

        Assert.Equal(HomeAssistantReleaseAction.Prepared, result.Action);
        Assert.Equal("0.2.7", result.ReleaseVersion);
        Assert.Equal(preparedCommit, result.ReleaseCommitSha);
        Assert.Empty(RunGit(fixture.Root, "status", "--porcelain").StdOut);
    }

    [Fact]
    public void ReleaseService_RebuildsAMissingAssetFromTheExactHistoricalTagCommit() {
        using var fixture = HomeAssistantFixture.CreateZipIntegration("0.2.7");
        RunGit(fixture.Root, "init", "-b", "main");
        RunGit(fixture.Root, "config", "user.name", "PowerForge Tests");
        RunGit(fixture.Root, "config", "user.email", "powerforge-tests@example.invalid");
        RunGit(fixture.Root, "add", ".");
        RunGit(fixture.Root, "commit", "-m", "Release 0.2.7");
        var releaseCommit = RunGit(fixture.Root, "rev-parse", "HEAD").StdOut.Trim();
        fixture.UpdateIntegrationVersion("0.2.8");
        RunGit(fixture.Root, "add", ".");
        RunGit(fixture.Root, "commit", "-m", "Merge newer change");

        var marker = HomeAssistantReleasePolicy.BuildMarker(42, releaseCommit);
        var release = new HomeAssistantGitHubRelease {
            TagName = "v0.2.7",
            Body = marker + $"\n<!-- powerforge-homeassistant release-commit:{releaseCommit} -->\n<!-- powerforge-homeassistant required-asset:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("example.zip"))} -->",
            HtmlUrl = "https://github.example/releases/v0.2.7"
        };
        var github = new FakeGitHubClient {
            PullRequest = new HomeAssistantPullRequest {
                Number = 42,
                Merged = true,
                HeadSha = releaseCommit,
                MergeCommitSha = releaseCommit
            },
            LatestRelease = release,
            MarkerRelease = release,
            TagCommitSha = releaseCommit
        };
        github.PullRequest.ChangedFiles.Add("custom_components/example/sensor.py");
        var publisher = new RecordingPublisher(github);
        var service = new HomeAssistantReleaseService(
            new NullLogger(),
            new HomeAssistantRepositoryService(),
            new HomeAssistantReleaseGitService(),
            github,
            publisher);

        var prepared = service.Prepare(new HomeAssistantReleasePrepareSpec {
            RepositoryRoot = fixture.Root,
            Owner = "EvotecIT",
            Repository = "example",
            Token = "token",
            PullRequestNumber = 42,
            MergeCommitSha = releaseCommit,
            Apply = true
        });

        Assert.Equal(HomeAssistantReleaseAction.Prepared, prepared.Action);
        Assert.Equal(releaseCommit, prepared.ReleaseCommitSha);
        var buildRoot = Path.Combine(Path.GetTempPath(), "PowerForge.HomeAssistant.BuildTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(buildRoot)!);
        RunGit(fixture.Root, "worktree", "add", "--detach", buildRoot, releaseCommit);
        try {
            var built = service.Build(new HomeAssistantReleaseBuildSpec {
                RepositoryRoot = buildRoot,
                ReleaseVersion = prepared.ReleaseVersion,
                ReleaseCommitSha = releaseCommit
            });
            var result = service.Publish(new HomeAssistantReleasePublishSpec {
                Owner = "EvotecIT",
                Repository = "example",
                Token = "token",
                PullRequestNumber = 42,
                MergeCommitSha = releaseCommit,
                ReleaseVersion = prepared.ReleaseVersion,
                ReleaseCommitSha = releaseCommit,
                RequiredAssetName = built.RequiredAssetName,
                AssetFilePath = Assert.Single(built.AssetFiles)
            });

            Assert.Equal(HomeAssistantReleaseAction.Published, result.Action);
        } finally {
            RunGit(fixture.Root, "worktree", "remove", "--force", buildRoot);
        }
        Assert.Equal("0.2.7", publisher.CapturedManifestVersion);
        Assert.DoesNotContain("PowerForge.HomeAssistant.Worktrees", RunGit(fixture.Root, "worktree", "list", "--porcelain").StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublishStage_RejectsUnrelatedTargetReleaseBeforePublisherMutation() {
        const string mergeSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string releaseCommit = "cccccccccccccccccccccccccccccccccccccccc";
        var github = new FakeGitHubClient {
            PullRequest = new HomeAssistantPullRequest {
                Number = 42,
                Merged = true,
                HeadSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                MergeCommitSha = mergeSha
            },
            TagRelease = new HomeAssistantGitHubRelease {
                Id = 99,
                TagName = "v0.2.7",
                Body = "unrelated release",
                HtmlUrl = "https://github.example/releases/v0.2.7"
            },
            TagCommitSha = releaseCommit
        };
        var publisher = new RecordingPublisher();
        var service = new HomeAssistantReleaseService(
            new NullLogger(),
            new HomeAssistantRepositoryService(),
            new HomeAssistantReleaseGitService(),
            github,
            publisher);

        var exception = Assert.Throws<InvalidOperationException>(() => service.Publish(new HomeAssistantReleasePublishSpec {
            Owner = "EvotecIT",
            Repository = "example",
            Token = "write-token",
            PullRequestNumber = 42,
            MergeCommitSha = mergeSha,
            ReleaseVersion = "0.2.7",
            ReleaseCommitSha = releaseCommit
        }));

        Assert.Contains("source marker", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, publisher.PublishCalls);
    }

    [Fact]
    public void PublishStage_OnlyPermitsReplacementOnThePreflightVerifiedRelease() {
        using var fixture = HomeAssistantFixture.CreateZipIntegration("0.2.7");
        var snapshot = new HomeAssistantRepositoryService().Inspect(fixture.Root);
        var asset = Assert.Single(new HomeAssistantRepositoryService().BuildAssets(snapshot, fixture.Root, "0.2.7"));
        const string mergeSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string releaseCommit = "cccccccccccccccccccccccccccccccccccccccc";
        var marker = HomeAssistantReleasePolicy.BuildMarker(42, mergeSha);
        var release = new HomeAssistantGitHubRelease {
            Id = 123,
            TagName = "v0.2.7",
            Body = HomeAssistantReleasePolicy.BuildReleaseMetadata(
                marker,
                releaseCommit,
                "example.zip"),
            HtmlUrl = "https://github.example/releases/v0.2.7"
        };
        var github = new FakeGitHubClient {
            PullRequest = new HomeAssistantPullRequest {
                Number = 42,
                Merged = true,
                HeadSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                MergeCommitSha = mergeSha
            },
            MarkerRelease = release,
            TagRelease = release,
            TagCommitSha = releaseCommit
        };
        var publisher = new RecordingPublisher(github);
        var service = new HomeAssistantReleaseService(
            new NullLogger(),
            new HomeAssistantRepositoryService(),
            new HomeAssistantReleaseGitService(),
            github,
            publisher);

        var result = service.Publish(new HomeAssistantReleasePublishSpec {
            Owner = "EvotecIT",
            Repository = "example",
            Token = "write-token",
            PullRequestNumber = 42,
            MergeCommitSha = mergeSha,
            ReleaseVersion = "0.2.7",
            ReleaseCommitSha = releaseCommit,
            RequiredAssetName = "example.zip",
            AssetFilePath = asset
        });

        Assert.Equal(HomeAssistantReleaseAction.Published, result.Action);
        Assert.True(publisher.CapturedRequest!.RequireExpectedExistingRelease);
        Assert.Equal(123, publisher.CapturedRequest.ExpectedExistingReleaseId);
        Assert.Equal(marker, publisher.CapturedRequest.ExpectedReleaseBodyMarker);
        Assert.Equal(releaseCommit, publisher.CapturedRequest.ExpectedTagCommitSha);
        Assert.True(publisher.CapturedRequest.GenerateReleaseNotes);
        Assert.Contains(marker, publisher.CapturedRequest.ReleaseNotes, StringComparison.Ordinal);
        Assert.DoesNotContain("Release triggered by", publisher.CapturedRequest.ReleaseNotes, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryBuildsScrubCredentialsAndTrackedMutationsAreRejected() {
        using var fixture = HomeAssistantFixture.CreatePlugin("0.1.10");
        RunGit(fixture.Root, "init", "-b", "main");
        RunGit(fixture.Root, "config", "user.name", "PowerForge Tests");
        RunGit(fixture.Root, "config", "user.email", "powerforge-tests@example.invalid");
        RunGit(fixture.Root, "add", ".");
        RunGit(fixture.Root, "commit", "-m", "Plugin source");
        var runner = new RecordingProcessRunner(mutateTrackedFile: true);
        var repository = new HomeAssistantRepositoryService(runner);
        var snapshot = repository.Inspect(fixture.Root);

        repository.BuildAssets(snapshot, fixture.Root, snapshot.Version);

        Assert.NotEmpty(runner.Requests);
        Assert.All(runner.Requests, request => {
            Assert.Null(request.EnvironmentVariables!["GITHUB_TOKEN"]);
            Assert.Null(request.EnvironmentVariables["GH_TOKEN"]);
            Assert.Null(request.EnvironmentVariables["ACTIONS_ID_TOKEN_REQUEST_TOKEN"]);
            Assert.Null(request.EnvironmentVariables["GITHUB_OUTPUT"]);
        });
        var exception = Assert.Throws<InvalidOperationException>(() => new HomeAssistantReleaseGitService().EnsureNoTrackedChanges(fixture.Root));
        Assert.Contains("changed tracked files", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseGitService_PushUsesEphemeralAuthenticationOutsideArguments() {
        const string token = "test-secret-token";
        var runner = new RecordingProcessRunner(mutateTrackedFile: false);
        var service = new HomeAssistantReleaseGitService(processRunner: runner);

        service.Push(Path.GetTempPath(), "main", token, "EvotecIT", "example", "https://github.com");

        var request = Assert.Single(runner.Requests);
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains(token, StringComparison.Ordinal));
        Assert.Null(request.EnvironmentVariables!["GITHUB_TOKEN"]);
        Assert.Equal("https://github.com/EvotecIT/example.git", request.Arguments[1]);
        Assert.DoesNotContain(token, request.EnvironmentVariables["GIT_CONFIG_VALUE_3"]!, StringComparison.Ordinal);
        Assert.StartsWith("AUTHORIZATION: basic ", request.EnvironmentVariables["GIT_CONFIG_VALUE_3"], StringComparison.Ordinal);
        Assert.Equal("false", request.EnvironmentVariables["GIT_CONFIG_VALUE_2"]);
        Assert.True(Directory.Exists(request.EnvironmentVariables["GIT_CONFIG_VALUE_1"]!) is false);
    }

    private static string ReadJsonVersion(string path)
        => JsonNode.Parse(File.ReadAllText(path))!["version"]!.GetValue<string>();

    private static ProcessRunResult RunGit(string workingDirectory, params string[] arguments) {
        var result = new GitClient(defaultTimeout: TimeSpan.FromSeconds(30))
            .RunRawAsync(workingDirectory, arguments, TimeSpan.FromSeconds(30))
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        if (!result.Succeeded)
            throw new InvalidOperationException($"git {string.Join(" ", arguments)} failed: {result.StdErr}");
        return result;
    }

    private sealed class FakeGitHubClient : IHomeAssistantGitHubClient {
        internal HomeAssistantPullRequest PullRequest { get; set; } = new();
        internal HomeAssistantGitHubRelease? MarkerRelease { get; set; }
        internal HomeAssistantGitHubRelease? LatestRelease { get; set; }
        internal HomeAssistantGitHubRelease? TagRelease { get; set; }
        internal string? TagCommitSha { get; set; }
        internal long? ExcludedWorkflowRunId { get; private set; }

        public HomeAssistantPullRequest GetPullRequest(int number) => PullRequest;
        public HomeAssistantCheckSummary GetCheckSummary(string commitSha, long? excludedWorkflowRunId) {
            ExcludedWorkflowRunId = excludedWorkflowRunId;
            return new HomeAssistantCheckSummary { Total = 1 };
        }
        public HomeAssistantGitHubRelease? GetLatestRelease() => LatestRelease;
        public HomeAssistantGitHubRelease? FindReleaseByMarker(string marker) => MarkerRelease;
        public HomeAssistantGitHubRelease? GetReleaseByTag(string tagName) => TagRelease ?? MarkerRelease;
        public string? GetTagCommitSha(string tagName) => TagCommitSha;
    }

    private sealed class RecordingPublisher : IHomeAssistantReleasePublisher {
        private readonly FakeGitHubClient? _github;

        internal RecordingPublisher(FakeGitHubClient? github = null) {
            _github = github;
        }

        internal int PublishCalls { get; private set; }
        internal string? CapturedManifestVersion { get; private set; }
        internal GitHubReleasePublishRequest? CapturedRequest { get; private set; }

        public GitHubReleasePublishResult Publish(GitHubReleasePublishRequest request) {
            PublishCalls++;
            CapturedRequest = request;
            foreach (var asset in request.AssetFilePaths) {
                if (!asset.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                using var archive = ZipFile.OpenRead(asset);
                var manifest = archive.GetEntry("manifest.json") ?? throw new InvalidOperationException("The integration archive did not contain manifest.json.");
                using var reader = new StreamReader(manifest.Open());
                CapturedManifestVersion = JsonNode.Parse(reader.ReadToEnd())!["version"]!.GetValue<string>();
            }

            if (_github?.MarkerRelease is not null) {
                foreach (var asset in request.AssetFilePaths) {
                    var name = Path.GetFileName(asset);
                    _github.MarkerRelease.AssetNames.Add(name);
                    _github.MarkerRelease.AssetSizes[name] = new FileInfo(asset).Length;
                }
            }

            return new GitHubReleasePublishResult { Succeeded = true, ReusedExistingRelease = _github?.MarkerRelease is not null };
        }
    }

    private sealed class RecordingProcessRunner : IProcessRunner {
        private readonly bool _mutateTrackedFile;

        internal RecordingProcessRunner(bool mutateTrackedFile) {
            _mutateTrackedFile = mutateTrackedFile;
        }

        internal List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) {
            Requests.Add(request);
            if (request.Arguments.SequenceEqual(new[] { "run", "pack" })) {
                var release = Path.Combine(request.WorkingDirectory, "release");
                Directory.CreateDirectory(release);
                File.WriteAllText(Path.Combine(release, "example.js"), "export const value = 1;\n");
                if (_mutateTrackedFile)
                    File.AppendAllText(Path.Combine(request.WorkingDirectory, "package.json"), "\n");
            }

            return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false));
        }
    }

    private sealed class HomeAssistantFixture : IDisposable {
        private HomeAssistantFixture(string root) {
            Root = root;
        }

        internal string Root { get; }

        internal static HomeAssistantFixture CreateIntegration(string version) {
            var fixture = Create();
            var integration = Path.Combine(fixture.Root, "custom_components", "example");
            Directory.CreateDirectory(integration);
            File.WriteAllText(Path.Combine(fixture.Root, "hacs.json"), "{\"name\":\"Example\"}");
            File.WriteAllText(Path.Combine(integration, "manifest.json"), $"{{\"domain\":\"example\",\"version\":\"{version}\"}}");
            File.WriteAllText(Path.Combine(fixture.Root, "pyproject.toml"), $"[project]\nname = \"example\"\nversion = \"{version}\"\n\n[tool.pytest.ini_options]\n");
            return fixture;
        }

        internal static HomeAssistantFixture CreateZipIntegration(string version, bool includePackageJson = false) {
            var fixture = CreateIntegration(version);
            File.WriteAllText(Path.Combine(fixture.Root, "hacs.json"), "{\"name\":\"Example\",\"zip_release\":true,\"filename\":\"example.zip\"}");
            if (includePackageJson)
                File.WriteAllText(Path.Combine(fixture.Root, "package.json"), $"{{\"name\":\"example-tools\",\"version\":\"{version}\"}}");
            return fixture;
        }

        internal static HomeAssistantFixture CreatePlugin(string version) {
            var fixture = Create();
            File.WriteAllText(Path.Combine(fixture.Root, "hacs.json"), "{\"name\":\"Example\",\"filename\":\"example.js\"}");
            File.WriteAllText(Path.Combine(fixture.Root, "package.json"), $"{{\"name\":\"example\",\"version\":\"{version}\"}}");
            File.WriteAllText(
                Path.Combine(fixture.Root, "package-lock.json"),
                $"{{\"name\":\"example\",\"version\":\"{version}\",\"lockfileVersion\":3,\"packages\":{{\"\":{{\"name\":\"example\",\"version\":\"{version}\"}}}}}}");
            return fixture;
        }

        private static HomeAssistantFixture Create() {
            var root = Path.Combine(Path.GetTempPath(), "PowerForge.HomeAssistant.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new HomeAssistantFixture(root);
        }

        internal void UpdateIntegrationVersion(string version) {
            var manifestPath = Path.Combine(Root, "custom_components", "example", "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["version"] = version;
            File.WriteAllText(manifestPath, manifest.ToJsonString());
            File.WriteAllText(Path.Combine(Root, "pyproject.toml"), $"[project]\nname = \"example\"\nversion = \"{version}\"\n\n[tool.pytest.ini_options]\n");
        }

        public void Dispose() {
            if (!Directory.Exists(Root)) return;
            foreach (var file in new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories))
                file.Attributes = FileAttributes.Normal;
            Directory.Delete(Root, recursive: true);
        }
    }
}
