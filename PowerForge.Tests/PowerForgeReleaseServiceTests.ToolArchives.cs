using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void ToolArchive_UnixExecutablesPreserveLaunchPermissions()
    {
        var root = CreateSandbox();
        try
        {
            var outputRoot = Path.Combine(root, "output");
            Directory.CreateDirectory(outputRoot);

            var executablePath = Path.Combine(outputRoot, "PowerForgeWeb");
            var aliasPath = Path.Combine(outputRoot, "powerforge-web");
            File.WriteAllText(executablePath, "main");
            File.WriteAllText(aliasPath, "alias");

            var archivePath = Path.Combine(root, "PowerForgeWeb-osx-arm64.zip");
            ZipFile.CreateFromDirectory(outputRoot, archivePath);

            PowerForgeToolReleaseService.ApplyArchiveExecutablePermissions(
                "osx-arm64",
                outputRoot,
                archivePath,
                executablePath,
                aliasPath);

            using (var archive = ZipFile.OpenRead(archivePath))
            {
                Assert.Equal(unchecked((int)0x81ED0000u), archive.GetEntry("PowerForgeWeb")!.ExternalAttributes);
                Assert.Equal(unchecked((int)0x81ED0000u), archive.GetEntry("powerforge-web")!.ExternalAttributes);
            }

            var extractRoot = Path.Combine(root, "extracted");
            ZipFile.ExtractToDirectory(archivePath, extractRoot);
            if (!OperatingSystem.IsWindows())
            {
                var executableMode = File.GetUnixFileMode(Path.Combine(extractRoot, "PowerForgeWeb"));
                var aliasMode = File.GetUnixFileMode(Path.Combine(extractRoot, "powerforge-web"));
                Assert.True(executableMode.HasFlag(UnixFileMode.UserExecute));
                Assert.True(aliasMode.HasFlag(UnixFileMode.UserExecute));
            }
        }
        finally
        {
            TryDelete(root);
        }
    }
}
