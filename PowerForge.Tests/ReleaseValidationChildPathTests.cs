namespace PowerForge.Tests;

public sealed class ReleaseValidationChildPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.ChildPath.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationChildPathTests() => Directory.CreateDirectory(_root);

    [ArtifactBoundaryUnixFact]
    public async Task Child_only_path_selects_the_requested_executable()
    {
        if (OperatingSystem.IsWindows()) { return; }
        foreach (var name in new[] { "powerforge-path-" + Guid.NewGuid().ToString("N"), "sh", "dotnet" }) {
            var script = Path.Combine(_root, name);
            File.WriteAllText(script, "#!/bin/sh\nprintf child-path-selected\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var report = await new ReleaseValidationService().RunAsync(new() { Commands = [new() {
                FileName = name, Environment = new() { ["PATH"] = _root }, ExpectedOutput = "child-path-selected", TimeoutSeconds = 10
            }] }, request: new() { ProjectRoot = _root }).WaitAsync(TimeSpan.FromSeconds(15));

            Assert.True(report.Success, string.Join("\n", report.Errors));
            Assert.Single(report.Checks);
        }
    }

    [ArtifactBoundaryUnixFact]
    public async Task Path_selected_script_symlink_keeps_the_same_invocation_path_as_absolute_execution()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var bin = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
        var script = Path.Combine(_root, "launcher.sh");
        File.WriteAllText(script, "#!/bin/sh\nprintf '%s' \"$0\"\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var alias = Path.Combine(bin, "selected-probe");
        File.CreateSymbolicLink(alias, script);
        foreach (var name in new[] { "selected-probe", alias }) {
            var report = await new ReleaseValidationService().RunAsync(new() { Commands = [new() {
                FileName = name, Environment = new() { ["PATH"] = bin }, ExpectedOutput = alias, TimeoutSeconds = 10
            }] }, request: new() { ProjectRoot = _root });
            Assert.True(report.Success, string.Join("\n", report.Errors));
        }
    }

    [ArtifactBoundaryUnixFact]
    public async Task Relative_path_and_working_directory_preserve_physical_parent_traversal()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(target, "child")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(source, "link"), child);
        var script = Path.Combine(target, "probe");
        File.WriteAllText(script, "#!/bin/sh\nprintf physical-selected\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var fromWorkingDirectory in new[] { false, true }) {
            var result = await new ProcessRunner(ownProcessTree: true).RunAsync(new(
                "probe", fromWorkingDirectory ? source + "/link/.." : source, [], TimeSpan.FromSeconds(10),
                new Dictionary<string, string?> { ["PATH"] = fromWorkingDirectory ? "" : "link/.." }));
            Assert.True(result.Succeeded, result.StdErr);
            Assert.Equal("physical-selected", result.StdOut);
        }
    }

    [ArtifactBoundaryUnixFact]
    public async Task Relative_and_empty_child_path_entries_use_the_child_working_directory()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var bin = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
        var script = Path.Combine(bin, "probe");
        File.WriteAllText(script, "#!/bin/sh\nprintf relative-selected\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var path in new[] { "bin", "" }) {
            var report = await new ReleaseValidationService().RunAsync(new() { Commands = [new() {
                FileName = "probe", WorkingDirectory = path.Length == 0 ? bin : _root,
                Environment = new() { ["PATH"] = path }, ExpectedOutput = "relative-selected", TimeoutSeconds = 10
            }] }, request: new() { ProjectRoot = _root }).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(report.Success, string.Join("\n", report.Errors));
        }
    }

    [ArtifactBoundaryUnixFact]
    public async Task Removed_child_path_does_not_reuse_parent_search_path()
    {
        var report = await new ReleaseValidationService().RunAsync(new() { Commands = [new() {
            FileName = "sh", Arguments = ["-c", "printf parent-path-leaked"], Environment = new() { ["PATH"] = null }, TimeoutSeconds = 10
        }] }, request: new() { ProjectRoot = _root }).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.False(report.Success);
        Assert.Single(report.Errors);
        Assert.DoesNotContain("parent-path-leaked", report.Errors[0]);
        Assert.Empty(report.Checks);
    }

    [ArtifactBoundaryUnixFact]
    public async Task Absolute_executable_still_runs_without_child_path()
    {
        var report = await new ReleaseValidationService().RunAsync(new() { Commands = [new() {
            FileName = "/bin/sh", Arguments = ["-c", "printf absolute-selected"], Environment = new() { ["PATH"] = null },
            ExpectedOutput = "absolute-selected", TimeoutSeconds = 10
        }] }, request: new() { ProjectRoot = _root }).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.True(report.Success, string.Join("\n", report.Errors));
        Assert.Single(report.Checks);
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
