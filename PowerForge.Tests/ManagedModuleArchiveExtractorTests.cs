using System.IO.Compression;
using PowerForge;

namespace PowerForge.Tests;

// The parallel preflight assertion requires an uncontended process-wide extraction slot.
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ManagedModuleArchiveExtractorTests
{
    [Fact]
    public void ExtractPackage_preserves_module_package_directory()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        var destination = Path.Combine(temp.Path, "out");
        CreatePackageWithPackageFolder(packagePath);

        var result = new ManagedModuleArchiveExtractor().ExtractPackage(packagePath, destination);

        Assert.Equal(3, result.FileCount);
        Assert.True(File.Exists(Path.Combine(destination, "Company.Tools.psd1")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(destination, "package", "resources", "data.txt")));
        Assert.Equal("nested", File.ReadAllText(Path.Combine(destination, "templates", "sample.nuspec")));
        Assert.False(File.Exists(Path.Combine(destination, "package", "services", "metadata", "core-properties", "metadata.psmdcp")));
    }

    [Fact]
    public void ExtractPackage_flattens_single_package_id_root_folder()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        var destination = Path.Combine(temp.Path, "out");
        CreatePackageWithModuleRootFolder(packagePath);

        var result = new ManagedModuleArchiveExtractor().ExtractPackage(packagePath, destination);

        Assert.Equal(2, result.FileCount);
        Assert.True(File.Exists(Path.Combine(destination, "Company.Tools.psd1")));
        Assert.True(File.Exists(Path.Combine(destination, "Company.Tools.psm1")));
        Assert.False(File.Exists(Path.Combine(destination, "Company.Tools", "Company.Tools.psd1")));
        Assert.False(File.Exists(Path.Combine(destination, "Company.Tools.nuspec")));
    }

    [Fact]
    public void ExtractPackage_uses_known_package_id_to_flatten_without_reading_nuspec()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        var destination = Path.Combine(temp.Path, "out");
        CreatePackageWithModuleRootFolderWithoutNuspec(packagePath);

        var result = new ManagedModuleArchiveExtractor().ExtractPackage(packagePath, destination, "Company.Tools");

        Assert.Equal(2, result.FileCount);
        Assert.True(File.Exists(Path.Combine(destination, "Company.Tools.psd1")));
        Assert.True(File.Exists(Path.Combine(destination, "Company.Tools.psm1")));
        Assert.False(File.Exists(Path.Combine(destination, "Company.Tools", "Company.Tools.psd1")));
    }

