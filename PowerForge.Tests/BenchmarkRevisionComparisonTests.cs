using System.Diagnostics;
using System.Globalization;

namespace PowerForge.Tests;

public sealed class BenchmarkRevisionComparisonTests
{
    [Fact]
    public async Task Compare_RunsSidesAlternatelyFromATemporaryWorktreeAndRemovesIt()
    {
        using var repository = TestRepository.Create();
        File.WriteAllText(Path.Combine(repository.Root, "Bench", "NewBenchmark.cs"), "// untracked candidate file");
        var dotnet = new FakeDotNet(baselineNanoseconds: 1000, candidateNanoseconds: 1500);
        string lockPath = Path.Combine(repository.Scratch, "bench.lock");
        var service = new BenchmarkRevisionComparisonService(dotnet, new NullLogger(), _ => Task.FromResult<double?>(1.0), TimeSpan.FromMilliseconds(10));

        var result = await service.RunAsync(new BenchmarkRevisionComparisonSpec
        {
            RepositoryPath = repository.Root,
            Project = "Bench",
            Filter = "*Parse*",
            Framework = "net10.0",
            ScratchRoot = repository.Scratch,
            LockPath = lockPath,
            MaxLoadAverage = 6,
            BenchmarkArguments = new[] { "--iterationCount", "20" }
        });

        Assert.Equal("main", result.Baseline.Revision);
        Assert.Equal(repository.MainCommit, result.Baseline.Commit);
        Assert.Equal("working tree", result.Candidate.Revision);
        Assert.True(result.Candidate.Dirty);
        Assert.Equal(new[] { "1:baseline", "1:candidate", "2:candidate", "2:baseline" }, result.RunOrder);
        Assert.Equal(new[] { "baseline", "candidate", "candidate", "baseline" }, dotnet.RunSides);
        Assert.All(dotnet.RunArguments, arguments =>
        {
            Assert.Equal("20", arguments[arguments.IndexOf("--iterationCount") + 1]);
            Assert.Equal("5", arguments[arguments.IndexOf("--warmupCount") + 1]);
            Assert.Contains("json", arguments);
        });

        var row = Assert.Single(result.Benchmarks);
        Assert.Equal("Bench.ParseBenchmarks.Parse(Size: 10)", row.Benchmark);
        Assert.Equal(0.001, row.Baseline.MedianMs!.Value, 6);
        Assert.Equal(0.0015, row.Candidate.MedianMs!.Value, 6);
        Assert.Equal(1.5, row.Ratio!.Value, 6);
        Assert.Equal(1.5, row.RatioMin!.Value, 6);
        Assert.Equal(1.5, row.RatioMax!.Value, 6);
        Assert.Equal(2.0, row.AllocatedRatio!.Value, 6);
        Assert.Equal(2, row.Baseline.RoundMediansMs.Length);
        Assert.Equal("median", result.Runner.Statistic);
        Assert.Equal("alternating", result.Runner.Ordering);

        Assert.False(Directory.Exists(lockPath));
        Assert.Empty(Directory.GetFileSystemEntries(repository.Scratch));
        Assert.Single(repository.Git("worktree", "list", "--porcelain").Split('\n'), line => line.StartsWith("worktree ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)] // busy before the lock is taken
    [InlineData(1)] // quiet when the lock is taken, busy before the first run
    public async Task Compare_RemovesTheWorktreeAndReleasesTheLockWhenTheLoadNeverDrops(int quietReadings)
    {
        using var repository = TestRepository.Create();
        string lockPath = Path.Combine(repository.Scratch, "bench.lock");
        var dotnet = new FakeDotNet(1000, 1000);
        int readings = 0;
        bool lockHeldWhileWaiting = false;
        var service = new BenchmarkRevisionComparisonService(dotnet, new NullLogger(), _ =>
        {
            if (readings++ < quietReadings) return Task.FromResult<double?>(1.0);
            lockHeldWhileWaiting |= Directory.Exists(lockPath);
            return Task.FromResult<double?>(12.0);
        }, TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAsync<TimeoutException>(() => service.RunAsync(new BenchmarkRevisionComparisonSpec
        {
            RepositoryPath = repository.Root,
            Baseline = "main",
            Candidate = "main",
            Project = "Bench/Bench.csproj",
            Filter = "*",
            Framework = "net10.0",
            ScratchRoot = repository.Scratch,
            LockPath = lockPath,
            MaxLoadAverage = 6,
            LoadWaitTimeout = TimeSpan.FromMilliseconds(50)
        }));

        Assert.Empty(dotnet.RunSides);
        Assert.Equal(quietReadings > 0, lockHeldWhileWaiting);
        Assert.False(Directory.Exists(lockPath));
        Assert.Empty(Directory.GetFileSystemEntries(repository.Scratch));
        Assert.Single(repository.Git("worktree", "list", "--porcelain").Split('\n'), line => line.StartsWith("worktree ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lock_WaitsForTheHolderAndClearsOnlyStaleLocksItCanIdentify()
    {
        string root = Path.Combine(Path.GetTempPath(), "pf-bench-lock-" + Guid.NewGuid().ToString("N"));
        string lockPath = Path.Combine(root, "bench.lock");
        try
        {
            using (var held = await BenchmarkLockDirectory.AcquireAsync(lockPath, "first", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10), null, CancellationToken.None))
            {
                Assert.Contains("command=first", File.ReadAllText(Path.Combine(lockPath, "owner")));
                await Assert.ThrowsAsync<TimeoutException>(() => BenchmarkLockDirectory.AcquireAsync(
                    lockPath, "second", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10), null, CancellationToken.None));
            }
            Assert.False(Directory.Exists(lockPath));

            // A script that took the lock with mkdir and wrote its own owner format keeps it.
            Directory.CreateDirectory(lockPath);
            File.WriteAllText(Path.Combine(lockPath, "owner"), "session abc since 10:00\n");
            await Assert.ThrowsAsync<TimeoutException>(() => BenchmarkLockDirectory.AcquireAsync(
                lockPath, "second", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10), null, CancellationToken.None));

            // A lock left by a process of this host that has exited is taken over.
            File.WriteAllText(Path.Combine(lockPath, "owner"),
                "pid=" + ExitedProcessId().ToString(CultureInfo.InvariantCulture) + "\nhost=" + Environment.MachineName + "\n");

            // While another runner is reclaiming it, the stale lock is left to that runner.
            using (new FileStream(Path.Combine(root, ".bench.lock.reclaim"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                await Assert.ThrowsAsync<TimeoutException>(() => BenchmarkLockDirectory.AcquireAsync(
                    lockPath, "second", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10), null, CancellationToken.None));
                Assert.DoesNotContain("command=", File.ReadAllText(Path.Combine(lockPath, "owner")));
            }

            using (await BenchmarkLockDirectory.AcquireAsync(lockPath, "third", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10), null, CancellationToken.None))
                Assert.Contains("command=third", File.ReadAllText(Path.Combine(lockPath, "owner")));
            Assert.False(Directory.Exists(lockPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Compare_UsesTheRemoteDefaultBranchWhenThereIsNoLocalOne()
    {
        using var repository = TestRepository.Create();
        string clone = Path.Combine(Path.GetDirectoryName(repository.Root)!, "clone");
        repository.Git("clone", "--quiet", repository.Root, clone);
        repository.Git("-C", clone, "checkout", "--quiet", "--detach");
        repository.Git("-C", clone, "branch", "--quiet", "-D", "main");
        var dotnet = new FakeDotNet(1000, 1000);
        var service = new BenchmarkRevisionComparisonService(dotnet, new NullLogger(), _ => Task.FromResult<double?>(null), TimeSpan.FromMilliseconds(10));

        var result = await service.RunAsync(new BenchmarkRevisionComparisonSpec
        {
            RepositoryPath = clone,
            Project = "Bench/Bench.csproj",
            Filter = "*",
            Framework = "net10.0",
            Rounds = 1,
            ScratchRoot = repository.Scratch
        });

        Assert.Equal("origin/main", result.Baseline.Revision);
        Assert.Equal(repository.MainCommit, result.Baseline.Commit);
    }

    [Fact]
    public async Task Compare_RemovesAWorktreeWhoseCheckoutFailed()
    {
        using var repository = TestRepository.Create();
        string hook = Path.Combine(repository.Root, ".git", "hooks", "post-checkout");
        File.WriteAllText(hook, "#!/bin/sh\nexit 1\n");
        repository.Git("config", "core.hooksPath", Path.GetDirectoryName(hook)!);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var service = new BenchmarkRevisionComparisonService(new FakeDotNet(1000, 1000), new NullLogger(), _ => Task.FromResult<double?>(null), TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(new BenchmarkRevisionComparisonSpec
        {
            RepositoryPath = repository.Root,
            Baseline = "main",
            Project = "Bench/Bench.csproj",
            Filter = "*",
            Framework = "net10.0",
            ScratchRoot = repository.Scratch,
            KeepArtifacts = true
        }));

        File.Delete(hook);
        Assert.Single(repository.Git("worktree", "list", "--porcelain").Split('\n'), line => line.StartsWith("worktree ", StringComparison.Ordinal));
        Assert.All(Directory.GetDirectories(repository.Scratch), work => Assert.False(Directory.Exists(Path.Combine(work, "baseline"))));
    }

    [Fact]
    public async Task Compare_FailsWhenNoRunProducesAReport()
    {
        using var repository = TestRepository.Create();
        var dotnet = new FakeDotNet(1000, 1000) { WriteReports = false };
        var service = new BenchmarkRevisionComparisonService(dotnet, new NullLogger(), _ => Task.FromResult<double?>(null), TimeSpan.FromMilliseconds(10));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(new BenchmarkRevisionComparisonSpec
        {
            RepositoryPath = repository.Root,
            Baseline = "main",
            Project = "Bench",
            Filter = "*",
            Framework = "net10.0",
            ScratchRoot = repository.Scratch
        }));

        Assert.Contains("No run produced a BenchmarkDotNet report", error.Message);
        Assert.Equal(4, dotnet.RunSides.Count);
    }

    [Fact]
    public void Aggregate_KeepsEveryJobOfABenchmarkApartOnBothSides()
    {
        // BenchmarkDotNet 0.15.8 report with two jobs of the same two benchmark cases (four rows sharing two full names).
        using var stream = typeof(BenchmarkRevisionComparisonTests).Assembly.GetManifestResourceStream(
            "PowerForge.Tests.Fixtures.Benchmarks.BenchmarkDotNet-0.15.8-jobs.json")!;
        string path = Path.Combine(Path.GetTempPath(), "pf-bench-jobs-" + Guid.NewGuid().ToString("N") + "-report-full.json");
        using (var file = File.Create(path)) stream.CopyTo(file);
        try
        {
            var report = new BenchmarkResultImporter().Import(path);
            var rows = BenchmarkRevisionComparison.Aggregate(new[] { report }, new[] { report });

            Assert.Equal(4, rows.Length);
            Assert.Equal(4, rows.Select(row => row.Benchmark).Distinct(StringComparer.Ordinal).Count());
            Assert.All(rows, row => Assert.Equal(1.0, row.Ratio!.Value, 6));

            // A candidate that keeps only one of the jobs still lines up with the baseline's rows of that job.
            string kept = report.Summary[0].Engine;
            var oneJob = new BenchmarkRunResult { Summary = report.Summary.Where(row => row.Engine == kept).ToArray() };
            rows = BenchmarkRevisionComparison.Aggregate(new[] { report }, new[] { oneJob });
            Assert.Equal(4, rows.Length);
            Assert.Equal(2, rows.Count(row => row.Ratio is not null));
            Assert.All(rows.Where(row => row.Ratio is not null), row => Assert.EndsWith("[job " + kept + "]", row.Benchmark));

            // Reports that each ran a different single job never compare one job with the other.
            string other = report.Summary.First(row => row.Engine != kept).Engine;
            var otherJob = new BenchmarkRunResult { Summary = report.Summary.Where(row => row.Engine == other).ToArray() };
            rows = BenchmarkRevisionComparison.Aggregate(new[] { oneJob }, new[] { otherJob });
            Assert.Equal(4, rows.Length);
            Assert.All(rows, row => Assert.Null(row.Ratio));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(new[] { "--warmupCount", "3" }, "--launchCount 1 --warmupCount 3 --iterationCount 15 --outliers DontRemove --memory --exporters json")]
    [InlineData(new[] { "-e", "markdown", "--job", "short" }, "--launchCount 1 --warmupCount 5 --iterationCount 15 --outliers DontRemove --memory --exporters markdown json --job short")]
    public void BenchmarkArguments_CallerOptionsReplaceDefaultsAndKeepTheJsonExporter(string[] overrides, string expected)
        => Assert.Equal(expected, string.Join(" ", BenchmarkRevisionComparison.MergeBenchmarkArguments(overrides)));

    [Theory]
    [InlineData("--filter")]
    [InlineData("-a")]
    [InlineData("--FILTER")]
    [InlineData("--strategy", "@settings.rsp")]
    public void BenchmarkArguments_RejectOptionsTheComparisonOwnsAndResponseFiles(string option, string value = "x")
        => Assert.Throws<ArgumentException>(() => BenchmarkRevisionComparison.MergeBenchmarkArguments(new[] { option, value }));

    [Fact]
    public void LoadAverage_ReadsTheFifteenMinuteValue()
    {
        Assert.Equal(0.59, SystemLoadAverage.ParseLinux("0.52 0.58 0.59 1/389 12345\n"));
        Assert.Equal(2.31, SystemLoadAverage.ParseSysctl("{ 1.92 2.10 2.31 }\n"));
    }

    private static int ExitedProcessId()
    {
        using var process = Process.Start(new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true, UseShellExecute = false })!;
        process.WaitForExit();
        return process.Id;
    }

    private sealed class FakeDotNet : IProcessRunner
    {
        private readonly ProcessRunner _real = new();
        private readonly int _baselineNanoseconds;
        private readonly int _candidateNanoseconds;

        internal FakeDotNet(int baselineNanoseconds, int candidateNanoseconds)
        {
            _baselineNanoseconds = baselineNanoseconds;
            _candidateNanoseconds = candidateNanoseconds;
        }

        internal bool WriteReports { get; set; } = true;
        internal List<string> RunSides { get; } = new();
        internal List<List<string>> RunArguments { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            if (request.FileName != "dotnet")
                return _real.RunAsync(request, cancellationToken);

            var arguments = request.Arguments.ToList();
            if (arguments[0] == "run")
            {
                string project = arguments[arguments.IndexOf("--project") + 1];
                string side = project.Contains(Path.DirectorySeparatorChar + "baseline" + Path.DirectorySeparatorChar) ? "baseline" : "candidate";
                RunSides.Add(side);
                RunArguments.Add(arguments);
                int nanoseconds = side == "baseline" ? _baselineNanoseconds : _candidateNanoseconds;
                if (!WriteReports)
                    return Task.FromResult(new ProcessRunResult(1, string.Empty, "crashed", "dotnet", TimeSpan.Zero, timedOut: false));
                string results = Path.Combine(arguments[arguments.IndexOf("--artifacts") + 1], "results");
                Directory.CreateDirectory(results);
                File.WriteAllText(Path.Combine(results, "Bench.ParseBenchmarks-report-full.json"), Report(nanoseconds, allocated: side == "baseline" ? 100 : 200));
            }
            return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty, "dotnet", TimeSpan.Zero, timedOut: false));
        }

        private static string Report(int nanoseconds, int allocated)
        {
            string values = string.Join(", ", Enumerable.Repeat(nanoseconds.ToString(CultureInfo.InvariantCulture), 15));
            return $$"""
            {
              "Title": "Bench.ParseBenchmarks",
              "HostEnvironmentInfo": { "OsVersion": "macOS 26", "RuntimeVersion": ".NET 10.0.0", "Architecture": "Arm64", "LogicalCoreCount": 8, "BenchmarkDotNetCaption": "BenchmarkDotNet v0.15.8" },
              "Benchmarks": [
                {
                  "FullName": "Bench.ParseBenchmarks.Parse(Size: 10)",
                  "Namespace": "Bench",
                  "Type": "ParseBenchmarks",
                  "Method": "Parse",
                  "MethodTitle": "Parse",
                  "Parameters": "Size=10",
                  "DisplayInfo": "ParseBenchmarks.Parse: Job-ABC [Size=10]",
                  "Statistics": { "Median": {{nanoseconds}}, "Mean": {{nanoseconds}}, "OriginalValues": [{{values}}] },
                  "Memory": { "BytesAllocatedPerOperation": {{allocated}} }
                }
              ]
            }
            """;
        }
    }

    private sealed class TestRepository : IDisposable
    {
        private TestRepository(string root, string scratch)
        {
            Root = root;
            Scratch = scratch;
        }

        internal string Root { get; }
        internal string Scratch { get; }
        internal string MainCommit { get; private set; } = string.Empty;

        internal static TestRepository Create()
        {
            string baseDirectory = Path.Combine(Path.GetTempPath(), "pf-bench-repo-" + Guid.NewGuid().ToString("N"));
            var repository = new TestRepository(Path.Combine(baseDirectory, "repo"), Path.Combine(baseDirectory, "scratch"));
            Directory.CreateDirectory(Path.Combine(repository.Root, "Bench"));
            Directory.CreateDirectory(repository.Scratch);
            File.WriteAllText(Path.Combine(repository.Root, "Bench", "Bench.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(repository.Root, "Bench", "Bench.cs"), "// baseline");
            repository.Git("init", "--quiet", "--initial-branch=main");
            repository.Git("-c", "user.name=Test", "-c", "user.email=test@example.invalid", "add", ".");
            repository.Git("-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Initial");
            repository.MainCommit = repository.Git("rev-parse", "HEAD").Trim();
            return repository;
        }

        internal string Git(params string[] arguments)
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(" ", arguments)}: {error}");
            return output;
        }

        public void Dispose()
        {
            string baseDirectory = Path.GetDirectoryName(Root)!;
            if (Directory.Exists(baseDirectory)) Directory.Delete(baseDirectory, recursive: true);
        }
    }

    private sealed class NullLogger : ILogger
    {
        public bool IsVerbose => false;
        public void Info(string message) { }
        public void Success(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Verbose(string message) { }
    }
}
