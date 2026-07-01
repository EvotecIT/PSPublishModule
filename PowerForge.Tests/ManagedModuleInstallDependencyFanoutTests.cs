using PowerForge;
using System.Net;

namespace PowerForge.Tests;

public sealed class ManagedModuleInstallDependencyFanoutTests
{
    [Fact]
    public void ShouldStartDependencyInstallBeforeExtraction_RequiresFastSafeInstallPath()
    {
        var metadata = new ManagedModulePackageMetadata
        {
            Dependencies = new[] { new ManagedModuleDependencyInfo { Id = "Company.Core", VersionRange = "[1.0.0]" } }
        };
        var request = new ManagedModuleInstallRequest
        {
            AllowClobber = true
        };

        Assert.True(InvokeShouldStartDependencyInstallBeforeExtraction(request, metadata));

        request.AllowClobber = false;
        Assert.False(InvokeShouldStartDependencyInstallBeforeExtraction(request, metadata));

        request.AllowClobber = true;
        request.AuthenticodeCheck = true;
        Assert.False(InvokeShouldStartDependencyInstallBeforeExtraction(request, metadata));

        request.AuthenticodeCheck = false;
        request.SkipDependencyCheck = true;
        Assert.False(InvokeShouldStartDependencyInstallBeforeExtraction(request, metadata));

        request.SkipDependencyCheck = false;
        Assert.False(InvokeShouldStartDependencyInstallBeforeExtraction(request, new ManagedModulePackageMetadata()));
    }

    [Fact]
    public async Task InstallAsync_seeds_first_dependency_before_broad_parallel_fanout()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var featureNames = Enumerable.Range(1, 33)
            .Select(static index => "Company.Feature" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.0.0.nupkg"),
            "Company.Core",
            "1.0.0",
            files: CreateModuleFiles("Company.Core", "1.0.0"));

        foreach (var featureName in featureNames)
        {
            TestPackageFactory.Create(
                Path.Combine(feed.Path, featureName + ".1.0.0.nupkg"),
                featureName,
                "1.0.0",
                dependencies: new[] { new TestDependency("Company.Core", "[1.0.0]", null) },
                files: CreateModuleFiles(featureName, "1.0.0"));
        }

        var rootDependencies = new[] { new TestDependency("Company.Core", "[1.0.0]", null) }
            .Concat(featureNames.Select(static featureName => new TestDependency(featureName, "[1.0.0]", null)))
            .ToArray();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Root.1.0.0.nupkg"),
            "Company.Root",
            "1.0.0",
            dependencies: rootDependencies,
            files: CreateModuleFiles("Company.Root", "1.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Root",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal("Company.Core", result.DependencyResults[0].Name);
        Assert.Equal(ManagedModuleInstallStatus.Installed, result.DependencyResults[0].Status);
        var nestedCoreResults = result.DependencyResults
            .Skip(1)
            .SelectMany(static dependency => dependency.DependencyResults)
            .Where(static dependency => dependency.Name == "Company.Core")
            .ToArray();
        Assert.Equal(featureNames.Length, nestedCoreResults.Length);
        Assert.All(nestedCoreResults, dependency =>
        {
            Assert.Equal(ManagedModuleInstallStatus.AlreadyInstalled, dependency.Status);
            Assert.Equal(TimeSpan.Zero, dependency.CoalescedWaitElapsed);
        });
    }

    [Fact]
    public async Task InstallAsync_prewarms_dependency_versions_from_repository_metadata_before_root_download()
    {
        using var moduleRoot = new TemporaryDirectory();
        var handler = new RepositoryDependencyHintHandler();
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), new HttpClient(handler));
        var service = new ManagedModuleInstallService(new NullLogger(), repositoryClient);

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Feed", "https://example.test/api/v2"),
            Name = "Company.Root",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            AllowClobber = true
        });

        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        Assert.True(handler.CoreLatestRequestedBeforeRootDownload);
        Assert.Contains(handler.Requests, static request => request.EndsWith("id='Company.Core'&$filter=IsLatestVersion&$top=1&semVerLevel=2.0.0", StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string moduleName, string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [moduleName + ".psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static bool InvokeShouldStartDependencyInstallBeforeExtraction(
        ManagedModuleInstallRequest request,
        ManagedModulePackageMetadata metadata)
    {
        var method = typeof(ManagedModuleInstallService).GetMethod(
            "ShouldStartDependencyInstallBeforeExtraction",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        return (bool)method!.Invoke(null, new object?[] { request, metadata })!;
    }

    private sealed class RepositoryDependencyHintHandler : HttpMessageHandler
    {
        private readonly object _syncRoot = new();
        private readonly List<string> _requests = new();

        public bool CoreLatestRequestedBeforeRootDownload { get; private set; }

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_syncRoot)
                {
                    return _requests.ToArray();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            lock (_syncRoot)
            {
                if (url.Equals("https://example.test/api/v2/package/Company.Root/1.0.0", StringComparison.OrdinalIgnoreCase))
                    CoreLatestRequestedBeforeRootDownload = _requests.Any(static item => item.Contains("id='Company.Core'&$filter=IsLatestVersion", StringComparison.Ordinal));

                _requests.Add(url);
            }

            if (url.Equals("https://example.test/api/v2/FindPackagesById()?id='Company.Root'&$filter=IsLatestVersion&$top=1&semVerLevel=2.0.0", StringComparison.OrdinalIgnoreCase))
            {
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
                    "<entry><content><m:properties><d:Id>Company.Root</d:Id><d:Version>1.0.0</d:Version><d:Dependencies>Company.Core:[1.0.0, ):</d:Dependencies></m:properties></content></entry>" +
                    "</feed>");
            }

            if (url.Equals("https://example.test/api/v2/FindPackagesById()?id='Company.Core'&$filter=IsLatestVersion&$top=1&semVerLevel=2.0.0", StringComparison.OrdinalIgnoreCase))
            {
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
                    "<entry><content><m:properties><d:Id>Company.Core</d:Id><d:Version>1.0.0</d:Version></m:properties></content></entry>" +
                    "</feed>");
            }

            if (url.Equals("https://example.test/api/v2/package/Company.Root/1.0.0", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = TestPackageFactory.CreateBytes(
                    "Company.Root",
                    "1.0.0",
                    files: CreateModuleFiles("Company.Root", "1.0.0"));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                });
            }

            if (url.Equals("https://example.test/api/v2/package/Company.Core/1.0.0", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = TestPackageFactory.CreateBytes(
                    "Company.Core",
                    "1.0.0",
                    files: CreateModuleFiles("Company.Core", "1.0.0"));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Xml(string xml)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, System.Text.Encoding.UTF8, "application/xml")
            });
    }
}
