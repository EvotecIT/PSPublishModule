using System.Collections;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerProvenanceEdgeCaseTests
{
    [Fact]
    public void Run_TreatsNullStepsAsAnEmptyPlan()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Steps = null!
            };

            var result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Empty(DotNetPublishPipelineRunner.EnumeratePlannedMsiVersionStatePaths(plan));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void VerifiedMsiVersionStateWrites_ResolveSymlinkedProjectRoot()
    {
        if (OperatingSystem.IsWindows())
            return;

        var testRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var repositoryRoot = Path.Combine(testRoot, "repository");
        var physicalProjectRoot = Path.Combine(repositoryRoot, "src", "app");
        var linkedProjectRoot = Path.Combine(testRoot, "project-link");
        var reservationOwner = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(physicalProjectRoot);

        try
        {
            RunGit(repositoryRoot, "init");
            RunGit(repositoryRoot, "config user.name \"PowerForge Tests\"");
            RunGit(repositoryRoot, "config user.email \"powerforge-tests@example.invalid\"");
            var physicalStatePath = Path.Combine(
                physicalProjectRoot,
                "Build",
                "versioning",
                "app.msi.state.json");
            Directory.CreateDirectory(Path.GetDirectoryName(physicalStatePath)!);
            var initialBytes = Encoding.UTF8.GetBytes("{\"Version\":\"1.0.0\"}");
            File.WriteAllBytes(physicalStatePath, initialBytes);
            RunGit(repositoryRoot, "add src/app/Build/versioning/app.msi.state.json");
            RunGit(repositoryRoot, "commit -m \"test source\"");
            Directory.CreateSymbolicLink(linkedProjectRoot, physicalProjectRoot);

            var linkedStatePath = Path.Combine(
                linkedProjectRoot,
                "Build",
                "versioning",
                "app.msi.state.json");
            var initiallyCleanState =
                DotNetPublishPipelineRunner.CaptureCleanTrackedGeneratedProvenanceState(
                    linkedProjectRoot,
                    new[] { linkedStatePath });
            var writerBytes = Encoding.UTF8.GetBytes("{\"Version\":\"1.0.1\"}");
            File.WriteAllBytes(linkedStatePath, writerBytes);
            DotNetPublishPipelineRunner.RecordMsiVersionStateWrite(
                reservationOwner,
                linkedStatePath,
                Convert.ToHexString(SHA256.HashData(initialBytes)),
                Convert.ToHexString(SHA256.HashData(writerBytes)));

            Assert.Equal(
                new[] { linkedStatePath },
                DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
                    linkedProjectRoot,
                    initiallyCleanState,
                    reservationOwner));
        }
        finally
        {
            DotNetPublishPipelineRunner.ClearMsiVersionStateWrites(reservationOwner);
            if (Directory.Exists(linkedProjectRoot))
                Directory.Delete(linkedProjectRoot);
            if (Directory.Exists(testRoot))
                DeleteTestDirectory(testRoot);
        }
    }

    [Fact]
    public void VerifiedMsiVersionStateWrites_TreatColonPrefixedStatePathLiterally()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var reservationOwner = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);

        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            var versionStatePath = Path.Combine(root, ":state.json");
            var initialBytes = Encoding.UTF8.GetBytes("{\"Version\":\"1.0.0\"}");
            File.WriteAllBytes(versionStatePath, initialBytes);
            RunGit(root, "add -- ./:state.json");
            RunGit(root, "commit -m \"test source\"");

            var initiallyCleanState =
                DotNetPublishPipelineRunner.CaptureCleanTrackedGeneratedProvenanceState(
                    root,
                    new[] { versionStatePath });
            var writerBytes = Encoding.UTF8.GetBytes("{\"Version\":\"1.0.1\"}");
            File.WriteAllBytes(versionStatePath, writerBytes);
            DotNetPublishPipelineRunner.RecordMsiVersionStateWrite(
                reservationOwner,
                versionStatePath,
                Convert.ToHexString(SHA256.HashData(initialBytes)),
                Convert.ToHexString(SHA256.HashData(writerBytes)));

            Assert.Equal(
                new[] { versionStatePath },
                DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
                    root,
                    initiallyCleanState,
                    reservationOwner));

            var stagedBytes = Encoding.UTF8.GetBytes("{\"Version\":\"9.9.9\"}");
            File.WriteAllBytes(versionStatePath, stagedBytes);
            RunGit(root, "add -- ./:state.json");
            File.WriteAllBytes(versionStatePath, writerBytes);
            Assert.Empty(DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
                root,
                initiallyCleanState,
                reservationOwner));
        }
        finally
        {
            DotNetPublishPipelineRunner.ClearMsiVersionStateWrites(reservationOwner);
            if (Directory.Exists(root))
                DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void VerifiedMsiVersionStateWrites_RejectTrackedSymlinkStatePath()
    {
        if (OperatingSystem.IsWindows())
            return;

        var testRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var repositoryRoot = Path.Combine(testRoot, "repository");
        var reservationOwner = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(repositoryRoot);

        try
        {
            RunGit(repositoryRoot, "init");
            RunGit(repositoryRoot, "config user.name \"PowerForge Tests\"");
            RunGit(repositoryRoot, "config user.email \"powerforge-tests@example.invalid\"");
            var firstTarget = Path.Combine(testRoot, "first-state.json");
            var secondTarget = Path.Combine(testRoot, "second-state.json");
            var initialBytes = Encoding.UTF8.GetBytes("{\"Version\":\"1.0.0\"}");
            var writerBytes = Encoding.UTF8.GetBytes("{\"Version\":\"1.0.1\"}");
            File.WriteAllBytes(firstTarget, initialBytes);
            File.WriteAllBytes(secondTarget, writerBytes);
            var versionStatePath = Path.Combine(
                repositoryRoot,
                "Build",
                "versioning",
                "app.msi.state.json");
            Directory.CreateDirectory(Path.GetDirectoryName(versionStatePath)!);
            File.CreateSymbolicLink(versionStatePath, firstTarget);
            RunGit(repositoryRoot, "add Build/versioning/app.msi.state.json");
            RunGit(repositoryRoot, "commit -m \"test source\"");

            var initiallyCleanState =
                DotNetPublishPipelineRunner.CaptureCleanTrackedGeneratedProvenanceState(
                    repositoryRoot,
                    new[] { versionStatePath });
            File.Delete(versionStatePath);
            File.CreateSymbolicLink(versionStatePath, secondTarget);
            DotNetPublishPipelineRunner.RecordMsiVersionStateWrite(
                reservationOwner,
                versionStatePath,
                Convert.ToHexString(SHA256.HashData(initialBytes)),
                Convert.ToHexString(SHA256.HashData(writerBytes)));

            Assert.Empty(DotNetPublishPipelineRunner.GetVerifiedMsiVersionStateWrites(
                repositoryRoot,
                initiallyCleanState,
                reservationOwner));
        }
        finally
        {
            DotNetPublishPipelineRunner.ClearMsiVersionStateWrites(reservationOwner);
            if (Directory.Exists(testRoot))
                DeleteTestDirectory(testRoot);
        }
    }

    [Fact]
    public void ReadSourceProvenance_FailsClosedWhenStatusChangesDuringWriterVerification()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var reservationOwner = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);

        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            var versionStatePath = Path.Combine(root, "version.json");
            var initialBytes = Encoding.UTF8.GetBytes("{\"Version\":\"1.0.0\"}");
            var writerBytes = Encoding.UTF8.GetBytes("{\"Version\":\"1.0.1\"}");
            var stagedBytes = Encoding.UTF8.GetBytes("{\"Version\":\"9.9.9\"}");
            File.WriteAllBytes(versionStatePath, initialBytes);
            RunGit(root, "add version.json");
            RunGit(root, "commit -m \"test source\"");
            File.WriteAllBytes(versionStatePath, writerBytes);
            DotNetPublishPipelineRunner.RecordMsiVersionStateWrite(
                reservationOwner,
                versionStatePath,
                Convert.ToHexString(SHA256.HashData(initialBytes)),
                Convert.ToHexString(SHA256.HashData(writerBytes)));
            var initialState = new MutatingReadOnlyDictionary(
                new KeyValuePair<string, string>(
                    versionStatePath,
                    Convert.ToHexString(SHA256.HashData(initialBytes))),
                () =>
                {
                    File.WriteAllBytes(versionStatePath, stagedBytes);
                    RunGit(root, "add version.json");
                    File.WriteAllBytes(versionStatePath, writerBytes);
                });

            var provenance = DotNetPublishPipelineRunner.ReadSourceProvenance(
                root,
                generatedPaths: null,
                trackedGeneratedPaths: null,
                cleanTrackedGeneratedProvenanceState: initialState,
                msiReservationOwner: reservationOwner);

            Assert.True(provenance.Dirty);
        }
        finally
        {
            DotNetPublishPipelineRunner.ClearMsiVersionStateWrites(reservationOwner);
            if (Directory.Exists(root))
                DeleteTestDirectory(root);
        }
    }

    private static string RunGit(string root, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.NotNull(process);
        string output = process!.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10000), $"git {arguments} timed out");
        Assert.True(process.ExitCode == 0, $"git {arguments} failed: {error}");
        return output;
    }

    private static void DeleteTestDirectory(string root)
    {
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
            file.Attributes = FileAttributes.Normal;
        Directory.Delete(root, recursive: true);
    }

    private sealed class MutatingReadOnlyDictionary : IReadOnlyDictionary<string, string>
    {
        private readonly KeyValuePair<string, string> _item;
        private readonly Action _afterYield;

        internal MutatingReadOnlyDictionary(
            KeyValuePair<string, string> item,
            Action afterYield)
        {
            _item = item;
            _afterYield = afterYield;
        }

        public int Count => 1;

        public IEnumerable<string> Keys => new[] { _item.Key };

        public IEnumerable<string> Values => new[] { _item.Value };

        public string this[string key] => string.Equals(key, _item.Key, StringComparison.Ordinal)
            ? _item.Value
            : throw new KeyNotFoundException();

        public bool ContainsKey(string key)
            => string.Equals(key, _item.Key, StringComparison.Ordinal);

        public bool TryGetValue(string key, out string value)
        {
            if (ContainsKey(key))
            {
                value = _item.Value;
                return true;
            }

            value = string.Empty;
            return false;
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            yield return _item;
            _afterYield();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
