using System.Security.Cryptography;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void NoBuildPublishSnapshot_AllowsMacOsReadNotificationsWithoutStateChanges()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "App.runtimeconfig.json");
            byte[] bytes = "{\"runtimeOptions\":{}}"u8.ToArray();
            File.WriteAllBytes(sourcePath, bytes);
            var input = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "App.runtimeconfig.json",
                new Dictionary<string, string>
                {
                    ["CopyToPublishDirectory"] = "PreserveNewest"
                },
                Convert.ToHexString(SHA256.HashData(bytes)));

            using DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot snapshot =
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create([input], null);
            string snapshotPath = Assert.Single(Directory.GetFiles(
                Path.Combine(Path.GetDirectoryName(snapshot.TargetsPath)!, "inputs"),
                "*",
                SearchOption.AllDirectories));

            _ = File.ReadAllBytes(snapshotPath);
            File.Copy(snapshotPath, Path.Combine(root, "published.runtimeconfig.json"));

            snapshot.ValidateUnchanged();
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void NoBuildPublishSnapshot_RejectsMacOsContentMutation()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "App.runtimeconfig.json");
            byte[] bytes = "{\"runtimeOptions\":{}}"u8.ToArray();
            File.WriteAllBytes(sourcePath, bytes);
            var input = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "App.runtimeconfig.json",
                new Dictionary<string, string>(),
                Convert.ToHexString(SHA256.HashData(bytes)));

            using DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot snapshot =
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create([input], null);
            string snapshotPath = Assert.Single(Directory.GetFiles(
                Path.Combine(Path.GetDirectoryName(snapshot.TargetsPath)!, "inputs"),
                "*",
                SearchOption.AllDirectories));

            DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(snapshotPath);
            UnixFileMode unixFileMode = File.GetUnixFileMode(snapshotPath);

            File.WriteAllText(snapshotPath, "mutated");
            File.WriteAllBytes(snapshotPath, bytes);
            File.SetLastWriteTimeUtc(snapshotPath, lastWriteTimeUtc);
            File.SetUnixFileMode(snapshotPath, unixFileMode);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                snapshot.ValidateUnchanged);
            Assert.Contains("snapshot", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
