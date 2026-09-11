namespace PowerForge.Tests;

public sealed partial class ReleaseValidationClosureRegressionTests
{
    [Theory]
    [InlineData("{ToolPath}", null, true, true)]
    [InlineData("{toolpath}", "{WorkRoot}/", true, true)]
    [InlineData("{ToolPath}", "{ProjectRoot}", true, false)]
    [InlineData("{toolpath}", "{ProjectRoot}", true, false)]
    [InlineData("{ToolPath}", "{ProjectRoot}", false, true)]
    public async Task Tool_probe_dispatch_preserves_the_isolated_manifest_contract(
        string executable, string? workingDirectory, bool manifestInstall, bool expectedSuccess)
    {
        Package("Example.Tool.1.2.3.nupkg");
        var calls = new List<ProcessRunRequest>();
        var runner = new Runner((request, _) => {
            calls.Add(request);
            return Task.FromResult(Success());
        });
        var report = await new ReleaseValidationService(runner).RunAsync(new() {
            Tools = [new() {
                PackageRoot = _root, PackageId = "Example.Tool", CommandName = "example",
                IncludeManifestInstall = manifestInstall,
                Commands = [new() { FileName = executable, WorkingDirectory = workingDirectory, Arguments = ["--probe"] }]
            }]
        }, request: new() { ProjectRoot = _root });

        Assert.Equal(expectedSuccess, report.Success);
        var probes = calls.Where(request => request.Arguments.Contains("--probe")).ToArray();
        if (!expectedSuccess) {
            Assert.Single(probes);
            Assert.Contains("{WorkRoot}", Assert.Single(report.Errors));
            Assert.DoesNotContain(calls, request => request.Arguments.Take(2).SequenceEqual(new[] { "tool", "run" }));
        } else if (manifestInstall) {
            Assert.Equal(2, probes.Length);
            Assert.EndsWith("example" + (OperatingSystem.IsWindows() ? ".exe" : ""), probes[0].FileName);
            Assert.Equal("dotnet", probes[1].FileName);
            Assert.Equal(new[] { "tool", "run", "example", "--", "--probe" }, probes[1].Arguments);
            var manifestCreation = Assert.Single(calls, request => request.Arguments.SequenceEqual(new[] { "new", "tool-manifest" }));
            Assert.Equal(manifestCreation.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar),
                probes[1].WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar));
        } else {
            Assert.Equal(_root, Assert.Single(probes).WorkingDirectory);
        }
        Assert.All(calls, request => {
            var workspace = request.EnvironmentVariables!["DOTNET_CLI_HOME"]!;
            Assert.False(Directory.Exists(workspace));
        });
    }
}
