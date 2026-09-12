using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PowerForge.Tests;

public sealed class ReleaseValidationPublicationPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.PublicationPolicy.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _feed;

    public ReleaseValidationPublicationPolicyTests()
    {
        Directory.CreateDirectory(_root);
        _feed = Directory.CreateDirectory(Path.Combine(_root, "empty-feed")).FullName;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deferred_publisher_orders_dependencies_and_blocks_consumers_of_failed_packages(bool failDependency)
    {
        var app = Project("A", ["B"]);
        var dependency = Project("B");
        var independent = Project("C");
        var release = Release(app, dependency, independent);
        var attempts = new List<string>();
        var owner = Owner((path, _, _, _, _, _) => {
            var id = Path.GetFileName(path).Split('.')[0];
            attempts.Add(id);
            return new() { Outcome = failDependency && id == "B"
                ? DotNetRepositoryReleaseService.PackagePushOutcome.Failed
                : DotNetRepositoryReleaseService.PackagePushOutcome.Published,
                Message = "Fixture publication result" };
        });
        var configuration = Configuration();
        configuration.PublicationSpec!.PublishFailFast = false;

        var result = new ProjectBuildPublishHostService(new NullLogger(), null, owner)
            .PublishNuGet(configuration, release, _root);

        Assert.Equal(!failDependency, result.Success);
        Assert.Equal(!failDependency, release.Success);
        if (failDependency) {
            Assert.Equal(new[] { "B", "C" }, attempts);
            Assert.Contains(app.Packages[0], result.FailedItems);
            Assert.Contains(dependency.Packages[0], result.FailedItems);
            Assert.Contains("required selected package(s) B", app.ErrorMessage);
            Assert.Equal(independent.Packages, result.PublishedItems);
        } else {
            Assert.Equal(new[] { "B", "A", "C" }, attempts);
            Assert.Equal(new[] { dependency.Packages[0], app.Packages[0], independent.Packages[0] }, result.PublishedItems);
            Assert.Empty(result.FailedItems);
        }
    }

    [Theory]
    [InlineData("cycle", "dependency cycle")]
    [InlineData("duplicate", "more than one selected project")]
    [InlineData("missing-version", "no resolved version")]
    [InlineData("project-error", "has errors")]
    public void Deferred_publisher_admits_the_entire_release_before_any_push(string variant, string expectedError)
    {
        var first = Project("A", variant == "cycle" ? ["B"] : []);
        var later = Project("B", variant == "cycle" ? ["A"] : [], variant == "duplicate" ? "A" : null);
        if (variant == "missing-version") { later.NewVersion = ""; }
        if (variant == "project-error") { later.ErrorMessage = "Build fixture error"; }
        var attempts = new List<string>();
        var publisher = new ProjectBuildPublishHostService(new NullLogger(), null,
            Owner((path, _, _, _, _, _) => {
                attempts.Add(path);
                return new() { Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Published };
            }));
        var release = Release(first, later);

        if (variant is "cycle" or "duplicate") {
            var error = Assert.Throws<InvalidOperationException>(() => publisher.PublishNuGet(Configuration(), release, _root));
            Assert.Contains(expectedError, error.Message, StringComparison.OrdinalIgnoreCase);
        } else {
            var result = publisher.PublishNuGet(Configuration(), release, _root);
            Assert.False(result.Success);
            Assert.False(release.Success);
            Assert.Contains(expectedError, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(result.PublishedItems);
        }
        Assert.Empty(attempts);
        Assert.Empty(release.PublishedPackages);
    }

    [Fact]
    public void Oversized_later_package_stops_the_canonical_checkpoint_owner_before_all_pushes()
    {
        var first = Project("A");
        var later = Project("B");
        using (var stream = new FileStream(later.Packages[0], FileMode.Open, FileAccess.Write, FileShare.None)) {
            if (OperatingSystem.IsWindows()) {
                Assert.True(DeviceIoControl(stream.SafeFileHandle, 0x000900c4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero),
                    "The oversized-package fixture must be sparse on Windows.");
            }
            stream.SetLength(DotNetRepositoryReleaseService.NuGetOrgPackageSizeLimitBytes + 1);
        }
        var attempts = 0;
        var owner = Owner((_, _, _, _, _, _) => {
            attempts++;
            return new() { Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Published };
        });
        var spec = Configuration().PublicationSpec!;
        spec.PublishSource = "https://api.nuget.org/v3/index.json";
        // Version discovery stays on an empty local feed; the nuget.org URL only selects its size policy.
        Assert.Equal(new[] { _feed }, spec.VersionSources);

        var result = owner.PublishExistingPackages(spec, Release(first, later));

        Assert.False(result.Success);
        Assert.Contains("exceeds the nuget.org package limit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFileName(later.Packages[0]), result.ErrorMessage);
        Assert.Empty(result.PublishedItems);
        Assert.Equal(0, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Captured_named_feed_uses_the_package_root_and_cannot_be_redirected_after_build(bool changeConfigAfterBuild)
    {
        var child = Directory.CreateDirectory(Path.Combine(_root, "child")).FullName;
        var originalFeed = Directory.CreateDirectory(Path.Combine(_root, "original-feed")).FullName;
        var redirectedFeed = Directory.CreateDirectory(Path.Combine(_root, "redirected-feed")).FullName;
        WriteSources(Path.Combine(_root, "NuGet.Config"), redirectedFeed);
        WriteSources(Path.Combine(child, "NuGet.Config"), originalFeed);
        var project = Project("Fixture");
        var hostCalls = 0;
        var host = new ProjectBuildHostService(new NullLogger(),
            executeRelease: spec => {
                hostCalls++;
                Assert.False(spec.Publish);
                return Release(project);
            },
            publishGitHub: _ => throw new InvalidOperationException("Unexpected GitHub publication"),
            validateGitHubPreflight: null);
        var configPath = Path.Combine(_root, "release.json");
        var config = new ProjectBuildConfiguration {
            RootPath = child, Build = true, PublishNuget = true, PublishGitHub = false,
            PublishSource = "release-feed", PublishApiKey = "fixture-key", NugetSource = [_feed],
            IncludePrerelease = true, SkipDuplicate = false, PublishFailFast = false
        };
        var checkpoint = host.Execute(new() { ConfigPath = configPath, ExecuteBuild = true, DeferPublishing = true }, config, configPath);
        Assert.True(checkpoint.Success, checkpoint.ErrorMessage);
        var captured = Assert.IsType<ProjectBuildPublishHostConfiguration>(checkpoint.DeferredPublicationConfiguration);
        Assert.Equal(child, captured.PublicationSpec!.RootPath);
        Assert.Equal(originalFeed, captured.PublicationSpec.PublishSource);
        Assert.Equal(new[] { _feed }, captured.PublicationSpec.VersionSources);
        Assert.True(captured.PublicationSpec.IncludePrerelease);
        Assert.False(captured.PublicationSpec.SkipDuplicate);
        Assert.False(captured.PublicationSpec.PublishFailFast);
        if (changeConfigAfterBuild) { WriteSources(Path.Combine(child, "NuGet.Config"), redirectedFeed); }
        var pushes = new List<(string Source, string? Root)>();
        var publisher = new ProjectBuildPublishHostService(new NullLogger(), null,
            Owner((_, _, source, _, _, root) => {
                pushes.Add((source, root));
                return new() { Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Published };
            }));

        var result = publisher.PublishNuGet(captured, checkpoint.Result.Release!, _root);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal((originalFeed, (string?)child), Assert.Single(pushes));
        Assert.Equal(project.Packages, result.PublishedItems);
        Assert.Equal(2, hostCalls);
    }

    [Fact]
    public void Same_run_module_package_checkpoint_keeps_its_captured_local_feed()
    {
        var moduleRoot = Directory.CreateDirectory(Path.Combine(_root, "Module")).FullName;
        var originalFeed = Directory.CreateDirectory(Path.Combine(_root, "module-feed")).FullName;
        var redirectedFeed = Directory.CreateDirectory(Path.Combine(_root, "redirected-module-feed")).FullName;
        WriteSources(Path.Combine(_root, "NuGet.Config"), redirectedFeed);
        WriteSources(Path.Combine(moduleRoot, "NuGet.Config"), originalFeed);
        File.WriteAllText(Path.Combine(_root, "module.json"), JsonSerializer.Serialize(new {
            Build = new { Name = "Fixture", SourcePath = "Module" },
            Segments = new[] { new { Type = "PackageBuild", Configuration = new {
                RootPath = ".", Build = true, UpdateVersions = false, PublishNuget = true,
                PublishGitHub = false, PublishSource = "release-feed", PublishApiKey = "local-fixture",
                NugetSource = new[] { _feed }
            } } }
        }));
        var project = Project("ModuleFixture");
        var plans = 0;
        var host = new ProjectBuildHostService(new NullLogger(), executeRelease: spec => {
            plans++;
            Assert.True(spec.WhatIf);
            Assert.False(spec.Publish);
            return Release(project);
        }, publishGitHub: null, validateGitHubPreflight: null);
        var service = new ModulePackageReleaseCheckpointService(host, new NullLogger());
        var releaseConfig = Path.Combine(_root, "release.json");
        var spec = new PowerForgeReleaseSpec { Module = new() {
            RepositoryRoot = _root, ConfigPath = "module.json", IncludesPackages = true
        } };
        var checkpoint = Assert.Single(service.Capture(releaseConfig, spec));
        Assert.Equal(originalFeed, checkpoint.PublicationConfiguration!.PublicationSpec!.PublishSource);
        Assert.Equal(moduleRoot, checkpoint.PublicationConfiguration.PublicationSpec.RootPath);
        WriteSources(Path.Combine(moduleRoot, "NuGet.Config"), redirectedFeed);

        var result = Assert.Single(service.PublishNuGet(releaseConfig, spec, [checkpoint], releaseAssets: null,
            requireStagedAssets: false, remotePublishAttempted: null, progress: null, CancellationToken.None));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(originalFeed, result.PublishSource);
        Assert.Equal(project.Packages, result.PublishedPackages);
        Assert.Equal(1, plans);
        var published = Assert.Single(Directory.GetFiles(originalFeed, "*.nupkg", SearchOption.AllDirectories));
        Assert.Equal(File.ReadAllBytes(project.Packages[0]), File.ReadAllBytes(published));
        Assert.Empty(Directory.GetFiles(redirectedFeed, "*.nupkg", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("remote")]
    [InlineData("local")]
    [InlineData("credential")]
    [InlineData("between-pushes")]
    public void Named_remote_checkpoint_pins_endpoint_and_guards_existing_authentication(string mutation)
    {
        var child = Directory.CreateDirectory(Path.Combine(_root, "remote-child")).FullName;
        var configPath = Path.Combine(child, "NuGet.Config");
        const string endpoint = "https://original.invalid/v3/index.json";
        WriteRemoteSource(configPath, endpoint, "fixture-password");
        var configBefore = File.ReadAllText(configPath);
        var first = Project("A");
        var later = Project("B");
        var host = new ProjectBuildHostService(new NullLogger(),
            executeRelease: _ => Release(first, later), publishGitHub: null, validateGitHubPreflight: null);
        var checkpoint = host.Execute(new() {
            ConfigPath = Path.Combine(_root, "release.json"), ExecuteBuild = true, DeferPublishing = true
        }, new ProjectBuildConfiguration {
            RootPath = child, Build = true, PublishNuget = true, PublishGitHub = false,
            PublishSource = "release-feed", PublishApiKey = "fixture-key", NugetSource = [_feed]
        }, Path.Combine(_root, "release.json"));
        Assert.True(checkpoint.Success, checkpoint.ErrorMessage);
        var captured = checkpoint.DeferredPublicationConfiguration!;
        Assert.Equal(endpoint, captured.PublicationSpec!.PublishSource);
        Assert.Equal(child, captured.PublicationSpec.RootPath);
        Assert.NotNull(captured.ValidatePublicationContext);
        if (mutation == "remote") { WriteRemoteSource(configPath, "https://redirected.invalid/v3/index.json", "fixture-password"); }
        if (mutation == "local") { WriteRemoteSource(configPath, _feed, "fixture-password"); }
        if (mutation == "credential") { WriteRemoteSource(configPath, endpoint, "replacement-password"); }
        var attempts = new List<string>();
        var publisher = new ProjectBuildPublishHostService(new NullLogger(), null,
            Owner((path, _, source, _, _, root) => {
                attempts.Add(path);
                Assert.Equal(endpoint, source);
                Assert.Equal(child, root);
                Assert.Equal(configBefore, File.ReadAllText(configPath));
                if (mutation == "between-pushes") { WriteRemoteSource(configPath, endpoint, "replacement-password"); }
                return new() { Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Published };
            }));

        if (mutation == "unchanged") {
            var result = publisher.PublishNuGet(captured, checkpoint.Result.Release!, _root);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(new[] { first.Packages[0], later.Packages[0] }, attempts);
            Assert.Equal(attempts, result.PublishedItems);
            Assert.Equal(configBefore, File.ReadAllText(configPath));
        } else {
            var error = Assert.Throws<InvalidOperationException>(() => publisher.PublishNuGet(captured, checkpoint.Result.Release!, _root));
            Assert.Contains("changed after checkpoint capture", error.Message);
            Assert.DoesNotContain("fixture-password", error.Message);
            Assert.DoesNotContain("replacement-password", error.Message);
            Assert.Equal(mutation == "between-pushes" ? new[] { first.Packages[0] } : [], attempts);
        }
    }

    [Fact]
    public void Module_remote_checkpoint_rejects_changed_authentication_before_the_push_boundary()
    {
        var moduleRoot = Directory.CreateDirectory(Path.Combine(_root, "Module")).FullName;
        const string endpoint = "https://module-original.invalid/v3/index.json";
        var sourceConfig = Path.Combine(moduleRoot, "NuGet.Config");
        WriteRemoteSource(sourceConfig, endpoint, "fixture-password");
        File.WriteAllText(Path.Combine(_root, "module.json"), JsonSerializer.Serialize(new {
            Build = new { Name = "Fixture", SourcePath = "Module" },
            Segments = new[] { new { Type = "PackageBuild", Configuration = new {
                RootPath = ".", Build = true, UpdateVersions = false, PublishNuget = true,
                PublishGitHub = false, PublishSource = "release-feed", PublishApiKey = "fixture-key",
                NugetSource = new[] { _feed }
            } } }
        }));
        var project = Project("ModuleRemoteFixture");
        var host = new ProjectBuildHostService(new NullLogger(), executeRelease: _ => Release(project),
            publishGitHub: null, validateGitHubPreflight: null);
        var spec = new PowerForgeReleaseSpec { Module = new() {
            RepositoryRoot = _root, ConfigPath = "module.json", IncludesPackages = true
        } };
        var checkpoint = Assert.Single(new ModulePackageReleaseCheckpointService(host, new NullLogger())
            .Capture(Path.Combine(_root, "release.json"), spec));
        var captured = checkpoint.PublicationConfiguration!;
        Assert.Equal(endpoint, captured.PublicationSpec!.PublishSource);
        Assert.Equal(moduleRoot, captured.PublicationSpec.RootPath);
        Assert.NotNull(captured.ValidatePublicationContext);
        WriteRemoteSource(sourceConfig, endpoint, "replacement-password");
        var attempts = 0;
        var publisher = new ProjectBuildPublishHostService(new NullLogger(), null,
            Owner((_, _, _, _, _, _) => {
                attempts++;
                return new() { Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Published };
            }));

        var error = Assert.Throws<InvalidOperationException>(() => publisher.PublishNuGet(captured, checkpoint.Release, _root));

        Assert.Contains("changed after checkpoint capture", error.Message);
        Assert.Equal(0, attempts);
        Assert.Empty(checkpoint.Release.PublishedPackages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Non_NuGet_checkpoints_do_not_resolve_an_unused_unknown_publish_source(bool publishGitHub)
    {
        var project = Project("Fixture");
        var host = new ProjectBuildHostService(new NullLogger(), executeRelease: _ => Release(project),
            publishGitHub: _ => throw new InvalidOperationException("Deferred GitHub publication ran during build"),
            validateGitHubPreflight: (_, _, _) => null);
        var configPath = Path.Combine(_root, "release.json");

        var result = host.Execute(new() { ConfigPath = configPath, ExecuteBuild = true, DeferPublishing = true },
            new ProjectBuildConfiguration {
                RootPath = _root, Build = true, PublishNuget = false, PublishGitHub = publishGitHub,
                PublishSource = "unknown-unused-source", NugetSource = [_feed],
                GitHubAccessToken = "fixture-token", GitHubUsername = "fixture-owner", GitHubRepositoryName = "fixture-repo"
            }, configPath);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.DeferredPublicationConfiguration);
        Assert.False(result.DeferredPublicationConfiguration.PublishNuget);
        Assert.Equal(publishGitHub, result.DeferredPublicationConfiguration.PublishGitHub);
        Assert.Null(result.DeferredPublicationConfiguration.ValidatePublicationContext);
        Assert.Empty(result.Result.Release!.PublishedPackages);
        Assert.Empty(result.Result.GitHub);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Canonical_checkpoint_publication_honors_cancellation_before_and_between_pushes(bool cancelAfterFirstPush)
    {
        var first = Project("A");
        var second = Project("B");
        using var cancellation = new CancellationTokenSource();
        var attempts = new List<string>();
        var publisher = new ProjectBuildPublishHostService(new NullLogger(), null,
            Owner((path, _, _, _, _, _) => {
                attempts.Add(path);
                cancellation.Cancel();
                return new() { Outcome = DotNetRepositoryReleaseService.PackagePushOutcome.Published };
            }));
        if (!cancelAfterFirstPush) { cancellation.Cancel(); }

        Assert.ThrowsAny<OperationCanceledException>(() => publisher.PublishNuGet(Configuration(), Release(first, second),
            _root, cancellationToken: cancellation.Token));

        Assert.Equal(cancelAfterFirstPush ? new[] { first.Packages[0] } : [], attempts);
    }

    [Fact]
    public void Interrupted_host_checkpoint_preserves_already_published_artifact_evidence()
    {
        var first = Project("A");
        var later = Project("B");
        var release = Release(first, later);
        var configPath = Path.Combine(_root, "release.json");
        var checkpoint = new ProjectBuildHostExecutionResult {
            Success = true, RootPath = _root, ConfigPath = configPath,
            DeferredPublicationConfiguration = Configuration(), Result = new() { Success = true, Release = release }
        };
        var assets = release.Projects.SelectMany(project => project.Packages).Select(path => new PowerForgeReleaseAssetEntry {
            Category = PowerForgeReleaseAssetCategory.Package, Path = path, StagedPath = path,
            StagedSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
        }).ToArray();
        var host = new ProjectBuildHostService(new NullLogger(),
            executeRelease: _ => throw new InvalidOperationException("Checkpoint must not rebuild"),
            publishGitHub: null, validateGitHubPreflight: null);

        var error = Assert.Throws<InvalidOperationException>(() => host.Execute(new ProjectBuildHostRequest {
            ConfigPath = configPath, PublicationCheckpoint = checkpoint, PublicationAssets = assets,
            RemotePublishAttempted = () => {
                if (Directory.EnumerateFiles(_feed, "*.nupkg", SearchOption.AllDirectories).Any())
                    throw new InvalidOperationException("Stop after the first completed publication");
            }
        }, new ProjectBuildConfiguration(), configPath));

        Assert.Contains("Stop after", error.Message);
        Assert.Equal(first.Packages, release.PublishedPackages);
        Assert.DoesNotContain(later.Packages[0], release.PublishedPackages);
        Assert.Equal("A.1.0.0.nupkg", Path.GetFileName(Assert.Single(Directory.GetFiles(_feed, "*.nupkg", SearchOption.AllDirectories))));
    }

    private static void WriteRemoteSource(string path, string source, string password)
        => File.WriteAllText(path, $"<configuration><packageSources><clear/><add key=\"release-feed\" value=\"{SecurityElement.Escape(source)}\"/></packageSources><packageSourceCredentials><release-feed><add key=\"Username\" value=\"fixture-user\"/><add key=\"ClearTextPassword\" value=\"{password}\"/><add key=\"ValidAuthenticationTypes\" value=\"basic\"/></release-feed></packageSourceCredentials></configuration>");

    private ProjectBuildPublishHostConfiguration Configuration() => new() {
        ConfigPath = Path.Combine(_root, "release.json"), PublishNuget = true, PublishSource = _feed, PublishApiKey = "fixture-key",
        PublicationSpec = new() { RootPath = _root, PublishSource = _feed, PublishApiKey = "fixture-key",
            VersionSources = [_feed], IncludePrerelease = false, PublishFailFast = true, SkipDuplicate = true }
    };

    private static DotNetRepositoryReleaseService Owner(DotNetRepositoryReleaseService.PackagePushHandler push)
        => new(new NullLogger(), null, null, push);

    private static DotNetRepositoryReleaseResult Release(params DotNetRepositoryProjectResult[] projects)
        => new() { Success = true, ResolvedVersion = "1.0.0", Projects = projects.ToList() };

    private DotNetRepositoryProjectResult Project(string name, string[]? dependencies = null, string? packageId = null)
    {
        packageId ??= name;
        var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        var path = Path.Combine(directory, packageId + ".1.0.0.nupkg");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) {
            using var writer = new StreamWriter(archive.CreateEntry(packageId + ".nuspec").Open());
            var dependencyXml = string.Join("", (dependencies ?? []).Select(id => $"<dependency id=\"{id}\" version=\"[1.0.0]\" />"));
            writer.Write($"<package><metadata><id>{packageId}</id><version>1.0.0</version><authors>Tests</authors><description>Publication policy fixture</description><dependencies><group targetFramework=\"net8.0\">{dependencyXml}</group></dependencies></metadata></package>");
        }
        return new() { ProjectName = name, PackageId = packageId, NewVersion = "1.0.0", IsPackable = true,
            CsprojPath = Path.Combine(directory, name + ".csproj"), Packages = [path] };
    }

    private static void WriteSources(string configPath, string source)
        => File.WriteAllText(configPath, $"<configuration><packageSources><clear/><add key=\"release-feed\" value=\"{SecurityElement.Escape(source)}\"/></packageSources></configuration>");

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint controlCode, IntPtr input, uint inputSize,
        IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }
}
