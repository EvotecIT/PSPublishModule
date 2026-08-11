namespace PowerForge.Tests;

public sealed class AppleRemotePackageMirrorLeaseTests
{
    [Fact]
    public async Task AcquireRemotePackageMirrorLease_SerializesConcurrentUsersOfTheSameMirror()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.RemotePackageMirrorLeaseTests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var mirrorPath = Path.Combine(root.FullName, "package.git");
            var firstLease = AppleReleaseSourceTrustService.AcquireRemotePackageMirrorLease(mirrorPath);
            var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondLease = Task.Run(() =>
            {
                secondStarted.SetResult(true);
                using var lease = AppleReleaseSourceTrustService.AcquireRemotePackageMirrorLease(mirrorPath);
            });

            await secondStarted.Task;
            await Task.Delay(150);
            Assert.False(secondLease.IsCompleted);

            firstLease.Dispose();
            await secondLease.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void EnsureRemotePackageMirror_AcceptsRevisionMaterializedByConcurrentFetcher()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.RemotePackageMirrorLeaseTests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var revisionAvailable = false;
            var fetchCalls = 0;
            var runner = new StubProcessRunner(request =>
            {
                if (request.Arguments.Contains("fetch"))
                {
                    fetchCalls++;
                    revisionAvailable = true;
                    return Failure(request, "simulated competing fetch lock");
                }
                if (request.Arguments.Contains("cat-file"))
                    return revisionAvailable ? Success(request) : Failure(request, "missing revision");
                if (request.Arguments.Contains("--show-object-format"))
                    return Success(request, "sha1\n");
                return Success(request);
            });
            var git = new GitClient(runner, "/usr/bin/git", TimeSpan.FromSeconds(30));
            var service = new AppleReleaseSourceTrustService(gitClient: git);
            var mirrorPath = Path.Combine(root.FullName, "package.git");

            service.EnsureRemotePackageMirror(
                mirrorPath,
                "https://example.invalid/package.git",
                new string('a', 40));

            Assert.Equal(1, fetchCalls);
            Assert.True(Directory.Exists(mirrorPath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static ProcessRunResult Success(ProcessRunRequest request)
        => new(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);

    private static ProcessRunResult Success(ProcessRunRequest request, string output)
        => new(0, output, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);

    private static ProcessRunResult Failure(ProcessRunRequest request, string error)
        => new(1, string.Empty, error, request.FileName, TimeSpan.Zero, timedOut: false);

    private sealed class StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(execute(request));
    }
}
