namespace PowerForge.Tests;

public sealed class ReleaseValidationWindowsPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.WindowsPath.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationWindowsPathTests() => Directory.CreateDirectory(_root);

    [WindowsFact]
    public async Task Child_path_selects_unique_and_parent_shadowing_executables()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_root, "child bin")).FullName;
        foreach (var name in new[] { "unique-probe", "dotnet" }) {
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), Path.Combine(bin, name + ".exe"));
            foreach (var path in new[] { bin, "child bin", "\"" + bin + "\"" }) {
                var result = await Run(name, path);
                Assert.True(result.Succeeded, result.StdErr);
                Assert.Equal("child-selected", result.StdOut.Trim());
            }
        }
    }

    [WindowsFact]
    public async Task Empty_path_searches_child_directory_and_absolute_execution_needs_no_path()
    {
        var executable = Path.Combine(_root, "probe.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), executable);
        foreach (var name in new[] { "probe", "probe.exe", executable }) {
            var result = await Run(name, "");
            Assert.True(result.Succeeded, result.StdErr);
            Assert.Equal("child-selected", result.StdOut.Trim());
        }
        var absolute = await Run(executable, null);
        Assert.True(absolute.Succeeded, absolute.StdErr);
        Assert.Equal("child-selected", absolute.StdOut.Trim());
    }

    [WindowsFact]
    public async Task Overridden_or_removed_path_does_not_fall_back_to_parent_or_system_search()
    {
        foreach (var path in new string?[] { null, _root }) {
            var result = await Run("cmd", path);
            Assert.False(result.Succeeded);
            Assert.DoesNotContain("child-selected", result.StdOut);
        }
    }

    private Task<ProcessRunResult> Run(string name, string? path)
        => new ProcessRunner(ownProcessTree: true).RunAsync(new(name, _root,
            ["/d", "/c", "echo child-selected"], TimeSpan.FromSeconds(10),
            new Dictionary<string, string?> { ["PATH"] = path }));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
