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
