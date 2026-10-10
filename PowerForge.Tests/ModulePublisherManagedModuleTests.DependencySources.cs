using System.IO.Compression;
using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed partial class ModulePublisherManagedModuleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publish_preserves_dependencies_when_read_and_publish_feeds_differ(bool skipDependenciesCheck)
    {
        using var root = new TemporaryDirectory();
        using var readFeed = new TemporaryDirectory();
        using var publishFeed = new TemporaryDirectory();
        if (!skipDependenciesCheck)
            TestPackageFactory.Create(Path.Combine(readFeed.Path, "Company.Core.1.2.0.nupkg"), "Company.Core", "1.2.0");
        var manifest = WriteDependencyModule(root.Path);
        var publish = DependencyPublish(readFeed.Path, publishFeed.Path, skipDependenciesCheck);
        // Exercise deferred-secret cloning as well as the ordinary configuration path.
        File.WriteAllText(Path.Combine(root.Path, "publish.key"), "synthetic-api-key");
        publish.ApiKeyFilePath = "publish.key";
        var publisher = new ModulePublisher(new NullLogger(), new StubPowerShellRunner(_ => throw new InvalidOperationException("Managed publishing does not invoke PowerShell.")));

        var result = publisher.Publish(publish, CreatePlan(root.Path), new ModuleBuildResult(root.Path, manifest,
            new ExportSet([], [], [])), []);

        Assert.True(result.Succeeded);
        var packagePath = Path.Combine(publishFeed.Path, "PSPublishModule.3.0.13.nupkg");
        using var package = ZipFile.OpenRead(packagePath);
        using var nuspecStream = package.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
        var dependency = XDocument.Load(nuspecStream).Descendants().Single(element => element.Name.LocalName == "dependency");
        Assert.Equal("Company.Core", dependency.Attribute("id")?.Value);
        Assert.Equal("[1.2.0]", dependency.Attribute("version")?.Value);
        using var manifestReader = new StreamReader(package.GetEntry("PSPublishModule.psd1")!.Open());
        Assert.Contains("RequiredVersion = '1.2.0'", manifestReader.ReadToEnd());
        Assert.False(File.Exists(Path.Combine(publishFeed.Path, "Company.Core.1.2.0.nupkg")));
    }

    [Fact]
    public void Publish_strict_validation_rejects_incompatible_dependency_in_read_feed()
    {
        using var root = new TemporaryDirectory();
        using var readFeed = new TemporaryDirectory();
        using var publishFeed = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(readFeed.Path, "Company.Core.2.0.0.nupkg"), "Company.Core", "2.0.0");
        var manifest = WriteDependencyModule(root.Path);
        var publisher = new ModulePublisher(new NullLogger());

        var exception = Assert.Throws<InvalidOperationException>(() => publisher.Publish(
            DependencyPublish(readFeed.Path, publishFeed.Path, false), CreatePlan(root.Path),
            new ModuleBuildResult(root.Path, manifest, new ExportSet([], [], [])), []));

        Assert.Contains("Company.Core", exception.Message);
        Assert.Contains("SkipDependenciesCheck", exception.Message);
        Assert.Empty(Directory.GetFiles(publishFeed.Path, "*.nupkg"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publish_PSResourceGet_uses_read_feed_for_validation_and_publish_feed_for_upload(bool skipDependenciesCheck)
    {
        using var root = new TemporaryDirectory();
        using var readFeed = new TemporaryDirectory();
        using var publishFeed = new TemporaryDirectory();
        if (!skipDependenciesCheck)
            TestPackageFactory.Create(Path.Combine(readFeed.Path, "Company.Core.1.2.0.nupkg"), "Company.Core", "1.2.0");
        var manifest = WriteDependencyModule(root.Path);
        var publish = DependencyPublish(readFeed.Path, publishFeed.Path, skipDependenciesCheck);
        publish.Tool = PublishTool.PSResourceGet;
        publish.ApiKey = "synthetic-api-key";
        var uploads = 0;
        var registrations = 0;
        var publisher = new ModulePublisher(new NullLogger(), new StubPowerShellRunner(request =>
        {
            var script = File.ReadAllText(request.ScriptPath!);
            if (script.Contains("PFPSRG::REPO::CREATED", StringComparison.Ordinal))
            {
                Assert.Equal(publishFeed.Path, request.Arguments[1]);
                registrations++;
                return new PowerShellRunResult(0, "PFPSRG::REPO::CREATED::0\nPFPSRG::REPO::NAME::" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("ExistingUpload")), "", "pwsh");
            }
            if (script.Contains("PFPSRG::PUBLISH::OK", StringComparison.Ordinal))
            {
                Assert.Equal("ExistingUpload", request.Arguments[2]);
                Assert.Equal("1", request.Arguments[5]);
                Assert.Equal("0", request.Arguments[6]);
                Assert.Contains("RequiredVersion = '1.2.0'", File.ReadAllText(Path.Combine(request.Arguments[0], "PSPublishModule.psd1")));
                uploads++;
                return new PowerShellRunResult(0, "PFPSRG::PUBLISH::OK", "", "pwsh");
            }
            throw new InvalidOperationException("Dependency/version lookup must use the explicit read feed, without repository aliases.");
        }));

        var result = publisher.Publish(publish, CreatePlan(root.Path), new ModuleBuildResult(root.Path, manifest,
            new ExportSet([], [], [])), []);

        Assert.True(result.Succeeded);
        Assert.Equal(1, registrations);
        Assert.Equal(1, uploads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Publish_PSResourceGet_distinguishes_missing_dependencies_from_lookup_failures(bool notFound)
    {
        using var root = new TemporaryDirectory();
        var manifest = WriteDependencyModule(root.Path);
        var publish = DependencyPublish(root.Path, root.Path, false);
        publish.Tool = PublishTool.PSResourceGet;
        publish.ApiKey = "synthetic-api-key";
        publish.Force = true;
        publish.Repository!.EnsureRegistered = false;
        var publisher = new ModulePublisher(new NullLogger(), new StubPowerShellRunner(request =>
        {
            Assert.Contains("Find-PSResource", File.ReadAllText(request.ScriptPath!));
            return new PowerShellRunResult(1, "", notFound
                ? "Package with name 'Company.Core' could not be found in repository 'Local'."
                : "401 Unauthorized reading Company.Core", "pwsh");
        }));

        var error = Assert.Throws<InvalidOperationException>(() => publisher.Publish(publish, CreatePlan(root.Path),
            new ModuleBuildResult(root.Path, manifest, new ExportSet([], [], [])), []));

        if (notFound)
        {
            Assert.Contains("Missing or incompatible: Company.Core", error.Message);
            Assert.Contains("RepositorySourceUri", error.Message);
            Assert.Contains("SkipDependenciesCheck", error.Message);
        }
        else
        {
            Assert.Contains("401 Unauthorized", error.Message);
            Assert.DoesNotContain("Missing or incompatible", error.Message);
        }
    }

    [Theory]
    [InlineData(PublishTool.ManagedModule)]
    [InlineData(PublishTool.PSResourceGet)]
    public void ValidateVersionForPublish_checks_consumer_feed_when_upload_feed_is_empty(PublishTool tool)
    {
        using var root = new TemporaryDirectory();
        using var readFeed = new TemporaryDirectory();
        using var publishFeed = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(readFeed.Path, "PSPublishModule.3.0.13.nupkg"), "PSPublishModule", "3.0.13");
        var publish = DependencyPublish(readFeed.Path, publishFeed.Path, false);
        publish.Tool = tool;
        var publisher = new ModulePublisher(new NullLogger(), new StubPowerShellRunner(_ => throw new Exception("Version lookup must use the consumer feed.")));

        var result = publisher.ValidateVersionForPublish(publish, CreatePlan(root.Path), allowExistingExactVersion: true);

        Assert.Equal(ModulePublishVersionPreflightResult.AlreadyPublished, result);
    }

    [Theory]
    [InlineData(PublishTool.ManagedModule, true)]
    [InlineData(PublishTool.PSResourceGet, true)]
    [InlineData(PublishTool.PowerShellGet, false)]
    public void Publish_rejects_conflicting_or_unsupported_skip_before_repository_operations(PublishTool tool, bool mirror)
    {
        using var root = new TemporaryDirectory();
        var manifest = WriteDependencyModule(root.Path);
        var publish = DependencyPublish(root.Path, root.Path, true);
        publish.Tool = tool;
        publish.PublishRequiredModules = mirror;
        var publisher = new ModulePublisher(new NullLogger(), new StubPowerShellRunner(_ => throw new Exception("Repository operation occurred before policy validation.")));

        var exception = Assert.ThrowsAny<Exception>(() => publisher.Publish(publish, CreatePlan(root.Path),
            new ModuleBuildResult(root.Path, manifest, new ExportSet([], [], [])), []));

        Assert.Contains("SkipDependenciesCheck", exception.Message);
    }

    private static string WriteDependencyModule(string path)
    {
        var manifest = Path.Combine(path, "PSPublishModule.psd1");
        File.WriteAllText(manifest, "@{ ModuleVersion = '3.0.13'; RootModule = 'PSPublishModule.psm1'; Author = 'Evotec'; Description = 'Dependency source test'; RequiredModules = @(@{ ModuleName = 'Company.Core'; RequiredVersion = '1.2.0' }) }");
        File.WriteAllText(Path.Combine(path, "PSPublishModule.psm1"), "");
        return manifest;
    }

    private static PublishConfiguration DependencyPublish(string read, string publish, bool skip)
        => new()
        {
            Enabled = true,
            Tool = PublishTool.ManagedModule,
            RepositoryName = "Local",
            SkipDependenciesCheck = skip,
            Repository = new PublishRepositoryConfiguration
            {
                Name = "Local",
                Uri = publish,
                SourceUri = read,
                PublishUri = publish
            }
        };
}
