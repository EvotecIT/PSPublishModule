using System.Net;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleDependencyInstallServiceTests
{
    [Fact]
    public async Task InstallAsync_uses_installed_dependency_when_it_satisfies_declared_range()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var existingDependencyPath = Path.Combine(moduleRoot.Path, "Company.Core", "1.1.0");
        Directory.CreateDirectory(existingDependencyPath);
        File.WriteAllText(Path.Combine(existingDependencyPath, "Company.Core.psd1"), "@{ ModuleVersion = '1.1.0' }");
        File.WriteAllText(Path.Combine(existingDependencyPath, "marker.txt"), "keep");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0,2.0.0)", null) },
            files: CreateToolFiles("1.0.0"));
        var service = new ManagedModuleInstallService(new NullLogger());

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        var dependency = Assert.Single(result.DependencyResults);
        Assert.Equal(ManagedModuleInstallStatus.AlreadyInstalled, dependency.Status);
        Assert.Equal("Company.Core", dependency.Name);
        Assert.Equal("1.1.0", dependency.Version);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(existingDependencyPath, "marker.txt")));
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task InstallAsync_skips_dependency_version_query_for_satisfied_dependency_when_only_repository_trust_is_required()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var handler = new SatisfiedDependencyTrustFeedHandler();
        using var httpClient = new HttpClient(handler);
        var existingDependencyPath = Path.Combine(moduleRoot.Path, "Company.Core", "1.1.0");
        Directory.CreateDirectory(existingDependencyPath);
        File.WriteAllText(Path.Combine(existingDependencyPath, "Company.Core.psd1"), "@{ ModuleVersion = '1.1.0' }");
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), httpClient);
        var service = new ManagedModuleInstallService(new NullLogger(), repositoryClient);

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository(
                "Gallery",
                "https://example.test/v3/index.json",
                ManagedModuleRepositoryKind.NuGetV3,
                trusted: true),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            TrustPolicy = new ManagedModuleTrustPolicy
            {
                RequireTrustedRepository = true
            }
        });

        var dependency = Assert.Single(result.DependencyResults);
        Assert.Equal(ManagedModuleInstallStatus.AlreadyInstalled, dependency.Status);
        Assert.Equal("Company.Core", dependency.Name);
        Assert.Equal("1.1.0", dependency.Version);
        Assert.Equal(0, handler.DependencyVersionQueryCount);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0", "Company.Tools.psd1")));
    }

    [Fact]
    public async Task InstallAsync_validates_dependency_package_when_dependency_trust_policy_is_active()
    {
        using var feed = new TemporaryDirectory();
        using var moduleRoot = new TemporaryDirectory();
        var existingDependencyPath = Path.Combine(moduleRoot.Path, "Company.Core", "1.0.0");
        Directory.CreateDirectory(existingDependencyPath);
        File.WriteAllText(Path.Combine(existingDependencyPath, "Company.Core.psd1"), "@{ ModuleVersion = '1.0.0' }");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Core.1.1.0.nupkg"),
            "Company.Core",
            "1.1.0",
            files: CreateCoreFiles("1.1.0"),
            authors: "OtherPublisher");
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            dependencies: new[] { new TestDependency("Company.Core", "[1.0.0,2.0.0)", null) },
            files: CreateToolFiles("1.0.0"),
            authors: "Evotec");
        var service = new ManagedModuleInstallService(new NullLogger());

        var exception = await Assert.ThrowsAsync<ManagedModuleTrustException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Local", feed.Path),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            TrustPolicy = new ManagedModuleTrustPolicy
            {
                AllowedAuthors = new[] { "Evotec" }
            }
        }));

        Assert.Equal("Company.Core", exception.ModuleName);
        Assert.Equal("PackageAuthorNotAllowed", exception.Reason);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.0.0")));
    }

    [Fact]
    public async Task InstallAsync_keeps_parallel_dependency_request_counts_scoped()
    {
        using var moduleRoot = new TemporaryDirectory();
        using var handler = new ParallelDependencyFeedHandler();
        using var httpClient = new HttpClient(handler);
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), httpClient);
        var service = new ManagedModuleInstallService(new NullLogger(), repositoryClient);

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json"),
            Name = "Company.Tools",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal(6, result.RepositoryRequestCount);
        Assert.Equal(2, result.PackageRepositoryRequestCount);
        Assert.Equal(6, repositoryClient.RequestCount);
        var dependencies = result.DependencyResults.OrderBy(dependency => dependency.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(new[] { "Company.CoreA", "Company.CoreB" }, dependencies.Select(dependency => dependency.Name));
        Assert.All(dependencies, dependency => Assert.Equal(1, dependency.RepositoryRequestCount));
        Assert.All(dependencies, dependency => Assert.Equal(1, dependency.PackageRepositoryRequestCount));
    }

    private static IReadOnlyDictionary<string, string> CreateToolFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static IReadOnlyDictionary<string, string> CreateCoreFiles(string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Company.Core.psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private sealed class SatisfiedDependencyTrustFeedHandler : HttpMessageHandler
    {
        private int _dependencyVersionQueryCount;

        public int DependencyVersionQueryCount => _dependencyVersionQueryCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (uri == "https://example.test/v3/index.json")
                return Task.FromResult(Json("{\"resources\":[{\"@id\":\"https://example.test/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}"));

            if (uri == "https://example.test/packages/company.tools/1.0.0/company.tools.1.0.0.nupkg")
                return Task.FromResult(Package("Company.Tools", "1.0.0", new[] { new TestDependency("Company.Core", "[1.0.0,2.0.0)", null) }));

            if (uri == "https://example.test/packages/company.core/index.json")
            {
                System.Threading.Interlocked.Increment(ref _dependencyVersionQueryCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string json)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };

        private static HttpResponseMessage Package(string id, string version, IReadOnlyList<TestDependency>? dependencies)
            => new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(CreatePackageBytes(id, version, dependencies))
            };

        private static byte[] CreatePackageBytes(string id, string version, IReadOnlyList<TestDependency>? dependencies)
        {
            using var directory = new TemporaryDirectory();
            var packagePath = Path.Combine(directory.Path, id + "." + version + ".nupkg");
            TestPackageFactory.Create(
                packagePath,
                id,
                version,
                dependencies,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [id + ".psd1"] = "@{ ModuleVersion = '" + version + "' }"
                });
            return File.ReadAllBytes(packagePath);
        }
    }

    private sealed class ParallelDependencyFeedHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<object?> _firstDependencySeen = CreateCompletionSource();
        private readonly TaskCompletionSource<object?> _releaseDependencies = CreateCompletionSource();
        private int _dependencyRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (uri == "https://example.test/v3/index.json")
            {
                return Json("{\"resources\":[" +
                            "{\"@id\":\"https://example.test/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}" +
                            "]}");
            }

            if (uri == "https://example.test/packages/company.tools/1.0.0/company.tools.1.0.0.nupkg")
            {
                return Package("Company.Tools", "1.0.0", new[]
                {
                    new TestDependency("Company.CoreA", "1.0.0", null),
                    new TestDependency("Company.CoreB", "1.0.0", null)
                });
            }

            if (uri is "https://example.test/packages/company.corea/index.json" or
                "https://example.test/packages/company.coreb/index.json")
            {
                return Json("{\"versions\":[\"1.0.0\"]}");
            }

            if (uri is "https://example.test/packages/company.corea/1.0.0/company.corea.1.0.0.nupkg" or
                "https://example.test/packages/company.coreb/1.0.0/company.coreb.1.0.0.nupkg")
            {
                if (System.Threading.Interlocked.Increment(ref _dependencyRequests) == 1)
                {
                    _firstDependencySeen.TrySetResult(null);
                    await WaitWithCancellationAsync(_releaseDependencies.Task, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WaitWithCancellationAsync(_firstDependencySeen.Task, cancellationToken).ConfigureAwait(false);
                    _releaseDependencies.TrySetResult(null);
                }

                var name = uri.Contains("company.corea", StringComparison.OrdinalIgnoreCase)
                    ? "Company.CoreA"
                    : "Company.CoreB";
                return Package(name, "1.0.0", dependencies: null);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _firstDependencySeen.TrySetResult(null);
                _releaseDependencies.TrySetResult(null);
            }

            base.Dispose(disposing);
        }

        private static TaskCompletionSource<object?> CreateCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static async Task WaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
        {
            var cancellation = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(static state =>
                ((TaskCompletionSource<object?>)state!).TrySetCanceled(), cancellation);
            var completed = await Task.WhenAny(task, cancellation.Task).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
        }

        private static HttpResponseMessage Json(string json)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };

        private static HttpResponseMessage Package(string id, string version, IReadOnlyList<TestDependency>? dependencies)
            => new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(CreatePackageBytes(id, version, dependencies))
            };

        private static byte[] CreatePackageBytes(string id, string version, IReadOnlyList<TestDependency>? dependencies)
        {
            using var directory = new TemporaryDirectory();
            var packagePath = Path.Combine(directory.Path, id + "." + version + ".nupkg");
            TestPackageFactory.Create(
                packagePath,
                id,
                version,
                dependencies,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [id + ".psd1"] = "@{ ModuleVersion = '" + version + "' }"
                });
            return File.ReadAllBytes(packagePath);
        }
    }
}
