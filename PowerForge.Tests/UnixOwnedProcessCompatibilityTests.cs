namespace PowerForge.Tests;

public sealed class UnixOwnedProcessCompatibilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge Unix ' quoted " + Guid.NewGuid().ToString("N"));
    public UnixOwnedProcessCompatibilityTests() => Directory.CreateDirectory(_root);

    [ReleaseValidationLinuxFact]
    public async Task Owned_launcher_preserves_structured_arguments_directory_and_environment()
    {
        string[] values = ["", "two words", "single'quote", "double\"quote", "$(printf injected); * & |", "zażółć😀漢字"];
        string[] arguments = ["-c", "printf '%s\\0' \"$PWD\" \"$POWERFORGE_ARG_TEST\" \"$@\"", "probe", .. values];
        const string environmentValue = "literal $HOME 'quoted' 漢字";
        var expected = string.Join('\0', new[] { _root, environmentValue }.Concat(values)) + '\0';
        foreach (var mode in new[] { "managed", "owned", "portable-fallback" }) {
            var command = mode == "portable-fallback"
                ? UnixOwnedProcessExecution.CreateWorkingDirectoryFallback(new[] { "/bin/sh" }.Concat(arguments), _root).ToArray()
                : new[] { "/bin/sh" }.Concat(arguments).ToArray();
            var result = await new ProcessRunner(ownProcessTree: mode != "managed").RunAsync(new(command[0], _root, command.Skip(1).ToArray(),
                TimeSpan.FromSeconds(10), new Dictionary<string, string?> { ["POWERFORGE_ARG_TEST"] = environmentValue },
                captureOutput: true, captureError: true, inheritEnvironment: false)).WaitAsync(TimeSpan.FromSeconds(15));

            Assert.True(result.Succeeded, result.StdErr);
            Assert.Equal(expected, result.StdOut);
            Assert.Equal(string.Empty, result.StdErr);
        }
    }

    [ReleaseValidationLinuxFact]
    public async Task Missing_working_directory_fails_without_running_the_probe()
    {
        var missing = Path.Combine(_root, "missing ' directory");
        foreach (var fallback in new[] { false, true }) {
            string[] command = ["/bin/sh", "-c", "printf probe-must-not-run"];
            if (fallback) command = UnixOwnedProcessExecution.CreateWorkingDirectoryFallback(command, missing).ToArray();
            var result = await new ProcessRunner(ownProcessTree: true).RunAsync(new(command[0], fallback ? _root : missing,
                command.Skip(1).ToArray(), TimeSpan.FromSeconds(10))).WaitAsync(TimeSpan.FromSeconds(15));

            Assert.False(result.Succeeded);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal(string.Empty, result.StdOut);
            Assert.False(result.TimedOut);
        }
    }

    [ReleaseValidationLinuxFact]
    public async Task Portable_fallback_exec_preserves_the_owned_group_leader_identity()
    {
        var command = UnixOwnedProcessExecution.CreateWorkingDirectoryFallback(
            ["/bin/sh", "-c", "printf '%s' \"$$\""], _root).ToArray();
        var request = new ProcessRunRequest(command[0], _root, command.Skip(1).ToArray(), TimeSpan.FromSeconds(10));
        var startedId = 0;
        request.SetStartedProcessBoundary(id => startedId = id);
        var result = await new ProcessRunner(ownProcessTree: true).RunAsync(request);

        Assert.True(result.Succeeded, result.StdErr);
        Assert.True(startedId > 0);
        Assert.Equal(startedId.ToString(System.Globalization.CultureInfo.InvariantCulture), result.StdOut);
    }

    [ReleaseValidationLinuxFact]
    public async Task Portable_fallback_ignores_cdpath_for_a_relative_working_directory()
    {
        var name = "PowerForge.Relative." + Guid.NewGuid().ToString("N");
        var expected = Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, name)).FullName;
        var alternate = Directory.CreateDirectory(Path.Combine(_root, "alternate", name)).FullName;
        try {
            var command = UnixOwnedProcessExecution.CreateWorkingDirectoryFallback(
                ["/bin/sh", "-c", "printf '%s' \"$PWD\""], name).ToArray();
            var request = new ProcessRunRequest(command[0], Environment.CurrentDirectory, command.Skip(1).ToArray(),
                TimeSpan.FromSeconds(10), new Dictionary<string, string?> {
                    ["CDPATH"] = Path.GetDirectoryName(alternate), ["OLDPWD"] = alternate
                });
            var result = await new ProcessRunner(ownProcessTree: true).RunAsync(request);

            Assert.True(result.Succeeded, result.StdErr);
            Assert.Equal(expected, result.StdOut);
            Assert.Empty(result.StdErr);
        }
        finally { Directory.Delete(expected); }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [ReleaseValidationLinuxFact]
    public async Task Portable_fallback_preserves_symlink_parent_traversal()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(target, "child")).FullName;
        var link = Path.Combine(source, "link");
        Directory.CreateSymbolicLink(link, child);
        var workingDirectory = link + "/..";
        var command = UnixOwnedProcessExecution.CreateWorkingDirectoryFallback(
            ["/bin/sh", "-c", "printf '%s' \"$PWD\""], workingDirectory).ToArray();

        var result = await new ProcessRunner(ownProcessTree: true).RunAsync(
            new(command[0], _root, command.Skip(1).ToArray(), TimeSpan.FromSeconds(10)));

        Assert.True(result.Succeeded, result.StdErr);
        Assert.Equal(target, result.StdOut);
        Assert.Empty(result.StdErr);
    }
}
