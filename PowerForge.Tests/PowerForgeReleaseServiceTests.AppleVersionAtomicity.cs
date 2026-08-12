namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void AppleVersionSource_UpdateDoesNotOverwriteAnAtomicEditorAfterComparison()
    {
        var root = CreateSandbox();
        try
        {
            WriteXcodeGenVersionSource(root, "1.5.0", "13");
            var sourcePath = Path.Combine(root, "project.yml");
            var approvedContent = File.ReadAllText(sourcePath);
            var editorContent = approvedContent.Replace(
                "name: CasaRay",
                "name: CasaRay-Edited",
                StringComparison.Ordinal);
            var service = new AppleReleaseVersionSourceService(path =>
            {
                var editorPath = path + ".editor";
                File.WriteAllText(editorPath, editorContent);
                File.Move(editorPath, path, overwrite: true);
            });

            var exception = Assert.Throws<InvalidOperationException>(() =>
                service.Update(
                    sourcePath,
                    approvedContent,
                    "1.6.0",
                    "14",
                    highestRemoteBuildNumber: 13,
                    whatIf: false));

            Assert.Contains("changed while", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(editorContent, File.ReadAllText(sourcePath));
            var version = new AppleReleaseVersionSourceService().Read(sourcePath);
            Assert.Equal("1.5.0", version.MarketingVersion);
            Assert.Equal("13", version.BuildNumber);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void AppleVersionSource_UpdatePreservesApprovedSourceWhenInterruptedBeforeAtomicPublish()
    {
        var root = CreateSandbox();
        try
        {
            WriteXcodeGenVersionSource(root, "1.5.0", "13");
            var sourcePath = Path.Combine(root, "project.yml");
            var approvedContent = File.ReadAllText(sourcePath);
            var service = new AppleReleaseVersionSourceService(_ =>
                throw new IOException("Simulated interruption before atomic publication."));

            var exception = Assert.Throws<IOException>(() =>
                service.Update(
                    sourcePath,
                    approvedContent,
                    "1.6.0",
                    "14",
                    highestRemoteBuildNumber: 13,
                    whatIf: false));

            Assert.Contains("interruption", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(approvedContent, File.ReadAllText(sourcePath));
            Assert.Empty(Directory.EnumerateFiles(root, ".project.yml.*.tmp"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void AppleVersionSource_UpdateSucceedsWhenCommittedBackupCleanupFails()
    {
        var root = CreateSandbox();
        try
        {
            WriteXcodeGenVersionSource(root, "1.5.0", "13");
            var sourcePath = Path.Combine(root, "project.yml");
            var approvedContent = File.ReadAllText(sourcePath);
            var service = new AppleReleaseVersionSourceService(
                deleteFile: path => throw new IOException($"Simulated cleanup failure for {path}"));

            var receipt = service.Update(
                sourcePath,
                approvedContent,
                "1.6.0",
                "14",
                highestRemoteBuildNumber: 13,
                whatIf: false);

            Assert.True(receipt.Changed);
            var version = new AppleReleaseVersionSourceService().Read(sourcePath);
            Assert.Equal("1.6.0", version.MarketingVersion);
            Assert.Equal("14", version.BuildNumber);
            Assert.Single(Directory.EnumerateFiles(root, ".project.yml.*.previous"));
        }
        finally
        {
            TryDelete(root);
        }
    }
}
