namespace PowerForge.Tests;

public sealed class AppleReleaseSourceTrustPerformanceTests
{
    [Fact]
    public void EnumerateTreeWithoutLinks_rejects_a_link_before_descending_into_it()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "PowerForge.LinkAwareTreeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ordinaryDirectory = Directory.CreateDirectory(Path.Combine(root, "Sources"));
            File.WriteAllText(Path.Combine(ordinaryDirectory.FullName, "Source.swift"), "struct Source { }");
            Directory.CreateSymbolicLink(Path.Combine(ordinaryDirectory.FullName, "Recursive"), root);

            var error = Assert.Throws<InvalidOperationException>(() =>
                AppleReleaseSourceTrustService.EnumerateTreeWithoutLinks(root, "Xcode source input"));

            Assert.Contains("symbolic link", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnsureNoCustomGitFilters_batches_large_trees_without_weakening_filter_rejection()
    {
        var runner = new GitAttributeRunner();
        var gitClient = new GitClient(runner);
        var service = new AppleReleaseSourceTrustService(
            new HomeAssistantReleaseGitService(gitClient),
            gitClient);
        var paths = Enumerable.Range(0, 600).Select(index => $"Sources/File{index:D4}.swift").ToArray();

        service.EnsureNoCustomGitFilters("/trusted/repository", paths, "Swift package source input");

        Assert.Equal(3, runner.Requests.Count);
        Assert.All(runner.Requests, request => Assert.True(
            request.Arguments.Count(argument => argument.EndsWith(".swift", StringComparison.Ordinal)) <= 256));

        runner.FilteredPath = paths[511];
        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.EnsureNoCustomGitFilters("/trusted/repository", paths, "Swift package source input"));
        Assert.Contains(paths[511], exception.Message, StringComparison.Ordinal);
        Assert.Contains("custom Git filter", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureTrackedFile_reuses_Git_proof_but_rehashes_bytes_on_every_reference()
    {
        var temporaryRoot = Path.GetTempPath();
        if (OperatingSystem.IsMacOS() && temporaryRoot.StartsWith("/var/", StringComparison.Ordinal))
            temporaryRoot = "/private" + temporaryRoot;
        var root = Path.Combine(temporaryRoot, "PowerForge.TrackedFileCacheTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var header = Path.Combine(root, "Common.h");
            File.WriteAllText(header, "#define COMMON_VALUE 1\n");
            RunGit(root, "init", "--quiet");
            RunGit(root, "add", "Common.h");
            RunGit(
                root,
                "-c", "user.name=PowerForge Tests",
                "-c", "user.email=powerforge-tests@example.invalid",
                "commit", "--quiet", "-m", "fixture");

            var runner = new CountingProcessRunner();
            var git = new GitClient(runner, defaultTimeout: TimeSpan.FromSeconds(30));
            var service = new AppleReleaseSourceTrustService(
                new HomeAssistantReleaseGitService(git, runner),
                git);

            service.EnsureTrackedFile(root, header, "shared package header");
            var initialGitCalls = runner.Requests.Count;
            for (var index = 0; index < 500; index++)
                service.EnsureTrackedFile(root, header, "shared package header");

            Assert.Equal(initialGitCalls, runner.Requests.Count);
            File.WriteAllText(header, "#define COMMON_VALUE 2\n");
            var exception = Assert.Throws<InvalidOperationException>(() =>
                service.EnsureTrackedFile(root, header, "shared package header"));
            Assert.Contains("changed after it was validated", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(initialGitCalls, runner.Requests.Count);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void RunGit(string root, params string[] arguments)
    {
        var result = new ProcessRunner().RunAsync(new ProcessRunRequest(
                "git",
                root,
                arguments,
                TimeSpan.FromSeconds(30)))
            .GetAwaiter()
            .GetResult();
        if (!result.Succeeded)
            throw new InvalidOperationException(result.StdErr);
    }

    private sealed class CountingProcessRunner : IProcessRunner
    {
        private readonly ProcessRunner _inner = new();

        internal List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return _inner.RunAsync(request, cancellationToken);
        }
    }

    private sealed class GitAttributeRunner : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = new();

        internal string? FilteredPath { get; set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var separator = request.Arguments.ToList().IndexOf("--");
            var paths = request.Arguments.Skip(separator + 1).ToArray();
            var output = string.Concat(paths.Select(path =>
                path + "\0filter\0" +
                (path.Equals(FilteredPath, StringComparison.Ordinal) ? "malicious" : "unspecified") +
                "\0"));
            return Task.FromResult(new ProcessRunResult(
                0,
                output,
                string.Empty,
                "git",
                TimeSpan.Zero,
                false));
        }
    }
}
