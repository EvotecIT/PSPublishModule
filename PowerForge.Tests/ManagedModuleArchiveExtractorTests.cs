using System.IO.Compression;
using PowerForge;

namespace PowerForge.Tests;

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

        Assert.Equal(2, result.FileCount);
        Assert.True(File.Exists(Path.Combine(destination, "Company.Tools.psd1")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(destination, "package", "resources", "data.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "package", "services", "metadata", "core-properties", "metadata.psmdcp")));
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

        Assert.Equal(2, result.FileCount);
        Assert.True(File.Exists(Path.Combine(destination, "Company.Tools.psd1")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(destination, "package", "resources", "data.txt")));
        Assert.False(File.Exists(Path.Combine(destination, "package", "services", "metadata", "core-properties", "metadata.psmdcp")));
    }
#endif

    private static void CreatePackageWithPackageFolder(string packagePath)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        AddEntry(archive, "Company.Tools.nuspec", TestPackageFactory.CreateNuspec("Company.Tools", "1.0.0"));
        AddEntry(archive, "Company.Tools.psd1", "@{ ModuleVersion = '1.0.0' }");
        AddEntry(archive, "package/resources/data.txt", "keep");
        AddEntry(archive, "package/services/metadata/core-properties/metadata.psmdcp", "metadata");
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
