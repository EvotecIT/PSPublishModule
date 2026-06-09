using PowerForge;

namespace PowerForge.Tests;

public sealed class DotNetNuGetClientTests
{
    [Fact]
    public async Task PushPackageAsync_UsesResponseFileAndCleansItUp()
    {
        ProcessRunRequest? captured = null;
        string? responseFilePath = null;
        string? responseFileContent = null;
        var processRunner = new StubProcessRunner(request => {
            captured = request;
            responseFilePath = request.Arguments.Single()[1..];
            responseFileContent = File.ReadAllText(responseFilePath);
            return new ProcessRunResult(0, "ok", string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });
        var runtimeDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var client = new DotNetNuGetClient(processRunner, runtimeDirectoryRoot: runtimeDirectory);

        try
        {
            var result = await client.PushPackageAsync(new DotNetNuGetPushRequest(
                packagePath: @"C:\repo\Artifacts\Test.1.0.0.nupkg",
                apiKey: "secret",
                source: "https://api.nuget.org/v3/index.json"));

            Assert.NotNull(captured);
            Assert.Equal("dotnet", captured!.FileName);
            Assert.Single(captured.Arguments);
            Assert.StartsWith("@", captured.Arguments[0], StringComparison.Ordinal);
            Assert.NotNull(responseFileContent);
            Assert.Contains("nuget", responseFileContent!, StringComparison.Ordinal);
            Assert.Contains("push", responseFileContent!, StringComparison.Ordinal);
            Assert.Contains("--skip-duplicate", responseFileContent!, StringComparison.Ordinal);
            Assert.True(result.Succeeded);
            Assert.NotNull(responseFilePath);
            Assert.False(File.Exists(responseFilePath!));
        }
        finally
        {
            try { Directory.Delete(runtimeDirectory, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SignPackageAsync_BuildsStructuredArguments()
    {
        ProcessRunRequest? captured = null;
        var processRunner = new StubProcessRunner(request => {
            captured = request;
            return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });
        var client = new DotNetNuGetClient(processRunner);

        var result = await client.SignPackageAsync(new DotNetNuGetSignRequest(
            packagePath: @"C:\repo\Artifacts\Test.1.0.0.nupkg",
            certificateFingerprint: "ABC123",
            certificateStoreLocation: "CurrentUser",
            timeStampServer: "http://timestamp.digicert.com"));

        Assert.NotNull(captured);
        Assert.Equal(
            [
                "nuget",
                "sign",
                @"C:\repo\Artifacts\Test.1.0.0.nupkg",
                "--certificate-fingerprint",
                "ABC123",
                "--certificate-store-location",
                "CurrentUser",
                "--certificate-store-name",
                "My",
                "--timestamper",
                "http://timestamp.digicert.com",
                "--overwrite"
            ],
            captured!.Arguments);
        Assert.True(result.Succeeded);
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_execute(request));
    }
}
