using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModulePublishServiceAliasTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_existing_package_reuses_aliased_output_before_remote_publish(bool force)
    {
        using var root = new TemporaryDirectory();
        var sourceDirectory = Path.Combine(root.Path, "packages");
        var outputDirectory = Path.Combine(root.Path, "output");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(outputDirectory);
        var packagePath = Path.Combine(sourceDirectory, "Company.Tools.1.0.0.nupkg");
        var destinationPath = Path.Combine(outputDirectory, Path.GetFileName(packagePath));
        TestPackageFactory.Create(packagePath, "Company.Tools", "1.0.0");
        TestFileLink.CreateHardLink(destinationPath, packagePath);
        var requests = new List<ManagedModuleRepositoryClientTests.RecordedRequest>();
        using var httpClient = new HttpClient(new ManagedModuleRepositoryClientTests.ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), httpClient);
        var service = new ManagedModulePublishService(new NullLogger(), repositoryClient);

        var result = await service.PublishAsync(new ManagedModulePublishRequest
        {
            PackagePath = packagePath,
            Repository = new ManagedModuleRepository("Remote", "https://example.test/v3/index.json"),
            OutputDirectory = outputDirectory,
            SkipDependenciesCheck = true,
            Force = force
        });

        Assert.True(result.Published);
        Assert.False(result.Duplicate);
        Assert.Equal(destinationPath, result.PackagePath);
        Assert.Equal(File.ReadAllBytes(packagePath), File.ReadAllBytes(destinationPath));
        Assert.Contains(requests, request => request.Url == "https://example.test/publish/" && request.Method == HttpMethod.Put);
    }
}
