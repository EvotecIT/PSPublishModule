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
            RewriteCentralDirectoryAsDos(archivePath);

            var controlExtractRoot = Path.Combine(root, "control-extracted");
            ZipFile.ExtractToDirectory(archivePath, controlExtractRoot);
            if (!OperatingSystem.IsWindows())
            {
                var controlMode = File.GetUnixFileMode(Path.Combine(controlExtractRoot, "PowerForgeWeb"));
                Assert.False(controlMode.HasFlag(UnixFileMode.UserExecute));
            }

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
            Assert.All(ReadCentralDirectoryCreatorSystems(archivePath), creatorSystem => Assert.Equal((byte)3, creatorSystem));

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

    private static void RewriteCentralDirectoryAsDos(string archivePath)
    {
        var bytes = File.ReadAllBytes(archivePath);
        var eocdOffset = FindSignatureFromEnd(bytes, 0x06054B50);
        Assert.True(eocdOffset >= 0);
        var entryCount = ReadUInt16(bytes, eocdOffset + 10);
        var centralOffset = checked((int)ReadUInt32(bytes, eocdOffset + 16));

        for (var index = 0; index < entryCount; index++)
        {
            Assert.Equal(0x02014B50u, ReadUInt32(bytes, centralOffset));
            bytes[centralOffset + 5] = 0;
            WriteUInt32(bytes, centralOffset + 38, 0);
            centralOffset += 46
                             + ReadUInt16(bytes, centralOffset + 28)
                             + ReadUInt16(bytes, centralOffset + 30)
                             + ReadUInt16(bytes, centralOffset + 32);
        }

        File.WriteAllBytes(archivePath, bytes);
    }

    private static byte[] ReadCentralDirectoryCreatorSystems(string archivePath)
    {
        var bytes = File.ReadAllBytes(archivePath);
        var eocdOffset = FindSignatureFromEnd(bytes, 0x06054B50);
        Assert.True(eocdOffset >= 0);
        var entryCount = ReadUInt16(bytes, eocdOffset + 10);
        var centralOffset = checked((int)ReadUInt32(bytes, eocdOffset + 16));
        var creatorSystems = new byte[entryCount];

        for (var index = 0; index < entryCount; index++)
        {
            Assert.Equal(0x02014B50u, ReadUInt32(bytes, centralOffset));
            creatorSystems[index] = bytes[centralOffset + 5];
            centralOffset += 46
                             + ReadUInt16(bytes, centralOffset + 28)
                             + ReadUInt16(bytes, centralOffset + 30)
                             + ReadUInt16(bytes, centralOffset + 32);
        }

        return creatorSystems;
    }

    private static int FindSignatureFromEnd(byte[] bytes, uint signature)
    {
        for (var offset = bytes.Length - 4; offset >= 0; offset--)
        {
            if (ReadUInt32(bytes, offset) == signature)
                return offset;
        }

        return -1;
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
        => (ushort)(bytes[offset] | bytes[offset + 1] << 8);

    private static uint ReadUInt32(byte[] bytes, int offset)
        => (uint)(bytes[offset]
                  | bytes[offset + 1] << 8
                  | bytes[offset + 2] << 16
                  | bytes[offset + 3] << 24);

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}
