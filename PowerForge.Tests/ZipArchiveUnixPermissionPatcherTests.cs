using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class ZipArchiveUnixPermissionPatcherTests
{
    [Fact]
    public void ApplyUnixFilePermissions_PreservesRequestedMode()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string archivePath = Path.Combine(root, "helpers.zip");
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("bin/helper").Open());
                writer.Write("#!/bin/sh\nexit 0\n");
            }

            ZipArchiveUnixPermissionPatcher.ApplyUnixFilePermissions(
                archivePath,
                new Dictionary<string, int>(StringComparer.Ordinal) { ["bin/helper"] = 0x1E8 });

            using ZipArchive patched = ZipFile.OpenRead(archivePath);
            ZipArchiveEntry entry = Assert.Single(patched.Entries);
            Assert.Equal(0x8000 | 0x1E8, (entry.ExternalAttributes >> 16) & 0xFFFF);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
