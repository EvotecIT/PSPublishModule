using System.Security.Cryptography;
using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class ReleaseValidationPackagePublicationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.PackagePublication.Tests", Guid.NewGuid().ToString("N"));

    public ReleaseValidationPackagePublicationTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("json", true)]
    [InlineData("json", false)]
    [InlineData("script", false)]
    [InlineData("cancel", true)]
    [InlineData("publish-failure", true)]
    [InlineData("coordinated", true)]
    [InlineData("coordinated", false)]
    public void Package_publication_waits_for_real_staged_validation(string mode, bool validationSucceeds)
    {
        var configPath = Write("release.json", "{}");
        var package = Write("Fixture.1.2.3.nupkg", "validated package bytes");
        var input = mode == "script"
            ? Write("probe.ps1", "exit 1")
            : Write("probe.json", ReleaseValidationService.Serialize(new() {
                Commands = [new() { Name = "Package probe", FileName = "probe", ExpectedOutput = "observed" }]
            }));
        var order = new List<string>();
        var requests = new List<ProjectBuildHostRequest>();
        var checkpoint = Checkpoint(package);
        checkpoint.Result.Release!.ResolvedVersionsByProject["Fixture"] = "1.2.3";
        var coordinated = mode == "coordinated";
        using var cancellation = new CancellationTokenSource();
        var runner = new Runner(request => {
            order.Add("validation");
            if (mode == "cancel") { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            return new ProcessRunResult(validationSucceeds ? 0 : 1, "observed", validationSucceeds ? "" : "probe failed",
                request.FileName, TimeSpan.Zero, false);
        });
        PowerForgeReleaseValidationContext? validationContext = null;
        var service = new PowerForgeReleaseService(new NullLogger(),
            executePackages: (request, config, _) => {
                requests.Add(request);
                Assert.True(config.PublishNuget);
                Assert.True(config.PublishGitHub);
                if (request.PlanOnly == true) {
                    Assert.True(coordinated);
                    Assert.False(request.PublishNuget);
                    Assert.False(request.PublishGitHub);
                    Assert.False(request.ExecuteBuild);
                    order.Add("plan");
                    return checkpoint;
                }
                if (request.PublicationCheckpoint is null) {
                    order.Add("build");
                    Assert.True(request.DeferPublishing);
                    Assert.True(request.ExecuteBuild);
                    Assert.True(request.PublishNuget);
                    Assert.True(request.PublishGitHub);
                    return checkpoint;
                }
                order.Add("publish");
                Assert.Same(checkpoint, request.PublicationCheckpoint);
                Assert.NotNull(validationContext);
                var staged = Assert.Single(request.PublicationAssets, asset => asset.Path == package);
                Assert.Contains(staged.StagedPath!, validationContext.StagedAssets);
                Assert.NotEqual(package, staged.StagedPath);
                Assert.Equal("validated package bytes", File.ReadAllText(staged.StagedPath!));
                Assert.False(string.IsNullOrWhiteSpace(staged.StagedSha256));
                if (mode == "publish-failure") {
                    return new() { Success = false, ErrorMessage = "Publication fixture failure" };
                }
                return checkpoint;
            },
            planTools: (_, _, _) => throw new InvalidOperationException("Unexpected tool planning"),
            runTools: _ => throw new InvalidOperationException("Unexpected tool build"),
            loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("Unexpected tool configuration"),
            planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("Unexpected tool planning"),
            runDotNetTools: _ => throw new InvalidOperationException("Unexpected tool build"),
            publishGitHubRelease: _ => throw new InvalidOperationException("Unexpected unified GitHub publication"),
            executeModuleBuild: (_, _) => {
                Assert.True(coordinated);
                order.Add("module");
                return new() { ExitCode = 0 };
            },
            runReleaseValidation: (action, context, directory, token) => {
                validationContext = context;
                return new PowerForgeReleaseValidationService(new NullLogger(), runner).Run(action, context, directory, token);
            });
        var spec = new PowerForgeReleaseSpec {
            Packages = new() { RootPath = _root, Build = true, PublishNuget = true, PublishGitHub = true },
            Validation = new() { AfterStaging = [mode == "script" ? new() { FilePath = input } : new() { ConfigPath = input }] }
        };
        if (coordinated) {
            Directory.CreateDirectory(Path.Combine(_root, "Module"));
            Write("Module/Fixture.psd1", "@{ ModuleVersion = '1.2.3' }");
            Write("module.json", "{\"SchemaVersion\":1,\"Build\":{\"Name\":\"Fixture\",\"SourcePath\":\"Module\",\"Version\":\"1.2.3\"},\"Segments\":[]}");
            spec.Module = new() {
                RepositoryRoot = _root, ConfigPath = "module.json", ManifestPath = "Module/Fixture.psd1",
                ModuleVersion = "1.2.3", SynchronizeVersionWithPackages = true, VersionPrimaryProject = "Fixture"
            };
        }
        var request = new PowerForgeReleaseRequest {
            ConfigPath = configPath, StageRoot = Path.Combine(_root, "staged"), PackagesOnly = !coordinated,
            ModuleRunMode = ConfigurationGateMode.Build,
            PublishNuget = true, PublishProjectGitHub = true, CancellationToken = cancellation.Token
        };

        if (mode == "cancel") {
            Assert.ThrowsAny<OperationCanceledException>(() => service.Execute(spec, request));
            Assert.Single(requests);
            Assert.Equal(new[] { "build", "validation" }, order);
            return;
        }
        var result = service.Execute(spec, request);

        Assert.Equal(validationSucceeds && mode != "publish-failure", result.Success);
        Assert.Equal(validationSucceeds, Assert.Single(result.ReleaseValidations).Succeeded);
        var expectedOrder = new List<string>();
        if (coordinated) { expectedOrder.AddRange(["plan", "module"]); }
        expectedOrder.AddRange(["build", "validation"]);
        if (validationSucceeds) { expectedOrder.Add("publish"); }
        Assert.Equal(expectedOrder, order);
        Assert.Equal((validationSucceeds ? 2 : 1) + (coordinated ? 1 : 0), requests.Count);
        if (mode == "publish-failure") {
            Assert.Equal("Publication fixture failure", result.ErrorMessage);
        } else if (!validationSucceeds) {
            Assert.Contains("failed", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
            Assert.Same(checkpoint, result.Packages);
        } else {
            Assert.Same(checkpoint, result.Packages);
            Assert.Null(result.ErrorMessage);
        }
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void Host_defers_both_destinations_and_retains_resolved_actions(bool publishNuget, bool publishGitHub, bool useDefaults)
    {
        var calls = new List<bool>();
        var preflights = 0;
        var host = new ProjectBuildHostService(new NullLogger(),
            executeRelease: spec => {
                calls.Add(spec.WhatIf);
                Assert.False(spec.Publish);
                Assert.True(spec.Pack);
                return new() { Success = true, ResolvedVersion = "1.2.3" };
            },
            publishGitHub: _ => throw new InvalidOperationException("Publishing ran before staged validation"),
            validateGitHubPreflight: (_, _, _) => { preflights++; return null; });
        var config = new ProjectBuildConfiguration {
            RootPath = _root, Build = useDefaults ? null : false,
            PublishNuget = useDefaults ? null : false, PublishGitHub = useDefaults ? null : false,
            PublishApiKey = "fixture-key", GitHubAccessToken = "fixture-token",
            GitHubUsername = "fixture-owner", GitHubRepositoryName = "fixture-repo"
        };

        var result = host.Execute(new() {
            ConfigPath = Path.Combine(_root, "release.json"), ExecuteBuild = true, DeferPublishing = true,
            PublishNuget = useDefaults ? null : publishNuget, PublishGitHub = useDefaults ? null : publishGitHub,
            RemotePublishAttempted = () => throw new InvalidOperationException("Remote publication ran before validation")
        }, config, Path.Combine(_root, "release.json"));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(new[] { true, false }, calls);
        Assert.NotNull(result.DeferredPublicationConfiguration);
        Assert.Equal(publishNuget, result.DeferredPublicationConfiguration.PublishNuget);
        Assert.Equal(publishGitHub, result.DeferredPublicationConfiguration.PublishGitHub);
        Assert.Equal(publishGitHub ? 1 : 0, preflights);
        Assert.Equal(useDefaults ? (bool?)null : false, config.PublishNuget);
        Assert.Equal(useDefaults ? (bool?)null : false, config.PublishGitHub);
        Assert.Empty(result.Result.GitHub);
        Assert.Empty(result.Result.Release!.PublishedPackages);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("changed")]
    public void Host_publishes_the_captured_staged_checkpoint_without_replanning(string mode)
    {
        var original = Write("Fixture.1.2.3.nupkg", "built bytes");
        var staged = Write("staged.nupkg", "validated bytes");
        var checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(staged)));
        var checkpoint = Checkpoint(original);
        checkpoint.DeferredPublicationConfiguration = new() {
            PublishGitHub = true, PublishNuget = false, GitHubUsername = "fixture-owner",
            GitHubRepositoryName = "fixture-repo", GitHubTagName = "v1.2.3", GitHubIsPreRelease = true
        };
        var publications = new List<ProjectBuildGitHubPublishRequest>();
        string? snapshotPath = null;
        var host = new ProjectBuildHostService(new NullLogger(),
            executeRelease: _ => throw new InvalidOperationException("Checkpoint publication must not plan or rebuild"),
            publishGitHub: request => {
                publications.Add(request);
                snapshotPath = Assert.Single(Assert.Single(request.Release.Projects).Packages);
                Assert.NotEqual(original, snapshotPath);
                Assert.NotEqual(staged, snapshotPath);
                Assert.Equal("validated bytes", File.ReadAllText(snapshotPath));
                Assert.Equal("fixture-owner", request.Owner);
                Assert.Equal("fixture-repo", request.Repository);
                Assert.Equal("v1.2.3", request.TagName);
                Assert.True(request.IsPreRelease);
                return new() { Success = true, Results = { new() { Success = true, ProjectName = "Fixture", ReleaseId = 123 } } };
            },
            validateGitHubPreflight: (_, _, _) => throw new InvalidOperationException("Checkpoint publication must not repeat build preflight"));
        using var cancellation = new CancellationTokenSource();
        if (mode == "cancel") { cancellation.Cancel(); }
        if (mode == "changed") { File.WriteAllText(staged, "changed after validation"); }
        var request = new ProjectBuildHostRequest {
            ConfigPath = Path.Combine(_root, "release.json"), PublicationCheckpoint = checkpoint,
            PublicationAssets = [new() { Category = PowerForgeReleaseAssetCategory.Package, Path = original,
                StagedPath = staged, StagedSha256 = checksum }],
            CancellationToken = cancellation.Token,
            BuildSpecPrepared = _ => throw new InvalidOperationException("Checkpoint publication prepared a new build")
        };
        // Conflicting current flags cannot replace the original checkpoint's resolved publication decision.
        var config = new ProjectBuildConfiguration { PublishGitHub = false, PublishNuget = true, RootPath = "missing-root" };

        if (mode == "cancel") {
            Assert.ThrowsAny<OperationCanceledException>(() => host.Execute(request, config, request.ConfigPath));
        } else if (mode == "changed") {
            var exception = Assert.Throws<InvalidOperationException>(() => host.Execute(request, config, request.ConfigPath));
            Assert.Contains("changed after release staging", exception.Message);
        } else {
            var result = host.Execute(request, config, request.ConfigPath);
            Assert.Same(checkpoint, result);
            Assert.True(result.Success);
            Assert.Equal(123, Assert.Single(result.Result.GitHub).ReleaseId);
            Assert.Equal(original, Assert.Single(Assert.Single(result.Result.Release!.Projects).Packages));
            Assert.False(File.Exists(snapshotPath));
        }
        Assert.Equal(mode == "success" ? 1 : 0, publications.Count);
    }

    [Fact]
    public void Host_returns_a_build_only_checkpoint_without_requiring_staged_artifacts()
    {
        var builds = 0;
        var missingPackage = Path.Combine(_root, "unstaged.nupkg");
        var host = new ProjectBuildHostService(new NullLogger(),
            executeRelease: _ => {
                builds++;
                return Checkpoint(missingPackage).Result.Release!;
            },
            publishGitHub: _ => throw new InvalidOperationException("Build-only checkpoint attempted publication"),
            validateGitHubPreflight: (_, _, _) => throw new InvalidOperationException("Build-only checkpoint attempted remote preflight"));
        var config = new ProjectBuildConfiguration { RootPath = _root, Build = true, PublishNuget = false, PublishGitHub = false };
        var configPath = Path.Combine(_root, "release.json");
        var built = host.Execute(new() { ConfigPath = configPath, ExecuteBuild = true, DeferPublishing = true }, config, configPath);

        var result = host.Execute(new() {
            ConfigPath = configPath, PublicationCheckpoint = built,
            RemotePublishAttempted = () => throw new InvalidOperationException("Build-only checkpoint attempted remote publication")
        }, config, configPath);

        Assert.True(built.Success, built.ErrorMessage);
        Assert.Same(built, result);
        Assert.Equal(2, builds);
        Assert.False(File.Exists(missingPackage));
        Assert.Empty(result.Result.Release!.PublishedPackages);
        Assert.Empty(result.Result.GitHub);
    }

    [Fact]
    public void GitHub_checkpoint_preserves_package_companions_and_includes_release_zip()
    {
        var originalDirectory = Directory.CreateDirectory(Path.Combine(_root, "original")).FullName;
        var stagedDirectory = Directory.CreateDirectory(Path.Combine(_root, "stage")).FullName;
        var names = new[] { "Fixture.1.2.3.nupkg", "Fixture.1.2.3.snupkg", "Fixture.1.2.3.zip" };
        var assets = names.Select((name, index) => {
            var original = Path.Combine(originalDirectory, name);
            var staged = Path.Combine(stagedDirectory, name);
            File.WriteAllText(original, "original " + index);
            File.WriteAllText(staged, "validated " + index);
            return StagedAsset(original, staged);
        }).ToArray();
        var checkpoint = Checkpoint(assets[0].Path);
        assets[2].IsFinalPackageOutput = false;
        var originalProject = Assert.Single(checkpoint.Result.Release!.Projects);
        originalProject.SymbolPackages.Add(assets[1].Path);
        originalProject.ReleaseZipPath = assets[2].Path;
        checkpoint.DeferredPublicationConfiguration = new() { PublishGitHub = true };
        string[] snapshotPaths = [];
        var host = new ProjectBuildHostService(new NullLogger(),
            executeRelease: _ => throw new InvalidOperationException("Checkpoint must not rebuild"),
            publishGitHub: request => {
                var project = Assert.Single(request.Release.Projects);
                var package = Assert.Single(project.Packages);
                var symbols = Assert.Single(project.SymbolPackages);
                Assert.Equal(Path.ChangeExtension(package, ".snupkg"), symbols);
                Assert.NotNull(project.ReleaseZipPath);
                snapshotPaths = [package, symbols, project.ReleaseZipPath];
                Assert.Equal(names, snapshotPaths.Select(Path.GetFileName));
                Assert.Equal(new[] { "validated 0", "validated 1", "validated 2" }, snapshotPaths.Select(File.ReadAllText));
                Assert.All(snapshotPaths, path => Assert.DoesNotContain(path, assets.Select(asset => asset.StagedPath)));
                return new() { Success = true, Results = { new() { Success = true, ProjectName = "Fixture", ReleaseId = 456 } } };
            }, validateGitHubPreflight: null);
        var request = new ProjectBuildHostRequest { ConfigPath = checkpoint.ConfigPath,
            PublicationCheckpoint = checkpoint, PublicationAssets = assets };

        var result = host.Execute(request, new ProjectBuildConfiguration(), checkpoint.ConfigPath);

        Assert.Same(checkpoint, result);
        Assert.Equal(456, Assert.Single(result.Result.GitHub).ReleaseId);
        Assert.Equal(assets[0].Path, Assert.Single(originalProject.Packages));
        Assert.Equal(assets[1].Path, Assert.Single(originalProject.SymbolPackages));
        Assert.Equal(assets[2].Path, originalProject.ReleaseZipPath);
        Assert.All(snapshotPaths, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void NuGet_checkpoint_publishes_validated_packages_and_symbols_to_a_private_local_feed()
    {
        var feed = Directory.CreateDirectory(Path.Combine(_root, "feed")).FullName;
        var originalDirectory = Directory.CreateDirectory(Path.Combine(_root, "original")).FullName;
        var stagedDirectory = Directory.CreateDirectory(Path.Combine(_root, "stage")).FullName;
        var package = Path.Combine(stagedDirectory, "Fixture.1.2.3.nupkg");
        var symbols = Path.ChangeExtension(package, ".snupkg");
        CreatePackage(package, symbols: false);
        CreatePackage(symbols, symbols: true);
        var assets = new[] { package, symbols }.Select(path => {
            var original = Path.Combine(originalDirectory, Path.GetFileName(path));
            File.WriteAllText(original, "unvalidated source bytes");
            return StagedAsset(original, path);
        }).ToArray();
        var checkpoint = Checkpoint(assets[0].Path);
        checkpoint.Result.Release!.Projects[0].SymbolPackages.Add(assets[1].Path);
        checkpoint.DeferredPublicationConfiguration = new() {
            ConfigPath = checkpoint.ConfigPath, PublishNuget = true, PublishGitHub = false,
            PublishSource = feed, PublishApiKey = "local-fixture", IncludeSymbols = true, SkipDuplicate = false
        };
        var host = new ProjectBuildHostService(new NullLogger(),
            executeRelease: _ => throw new InvalidOperationException("Checkpoint must not rebuild"),
            publishGitHub: _ => throw new InvalidOperationException("Local NuGet checkpoint attempted GitHub publication"),
            validateGitHubPreflight: null);
        var request = new ProjectBuildHostRequest { ConfigPath = checkpoint.ConfigPath,
            PublicationCheckpoint = checkpoint, PublicationAssets = assets };

        var result = host.Execute(request, new ProjectBuildConfiguration(), checkpoint.ConfigPath);

        Assert.Same(checkpoint, result);
        Assert.True(result.Success);
        var release = result.Result.Release!;
        Assert.Equal(feed, release.PublishSource);
        Assert.Equal(new[] { package, symbols }.OrderBy(path => path), release.PublishedPackages.OrderBy(path => path));
        Assert.Empty(release.FailedPackages);
        Assert.Empty(release.SkippedDuplicatePackages);
        Assert.Empty(result.Result.GitHub);
        var publishedPackage = Assert.Single(Directory.GetFiles(feed, "*.nupkg", SearchOption.AllDirectories));
        var publishedSymbols = Assert.Single(Directory.GetFiles(feed, "*.snupkg", SearchOption.AllDirectories));
        Assert.Equal(Path.GetDirectoryName(publishedPackage), Path.GetDirectoryName(publishedSymbols));
        Assert.Equal(File.ReadAllBytes(package), File.ReadAllBytes(publishedPackage));
        Assert.Equal(File.ReadAllBytes(symbols), File.ReadAllBytes(publishedSymbols));
    }

    private static PowerForgeReleaseAssetEntry StagedAsset(string original, string staged) => new() {
        Category = PowerForgeReleaseAssetCategory.Package, Path = original, StagedPath = staged,
        StagedSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(staged))), IsFinalPackageOutput = true
    };

    private static void CreatePackage(string path, bool symbols)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("Fixture.nuspec").Open())) {
            writer.Write("<package><metadata><id>Fixture</id><version>1.2.3</version><authors>Tests</authors><description>Local checkpoint fixture</description></metadata></package>");
        }
        using var payload = new StreamWriter(archive.CreateEntry(symbols ? "lib/net8.0/Fixture.pdb" : "lib/net8.0/_._").Open());
        payload.Write("validated fixture");
    }

    private ProjectBuildHostExecutionResult Checkpoint(string package) => new() {
        Success = true, RootPath = _root, ConfigPath = Path.Combine(_root, "release.json"),
        Result = new() { Release = new() { Success = true, ResolvedVersion = "1.2.3", Projects = [new() {
            ProjectName = "Fixture", PackageId = "Fixture", IsPackable = true, NewVersion = "1.2.3", Packages = [package]
        }] } }
    };

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class Runner(Func<ProcessRunRequest, ProcessRunResult> execute) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(execute(request));
    }

    public void Dispose()
    {
        // The local feed preserves the immutable publication snapshot's read-only bit.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }
}
