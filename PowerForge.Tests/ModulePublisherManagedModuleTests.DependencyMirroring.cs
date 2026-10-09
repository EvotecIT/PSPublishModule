using System.Net;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class ModulePublisherManagedModuleTests
{
    [Theory]
    [InlineData(PublishTool.PSResourceGet)]
    [InlineData(PublishTool.ManagedModule)]
    public void Publish_split_feed_mirroring_preserves_repository_authentication_and_push_api_key(PublishTool tool)
    {
        using var root = new TemporaryDirectory();
        using var source = new TemporaryDirectory();
        using var upload = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(source.Path, "Company.Core.1.2.0.nupkg"), "Company.Core", "1.2.0");
        var manifest = WriteDependencyModule(root.Path);
        var publish = DependencyPublish("https://feed.test/virtual/index.json", "https://feed.test/local/index.json", false);
        publish.Tool = tool;
        publish.ApiKey = "push-key";
        publish.Repository!.Credential = new RepositoryCredential { UserName = "publisher", Secret = "read-token" };
        publish.PublishRequiredModules = true;
        publish.RequiredModuleSourceRepository = "Upstream";
        publish.RequiredModuleSourceRepositoryUri = source.Path;
        using var handler = new ProtectedSplitFeedHandler(upload.Path);
        using var client = new HttpClient(handler);
        var nativeUploads = 0;
        var publisher = new ModulePublisher(new NullLogger(), new StubPowerShellRunner(request =>
        {
            var script = File.ReadAllText(request.ScriptPath!);
            if (script.Contains("PFPSRG::REPO::CREATED", StringComparison.Ordinal))
                return new PowerShellRunResult(0, "PFPSRG::REPO::CREATED::0", "", "pwsh");
            Assert.Contains("PFPSRG::PUBLISH::OK", script);
            Assert.Equal("push-key", request.Arguments[3]);
            Assert.Equal("publisher", request.Arguments[7]);
            Assert.Equal("read-token", request.Arguments[8]);
            nativeUploads++;
            return new PowerShellRunResult(0, "PFPSRG::PUBLISH::OK", "", "pwsh");
        }), client);

        var result = publisher.Publish(publish, CreatePlan(root.Path), new ModuleBuildResult(root.Path, manifest,
            new ExportSet([], [], [])), []);

        Assert.True(result.Succeeded);
        Assert.Contains(Directory.GetFiles(upload.Path, "*.nupkg"), path => Path.GetFileName(path).Equals("Company.Core.1.2.0.nupkg", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(tool == PublishTool.PSResourceGet ? 1 : 0, nativeUploads);
        Assert.Equal(tool == PublishTool.ManagedModule ? 2 : 1, handler.Uploads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publish_PSResourceGet_split_feed_keeps_registered_upstream_aliases(bool alreadyAvailable)
    {
        using var root = new TemporaryDirectory();
        using var readFeed = new TemporaryDirectory();
        using var upload = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(readFeed.Path, "Company.Transitive.2.0.0.nupkg"), "Company.Transitive", "2.0.0");
        if (alreadyAvailable)
            TestPackageFactory.Create(Path.Combine(readFeed.Path, "Company.Core.1.2.0.nupkg"), "Company.Core", "1.2.0");
        var manifest = WriteDependencyModule(root.Path);
        var publish = DependencyPublish(readFeed.Path, upload.Path, false);
        publish.Tool = PublishTool.PSResourceGet;
        publish.ApiKey = "push-key";
        publish.PublishRequiredModules = true;
        publish.RequiredModuleSourceRepository = "InternalUpstream";
        var sourceQueries = 0;
        var dependencyUploads = 0;
        var publisher = new ModulePublisher(new NullLogger(), new StubPowerShellRunner(request =>
        {
            var script = File.ReadAllText(request.ScriptPath!);
            if (script.Contains("PFPSRG::REPO::CREATED", StringComparison.Ordinal))
                return new PowerShellRunResult(0, "PFPSRG::REPO::CREATED::0", "", "pwsh");
            if (script.Contains("Find-PSResource", StringComparison.Ordinal))
            {
                var repository = Encoding.UTF8.GetString(Convert.FromBase64String(request.Arguments[2]));
                if (repository.Contains("InternalUpstream", StringComparison.Ordinal))
                {
                    sourceQueries++;
                    return new PowerShellRunResult(0, string.Join("::", new[] { "PFPSRG::ITEM", "Company.Core", "1.2.0", "InternalUpstream", "Test", "Dependency", Guid.Empty.ToString(), "" }
                        .Select((value, index) => index == 0 ? value : Convert.ToBase64String(Encoding.UTF8.GetBytes(value)))), "", "pwsh");
                }
                throw new InvalidOperationException("Dependency queries must use the consumer feed, not the upload repository: " + repository);
            }
            if (script.Contains("Save-PSResource", StringComparison.Ordinal))
            {
                Assert.Equal("InternalUpstream", request.Arguments[2]);
                var savedModule = Directory.CreateDirectory(Path.Combine(request.Arguments[3], "Company.Core", "1.2.0")).FullName;
                File.WriteAllText(Path.Combine(savedModule, "Company.Core.psd1"), "@{ ModuleVersion = '1.2.0'; RootModule = 'Company.Core.psm1' }");
                File.WriteAllText(Path.Combine(savedModule, "Company.Core.psm1"), "");
                var transitiveModule = Directory.CreateDirectory(Path.Combine(request.Arguments[3], "Company.Transitive", "2.0.0")).FullName;
                File.WriteAllText(Path.Combine(transitiveModule, "Company.Transitive.psd1"), "@{ ModuleVersion = '2.0.0'; RootModule = 'Company.Transitive.psm1' }");
                File.WriteAllText(Path.Combine(transitiveModule, "Company.Transitive.psm1"), "");
                var savedItems = string.Join(Environment.NewLine, new[]
                {
                    SavedItem("Company.Core", "1.2.0"),
                    SavedItem("Company.Transitive", "2.0.0")
                });
                return new PowerShellRunResult(0, savedItems, "", "pwsh");
            }
            Assert.Contains("PFPSRG::PUBLISH::OK", script);
            Assert.False(File.Exists(Path.Combine(request.Arguments[0], "Company.Transitive.psd1")), "A transitive dependency available through the consumer feed must not be uploaded again.");
            if (File.Exists(Path.Combine(request.Arguments[0], "Company.Core.psd1")))
            {
                dependencyUploads++;
                TestPackageFactory.Create(Path.Combine(readFeed.Path, "Company.Core.1.2.0.nupkg"), "Company.Core", "1.2.0");
            }
            return new PowerShellRunResult(0, "PFPSRG::PUBLISH::OK", "", "pwsh");
        }));

        var result = publisher.Publish(publish, CreatePlan(root.Path), new ModuleBuildResult(root.Path, manifest,
            new ExportSet([], [], [])), []);

        Assert.True(result.Succeeded);
        Assert.Equal(alreadyAvailable ? 0 : 1, sourceQueries);
        Assert.Equal(alreadyAvailable ? 0 : 1, dependencyUploads);

        static string SavedItem(string name, string version)
            => "PFPSRG::SAVE::ITEM::" + Convert.ToBase64String(Encoding.UTF8.GetBytes(name)) + "::" +
               Convert.ToBase64String(Encoding.UTF8.GetBytes(version));
    }

    private sealed class ProtectedSplitFeedHandler(string upload) : HttpMessageHandler
    {
        internal int Uploads { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes("publisher:read-token")), request.Headers.Authorization?.Parameter);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/virtual/index.json")
                return Json("{\"version\":\"3.0.0\",\"resources\":[{\"@id\":\"https://feed.test/flat/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}");
            if (path == "/local/index.json")
                return Json("{\"version\":\"3.0.0\",\"resources\":[{\"@id\":\"https://feed.test/upload/\",\"@type\":\"PackagePublish/2.0.0\"}]}");
            if (path == "/upload/")
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal("push-key", Assert.Single(request.Headers.GetValues("X-NuGet-ApiKey")));
                var package = Assert.Single(Assert.IsType<MultipartFormDataContent>(request.Content));
                var packageName = package.Headers.ContentDisposition!.FileName!.Trim('"');
                await File.WriteAllBytesAsync(Path.Combine(upload, packageName), await package.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
                Uploads++;
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            Assert.StartsWith("/flat/", path);
            var packageId = path.Split('/')[2];
            var version = packageId.Equals("company.core", StringComparison.Ordinal) ? "1.2.0" : "3.0.13";
            var packageExists = Directory.EnumerateFiles(upload, "*.nupkg").Any(file => Path.GetFileName(file).Equals(packageId + "." + version + ".nupkg", StringComparison.OrdinalIgnoreCase));
            return packageExists ? Json("{\"versions\":[\"" + version + "\"]}") : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    }
}
