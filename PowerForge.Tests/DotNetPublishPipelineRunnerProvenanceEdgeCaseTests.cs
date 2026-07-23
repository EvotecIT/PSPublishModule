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
                Directory.Delete(root, recursive: true);
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
                Directory.Delete(testRoot, recursive: true);
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
}
