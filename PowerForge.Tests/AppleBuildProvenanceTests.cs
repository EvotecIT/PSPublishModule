using System.Diagnostics;
using PowerForge;

namespace PowerForge.Tests;

public sealed class AppleBuildProvenanceTests
{
    [Fact]
    public void ResolveLocalSourceRevision_requires_a_clean_working_tree()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.AppleBuildProvenance",
            Guid.NewGuid().ToString("N")));
        try
        {
            RunGit(root.FullName, "init");
            RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
            RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
            File.WriteAllText(Path.Combine(root.FullName, "tracked.txt"), "clean");
            RunGit(root.FullName, "add", "tracked.txt");
            RunGit(root.FullName, "commit", "-m", "fixture");
            var head = RunGit(root.FullName, "rev-parse", "HEAD").Trim().ToLowerInvariant();

            Assert.Equal(head, AppleBuildProvenance.ResolveLocalSourceRevision(root.FullName));

            File.WriteAllText(Path.Combine(root.FullName, "untracked.txt"), "dirty");
            Assert.Null(AppleBuildProvenance.ResolveLocalSourceRevision(root.FullName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveLocalSourceRevision_fails_closed_when_status_cannot_be_read()
    {
        var runner = new StubProcessRunner(request =>
        {
            return request.Arguments.FirstOrDefault() switch
            {
                "for-each-ref" => new ProcessRunResult(
                    0,
                    string.Empty,
                    string.Empty,
                    request.FileName,
                    TimeSpan.Zero,
                    timedOut: false),
                "rev-parse" => new ProcessRunResult(
                    0,
                    new string('a', 40),
                    string.Empty,
                    request.FileName,
                    TimeSpan.Zero,
                    timedOut: false),
                _ => new ProcessRunResult(
                    128,
                    string.Empty,
                    "status unavailable",
                    request.FileName,
                    TimeSpan.Zero,
                    timedOut: false),
            };
        });
        var git = GitClient.CreateTrustedSystemClient(runner, TimeSpan.FromSeconds(10));

        Assert.Null(AppleBuildProvenance.ResolveLocalSourceRevision(
            Directory.GetCurrentDirectory(),
            git));
    }

    [Fact]
    public void ResolveLocalSourceRevision_rejects_git_replacement_refs()
    {
        var root = CreateRepository();
        try
        {
            var head = RunGit(root.FullName, "rev-parse", "HEAD").Trim();
            var tree = RunGit(root.FullName, "rev-parse", "HEAD^{tree}").Trim();
            var replacement = RunGit(
                root.FullName,
                "commit-tree",
                tree,
                "-m",
                "replacement").Trim();
            RunGit(root.FullName, "replace", head, replacement);

            Assert.Null(AppleBuildProvenance.ResolveLocalSourceRevision(root.FullName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveLocalSourceRevision_rejects_mode_changes_hidden_by_git_config()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = CreateRepository();
        try
        {
            RunGit(root.FullName, "update-index", "--chmod=+x", "tracked.txt");
            RunGit(root.FullName, "commit", "-m", "mark executable");
            RunGit(root.FullName, "config", "core.fileMode", "false");
            var path = Path.Combine(root.FullName, "tracked.txt");
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(
                path,
                mode & ~(
                    UnixFileMode.UserExecute |
                    UnixFileMode.GroupExecute |
                    UnixFileMode.OtherExecute));

            Assert.True(string.IsNullOrWhiteSpace(
                RunGit(root.FullName, "status", "--porcelain")));
            Assert.Null(AppleBuildProvenance.ResolveLocalSourceRevision(root.FullName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveLocalSourceRevision_rejects_clean_filter_transformed_bytes()
    {
        var root = CreateRepository();
        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, ".gitattributes"),
                "tracked.txt text\n");
            File.WriteAllText(
                Path.Combine(root.FullName, "tracked.txt"),
                "line one\nline two\n");
            RunGit(root.FullName, "add", ".gitattributes", "tracked.txt");
            RunGit(root.FullName, "commit", "-m", "normalize tracked input");
            RunGit(root.FullName, "config", "core.autocrlf", "true");
            File.Delete(Path.Combine(root.FullName, "tracked.txt"));
            RunGit(root.FullName, "checkout", "--", "tracked.txt");

            Assert.True(string.IsNullOrWhiteSpace(
                RunGit(root.FullName, "status", "--porcelain")));
            Assert.Contains(
                "\r\n",
                File.ReadAllText(Path.Combine(root.FullName, "tracked.txt")),
                StringComparison.Ordinal);
            Assert.Null(AppleBuildProvenance.ResolveLocalSourceRevision(root.FullName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveLocalSourceRevision_rejects_external_hard_link_aliases()
    {
        var root = CreateRepository();
        var external = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.AppleBuildProvenance.External",
            Guid.NewGuid().ToString("N")));
        try
        {
            var trackedPath = Path.Combine(root.FullName, "tracked.txt");
            var externalPath = Path.Combine(external.FullName, "tracked.txt");
            File.WriteAllBytes(externalPath, File.ReadAllBytes(trackedPath));
            File.Delete(trackedPath);
            TestFileLink.CreateHardLink(trackedPath, externalPath);

            Assert.True(string.IsNullOrWhiteSpace(
                RunGit(root.FullName, "status", "--porcelain")));
            Assert.Null(AppleBuildProvenance.ResolveLocalSourceRevision(root.FullName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { external.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateUnchanged_rejects_a_transient_external_hard_link_alias()
    {
        var root = CreateRepository();
        var external = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.AppleBuildProvenance.External",
            Guid.NewGuid().ToString("N")));
        try
        {
            var snapshot = AppleBuildProvenance.Capture(root.FullName);
            var alias = Path.Combine(external.FullName, "tracked.txt");
            TestFileLink.CreateHardLink(
                alias,
                Path.Combine(root.FullName, "tracked.txt"));
            File.Delete(alias);

            Assert.Throws<InvalidOperationException>(() =>
                AppleBuildProvenance.ValidateUnchanged(snapshot));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { external.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("--assume-unchanged")]
    [InlineData("--skip-worktree")]
    public void ResolveLocalSourceRevision_rejects_hidden_tracked_file_changes(
        string indexFlag)
    {
        var root = CreateRepository();
        try
        {
            RunGit(root.FullName, "update-index", indexFlag, "tracked.txt");
            File.WriteAllText(Path.Combine(root.FullName, "tracked.txt"), "hidden change");

            Assert.Null(AppleBuildProvenance.ResolveLocalSourceRevision(root.FullName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveLocalSourceRevision_rejects_dirty_submodules_even_when_configured_to_ignore_all()
    {
        var submodule = CreateRepository();
        var root = CreateRepository();
        try
        {
            RunGit(
                root.FullName,
                "-c",
                "protocol.file.allow=always",
                "submodule",
                "add",
                submodule.FullName,
                "Dependencies/Sample");
            var modulesPath = Path.Combine(root.FullName, ".gitmodules");
            File.AppendAllText(modulesPath, "\tignore = all\n");
            RunGit(root.FullName, "add", ".gitmodules", "Dependencies/Sample");
            RunGit(root.FullName, "commit", "-m", "add ignored submodule");

            File.WriteAllText(
                Path.Combine(root.FullName, "Dependencies", "Sample", "tracked.txt"),
                "dirty submodule content");

            Assert.Null(AppleBuildProvenance.ResolveLocalSourceRevision(root.FullName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { submodule.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void IsGitMetadataMutation_ignores_only_renames_with_both_endpoints_inside_git()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.AppleBuildProvenance",
            Guid.NewGuid().ToString("N"));

        Assert.True(AppleBuildProvenance.IsGitMetadataMutation(
            new RenamedEventArgs(
                WatcherChangeTypes.Renamed,
                root,
                ".git/new.lock",
                ".git/old.lock"),
            root,
            StringComparison.Ordinal));
        Assert.False(AppleBuildProvenance.IsGitMetadataMutation(
            new RenamedEventArgs(
                WatcherChangeTypes.Renamed,
                root,
                "Sources/input.swift",
                ".git/input.swift"),
            root,
            StringComparison.Ordinal));
        Assert.False(AppleBuildProvenance.IsGitMetadataMutation(
            new RenamedEventArgs(
                WatcherChangeTypes.Renamed,
                root,
                ".git/input.swift",
                "Sources/input.swift"),
            root,
            StringComparison.Ordinal));
        Assert.True(AppleBuildProvenance.IsGitMetadataMutation(
            new FileSystemEventArgs(
                WatcherChangeTypes.Changed,
                root,
                ".git/index"),
            root,
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("POWERFORGE_SOURCE_REVISION=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("POWERFORGE_SOURCE_REVISION[sdk=iphoneos*]=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(" powerforge_source_revision [config=Release] = aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ")]
    public void AppendXcodeBuildSetting_rejects_all_owned_setting_variants(
        string argument)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AppleBuildProvenance.AppendXcodeBuildSetting(
                [argument],
                new string('b', 40)));

        Assert.Contains("owned by PowerForge", exception.Message);
    }

    [Fact]
    public void RejectIgnoredBuildInputs_rejects_inputs_copied_to_the_build()
    {
        var root = CreateRepository();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "local.xcconfig\n.build/\n");
            RunGit(root.FullName, "add", ".gitignore");
            RunGit(root.FullName, "commit", "-m", "ignore local input");
            File.WriteAllText(Path.Combine(root.FullName, "local.xcconfig"), "SETTING = local");
            Directory.CreateDirectory(Path.Combine(root.FullName, ".build"));
            File.WriteAllText(Path.Combine(root.FullName, ".build", "cache"), "generated");

            var exception = Assert.Throws<InvalidOperationException>(
                () => AppleBuildProvenance.RejectIgnoredBuildInputs(
                    root.FullName,
                    excludesGeneratedDirectories: true));

            Assert.Contains("local.xcconfig", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(".build/cache", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RejectIgnoredBuildInputs_rejects_generated_directories_for_live_builds()
    {
        var root = CreateRepository();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), ".build/\n");
            RunGit(root.FullName, "add", ".gitignore");
            RunGit(root.FullName, "commit", "-m", "ignore generated input");
            Directory.CreateDirectory(Path.Combine(root.FullName, ".build"));
            File.WriteAllText(Path.Combine(root.FullName, ".build", "generated.xcconfig"), "SETTING = local");

            var exception = Assert.Throws<InvalidOperationException>(
                () => AppleBuildProvenance.RejectIgnoredBuildInputs(
                    root.FullName,
                    excludesGeneratedDirectories: false));

            Assert.Contains(".build/generated.xcconfig", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RejectIgnoredBuildInputs_rejects_nested_generated_named_directories_for_mirrors()
    {
        var root = CreateRepository();
        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, ".gitignore"),
                "Sources/build/\n");
            RunGit(root.FullName, "add", ".gitignore");
            RunGit(root.FullName, "commit", "-m", "ignore nested input");
            var nested = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "Sources",
                "build"));
            File.WriteAllText(
                Path.Combine(nested.FullName, "schema.json"),
                "{}");

            var exception = Assert.Throws<InvalidOperationException>(
                () => AppleBuildProvenance.RejectIgnoredBuildInputs(
                    root.FullName,
                    excludesGeneratedDirectories: true));

            Assert.Contains(
                "Sources/build/schema.json",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static DirectoryInfo CreateRepository()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.AppleBuildProvenance",
            Guid.NewGuid().ToString("N")));
        RunGit(root.FullName, "init");
        RunGit(root.FullName, "config", "user.name", "PowerForge Tests");
        RunGit(root.FullName, "config", "user.email", "powerforge-tests@example.invalid");
        File.WriteAllText(Path.Combine(root.FullName, "tracked.txt"), Guid.NewGuid().ToString("N"));
        RunGit(root.FullName, "add", "tracked.txt");
        RunGit(root.FullName, "commit", "-m", "fixture");
        return root;
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error);
        return output;
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        internal StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_execute(request));
    }
}
