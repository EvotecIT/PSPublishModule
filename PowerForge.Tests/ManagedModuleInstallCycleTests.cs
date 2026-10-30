using PowerForge;
using System.Net;

namespace PowerForge.Tests;

public sealed class ManagedModuleInstallCycleTests
{
    [Fact]
    public async Task InstallAsync_parallel_cycle_finishes_without_caller_cancellation()
    {
        using var feed = new TemporaryDirectory();
        CreatePackage(feed.Path, "Company.Root", "Company.A", "Company.B");
        CreatePackage(feed.Path, "Company.A", "Company.B");
        CreatePackage(feed.Path, "Company.B", "Company.A");
        using var client = new HttpClient(new AsyncPackageHandler(feed.Path));
        var repository = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var service = new ManagedModuleInstallService(new NullLogger(), repository);

        // Repeated local installs expose the sibling coalescing race without a network dependency.
        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(async () =>
        {
            using var root = new TemporaryDirectory();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.InstallAsync(CreateRequest(feed.Path, root.Path), deadline.Token)
                    .WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("Managed module dependency cycle detected: Company.A -> Company.B -> Company.A.", exception.Message);
            Assert.False(deadline.IsCancellationRequested);
        })));
    }

    [Theory]
    [InlineData("self", false)]
    [InlineData("two", false)]
    [InlineData("long", false)]
    [InlineData("siblings", false)]
    [InlineData("fanout", false)]
    [InlineData("self", true)]
    [InlineData("two", true)]
    [InlineData("long", true)]
    [InlineData("siblings", true)]
    [InlineData("fanout", true)]
    public async Task InstallAsync_reports_the_directed_cycle_path(string shape, bool saveAsNupkg)
    {
        using var feed = new TemporaryDirectory();
        using var root = new TemporaryDirectory();
        var parallel = shape is "siblings" or "fanout";
        CreatePackage(feed.Path, "Company.Root", parallel ? new[] { "Company.A", "Company.B", "Company.C" } : new[] { "Company.A" });
        CreatePackage(feed.Path, "Company.A", shape == "self" ? new[] { "Company.A" } : shape == "fanout" ? new[] { "Company.B", "Company.C" } : new[] { "Company.B" });
        CreatePackage(feed.Path, "Company.B", shape == "long" ? "Company.C" : "Company.A");
        CreatePackage(feed.Path, "Company.C", shape == "long" ? new[] { "Company.A" } : Array.Empty<string>());
        using var client = new HttpClient(new AsyncPackageHandler(feed.Path));
        var service = new ManagedModuleInstallService(new NullLogger(), new ManagedModuleRepositoryClient(new NullLogger(), client));
        var request = CreateRequest(feed.Path, root.Path);
        request.SaveAsNupkg = saveAsNupkg;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.InstallAsync(request, deadline.Token).WaitAsync(TimeSpan.FromSeconds(10)));
        var path = shape == "self" ? "Company.A -> Company.A" : shape == "long"
            ? "Company.A -> Company.B -> Company.C -> Company.A" : "Company.A -> Company.B -> Company.A";
        Assert.Equal("Managed module dependency cycle detected: " + path + ".", exception.Message);
        Assert.False(deadline.IsCancellationRequested);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "Company.Root", "1.0.0")));
        Assert.False(File.Exists(Path.Combine(root.Path, "Company.Root.1.0.0.nupkg")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallAsync_keeps_acyclic_shared_dependency_coalescing(bool saveAsNupkg)
    {
        using var feed = new TemporaryDirectory();
        using var root = new TemporaryDirectory();
        CreatePackage(feed.Path, "Company.Root", "Company.A", "Company.B");
        CreatePackage(feed.Path, "Company.A", "Company.Shared");
        CreatePackage(feed.Path, "Company.B", "Company.Shared");
        CreatePackage(feed.Path, "Company.Shared");
        var handler = new AsyncPackageHandler(feed.Path);
        using var client = new HttpClient(handler);
        var service = new ManagedModuleInstallService(new NullLogger(), new ManagedModuleRepositoryClient(new NullLogger(), client));
        var request = CreateRequest(feed.Path, root.Path);
        request.SaveAsNupkg = saveAsNupkg;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await service.InstallAsync(request, deadline.Token).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        Assert.Equal(2, result.DependencyResults.Count);
        Assert.All(result.DependencyResults, dependency => Assert.Equal("Company.Shared", Assert.Single(dependency.DependencyResults).Name));
        Assert.Equal(1, handler.Downloads["company.shared.1.0.0.nupkg"]);
        Assert.True(saveAsNupkg ? File.Exists(result.ModulePath) : File.Exists(Path.Combine(result.ModulePath, "Company.Root.psd1")));
    }

    private static ManagedModuleInstallRequest CreateRequest(string feed, string root) => new()
    {
        Repository = new ManagedModuleRepository("Feed", "https://example.test/v3/index.json"),
        Name = "Company.Root",
        Version = "1.0.0",
        Scope = ManagedModuleInstallScope.Custom,
        ModuleRoot = root,
        DependencyConcurrency = 4
    };

    private static void CreatePackage(string feed, string name, params string[] dependencies)
    {
        TestPackageFactory.Create(
            Path.Combine(feed, name + ".1.0.0.nupkg"), name, "1.0.0",
            dependencies: dependencies.Select(id => new TestDependency(id, "[1.0.0]", null)).ToArray(),
            files: new Dictionary<string, string>
            {
                [name + ".psd1"] = "@{ ModuleVersion = '1.0.0' }"
            });
    }
    private sealed class AsyncPackageHandler(string feed) : HttpMessageHandler
    {
        public System.Collections.Concurrent.ConcurrentDictionary<string, int> Downloads { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Exercise real asynchronous package completion without relying on Gallery timing.
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            HttpContent content;
            if (path == "/v3/index.json")
                content = new StringContent("{\"version\":\"3.0.0\",\"resources\":[{\"@id\":\"https://example.test/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}");
            else if (path.EndsWith("/index.json", StringComparison.Ordinal))
                content = new StringContent("{\"versions\":[\"1.0.0\"]}");
            else
            {
                var file = Directory.GetFiles(feed, "*.nupkg").Single(item =>
                    Path.GetFileName(item).Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
                Downloads.AddOrUpdate(Path.GetFileName(path), 1, (_, count) => count + 1);
                content = new ByteArrayContent(File.ReadAllBytes(file));
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }

}
