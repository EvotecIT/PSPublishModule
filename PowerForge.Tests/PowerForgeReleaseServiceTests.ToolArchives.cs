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

    [Fact]
    public void RefreshBuiltArchivesAfterSigning_RestoresUnixExecutablePermissions()
    {
        var root = CreateSandbox();
        try
        {
            var outputRoot = Path.Combine(root, "output");
            Directory.CreateDirectory(outputRoot);
            var executablePath = Path.Combine(outputRoot, "PowerForge");
            var aliasPath = Path.Combine(outputRoot, "pf");
            File.WriteAllText(executablePath, "signed-main");
            File.WriteAllText(aliasPath, "signed-alias");
            var archivePath = Path.Combine(root, "PowerForge-linux-x64.zip");
            ZipFile.CreateFromDirectory(outputRoot, archivePath);
            RewriteCentralDirectoryAsDos(archivePath);

            var result = new PowerForgeReleaseResult {
                Tools = new PowerForgeToolReleaseResult {
                    Success = true,
                    Artefacts = [
                        new PowerForgeToolReleaseArtifactResult {
                            Runtime = "linux-x64",
                            OutputPath = outputRoot,
                            ExecutablePath = executablePath,
                            CommandAliasPath = aliasPath,
                            ZipPath = archivePath
                        }
                    ]
                }
            };

            PowerForgeReleaseService.RefreshBuiltArchivesAfterSigning(result, [outputRoot]);

            using var archive = ZipFile.OpenRead(archivePath);
            Assert.Equal(unchecked((int)0x81ED0000u), archive.GetEntry("PowerForge")!.ExternalAttributes);
            Assert.Equal(unchecked((int)0x81ED0000u), archive.GetEntry("pf")!.ExternalAttributes);
            Assert.All(ReadCentralDirectoryCreatorSystems(archivePath), creatorSystem => Assert.Equal((byte)3, creatorSystem));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void RefreshBuiltArchivesAfterSigning_RebuildsModuleZipFromSignedDirectory()
    {
        var root = CreateSandbox();
        try
        {
            var signedRoot = Path.Combine(root, "unpacked");
            var moduleRoot = Path.Combine(signedRoot, "PSPublishModule");
            Directory.CreateDirectory(moduleRoot);
            File.WriteAllText(Path.Combine(moduleRoot, "PSPublishModule.psd1"), "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(moduleRoot, "PSPublishModule.dll"), "signed");
            File.WriteAllText(Path.Combine(moduleRoot, "FullPackageOnly.dll"), "extra");

            var unsignedRoot = Path.Combine(root, "unsigned");
            var unsignedModuleRoot = Path.Combine(unsignedRoot, "PSPublishModule");
            Directory.CreateDirectory(unsignedModuleRoot);
            File.WriteAllText(Path.Combine(unsignedModuleRoot, "PSPublishModule.psd1"), "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(unsignedModuleRoot, "PSPublishModule.dll"), "unsigned");
            var archivePath = Path.Combine(root, "PSPublishModule.v1.0.0.zip");
            ZipFile.CreateFromDirectory(unsignedRoot, archivePath);
            var stagedPath = Path.Combine(root, "staged", Path.GetFileName(archivePath));
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
            File.Copy(archivePath, stagedPath);
            var result = new PowerForgeReleaseResult {
                ReleaseAssetEntries = [
                    new PowerForgeReleaseAssetEntry {
                        Path = archivePath,
                        StagedPath = stagedPath,
                        Category = PowerForgeReleaseAssetCategory.Module
                    }
                ],
                ReleaseAssets = [stagedPath]
            };

            PowerForgeReleaseService.RefreshBuiltArchivesAfterSigning(result, [signedRoot]);

            Assert.Equal("signed", ReadArchiveText(archivePath, "PSPublishModule/PSPublishModule.dll"));
            Assert.Equal("signed", ReadArchiveText(stagedPath, "PSPublishModule/PSPublishModule.dll"));
            using var archive = ZipFile.OpenRead(archivePath);
            Assert.Null(archive.GetEntry("PSPublishModule/FullPackageOnly.dll"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void RefreshBuiltArchivesAfterSigning_UnmatchedModuleZipFails()
    {
        var root = CreateSandbox();
        try
        {
            var archiveSource = Path.Combine(root, "archive-source", "PSPublishModule");
            Directory.CreateDirectory(archiveSource);
            File.WriteAllText(Path.Combine(archiveSource, "PSPublishModule.psd1"), "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(archiveSource, "PSPublishModule.dll"), "unsigned");
            var archivePath = Path.Combine(root, "PSPublishModule.v1.0.0.zip");
            ZipFile.CreateFromDirectory(Path.GetDirectoryName(archiveSource)!, archivePath);
            var unrelatedSignedRoot = Path.Combine(root, "signed");
            Directory.CreateDirectory(unrelatedSignedRoot);
            var result = new PowerForgeReleaseResult {
                ReleaseAssetEntries = [
                    new PowerForgeReleaseAssetEntry {
                        Path = archivePath,
                        Category = PowerForgeReleaseAssetCategory.Module
                    }
                ]
            };

            var exception = Assert.Throws<InvalidOperationException>(
                () => PowerForgeReleaseService.RefreshBuiltArchivesAfterSigning(result, [unrelatedSignedRoot]));

            Assert.Contains("unsigned archive cannot be published", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void RefreshBuiltArchivesAfterSigning_RefreshesStagedSignedPackage()
    {
        var root = CreateSandbox();
        try
        {
            var packagePath = Path.Combine(root, "Package.1.0.0.nupkg");
            var stagedPath = Path.Combine(root, "staged", Path.GetFileName(packagePath));
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
            File.WriteAllText(packagePath, "signed-package");
            File.WriteAllText(stagedPath, "unsigned-package");
            var result = new PowerForgeReleaseResult {
                ReleaseAssetEntries = [
                    new PowerForgeReleaseAssetEntry {
                        Path = packagePath,
                        StagedPath = stagedPath,
                        Category = PowerForgeReleaseAssetCategory.Package
                    }
                ],
                ReleaseAssets = [stagedPath]
            };

            var refreshed = PowerForgeReleaseService.RefreshBuiltArchivesAfterSigning(
                result,
                [],
                [packagePath]);

            Assert.Contains(stagedPath, refreshed);
            Assert.Equal("signed-package", File.ReadAllText(stagedPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string ReadArchiveText(string archivePath, string entryName)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        using var reader = new StreamReader(archive.GetEntry(entryName)!.Open());
        return reader.ReadToEnd();
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
