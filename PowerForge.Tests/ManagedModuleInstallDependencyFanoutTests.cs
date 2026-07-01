using PowerForge;
using System.IO.Compression;
using System.Net;

namespace PowerForge.Tests;

public sealed class ManagedModuleInstallDependencyFanoutTests
{
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

#if !NET472
    [Fact]
    public async Task InstallAsync_prefetches_dependency_package_before_root_download_completes()
    {
        using var moduleRoot = new TemporaryDirectory();
        var handler = new RepositoryDependencyHintHandler
        {
            DelayRootPackageUntilCorePackageRequested = true
        };
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
        Assert.True(handler.CorePackageRequestedBeforeRootPackageCompleted);
        Assert.Equal(1, handler.CorePackageDownloadCount);
        Assert.Equal(ManagedModuleInstallStatus.Installed, Assert.Single(result.DependencyResults).Status);
    }

    [Fact]
    public async Task InstallAsync_prefetch_does_not_install_dependency_when_root_package_fails()
    {
        using var moduleRoot = new TemporaryDirectory();
        var handler = new RepositoryDependencyHintHandler
        {
            DelayRootPackageUntilCorePackageRequested = true,
            RootPackageHasUnsafeEntry = true
        };
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), new HttpClient(handler));
        var service = new ManagedModuleInstallService(new NullLogger(), repositoryClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Feed", "https://example.test/api/v2"),
            Name = "Company.Root",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            AllowClobber = true
        }));

        Assert.Contains("unsafe path", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(handler.CorePackageRequestedBeforeRootPackageCompleted);
        Assert.Equal(1, handler.CorePackageDownloadCount);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Core")));
    }

    [Fact]
    public async Task InstallAsync_cancels_dependency_prefetch_when_root_package_fails()
    {
        using var moduleRoot = new TemporaryDirectory();
        var handler = new RepositoryDependencyHintHandler
        {
            DelayRootPackageUntilCorePackageRequested = true,
            DelayCorePackageCompletionUntilCanceled = true,
            RootPackageHasUnsafeEntry = true
        };
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), new HttpClient(handler));
        var service = new ManagedModuleInstallService(new NullLogger(), repositoryClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Feed", "https://example.test/api/v2"),
            Name = "Company.Root",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            AllowClobber = true
        }));

        Assert.Contains("unsafe path", exception.Message, StringComparison.OrdinalIgnoreCase);
        await WaitForConditionAsync(() => handler.CorePackageCancellationObserved, TimeSpan.FromSeconds(2));
        Assert.Equal(1, handler.CorePackageDownloadCount);
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Core")));
    }

    [Fact]
    public async Task InstallAsync_does_not_prefetch_dependencies_when_selected_root_is_already_installed()
    {
        using var moduleRoot = new TemporaryDirectory();
        WriteInstalledModule(moduleRoot.Path, "Company.Root", "1.0.0");
        var handler = new RepositoryDependencyHintHandler();
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), new HttpClient(handler));
        var service = new ManagedModuleInstallService(new NullLogger(), repositoryClient);

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Feed", "https://example.test/api/v2"),
            Name = "Company.Root",
            Version = "1.0.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path,
            AllowClobber = true
        });

        Assert.Equal(ManagedModuleInstallStatus.AlreadyInstalled, result.Status);
        Assert.Equal(0, handler.CorePackageDownloadCount);
        Assert.DoesNotContain(handler.Requests, static request => request.Contains("id='Company.Core'", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, static request => request.EndsWith("/package/Company.Core/1.0.0", StringComparison.OrdinalIgnoreCase));
    }
