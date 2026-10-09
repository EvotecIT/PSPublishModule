namespace PowerForge.Tests;

public sealed partial class DotNetNuGetClientTests
{
    [Theory]
    [InlineData("This package ID has been reserved. Please request access to upload to this reserved namespace.")]
    [InlineData("Conflict")]
    public async Task PushPackageAsync_DoesNotSkipRejectedUploads(string rejection)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-push-conflict-" + Guid.NewGuid().ToString("N")));
        var package = Path.Combine(root.FullName, "Sample.1.0.0.nupkg");
        var failure = $"error: Response status code does not indicate success: 409 ({rejection}).";
        var runner = new StubProcessRunner(request =>
        {
            // The CLI masks every HTTP 409 when --skip-duplicate is present, including namespace denials.
            var masked = File.ReadAllLines(request.Arguments.Single()[1..]).Contains("--skip-duplicate");
            return new ProcessRunResult(masked ? 0 : 1,
                masked ? $"Package '{package}' already exists at feed 'https://example.test'." : $"Pushing {package}...",
                masked ? string.Empty : failure, request.FileName, TimeSpan.Zero, timedOut: false);
        });
        try
        {
            var result = await new DotNetNuGetClient(runner, runtimeDirectoryRoot: root.FullName)
                .PushPackageAsync(new DotNetNuGetPushRequest(package, "test-key", "https://example.test/v3/index.json", true));
            Assert.False(result.Succeeded);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(rejection, result.StdErr, StringComparison.Ordinal);
        }
        finally { root.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task PushPackageAsync_AcceptsConfirmedDuplicatesOnlyWhenRequested(bool skipDuplicate, bool expectedSuccess)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-push-duplicate-" + Guid.NewGuid().ToString("N")));
        var runner = new StubProcessRunner(request => new ProcessRunResult(1, "Pushing Sample.1.0.0.nupkg...",
            "error: Response status code does not indicate success: 409 (A package with this ID and version already exists and cannot be modified).",
            request.FileName, TimeSpan.Zero, timedOut: false));
        try
        {
            var result = await new DotNetNuGetClient(runner, runtimeDirectoryRoot: root.FullName)
                .PushPackageAsync(new DotNetNuGetPushRequest(Path.Combine(root.FullName, "Sample.1.0.0.nupkg"), "test-key", "https://example.test", skipDuplicate));
            Assert.Equal(expectedSuccess, result.Succeeded);
            Assert.Equal(1, result.ExitCode);
            Assert.Equal(expectedSuccess, result.ErrorMessage is null);
        }
        finally { root.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(0, "Your package was pushed.", "", true)]
    [InlineData(1, "", "error: 409 (Package already exists and cannot be modified).", true)]
    [InlineData(1, "", "error: 409 (This package ID has been reserved).", false)]
    public async Task PushPackageAsync_PublishesCompanionAfterConfirmedPrimaryDuplicate(int symbolExitCode, string symbolOut, string symbolError, bool expectedSuccess)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-push-symbol-" + Guid.NewGuid().ToString("N")));
        var package = Path.Combine(root.FullName, "Sample.1.0.0.nupkg");
        var symbols = Path.ChangeExtension(package, ".snupkg");
        File.WriteAllText(package, "primary");
        File.WriteAllText(symbols, "symbols");
        var attempts = new List<string>();
        var runner = new StubProcessRunner(request =>
        {
            var lines = File.ReadAllLines(request.Arguments.Single()[1..]);
            attempts.Add(lines[2]);
            if (attempts.Count == 1)
                return new ProcessRunResult(1, $"Pushing {package}...", "error: 409 (Package already exists and cannot be modified).", request.FileName, TimeSpan.Zero, false);
            Assert.Contains("--no-symbols", lines);
            return new ProcessRunResult(symbolExitCode, $"Pushing {symbols}...\n{symbolOut}", symbolError, request.FileName, TimeSpan.Zero, false);
        });
        try
        {
            var result = await new DotNetNuGetClient(runner, runtimeDirectoryRoot: root.FullName)
                .PushPackageAsync(new DotNetNuGetPushRequest(package, "test-key", "https://example.test", true));
            Assert.Equal(new[] { package, symbols }, attempts);
            Assert.Equal(expectedSuccess, result.Succeeded);
            var aggregate = DotNetRepositoryReleaseService.ClassifyNuGetPushOutcome(result.ExitCode, true, result.StdErr, result.StdOut);
            Assert.Equal(expectedSuccess, aggregate.Outcome != DotNetRepositoryReleaseService.PackagePushOutcome.Failed);
            var outcomes = DotNetRepositoryReleaseService.ClassifyPublishedArtifacts(new[] { package, symbols }, aggregate, true);
            Assert.Equal(DotNetRepositoryReleaseService.PackagePushOutcome.SkippedDuplicate, outcomes[package]);
            Assert.Equal(expectedSuccess ? (symbolExitCode == 0 ? DotNetRepositoryReleaseService.PackagePushOutcome.Published : DotNetRepositoryReleaseService.PackagePushOutcome.SkippedDuplicate) : DotNetRepositoryReleaseService.PackagePushOutcome.Failed, outcomes[symbols]);
        }
        finally { root.Delete(recursive: true); }
    }
}