#if !NET472
    [Fact]
    public async Task ExtractPackageAsync_preserves_module_package_directory()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        var destination = Path.Combine(temp.Path, "out");
        CreatePackageWithPackageFolder(packagePath);

        await using var stream = File.OpenRead(packagePath);
        var result = await new ManagedModuleArchiveExtractor().ExtractPackageAsync(stream, destination, CancellationToken.None);

        Assert.Equal(3, result.FileCount);
        Assert.True(File.Exists(Path.Combine(destination, "Company.Tools.psd1")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(destination, "package", "resources", "data.txt")));
        Assert.Equal("nested", File.ReadAllText(Path.Combine(destination, "templates", "sample.nuspec")));
        Assert.False(File.Exists(Path.Combine(destination, "package", "services", "metadata", "core-properties", "metadata.psmdcp")));
    }

    [Fact]
    public async Task ExtractPackageAsync_flattens_single_package_id_root_folder()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        var destination = Path.Combine(temp.Path, "out");
        CreatePackageWithModuleRootFolder(packagePath);

        await using var stream = File.OpenRead(packagePath);
        var result = await new ManagedModuleArchiveExtractor().ExtractPackageAsync(stream, destination, CancellationToken.None);

        Assert.Equal(2, result.FileCount);
        Assert.True(File.Exists(Path.Combine(destination, "Company.Tools.psd1")));
        Assert.True(File.Exists(Path.Combine(destination, "Company.Tools.psm1")));
        Assert.False(File.Exists(Path.Combine(destination, "Company.Tools", "Company.Tools.psd1")));
        Assert.False(File.Exists(Path.Combine(destination, "Company.Tools.nuspec")));
    }

    [Fact]
    public async Task ExtractPackageAsync_uses_known_package_id_to_flatten_without_reading_nuspec()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        var destination = Path.Combine(temp.Path, "out");
        CreatePackageWithModuleRootFolderWithoutNuspec(packagePath);

        await using var stream = File.OpenRead(packagePath);
        var result = await new ManagedModuleArchiveExtractor().ExtractPackageAsync(stream, destination, "Company.Tools", CancellationToken.None);

        Assert.Equal(2, result.FileCount);
        Assert.True(File.Exists(Path.Combine(destination, "Company.Tools.psd1")));
        Assert.True(File.Exists(Path.Combine(destination, "Company.Tools.psm1")));
        Assert.False(File.Exists(Path.Combine(destination, "Company.Tools", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task ExtractPackageAsync_honors_cancellation_before_writing_payload()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        var destination = Path.Combine(temp.Path, "out");
        CreatePackageWithPackageFolder(packagePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await using var stream = File.OpenRead(packagePath);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ManagedModuleArchiveExtractor().ExtractPackageAsync(stream, destination, cancellation.Token));

        Assert.Empty(Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExtractPackageAsync_checks_parallel_payload_paths_before_writing()
    {
        using var temp = new TemporaryDirectory();
        using var stream = CreateGraphSizedBufferedArchive(includeInvalidPath: true);
        var destination = Path.Combine(temp.Path, "stage");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ManagedModuleArchiveExtractor().ExtractPackageAsync(stream, destination, "Company.Tools", CancellationToken.None));

        Assert.Contains("unsafe path", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(temp.Path, "outside.txt")));
        if (Environment.ProcessorCount > 1)
            Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task ExtractPackageAsync_preserves_graph_sized_payload_from_a_buffer_slice()
    {
        using var temp = new TemporaryDirectory();
        using var package = CreateGraphSizedBufferedArchive(includeInvalidPath: false);
        var bytes = package.ToArray();
        const int archiveOffset = 11;
        var buffer = new byte[archiveOffset + bytes.Length + 13];
        bytes.CopyTo(buffer, archiveOffset);
        using var stream = new MemoryStream(buffer, archiveOffset, bytes.Length, writable: false, publiclyVisible: true);
        var destination = Path.Combine(temp.Path, "stage");

        var result = await new ManagedModuleArchiveExtractor().ExtractPackageAsync(
            stream, destination, "Company.Tools", CancellationToken.None);

        Assert.Equal(8, result.FileCount);
        Assert.Equal(32L * 1024 * 1024, result.BytesWritten);
        for (var index = 0; index < 8; index++)
        {
            using var file = File.OpenRead(Path.Combine(destination, "payload", $"file-{index}.bin"));
            Assert.Equal(4L * 1024 * 1024, file.Length);
            Assert.Equal(index, file.ReadByte());
            file.Seek(-1, SeekOrigin.End);
            Assert.Equal(8 - index, file.ReadByte());
        }
    }

    private static MemoryStream CreateGraphSizedBufferedArchive(bool includeInvalidPath)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var payload = new byte[4 * 1024 * 1024];
            for (var index = 0; index < 8; index++)
            {
                payload[0] = (byte)index;
                payload[payload.Length - 1] = (byte)(8 - index);
                using var file = archive.CreateEntry($"payload/file-{index}.bin").Open();
                file.Write(payload, 0, payload.Length);
            }

            if (includeInvalidPath)
                AddEntry(archive, "../outside.txt", "must stay inside staging");
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void ExtractBufferedPackage_preserves_multi_entry_payload_contents()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        var destination = Path.Combine(temp.Path, "out");
        CreatePackageWithMultiEntryPayload(packagePath);
        var packageBytes = File.ReadAllBytes(packagePath);
        using var stream = new MemoryStream(packageBytes.Length);
        stream.Write(packageBytes, 0, packageBytes.Length);
        stream.Position = 0;

        var result = new ManagedModuleArchiveExtractor().ExtractBufferedPackage(
            stream,
            destination,
            "Company.Tools",
            CancellationToken.None);

        Assert.Equal(12, result.FileCount);
        for (var index = 0; index < result.FileCount; index++)
            Assert.Equal($"payload-{index}", File.ReadAllText(Path.Combine(destination, "payload", $"file-{index}.txt")));
    }
#endif

    private static void CreatePackageWithPackageFolder(string packagePath)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        AddEntry(archive, "Company.Tools.nuspec", TestPackageFactory.CreateNuspec("Company.Tools", "1.0.0"));
        AddEntry(archive, "Company.Tools.psd1", "@{ ModuleVersion = '1.0.0' }");
        AddEntry(archive, "package/resources/data.txt", "keep");
        AddEntry(archive, "templates/sample.nuspec", "nested");
        AddEntry(archive, "package/services/metadata/core-properties/metadata.psmdcp", "metadata");
    }

    private static void CreatePackageWithModuleRootFolder(string packagePath)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        AddEntry(archive, "Company.Tools.nuspec", TestPackageFactory.CreateNuspec("Company.Tools", "1.0.0"));
        AddEntry(archive, "Company.Tools/Company.Tools.psd1", "@{ ModuleVersion = '1.0.0' }");
        AddEntry(archive, "Company.Tools/Company.Tools.psm1", string.Empty);
        AddEntry(archive, "Company.Tools/Company.Tools.nuspec", TestPackageFactory.CreateNuspec("Company.Tools", "1.0.0"));
    }

    private static void CreatePackageWithModuleRootFolderWithoutNuspec(string packagePath)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        AddEntry(archive, "Company.Tools/Company.Tools.psd1", "@{ ModuleVersion = '1.0.0' }");
        AddEntry(archive, "Company.Tools/Company.Tools.psm1", string.Empty);
    }

    private static void CreatePackageWithMultiEntryPayload(string packagePath)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        AddEntry(archive, "Company.Tools.nuspec", TestPackageFactory.CreateNuspec("Company.Tools", "1.0.0"));
        for (var index = 0; index < 12; index++)
            AddEntry(archive, $"payload/file-{index}.txt", $"payload-{index}");
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
