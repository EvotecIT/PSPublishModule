using PowerForge;

namespace PowerForge.Net472SmokeTests;

public sealed class DotNetNuGetClientNet472SmokeTests
{
    [Fact]
    public async Task PushPackageAsync_UsesResponseFileAndCleansItUpUnderNet472()
    {
        var runtimeRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Net472SmokeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtimeRoot);

        var packagePath = Path.Combine(runtimeRoot, "sample.1.0.0.nupkg");
        File.WriteAllText(packagePath, "package");

        ProcessRunRequest? captured = null;
        var runner = new StubProcessRunner(request => {
            captured = request;
            return new ProcessRunResult(
                exitCode: 0,
                stdOut: "pushed",
                stdErr: string.Empty,
                executable: "dotnet",
                duration: TimeSpan.FromMilliseconds(10),
                timedOut: false);
        });
        var client = new DotNetNuGetClient(runner, runtimeDirectoryRoot: runtimeRoot);

        var result = await client.PushPackageAsync(new DotNetNuGetPushRequest(
            packagePath: packagePath,
            apiKey: "secret",
            source: "https://api.nuget.org/v3/index.json"));

        Assert.NotNull(captured);
        Assert.Equal("dotnet", captured!.FileName);
        Assert.Single(captured.Arguments);
        Assert.StartsWith("@", captured.Arguments[0], StringComparison.Ordinal);
        Assert.False(File.Exists(captured.Arguments[0].Substring(1)));
        Assert.True(result.Succeeded);
        Assert.Equal("dotnet", result.Executable);

        Directory.Delete(runtimeRoot, recursive: true);
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_execute(request));
        }
    }
}
