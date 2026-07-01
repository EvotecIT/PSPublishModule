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

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
