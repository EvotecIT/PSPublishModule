using System.Runtime.InteropServices;

namespace PowerForge.Tests;

public sealed class AppleSwiftPackageBuildSnapshotTests
{
    [Theory]
    [InlineData("root")]
    [InlineData("broken-root")]
    [InlineData("chained-escape")]
    [InlineData("special-file")]
    [InlineData("file-leave-and-return")]
    [InlineData("directory-leave-and-return")]
    [InlineData("cyclic-link")]
    public async Task Materialization_rejects_artifact_links_that_leave_binary_bytes_unbound(string scenario)
    {
        if (OperatingSystem.IsWindows()) return;
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N")));
        try
        {
            var runner = new ArtifactRunner(source =>
            {
                var artifacts = Path.Combine(source, "artifacts");
                var external = Path.Combine(fixture.FullName, "external");
                if (scenario is "file-leave-and-return" or "directory-leave-and-return")
                {
                    var real = Directory.CreateDirectory(Path.Combine(artifacts, "real"));
                    File.WriteAllText(Path.Combine(real.FullName, "Engine"), "approved binary");
                    Directory.CreateDirectory(external);
                    var jump = Path.Combine(external, "jump");
                    if (scenario == "file-leave-and-return")
                    {
                        File.CreateSymbolicLink(jump, Path.GetRelativePath(external, Path.Combine(real.FullName, "Engine")));
                        File.CreateSymbolicLink(Path.Combine(artifacts, "libEngine.a"), Path.GetRelativePath(artifacts, jump));
                    }
                    else
                    {
                        Directory.CreateSymbolicLink(jump, Path.GetRelativePath(external, real.FullName));
                        Directory.CreateSymbolicLink(Path.Combine(artifacts, "Alias"), Path.GetRelativePath(artifacts, jump));
                    }
                    return;
                }
                if (scenario == "cyclic-link")
                {
                    Directory.CreateDirectory(artifacts);
                    File.CreateSymbolicLink(Path.Combine(artifacts, "first"), "second");
                    File.CreateSymbolicLink(Path.Combine(artifacts, "second"), "first");
                    return;
                }
                if (scenario == "special-file")
                {
                    Directory.CreateDirectory(artifacts);
                    Assert.Equal(0, MakeFifo(Path.Combine(artifacts, "libEngine.a"), 0x180));
                    return;
                }
                if (scenario is "root" or "broken-root")
                {
                    if (scenario == "root")
                    {
                        Directory.CreateDirectory(external);
                        File.WriteAllText(Path.Combine(external, "libEngine.a"), "unbound binary");
                    }
                    Directory.CreateSymbolicLink(artifacts, external);
                    return;
                }
                Directory.CreateDirectory(Path.Combine(artifacts, "deep"));
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(source)!, "external.a"), "unbound binary");
                File.WriteAllText(Path.Combine(artifacts, "external.a"), "contained decoy");
                Directory.CreateSymbolicLink(Path.Combine(artifacts, "deep", "A"), "..");
                // Kernel resolution follows A first, then '..'. GetFullPath would select the decoy.
                File.CreateSymbolicLink(Path.Combine(artifacts, "libEngine.a"), "deep/A/../../external.a");
            });
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSnapshot(fixture.FullName, runner));
            if (scenario is "chained-escape" or "file-leave-and-return" or "directory-leave-and-return")
                Assert.Contains("escapes", exception.Message, StringComparison.OrdinalIgnoreCase);
            else if (scenario == "cyclic-link") Assert.Contains("link", exception.Message, StringComparison.OrdinalIgnoreCase);
            else if (scenario == "special-file") Assert.Contains("regular file", exception.Message, StringComparison.OrdinalIgnoreCase);
            else Assert.Contains("artifact", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public async Task Materialization_keeps_internal_framework_links_and_binds_their_real_target_bytes()
    {
        if (OperatingSystem.IsWindows()) return;
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N")));
        try
        {
            var runner = new ArtifactRunner(source =>
            {
                var framework = Directory.CreateDirectory(Path.Combine(source, "artifacts", "Engine.framework", "Versions", "A"));
                File.WriteAllText(Path.Combine(framework.FullName, "Engine"), "approved binary");
                Directory.CreateSymbolicLink(Path.Combine(framework.Parent!.FullName, "Current"), "A");
                File.CreateSymbolicLink(Path.Combine(framework.Parent.Parent!.FullName, "Engine"), "Versions/Current/Engine");
            });
            using var snapshot = await CreateSnapshot(fixture.FullName, runner);
            File.WriteAllText(Path.Combine(snapshot.SourcePackagesPath, "artifacts", "Engine.framework", "Engine"), "unapproved replacement");
            Assert.Throws<InvalidOperationException>(snapshot.ValidateUnchanged);
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("new-clone")]
    [InlineData("replacement-clone")]
    [InlineData("clone-and-write-restore")]
    public async Task Native_clone_observation_rejects_novel_or_changed_binary_inputs(string scenario)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N")));
        try
        {
            var runner = new ArtifactRunner(source =>
            {
                Directory.CreateDirectory(Path.Combine(source, "artifacts"));
                File.WriteAllText(Path.Combine(source, "artifacts", "libEngine.a"), "approved binary");
            });
            using var snapshot = await CreateSnapshot(fixture.FullName, runner);
            var source = Path.Combine(snapshot.SourcePackagesPath, "artifacts", "libEngine.a");
            var destination = scenario == "new-clone"
                ? Path.Combine(snapshot.SourcePackagesPath, "artifacts", "new.a")
                : Path.Combine(fixture.FullName, "clone.a");
            Assert.Equal(0, CloneFile(source, destination, 0));
            if (scenario == "replacement-clone")
            {
                File.Delete(source);
                File.Move(destination, source);
            }
            else if (scenario == "clone-and-write-restore")
            {
                File.WriteAllText(source, "unapproved binary");
                File.WriteAllText(source, "approved binary");
            }
            Assert.Throws<InvalidOperationException>(snapshot.ValidateUnchanged);
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(0x440000u, true)]
    [InlineData(0x440100u, true)]
    [InlineData(0x410000u, false)] // File-kind event cannot authorize a symbolic link.
    [InlineData(0x448100u, false)] // Clone plus owner change is not a read.
    [InlineData(0x441000u, false)] // Clone plus content modification.
    [InlineData(0x440800u, false)] // Clone plus rename.
    public async Task Native_framework_link_clone_flags_require_unchanged_bound_input(uint flags, bool accepted)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N")));
        try
        {
            using var snapshot = await CreateSnapshot(fixture.FullName, new ArtifactRunner(MaterializeFramework));
            var source = Path.Combine(snapshot.SourcePackagesPath, "artifacts", "Engine.framework", "Engine");
            ReportNativeChange(snapshot, source, flags);
            if (accepted) snapshot.ValidateUnchanged();
            else Assert.Throws<InvalidOperationException>(snapshot.ValidateUnchanged);
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("replace-link")]
    [InlineData("retarget-and-restore")]
    [InlineData("new-link")]
    public async Task Clone_flags_cannot_authorize_new_or_replaced_framework_links(string scenario)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N")));
        try
        {
            using var snapshot = await CreateSnapshot(fixture.FullName, new ArtifactRunner(MaterializeFramework));
            var framework = Path.Combine(snapshot.SourcePackagesPath, "artifacts", "Engine.framework");
            var source = Path.Combine(framework, "Engine");
            if (scenario == "new-link")
            {
                source = Path.Combine(framework, "NewEngine");
                File.CreateSymbolicLink(source, "Versions/Current/Engine");
            }
            else if (scenario == "replace-link")
            {
                File.Delete(source);
                File.CreateSymbolicLink(source, "Versions/Current/Engine");
            }
            else
            {
                var current = Path.Combine(framework, "Versions", "Current");
                File.Delete(current);
                Directory.CreateSymbolicLink(current, "..");
                File.Delete(current);
                Directory.CreateSymbolicLink(current, "A");
            }
            ReportNativeChange(snapshot, source, 0x440000);
            Assert.Throws<InvalidOperationException>(snapshot.ValidateUnchanged);
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static void MaterializeFramework(string source)
    {
        var version = Directory.CreateDirectory(Path.Combine(source, "artifacts", "Engine.framework", "Versions", "A"));
        File.WriteAllText(Path.Combine(version.FullName, "Engine"), "approved binary");
        Directory.CreateSymbolicLink(Path.Combine(version.Parent!.FullName, "Current"), "A");
        File.CreateSymbolicLink(Path.Combine(version.Parent.Parent!.FullName, "Engine"), "Versions/Current/Engine");
    }

    [Fact]
    public async Task Snapshot_roots_allow_concurrent_package_materialization_without_false_mutations()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N")));
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 4).Select(index => Task.Run(async () =>
            {
                var project = Directory.CreateDirectory(Path.Combine(fixture.FullName, index.ToString()));
                using var snapshot = await CreateSnapshot(project.FullName, new ArtifactRunner(MaterializeFramework));
                // Derived output is outside the immutable package input. Sibling snapshots may
                // be created concurrently under the shared parent without changing this root.
                Directory.CreateDirectory(snapshot.ArchiveDerivedDataPath);
                File.WriteAllText(Path.Combine(snapshot.ArchiveDerivedDataPath, "build.log"), "build output");
                snapshot.ValidateUnchanged();
            })));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("rename-restore")]
    [InlineData("permissions-restore")]
    public async Task Snapshot_root_rejects_transient_mutations_even_when_identity_is_restored(string mutation)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N")));
        try
        {
            using var snapshot = await CreateSnapshot(fixture.FullName, new ArtifactRunner(MaterializeFramework));
            if (mutation == "rename-restore")
            {
                var moved = snapshot.RootPath + "-moved";
                Directory.Move(snapshot.RootPath, moved);
                Directory.Move(moved, snapshot.RootPath);
            }
            else
            {
                var original = File.GetUnixFileMode(snapshot.RootPath);
                File.SetUnixFileMode(snapshot.RootPath, original | UnixFileMode.GroupRead);
                File.SetUnixFileMode(snapshot.RootPath, original);
            }
            Assert.Throws<InvalidOperationException>(snapshot.ValidateUnchanged);
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static void ReportNativeChange(AppleSwiftPackageBuildSnapshot snapshot, string path, uint flags)
    {
        // Replay the exact native event recorded from a real, unchanged framework link.
        // The existing OS callback boundary is used; no production-only test hook is added.
        const System.Reflection.BindingFlags access = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var monitor = typeof(AppleSwiftPackageBuildSnapshot).GetField("_monitor", access)!.GetValue(snapshot)!;
        var observer = typeof(AppleReleaseSourceMutationMonitor).GetField("_observer", access)!.GetValue(monitor)!;
        var changed = (Action<string, uint>)observer.GetType().GetField("_changed", access)!.GetValue(observer)!;
        changed(path, flags);
    }

    private static Task<AppleSwiftPackageBuildSnapshot> CreateSnapshot(string fixture, IProcessRunner runner)
    {
        var project = Directory.CreateDirectory(Path.Combine(fixture, "App.xcodeproj"));
        return AppleSwiftPackageBuildSnapshot.CreateAsync(runner, "/usr/bin/xcodebuild", project.FullName,
            false, "App", new Dictionary<string, string>(), fixture, StringComparison.Ordinal,
            TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    private sealed class ArtifactRunner(Action<string> materialize) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            var index = request.Arguments.ToList().IndexOf("-clonedSourcePackagesDirPath");
            materialize(request.Arguments[index + 1]);
            return Task.FromResult(new ProcessRunResult(0, "resolved", string.Empty, request.FileName, TimeSpan.Zero, false));
        }
    }

    [DllImport("libc", EntryPoint = "clonefile", SetLastError = true)]
    private static extern int CloneFile(string source, string destination, uint flags);

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo(string path, uint mode);
}
