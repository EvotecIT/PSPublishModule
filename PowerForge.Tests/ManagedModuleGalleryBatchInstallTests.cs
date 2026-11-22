using System.Net;
using System.Security.Cryptography;
using System.Text;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleGalleryBatchInstallTests
{
    private const string First = "Company.First";
    private const string Second = "Company.Second";

    [Theory]
    [InlineData("complete", false, 0)]
    [InlineData("complete", true, 0)]
    [InlineData("partial", false, 1)]
    [InlineData("bad-version", false, 1)]
    [InlineData("stable-preview", false, 1)]
    [InlineData("http-error", false, 2)]
    [InlineData("malformed", false, 2)]
    [InlineData("unexpected-id", false, 2)]
    public async Task Batch_install_streams_in_order_and_uses_individual_lookup_for_unusable_metadata(
        string response, bool prerelease, int individualLookups)
    {
        using var root = new TemporaryDirectory();
        using var handler = new GalleryHandler(response, prerelease);
        using var http = new HttpClient(handler);
        var service = CreateService(http);
        var installed = new List<ManagedModuleInstallResult>();

        await service.InstallBatchAsync(CreateRequests(root.Path, prerelease), installed.Add, CancellationToken.None);

        Assert.Equal(new[] { First, Second }, installed.Select(result => result.Name));
        Assert.All(installed, result =>
        {
            Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
            Assert.Equal("1.0.0", result.Version);
            Assert.True(File.Exists(Path.Combine(result.ModulePath!, result.Name + ".psd1")));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(handler.Packages[result.Name])),
                result.Download!.PackageSha256, ignoreCase: true);
        });
        Assert.Equal(1, handler.BatchCalls);
        Assert.Equal(individualLookups, handler.IndividualLookups.Count);
        if (individualLookups == 1)
            Assert.Equal(Second, Assert.Single(handler.IndividualLookups));
        Assert.Equal(1 + individualLookups + 2, installed.Sum(result => result.RepositoryRequestCount));
    }

    [Fact]
    public async Task Batch_install_checks_next_approval_after_reporting_previous_install()
    {
        using var root = new TemporaryDirectory();
        using var handler = new GalleryHandler("complete", prerelease: false);
        using var http = new HttpClient(handler);
        var installed = new List<ManagedModuleInstallResult>();

        await CreateService(http).InstallBatchAsync(CreateRequests(root.Path, prerelease: false), installed.Add,
            CancellationToken.None, request =>
            {
                if (request.Name == First)
                    return true;

                Assert.Equal(First, Assert.Single(installed).Name);
                return false;
            });

        Assert.Equal(First, Assert.Single(installed).Name);
        Assert.Equal(First, Assert.Single(handler.Downloads));
        Assert.False(Directory.Exists(Path.Combine(root.Path, Second)));
    }

    [Fact]
    public async Task Batch_install_preserves_a_higher_installed_version()
    {
        using var root = new TemporaryDirectory();
        var existing = Path.Combine(root.Path, First, "2.0.0");
        Directory.CreateDirectory(existing);
        await File.WriteAllTextAsync(Path.Combine(existing, First + ".psd1"), "@{ ModuleVersion = '2.0.0' }");
        using var handler = new GalleryHandler("complete", prerelease: false);
        using var http = new HttpClient(handler);
        var installed = new List<ManagedModuleInstallResult>();

        await CreateService(http).InstallBatchAsync(CreateRequests(root.Path, prerelease: false), installed.Add, CancellationToken.None);

        Assert.Equal(ManagedModuleInstallStatus.AlreadyInstalled, installed[0].Status);
        Assert.Equal("2.0.0", installed[0].Version);
        Assert.Equal(ManagedModuleInstallStatus.Installed, installed[1].Status);
        Assert.Equal(1, handler.BatchCalls);
        Assert.Empty(handler.IndividualLookups);
        Assert.Equal(new[] { Second }, handler.Downloads);
    }

    [Fact]
    public async Task Batch_install_cancellation_does_not_launch_individual_lookups()
    {
        using var root = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        using var handler = new GalleryHandler("complete", prerelease: false, onBatch: cancellation.Cancel);
        using var http = new HttpClient(handler);
        var installed = new List<ManagedModuleInstallResult>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(http).InstallBatchAsync(CreateRequests(root.Path, prerelease: false), installed.Add, cancellation.Token));

        Assert.Equal(1, handler.BatchCalls);
        Assert.Empty(handler.IndividualLookups);
        Assert.Empty(handler.Downloads);
        Assert.Empty(installed);
    }

    private static ManagedModuleInstallService CreateService(HttpClient http)
        => new(new NullLogger(), new ManagedModuleRepositoryClient(new NullLogger(), http,
            options: new ManagedModuleRepositoryClientOptions { DisableManagedModuleCatalog = true }));

    private static ManagedModuleInstallRequest[] CreateRequests(string root, bool prerelease)
    {
        var repository = new ManagedModuleRepository("PSGallery", ManagedModuleRestoreService.DefaultSource);
        return new[] { First, Second }.Select(name => new ManagedModuleInstallRequest
        {
            Name = name,
            Repository = repository,
            IncludePrerelease = prerelease,
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = root,
            AllowClobber = true
        }).ToArray();
    }

    private sealed class GalleryHandler(string response, bool prerelease, Action? onBatch = null) : HttpMessageHandler
    {
        private readonly string _flag = prerelease ? "IsAbsoluteLatestVersion" : "IsLatestVersion";

        public int BatchCalls { get; private set; }
        public List<string> IndividualLookups { get; } = new();
        public List<string> Downloads { get; } = new();
        public Dictionary<string, byte[]> Packages { get; } = new[] { First, Second }.ToDictionary(name => name, name =>
            TestPackageFactory.CreateBytes(name, "1.0.0", files: new Dictionary<string, string>
            {
                [name + ".psd1"] = "@{ ModuleVersion = '1.0.0' }"
            }));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "www.powershellgallery.com" && uri.AbsolutePath == "/api/v2/Packages()")
            {
                var query = Uri.UnescapeDataString(uri.Query);
                if (query == $"?$filter=(Id eq '{First}' or Id eq '{Second}') and {_flag}&$top=2&semVerLevel=2.0.0")
                {
                    BatchCalls++;
                    onBatch?.Invoke();
                    cancellationToken.ThrowIfCancellationRequested();
                    return Task.FromResult(response switch
                    {
                        "complete" => Feed(Second, First),
                        "partial" => Feed(First),
                        "bad-version" => Feed(new[] { First, Second }, "1..0"),
                        "stable-preview" => Feed(new[] { First, Second }, "1.0.0-preview"),
                        "http-error" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                        "malformed" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<feed") },
                        "unexpected-id" => Feed("Company.Unknown", First),
                        _ => throw new InvalidOperationException(response)
                    });
                }

                foreach (var name in Packages.Keys)
                {
                    if (query == $"?$filter=Id eq '{name}' and {_flag}&$top=1&semVerLevel=2.0.0")
                    {
                        IndividualLookups.Add(name);
                        return Task.FromResult(Feed(name));
                    }
                }
            }

            if (uri.Host == "cdn.powershellgallery.com")
            {
                foreach (var package in Packages)
                {
                    if (uri.AbsolutePath.Equals($"/packages/{package.Key}.1.0.0.nupkg", StringComparison.OrdinalIgnoreCase))
                    {
                        Downloads.Add(package.Key);
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package.Value) });
                    }
                }
            }

            throw new InvalidOperationException($"Unexpected gallery request {uri}");
        }

        private HttpResponseMessage Feed(params string[] ids) => Feed(ids, "1.0.0");

        private HttpResponseMessage Feed(string[] ids, string secondVersion)
        {
            var entries = string.Concat(ids.Select(id =>
                $"<entry><content><m:properties><d:Id>{id}</d:Id><d:Version>{(id == Second ? secondVersion : "1.0.0")}</d:Version>" +
                $"<d:{_flag}>true</d:{_flag}></m:properties></content></entry>"));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" " +
                    $"xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">{entries}</feed>",
                    Encoding.UTF8, "application/atom+xml")
            };
        }
    }
}
