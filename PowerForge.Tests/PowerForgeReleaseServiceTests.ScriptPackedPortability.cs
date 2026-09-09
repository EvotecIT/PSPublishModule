using System.IO.Compression;

namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void CreateModuleAssetEntries_AcceptsPortableScriptPackedDirectoryEntry()
    {
        string root = CreateSandbox();
        try
        {
            string scriptPackedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(scriptPackedPath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("scripts/");
                using var writer = new StreamWriter(archive.CreateEntry("scripts/Company.Tools.ps1").Open());
                writer.Write("Get-Date");
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    scriptPackedPath,
                    CreateScriptPackedPlan(root, scriptPackedPath, "scripts/Company.Tools.ps1"),
                    new[] { scriptPackedPath }));

            Assert.True(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("../escape/", true)]
    [InlineData("safe/../../escape/", true)]
    [InlineData("safe//payload.dll", false)]
    [InlineData("safe/demo:final.dll", false)]
    [InlineData("safe/CON.dll", false)]
    [InlineData("safe/com1.log", false)]
    [InlineData("safe/CON .txt", false)]
    [InlineData("safe/trailing-dot.", false)]
    [InlineData("safe/trailing-space ", false)]
    [InlineData("safe/bad?.dll", false)]
    [InlineData("safe/control\u0001.dll", false)]
    [InlineData("scripts\\Tool.ps1", false)]
    public void CreateModuleAssetEntries_RejectsNonPortableScriptPackedEntryOnEveryHost(
        string unsafeEntryName,
        bool directoryEntry)
    {
        string root = CreateSandbox();
        try
        {
            string scriptPackedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(scriptPackedPath, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Company.Tools.ps1").Open()))
                    writer.Write("Get-Date");

                ZipArchiveEntry unsafeEntry = archive.CreateEntry(unsafeEntryName);
                if (!directoryEntry)
                {
                    using var writer = new StreamWriter(unsafeEntry.Open());
                    writer.Write("unsafe");
                }
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    scriptPackedPath,
                    CreateScriptPackedPlan(root, scriptPackedPath, "Company.Tools.ps1"),
                    new[] { scriptPackedPath }));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_RejectsUnreadableScriptPackedPayload()
    {
        string root = CreateSandbox();
        try
        {
            string scriptPackedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(scriptPackedPath, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("Company.Tools.ps1").Open());
                writer.Write("Get-Date");
            }
            ReplaceCompressionMethodWithUnsupportedValue(scriptPackedPath);

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    scriptPackedPath,
                    CreateScriptPackedPlan(root, scriptPackedPath, "Company.Tools.ps1"),
                    new[] { scriptPackedPath }));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_RejectsOversizedScriptPackedPayloadDeclaration()
    {
        string root = CreateSandbox();
        try
        {
            string scriptPackedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(scriptPackedPath, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("Company.Tools.ps1").Open());
                writer.Write("Get-Date");
            }
            ReplaceUncompressedSize(scriptPackedPath, 0x80000001);

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    scriptPackedPath,
                    CreateScriptPackedPlan(root, scriptPackedPath, "Company.Tools.ps1"),
                    new[] { scriptPackedPath }));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("assets", false, "assets/data.json", false)]
    [InlineData("Assets", false, "assets/data.json", false)]
    [InlineData("assets", false, "assets/", true)]
    [InlineData("assets/", true, "Assets/", true)]
    [InlineData("assets/caf\u00E9.json", false, "assets/cafe\u0301.json", false)]
    [InlineData("assets/caf\u00E9", false, "assets/cafe\u0301/", true)]
    [InlineData("assets/caf\u00E9", false, "assets/cafe\u0301/data.json", false)]
    public void CreateModuleAssetEntries_RejectsScriptPackedNamespaceCollisions(
        string firstPath,
        bool firstDirectory,
        string secondPath,
        bool secondDirectory)
    {
        string root = CreateSandbox();
        try
        {
            string scriptPackedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(scriptPackedPath, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Company.Tools.ps1").Open()))
                    writer.Write("Get-Date");

                WriteArchiveEntry(archive, firstPath, firstDirectory);
                WriteArchiveEntry(archive, secondPath, secondDirectory);
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    scriptPackedPath,
                    CreateScriptPackedPlan(root, scriptPackedPath, "Company.Tools.ps1"),
                    new[] { scriptPackedPath }));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CreateModuleAssetEntries_RejectsScriptPackedSymbolicLinkEntry()
    {
        string root = CreateSandbox();
        try
        {
            string scriptPackedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(scriptPackedPath, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Company.Tools.ps1").Open()))
                    writer.Write("Get-Date");

                ZipArchiveEntry link = archive.CreateEntry("linked-content");
                link.ExternalAttributes = unchecked((int)0xA1FF0000);
                using var linkWriter = new StreamWriter(link.Open());
                linkWriter.Write("../outside");
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    scriptPackedPath,
                    CreateScriptPackedPlan(root, scriptPackedPath, "Company.Tools.ps1"),
                    new[] { scriptPackedPath }));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("scripts/Invoke:Tools.ps1")]
    [InlineData("CON/Company.Tools.ps1")]
    [InlineData("scripts./Company.Tools.ps1")]
    public void CreateModuleAssetEntries_RejectsNonPortableConfiguredScriptPackedEntryPointOnEveryHost(
        string entryPoint)
    {
        string root = CreateSandbox();
        try
        {
            string scriptPackedPath = Path.Combine(root, "Company.Tools.zip");
            using (ZipArchive archive = ZipFile.Open(scriptPackedPath, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry(entryPoint).Open());
                writer.Write("Get-Date");
            }

            PowerForgeReleaseAssetEntry entry = Assert.Single(
                PowerForgeReleaseService.CreateModuleAssetEntries(
                    scriptPackedPath,
                    CreateScriptPackedPlan(root, scriptPackedPath, entryPoint),
                    new[] { scriptPackedPath }));

            Assert.False(entry.IsFinalPackageOutput);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static PowerForgeModuleReleasePlanSummary CreateScriptPackedPlan(
        string root,
        string archivePath,
        string entryPoint)
        => new()
        {
            ModuleName = "Company.Tools",
            ModuleVersion = "4.0.0",
            ArtefactOutputs =
            [
                new PowerForgeModuleArtefactOutputSummary
                {
                    Type = ArtefactType.ScriptPacked,
                    OutputRoot = root,
                    OutputPath = archivePath,
                    EntryPointRelativePath = entryPoint
                }
            ]
        };

    private static void WriteArchiveEntry(ZipArchive archive, string path, bool directory)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        if (directory)
            return;

        using var writer = new StreamWriter(entry.Open());
        writer.Write("payload");
    }

    private static void ReplaceCompressionMethodWithUnsupportedValue(string archivePath)
        => PatchZipHeaders(
            archivePath,
            static (bytes, headerOffset, isLocalHeader) =>
                WriteUInt16LittleEndian(bytes, headerOffset + (isLocalHeader ? 8 : 10), 99));

    private static void ReplaceUncompressedSize(string archivePath, uint size)
        => PatchZipHeaders(
            archivePath,
            (bytes, headerOffset, isLocalHeader) =>
                WriteUInt32LittleEndian(bytes, headerOffset + (isLocalHeader ? 22 : 24), size));

    private static void PatchZipHeaders(
        string archivePath,
        Action<byte[], int, bool> patch)
    {
        byte[] bytes = File.ReadAllBytes(archivePath);
        bool localHeaderPatched = false;
        bool centralHeaderPatched = false;
        for (int index = 0; index <= bytes.Length - 28; index++)
        {
            bool isSignature = bytes[index] == 0x50 && bytes[index + 1] == 0x4B;
            if (!isSignature)
                continue;

            if (bytes[index + 2] == 0x03 && bytes[index + 3] == 0x04)
            {
                patch(bytes, index, true);
                localHeaderPatched = true;
            }
            else if (bytes[index + 2] == 0x01 && bytes[index + 3] == 0x02)
            {
                patch(bytes, index, false);
                centralHeaderPatched = true;
            }
        }

        Assert.True(localHeaderPatched);
        Assert.True(centralHeaderPatched);
        File.WriteAllBytes(archivePath, bytes);
    }

    private static void WriteUInt16LittleEndian(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32LittleEndian(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}
