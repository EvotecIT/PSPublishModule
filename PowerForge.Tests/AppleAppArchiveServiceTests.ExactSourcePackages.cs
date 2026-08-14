using System.Diagnostics;

namespace PowerForge.Tests;

public sealed partial class AppleAppArchiveServiceTests
{
    [Fact]
    public async Task CreateArchiveAsync_exact_source_builds_from_validated_private_package_checkouts()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            RunGit(root.FullName, "init", "--quiet");
            var runner = new ExactPackageProcessRunner(root.FullName);
            var progress = new List<string>();
            WritePackageLock(root.FullName, runner.RemoteUrl, runner.ApprovedRevision);
            CommitApprovedInputs(root.FullName);

            var result = await new AppleAppArchiveService(runner).CreateArchiveAsync(new AppleAppArchiveRequest
            {
                ProjectPath = project.FullName,
                Scheme = "App",
                ArchivePath = Path.Combine(root.FullName, "App.xcarchive"),
                RequireExactPackageSnapshot = true,
                Progress = progress.Add
            });

            Assert.True(result.Succeeded);
            Assert.Equal(64, result.ArchiveSha256?.Length);
            Assert.Equal(2, runner.Requests.Count);
            var resolve = runner.Requests[0];
            var archive = runner.Requests[1];
            Assert.Contains("-resolvePackageDependencies", resolve.Arguments);
            Assert.Contains("-clonedSourcePackagesDirPath", resolve.Arguments);
            Assert.Contains("-onlyUsePackageVersionsFromResolvedFile", resolve.Arguments);
            Assert.Equal("1", resolve.EnvironmentVariables!["GIT_CONFIG_NOSYSTEM"]);
            Assert.Equal("/usr/bin:/bin:/usr/sbin:/sbin", resolve.EnvironmentVariables["PATH"]);
            Assert.False(resolve.InheritEnvironment);
            Assert.Contains("-clonedSourcePackagesDirPath", archive.Arguments);
            Assert.Contains("-derivedDataPath", resolve.Arguments);
            Assert.Contains("-derivedDataPath", archive.Arguments);
            Assert.Equal("/usr/bin:/bin:/usr/sbin:/sbin", archive.EnvironmentVariables!["PATH"]);
            Assert.False(archive.InheritEnvironment);
            Assert.Equal(
                resolve.Arguments[Array.IndexOf(resolve.Arguments.ToArray(), "-clonedSourcePackagesDirPath") + 1],
                archive.Arguments[Array.IndexOf(archive.Arguments.ToArray(), "-clonedSourcePackagesDirPath") + 1]);
            Assert.NotEqual(
                resolve.Arguments[Array.IndexOf(resolve.Arguments.ToArray(), "-derivedDataPath") + 1],
                archive.Arguments[Array.IndexOf(archive.Arguments.ToArray(), "-derivedDataPath") + 1]);
            Assert.False(Directory.Exists(runner.SourcePackagesRoot));
            Assert.False(Directory.Exists(runner.DerivedDataRoot));
            Assert.Single(progress, message => message.Equals(
                "Validating materialized Swift package source and Git provenance",
                StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task CreateArchiveAsync_exact_source_accepts_xcode_private_repository_mirror_origins()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            RunGit(root.FullName, "init", "--quiet");
            var runner = new ExactPackageProcessRunner(root.FullName, materializeXcodeMirrorOrigin: true);
            WritePackageLock(root.FullName, runner.RemoteUrl, runner.ApprovedRevision);
            CommitApprovedInputs(root.FullName);

            var result = await new AppleAppArchiveService(runner).CreateArchiveAsync(new AppleAppArchiveRequest
            {
                ProjectPath = project.FullName,
                Scheme = "App",
                ArchivePath = Path.Combine(root.FullName, "App.xcarchive"),
                RequireExactPackageSnapshot = true
            });

            Assert.True(result.Succeeded);
            Assert.Equal(2, runner.Requests.Count);
            Assert.False(Directory.Exists(runner.SourcePackagesRoot));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task CreateArchiveAsync_exact_source_rejects_linked_xcode_repository_mirror_config()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            RunGit(root.FullName, "init", "--quiet");
            var runner = new ExactPackageProcessRunner(
                root.FullName,
                materializeXcodeMirrorOrigin: true,
                linkXcodeMirrorConfig: true);
            WritePackageLock(root.FullName, runner.RemoteUrl, runner.ApprovedRevision);
            CommitApprovedInputs(root.FullName);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(runner).CreateArchiveAsync(new AppleAppArchiveRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "App",
                    ArchivePath = Path.Combine(root.FullName, "App.xcarchive"),
                    RequireExactPackageSnapshot = true
                }));

            Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(true, false, "include external Git configuration")]
    [InlineData(false, true, "symbolic link")]
    public async Task CreateArchiveAsync_exact_source_rejects_xcode_repository_mirror_indirections(
        bool includeExternalConfig,
        bool linkObjectsInfo,
        string expectedMessage)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            RunGit(root.FullName, "init", "--quiet");
            var runner = new ExactPackageProcessRunner(
                root.FullName,
                materializeXcodeMirrorOrigin: true,
                includeExternalXcodeMirrorConfig: includeExternalConfig,
                linkXcodeMirrorObjectsInfo: linkObjectsInfo);
            WritePackageLock(root.FullName, runner.RemoteUrl, runner.ApprovedRevision);
            CommitApprovedInputs(root.FullName);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(runner).CreateArchiveAsync(new AppleAppArchiveRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "App",
                    ArchivePath = Path.Combine(root.FullName, "App.xcarchive"),
                    RequireExactPackageSnapshot = true
                }));

            Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task CreateArchiveAsync_exact_source_rejects_transient_package_checkout_mutation()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            RunGit(root.FullName, "init", "--quiet");
            var runner = new ExactPackageProcessRunner(root.FullName, mutateDuringArchive: true);
            WritePackageLock(root.FullName, runner.RemoteUrl, runner.ApprovedRevision);
            CommitApprovedInputs(root.FullName);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(runner).CreateArchiveAsync(new AppleAppArchiveRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "App",
                    ArchivePath = Path.Combine(root.FullName, "App.xcarchive"),
                    RequireExactPackageSnapshot = true
                }));

            Assert.Contains("materialized Swift package root changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(runner.SourcePackagesRoot));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task CreateArchiveAsync_exact_source_rejects_restored_package_bytes_changed_through_removed_hard_link_alias()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            RunGit(root.FullName, "init", "--quiet");
            var runner = new ExactPackageProcessRunner(root.FullName, mutatePackageViaHardLinkDuringArchive: true);
            WritePackageLock(root.FullName, runner.RemoteUrl, runner.ApprovedRevision);
            CommitApprovedInputs(root.FullName);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(runner).CreateArchiveAsync(new AppleAppArchiveRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "App",
                    ArchivePath = Path.Combine(root.FullName, "App.xcarchive"),
                    RequireExactPackageSnapshot = true
                }));

            Assert.Contains("hard-link alias", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(runner.SourcePackagesRoot));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task CreateArchiveAsync_exact_source_rejects_materialized_package_revision_outside_lock()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            RunGit(root.FullName, "init", "--quiet");
            var runner = new ExactPackageProcessRunner(root.FullName, materializeWrongRevision: true);
            WritePackageLock(root.FullName, runner.RemoteUrl, runner.ApprovedRevision);
            CommitApprovedInputs(root.FullName);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(runner).CreateArchiveAsync(new AppleAppArchiveRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "App",
                    ArchivePath = Path.Combine(root.FullName, "App.xcarchive"),
                    RequireExactPackageSnapshot = true
                }));

            Assert.Contains("approved revision", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CreateArchiveAsync_exact_source_rejects_materialized_binary_artifact_mutation()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            RunGit(root.FullName, "init", "--quiet");
            var runner = new ExactPackageProcessRunner(
                root.FullName,
                materializeBinaryArtifact: true,
                mutateBinaryArtifactDuringArchive: true);
            WritePackageLock(root.FullName, runner.RemoteUrl, runner.ApprovedRevision);
            CommitApprovedInputs(root.FullName);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(runner).CreateArchiveAsync(new AppleAppArchiveRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "App",
                    ArchivePath = Path.Combine(root.FullName, "App.xcarchive"),
                    RequireExactPackageSnapshot = true
                }));

            Assert.Contains("materialized Swift package root changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CreateArchiveAsync_exact_source_rejects_binary_artifact_replacement_after_resolver_completion()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            RunGit(root.FullName, "init", "--quiet");
            var runner = new ExactPackageProcessRunner(
                root.FullName,
                materializeBinaryArtifact: true,
                replaceBinaryArtifactAfterResolveCompletion: true);
            WritePackageLock(root.FullName, runner.RemoteUrl, runner.ApprovedRevision);
            CommitApprovedInputs(root.FullName);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(runner).CreateArchiveAsync(new AppleAppArchiveRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "App",
                    ArchivePath = Path.Combine(root.FullName, "App.xcarchive"),
                    RequireExactPackageSnapshot = true
                }));

            Assert.Contains("materialized Swift package root changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private sealed class ExactPackageProcessRunner : IProcessRunner
    {
        private readonly bool _mutateDuringArchive;
        private readonly bool _materializeWrongRevision;
        private readonly bool _materializeBinaryArtifact;
        private readonly bool _mutateBinaryArtifactDuringArchive;
        private readonly bool _replaceBinaryArtifactAfterResolveCompletion;
        private readonly bool _mutatePackageViaHardLinkDuringArchive;
        private readonly bool _materializeXcodeMirrorOrigin;
        private readonly bool _linkXcodeMirrorConfig;
        private readonly bool _includeExternalXcodeMirrorConfig;
        private readonly bool _linkXcodeMirrorObjectsInfo;
        private readonly string _fixtureRoot;
        private readonly string _remoteSourceRoot;

        internal ExactPackageProcessRunner(
            string fixtureRoot,
            bool mutateDuringArchive = false,
            bool materializeWrongRevision = false,
            bool materializeBinaryArtifact = false,
            bool mutateBinaryArtifactDuringArchive = false,
            bool replaceBinaryArtifactAfterResolveCompletion = false,
            bool mutatePackageViaHardLinkDuringArchive = false,
            bool materializeXcodeMirrorOrigin = false,
            bool linkXcodeMirrorConfig = false,
            bool includeExternalXcodeMirrorConfig = false,
            bool linkXcodeMirrorObjectsInfo = false)
        {
            _fixtureRoot = fixtureRoot;
            _mutateDuringArchive = mutateDuringArchive;
            _materializeWrongRevision = materializeWrongRevision;
            _materializeBinaryArtifact = materializeBinaryArtifact;
            _mutateBinaryArtifactDuringArchive = mutateBinaryArtifactDuringArchive;
            _replaceBinaryArtifactAfterResolveCompletion = replaceBinaryArtifactAfterResolveCompletion;
            _mutatePackageViaHardLinkDuringArchive = mutatePackageViaHardLinkDuringArchive;
            _materializeXcodeMirrorOrigin = materializeXcodeMirrorOrigin;
            _linkXcodeMirrorConfig = linkXcodeMirrorConfig;
            _includeExternalXcodeMirrorConfig = includeExternalXcodeMirrorConfig;
            _linkXcodeMirrorObjectsInfo = linkXcodeMirrorObjectsInfo;
            _remoteSourceRoot = Directory.CreateDirectory(Path.Combine(fixtureRoot, "RemoteShared")).FullName;
            RunGit(_remoteSourceRoot, "init", "--quiet");
            RunGit(_remoteSourceRoot, "config", "user.name", "PowerForge Tests");
            RunGit(_remoteSourceRoot, "config", "user.email", "powerforge-tests@example.invalid");
            File.WriteAllText(
                Path.Combine(_remoteSourceRoot, "Package.swift"),
                "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Shared\")\n");
            RunGit(_remoteSourceRoot, "add", ".");
            RunGit(_remoteSourceRoot, "commit", "--quiet", "-m", "Package fixture");
            ApprovedRevision = ReadGit(_remoteSourceRoot, "rev-parse", "HEAD").Trim();
        }

        internal List<ProcessRunRequest> Requests { get; } = new();

        internal string SourcePackagesRoot { get; private set; } = string.Empty;

        internal string DerivedDataRoot { get; private set; } = string.Empty;

        internal string RemoteUrl { get; } = "https://example.invalid/Shared.git";

        internal string ApprovedRevision { get; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Arguments.Contains("-resolvePackageDependencies"))
            {
                var index = Array.IndexOf(request.Arguments.ToArray(), "-clonedSourcePackagesDirPath");
                SourcePackagesRoot = request.Arguments[index + 1];
                var derivedIndex = Array.IndexOf(request.Arguments.ToArray(), "-derivedDataPath");
                DerivedDataRoot = request.Arguments[derivedIndex + 1];
                var checkouts = Directory.CreateDirectory(Path.Combine(SourcePackagesRoot, "checkouts")).FullName;
                var checkout = Path.Combine(checkouts, "Shared");
                RunGit(checkouts, "clone", "--quiet", "--no-hardlinks", _remoteSourceRoot, checkout);
                if (_materializeXcodeMirrorOrigin)
                {
                    var repositories = Directory.CreateDirectory(Path.Combine(SourcePackagesRoot, "repositories")).FullName;
                    var mirror = Path.Combine(repositories, "Shared-12345678");
                    RunGit(repositories, "clone", "--quiet", "--mirror", "--no-hardlinks", _remoteSourceRoot, mirror);
                    RunGit(mirror, "remote", "set-url", "origin", RemoteUrl);
                    if (_linkXcodeMirrorConfig)
                    {
                        var config = Path.Combine(mirror, "config");
                        var externalConfig = Path.Combine(_fixtureRoot, "linked-mirror-config");
                        File.Copy(config, externalConfig);
                        File.Delete(config);
                        File.CreateSymbolicLink(config, externalConfig);
                    }
                    if (_includeExternalXcodeMirrorConfig)
                    {
                        var externalConfig = Path.Combine(_fixtureRoot, "included-mirror-config");
                        File.WriteAllText(externalConfig, "[core]\n\tbare = true\n");
                        File.AppendAllText(
                            Path.Combine(mirror, "config"),
                            $"\n[include]\n\tpath = {externalConfig}\n");
                    }
                    if (_linkXcodeMirrorObjectsInfo)
                    {
                        var objectsInfo = Path.Combine(mirror, "objects", "info");
                        var externalObjectsInfo = Directory.CreateDirectory(
                            Path.Combine(_fixtureRoot, "linked-objects-info")).FullName;
                        Directory.Delete(objectsInfo, recursive: true);
                        Directory.CreateSymbolicLink(objectsInfo, externalObjectsInfo);
                    }
                    RunGit(checkout, "remote", "set-url", "origin", mirror);
                }
                else
                {
                    RunGit(checkout, "remote", "set-url", "origin", RemoteUrl);
                }
                if (_materializeWrongRevision)
                {
                    RunGit(checkout, "config", "user.name", "PowerForge Tests");
                    RunGit(checkout, "config", "user.email", "powerforge-tests@example.invalid");
                    File.AppendAllText(Path.Combine(checkout, "Package.swift"), "// replacement\n");
                    RunGit(checkout, "add", ".");
                    RunGit(checkout, "commit", "--quiet", "-m", "Unapproved replacement");
                }
                if (_materializeBinaryArtifact)
                {
                    var artifact = Directory.CreateDirectory(Path.Combine(SourcePackagesRoot, "artifacts", "Shared", "Framework.xcframework"));
                    File.WriteAllText(Path.Combine(artifact.FullName, "payload"), "approved binary artifact");
                }
            }
            else if (_mutateDuringArchive)
            {
                var manifest = Path.Combine(SourcePackagesRoot, "checkouts", "Shared", "Package.swift");
                var original = File.ReadAllText(manifest);
                File.WriteAllText(manifest, original + "// injected\n");
                File.WriteAllText(manifest, original);
            }
            else if (_mutatePackageViaHardLinkDuringArchive)
            {
                var manifest = Path.Combine(SourcePackagesRoot, "checkouts", "Shared", "Package.swift");
                var original = File.ReadAllText(manifest);
                var alias = Path.Combine(_fixtureRoot, $"package-alias-{Guid.NewGuid():N}");
                TestFileLink.CreateHardLink(alias, manifest);
                try
                {
                    File.WriteAllText(alias, original + "// injected through external alias\n");
                    File.WriteAllText(alias, original);
                }
                finally
                {
                    File.Delete(alias);
                }
            }
            else if (_mutateBinaryArtifactDuringArchive)
            {
                var payload = Path.Combine(SourcePackagesRoot, "artifacts", "Shared", "Framework.xcframework", "payload");
                File.WriteAllText(payload, "replacement binary artifact");
            }

            if (!request.Arguments.Contains("-resolvePackageDependencies"))
            {
                var archiveIndex = Array.IndexOf(request.Arguments.ToArray(), "-archivePath");
                var archive = Directory.CreateDirectory(request.Arguments[archiveIndex + 1]);
                File.WriteAllText(Path.Combine(archive.FullName, "payload"), "approved archive");
            }

            var result = new ProcessRunResult(
                0,
                "ok",
                string.Empty,
                request.FileName,
                TimeSpan.FromMilliseconds(1),
                false);
            if (request.Arguments.Contains("-resolvePackageDependencies") &&
                _replaceBinaryArtifactAfterResolveCompletion)
            {
                request.InvokeCompletionBoundary(result);
                var payload = Path.Combine(SourcePackagesRoot, "artifacts", "Shared", "Framework.xcframework", "payload");
                File.WriteAllText(payload, "replacement after resolver completion");
            }
            return Task.FromResult(result);
        }
    }

    private static void WritePackageLock(string root, string url, string revision)
    {
        File.WriteAllText(
            Path.Combine(root, "Package.resolved"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                pins = new[]
                {
                    new
                    {
                        identity = "shared",
                        kind = "remoteSourceControl",
                        location = url,
                        state = new { revision, version = "1.0.0" }
                    }
                },
                version = 3
            }));
    }

    private static void CommitApprovedInputs(string root)
    {
        RunGit(root, "config", "user.name", "PowerForge Tests");
        RunGit(root, "config", "user.email", "powerforge-tests@example.invalid");
        RunGit(root, "add", "App.xcodeproj/project.pbxproj", "Package.resolved");
        RunGit(root, "commit", "--quiet", "-m", "Approved exact inputs");
    }

    private static string ReadGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start git fixture process.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {output}{error}");
        return output;
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start git fixture process.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {output}{error}");
    }
}