#endif

    private static IReadOnlyDictionary<string, string> CreateModuleFiles(string moduleName, string version)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [moduleName + ".psd1"] = "@{ ModuleVersion = '" + version + "' }"
        };

    private static void WriteInstalledModule(string moduleRoot, string moduleName, string version)
    {
        var modulePath = Path.Combine(moduleRoot, moduleName, version);
        Directory.CreateDirectory(modulePath);
        File.WriteAllText(
            Path.Combine(modulePath, moduleName + ".psd1"),
            "@{ ModuleVersion = '" + version + "' }");
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopAt = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < stopAt)
        {
            if (condition())
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(15));
        }

        Assert.True(condition(), "The expected asynchronous condition was not observed before the timeout.");
    }

    private sealed class RepositoryDependencyHintHandler : HttpMessageHandler
    {
        private readonly object _syncRoot = new();
        private readonly List<string> _requests = new();
        private readonly TaskCompletionSource<bool> _corePackageRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _corePackageCancellationObserved;

        public bool CoreLatestRequestedBeforeRootDownload { get; private set; }

        public bool DelayRootPackageUntilCorePackageRequested { get; init; }

        public bool RootPackageHasUnsafeEntry { get; init; }

        public bool DelayCorePackageCompletionUntilCanceled { get; init; }

        public bool CorePackageRequestedBeforeRootPackageCompleted { get; private set; }

        public bool CorePackageCancellationObserved => _corePackageCancellationObserved;

        public int CorePackageDownloadCount { get; private set; }

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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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
                return CreateXmlResponse(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
                    "<entry><content><m:properties><d:Id>Company.Root</d:Id><d:Version>1.0.0</d:Version><d:Dependencies>Company.Core:[1.0.0, ):</d:Dependencies></m:properties></content></entry>" +
                    "</feed>");
            }

            if (url.Equals("https://example.test/api/v2/FindPackagesById()?id='Company.Core'&$filter=IsLatestVersion&$top=1&semVerLevel=2.0.0", StringComparison.OrdinalIgnoreCase))
            {
                return CreateXmlResponse(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
                    "<entry><content><m:properties><d:Id>Company.Core</d:Id><d:Version>1.0.0</d:Version></m:properties></content></entry>" +
                    "</feed>");
            }

            if (url.Equals("https://example.test/api/v2/package/Company.Root/1.0.0", StringComparison.OrdinalIgnoreCase))
            {
                if (DelayRootPackageUntilCorePackageRequested)
                {
                    var completed = await Task.WhenAny(
                            _corePackageRequested.Task,
                            Task.Delay(TimeSpan.FromSeconds(5), cancellationToken))
                        .ConfigureAwait(false);
                    CorePackageRequestedBeforeRootPackageCompleted = ReferenceEquals(completed, _corePackageRequested.Task);
                }

                var bytes = RootPackageHasUnsafeEntry
                    ? CreateUnsafeRootPackageBytes()
                    : TestPackageFactory.CreateBytes(
                        "Company.Root",
                        "1.0.0",
                        files: CreateModuleFiles("Company.Root", "1.0.0"),
                        dependencies: new[] { new TestDependency("Company.Core", "[1.0.0, )", null) });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                };
            }

            if (url.Equals("https://example.test/api/v2/package/Company.Core/1.0.0", StringComparison.OrdinalIgnoreCase))
            {
                lock (_syncRoot)
                {
                    CorePackageDownloadCount++;
                }

                _corePackageRequested.TrySetResult(true);
                if (DelayCorePackageCompletionUntilCanceled)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _corePackageCancellationObserved = true;
                        throw;
                    }
                }

                var bytes = TestPackageFactory.CreateBytes(
                    "Company.Core",
                    "1.0.0",
                    files: CreateModuleFiles("Company.Core", "1.0.0"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage CreateXmlResponse(string xml)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, System.Text.Encoding.UTF8, "application/xml")
            };

        private static byte[] CreateUnsafeRootPackageBytes()
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var nuspec = archive.CreateEntry("Company.Root.nuspec");
                using (var writer = new StreamWriter(nuspec.Open()))
                {
                    writer.Write(TestPackageFactory.CreateNuspec(
                        "Company.Root",
                        "1.0.0",
                        new[] { new TestDependency("Company.Core", "[1.0.0, )", null) }));
                }

                archive.CreateEntry("../escape.ps1");
            }

            return stream.ToArray();
        }
    }
}
