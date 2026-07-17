using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleLocalPublishIdentityTests
{
    [Fact]
    public async Task PublishPackageAsync_canonicalizes_arbitrary_package_filename()
    {
        using var source = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(source.Path, "renamed-package.nupkg");
        TestPackageFactory.Create(packagePath, "Company.Tools", "1.0");
        var client = new ManagedModuleRepositoryClient(new NullLogger());
        var repository = new ManagedModuleRepository("Local", destination.Path);
        using var download = new TemporaryDirectory();

        var result = await client.PublishPackageAsync(repository, packagePath);
        var versions = await client.GetVersionsAsync(repository, "Company.Tools");
        var downloaded = await client.DownloadPackageAsync(
            repository,
            "Company.Tools",
            "1.0.0",
            download.Path);

        var canonicalPath = Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg");
        Assert.True(result.Published);
        Assert.Equal(canonicalPath, result.PublishSource);
        Assert.True(File.Exists(canonicalPath));
        Assert.False(File.Exists(Path.Combine(destination.Path, Path.GetFileName(packagePath))));
        var listed = Assert.Single(versions);
        Assert.Equal("1.0", listed.Version);
        Assert.Equal(canonicalPath, listed.PackageSource);
        Assert.Equal("1.0", downloaded.Metadata!.Version);
        Assert.Equal(TestHash.ComputeSha256(canonicalPath), TestHash.ComputeSha256(downloaded.PackagePath));
    }

    [Fact]
    public async Task PublishPackageAsync_detects_same_identity_under_noncanonical_filename()
    {
        using var source = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(source.Path, "incoming.nupkg");
        var existingPath = Path.Combine(destination.Path, "legacy-name.nupkg");
        TestPackageFactory.Create(packagePath, "Company.Tools", "1.0.0", files: new Dictionary<string, string>
        {
            ["payload.txt"] = "incoming"
        });
        TestPackageFactory.Create(existingPath, "Company.Tools", "1.0", files: new Dictionary<string, string>
        {
            ["payload.txt"] = "existing"
        });
        var client = new ManagedModuleRepositoryClient(new NullLogger());
        var repository = new ManagedModuleRepository("Local", destination.Path);

        var duplicate = await client.PublishPackageAsync(repository, packagePath);
        var replaced = await client.PublishPackageAsync(repository, packagePath, force: true);

        Assert.False(duplicate.Published);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(existingPath, duplicate.PublishSource);
        Assert.True(replaced.Published);
        Assert.Equal(existingPath, replaced.PublishSource);
        Assert.Equal(TestHash.ComputeSha256(packagePath), TestHash.ComputeSha256(existingPath));
        Assert.Single(Directory.EnumerateFiles(destination.Path, "*.nupkg", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PublishPackageAsync_rejects_ambiguous_existing_local_identity()
    {
        using var source = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(source.Path, "incoming.nupkg");
        TestPackageFactory.Create(packagePath, "Company.Tools", "1.0.0");
        TestPackageFactory.Create(Path.Combine(destination.Path, "first.nupkg"), "Company.Tools", "1.0.0");
        TestPackageFactory.Create(Path.Combine(destination.Path, "second.nupkg"), "Company.Tools", "1.0.0");
        var client = new ManagedModuleRepositoryClient(new NullLogger());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.PublishPackageAsync(
            new ManagedModuleRepository("Local", destination.Path),
            packagePath,
            force: true));

        Assert.Contains("multiple package files", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Company.Tools", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Directory.EnumerateFiles(destination.Path, "*.nupkg", SearchOption.AllDirectories).Count());
    }
}
