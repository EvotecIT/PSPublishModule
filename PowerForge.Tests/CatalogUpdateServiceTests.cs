using System.Net;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class CatalogUpdateServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "catalog-update-" + Guid.NewGuid().ToString("N"))).FullName;
    private readonly CatalogUpdateSpec _profile = new()
    {
        ReleaseConfigPath = "release.json", StoreTargetName = "App",
        StoreInstallerUrlTemplate = "https://downloads.example.test/releases/{releaseId}/{artifactKey}/download",
        StoreArtifactKeys = new() { ["x64"] = "windows-x64-msi" }
    };
    private readonly PowerForgeReleaseSpec _release = new()
    {
        Winget = new() { Enabled = true, InstallerUrlTemplate = "https://example.test/v{PackageVersion}/{FileName}",
            Submission = new() { Token = "test-token" }, Packages = new[] { new PowerForgeReleaseWingetPackage
            {
                PackageIdentifier = "Fixture.App", Publisher = "Fixture", PackageName = "Fixture App", License = "MIT", ShortDescription = "A fixture",
                Installers = new[] { new PowerForgeReleaseWingetInstaller { Category = PowerForgeReleaseAssetCategory.Installer,
                    Target = "App", Runtime = "win-x64", Architecture = "x64", InstallerType = "msi", NestedInstallerType = null } }
            } } }
    };
    private string ProfilePath => Path.Combine(_root, "profile.json");
    private string ReleasePath => Path.Combine(_root, "release.json");
    private string Output => Path.Combine(_root, "prepared");
    private string StorePath => Path.Combine(_root, "store.json");

    public CatalogUpdateServiceTests()
    {
        Environment.SetEnvironmentVariable("CATALOG_TEST_STORE_TOKEN", "test-store-token");
        File.WriteAllText(ProfilePath, "profile"); File.WriteAllText(ReleasePath, "release"); File.WriteAllText(StorePath, "store identity");
        File.WriteAllText(Path.Combine(_root, "app.msi"), "signed fixture");
        var asset = new PowerForgeReleaseAssetEntry { Path = "app.msi", Category = PowerForgeReleaseAssetCategory.Installer,
            Target = "App", Runtime = "win-x64", Version = "1.2.3" };
        File.WriteAllText(Path.Combine(_root, "manifest.json"), "{\"schemaVersion\":1,\"assetEntries\":" +
            JsonSerializer.Serialize(new[] { asset }, ReleaseCatalogJsonContext.Default.PowerForgeReleaseAssetEntryArray) + "}");
        File.WriteAllLines(Path.Combine(_root, "SHA256SUMS.txt"), new[] { "app.msi", "manifest.json" }.Select(name =>
            DotNetPublishReleaseArtifactVerifier.ComputeSha256(Path.Combine(_root, name)) + " *" + name));
    }

    [Fact]
    public async Task Prepare_MapsVerifiedPublicStoreUrlsWithoutChangingWinget()
    {
        var reads = 0;
        var service = Service(verify: (artifact, store, _) => { Assert.True(store); Assert.Contains("/release-123/windows-x64-msi/", artifact.StoreUrl); reads++; return Task.CompletedTask; });
        var receipt = await Prepare(service);
        Assert.Equal(1, reads);
        Assert.Equal("1.2.3", receipt.PackageVersion);
        Assert.Contains("https://example.test/v1.2.3/app.msi", File.ReadAllText(Directory.GetFiles(Output, "*.installer.yaml", SearchOption.AllDirectories).Single()));
        Assert.Contains(receipt.Artifacts[0].StoreUrl, File.ReadAllText(Path.Combine(Output, "desktop-packages.json")));
        Assert.Equal(4, receipt.Files.Count);
    }

    [Fact]
    public async Task Submit_ResumesCompletedChannelsWithoutResubmitting()
    {
        var runner = new Runner();
        var handler = new StoreHandler();
        using var client = new HttpClient(handler);
        using var store = new StoreSubmissionService(httpClient: client);
        var service = Service(runner, store);
        await Prepare(service);
        var spec = StoreSpec();
        var first = await service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "all", spec, StorePath, true);
        Assert.Equal("Submitted", first.Winget.State); Assert.Equal("Submitted", first.Store.State);
        Assert.Equal("https://github.com/microsoft/winget-pkgs/pull/123", first.Winget.Reference);
        Assert.Equal("submission-123", first.Store.Reference);
        await service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "all", spec, StorePath, true);
        Assert.Equal(1, runner.Calls); Assert.Equal(3, handler.Mutations);
        var snapshot = await service.StatusAsync(_profile, ProfilePath, ReleasePath, Output, spec, StorePath);
        Assert.Equal("PUBLISHED", snapshot.Store.RemoteStatus); Assert.Equal(3, handler.Mutations);
        Assert.DoesNotContain("test-token", File.ReadAllText(Path.Combine(Output, "catalog-update.json")));
    }

    [Fact]
    public async Task Submit_InterruptedWinGetBlocksReplayButOtherChannelRemainsAvailable()
    {
        var runner = new Runner { Interrupt = true };
        var service = Service(runner);
        await Prepare(service);
        await Assert.ThrowsAsync<IOException>(() => service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "winget", null, null, true));
        Assert.Equal("Attempting", service.Read(Output, ProfilePath, ReleasePath).Winget.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "winget", null, null, true));
        Assert.Equal(1, runner.Calls);
        using var client = new HttpClient(new StoreHandler());
        using var store = new StoreSubmissionService(httpClient: client);
        var receipt = await Service(store: store).SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "store", StoreSpec(), StorePath, true);
        Assert.Equal("Submitted", receipt.Store.State); Assert.Equal("Attempting", receipt.Winget.State);
    }

    [Fact]
    public async Task Submit_ChangedCatalogOrFailedRemoteBytesCannotStartSubmission()
    {
        var runner = new Runner();
        var service = Service(runner);
        await Prepare(service);
        var rejecting = Service(runner, verify: (_, _, _) => throw new InvalidOperationException("changed remote bytes"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rejecting.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "winget", null, null, true));
        Assert.Equal("Prepared", service.Read(Output, ProfilePath, ReleasePath).Winget.State);
        File.AppendAllText(Path.Combine(Output, "desktop-packages.json"), " ");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "winget", null, null, true));
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Submit_PreflightDoesNotMutateOrWriteAttemptReceipt()
    {
        var runner = new Runner(); var service = Service(runner); await Prepare(service);
        var receipt = await service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "winget", null, null, false);
        Assert.Equal(0, runner.Calls); Assert.Equal("Prepared", receipt.Winget.State);
    }

    [Fact]
    public async Task ExistingDesktopSubmissionIdPerformsReadOnlyResume()
    {
        var handler = new StoreHandler(); using var client = new HttpClient(handler);
        using var service = new StoreSubmissionService(httpClient: client);
        var spec = StoreSpec(); spec.Targets[0].DesktopPackages = new[] { new StoreSubmissionDesktopPackage
        { PackageUrl = "https://example.test/app.msi", Architectures = new[] { "X64" }, Languages = new[] { "en-US" }, PackageType = "msi", IsSilentInstall = true } };
        var result = await service.RunAsync(spec, StorePath, new StoreSubmissionRequest { TargetName = "App", SubmissionId = "submission-123", WaitForCommit = false });
        Assert.Equal("PUBLISHED", result.FinalStatus); Assert.Equal(0, handler.Mutations);
    }

    private Task<CatalogUpdateReceipt> Prepare(CatalogUpdateService service) => service.PrepareAsync(_profile, ProfilePath, _release, ReleasePath,
        Path.Combine(_root, "manifest.json"), Path.Combine(_root, "SHA256SUMS.txt"), _root, Output, "release-123");

    [Fact]
    public async Task Reprepare_RequalifiesReleaseBeforeRestoringCompletedProgress()
    {
        var runner = new Runner(); var checks = 0;
        var service = Service(runner, verify: (_, _, _) => { checks++; return Task.CompletedTask; });
        await Prepare(service);
        await service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "winget", null, null, true);
        var resumed = await service.PrepareAsync(_profile, ProfilePath, _release, ReleasePath,
            Path.Combine(_root, "manifest.json"), Path.Combine(_root, "SHA256SUMS.txt"), _root,
            Path.Combine(_root, "resumed"), "release-123", resumeFrom: Output);
        Assert.Equal("Submitted", resumed.Winget.State);
        Assert.Equal("https://github.com/microsoft/winget-pkgs/pull/123", resumed.Winget.Reference);
        Assert.Equal(3, checks);
        Assert.Equal(1, runner.Calls);
    }

    [Theory]
    [InlineData("installer")]
    [InlineData("manifest")]
    public async Task Reprepare_RejectsSelfConsistentForgedReceipt(string changed)
    {
        var service = Service(); var receipt = await Prepare(service);
        if (changed == "installer") receipt.Artifacts[0].StoreUrl = "https://attacker.example.test/app.msi";
        else
        {
            var relative = receipt.Files.Keys.First(); var path = Path.Combine(Output, relative);
            File.AppendAllText(path, "changed input");
            receipt.Files[relative] = DotNetPublishReleaseArtifactVerifier.ComputeSha256(path);
        }
        File.WriteAllText(Path.Combine(Output, "catalog-update.json"),
            JsonSerializer.Serialize(receipt, ReleaseCatalogJsonContext.Default.CatalogUpdateReceipt));
        service.Read(Output, ProfilePath, ReleasePath);
        var resumedPath = Path.Combine(_root, "resumed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync(_profile, ProfilePath, _release, ReleasePath,
            Path.Combine(_root, "manifest.json"), Path.Combine(_root, "SHA256SUMS.txt"), _root,
            resumedPath, "release-123", resumeFrom: Output));
        Assert.False(Directory.Exists(resumedPath));
    }

    [Fact]
    public async Task Reservation_BlocksFreshRunAndConsumedKeyCannotReplay()
    {
        var runner = new Runner { Interrupt = true }; var service = Service(runner); await Prepare(service);
        var key = Guid.NewGuid().ToString("N");
        var reserved = service.Reserve(Output, ProfilePath, ReleasePath, "winget", key);
        Assert.Equal("Reserved", reserved.Winget.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "winget", null, null, true));
        await Assert.ThrowsAsync<IOException>(() => service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "winget", null, null, true, reservationKey: key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "winget", null, null, true, reservationKey: key));
        Assert.Equal(1, runner.Calls);
        Assert.Throws<InvalidOperationException>(() => service.Reconcile(Output, ProfilePath, ReleasePath, "winget", "none", false));
        var resumed = service.Reconcile(Output, ProfilePath, ReleasePath, "winget", "https://github.com/microsoft/winget-pkgs/pull/123", true);
        Assert.Equal("Submitted", resumed.Winget.State);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task StorePreflightNotReadyDoesNotReserveOrMutatePackages(bool notReady, bool active)
    {
        var handler = new StoreHandler { NotReady = notReady, Active = active };
        using var client = new HttpClient(handler);
        using var store = new StoreSubmissionService(httpClient: client);
        var service = Service(store: store); await Prepare(service);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "store", StoreSpec(), StorePath, true));
        Assert.Equal("Prepared", service.Read(Output, ProfilePath, ReleasePath).Store.State);
        Assert.Equal(0, handler.Mutations);
    }

    [Fact]
    public async Task StoreReservationBindsApplicationConfiguration()
    {
        var service = Service(); await Prepare(service); var key = Guid.NewGuid().ToString("N");
        service.Reserve(Output, ProfilePath, ReleasePath, "store", key, StorePath);
        File.AppendAllText(StorePath, "different product");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "store", StoreSpec(), StorePath, true, reservationKey: key));
    }

    [Fact]
    public async Task AuthenticationPreflightUsesEnvironmentIdentityWithoutPackageMutation()
    {
        var names = new[] { "CATALOG_TEST_TENANT", "CATALOG_TEST_CLIENT", "CATALOG_TEST_SECRET" };
        var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable(names[0], "tenant-from-env"); Environment.SetEnvironmentVariable(names[1], "client-from-env"); Environment.SetEnvironmentVariable(names[2], "fixture-secret");
            var handler = new StoreHandler(); using var client = new HttpClient(handler); using var store = new StoreSubmissionService(httpClient: client);
            var spec = StoreSpec(); spec.Authentication = new StoreSubmissionAuthenticationOptions
            { SellerId = "seller", TenantIdEnvVar = names[0], ClientIdEnvVar = names[1], ClientSecretEnvVar = names[2] };
            var service = Service(store: store); await Prepare(service);
            await service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "store", spec, StorePath, false, requireAuthentication: true);
            Assert.Equal(0, handler.Mutations); Assert.Equal(1, handler.TokenRequests);
            Assert.Equal("Prepared", service.Read(Output, ProfilePath, ReleasePath).Store.State);
        }
        finally { for (var i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], previous[i]); }
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.Unauthorized)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    [InlineData(true, HttpStatusCode.Forbidden)]
    public async Task RejectedWinGetPublisherCannotPassAuthenticationOrStartSubmission(bool execute, HttpStatusCode status)
    {
        _release.Winget!.Submission!.Token = "rejected-publisher-token";
        var runner = new Runner();
        var handler = new PublisherHandler { Status = status, ExpectedToken = "rejected-publisher-token" };
        using var client = new HttpClient(handler);
        var service = Service(runner, wingetClient: client); await Prepare(service);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(_profile, ProfilePath,
            _release, ReleasePath, Output, "winget", null, null, execute, requireAuthentication: true));
        Assert.DoesNotContain(handler.ExpectedToken, error.Message);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(0, runner.Calls);
        Assert.Equal("Prepared", service.Read(Output, ProfilePath, ReleasePath).Winget.State);
    }

    [Fact]
    public async Task WinGetAuthenticationUsesPublishingEnvironmentTokenWithoutMutation()
    {
        const string name = "CATALOG_TEST_WINGET_PUBLISHER";
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, "publisher-from-env");
            _release.Winget!.Submission!.Token = null;
            _release.Winget.Submission.TokenEnvName = name;
            var handler = new PublisherHandler { ExpectedToken = "publisher-from-env" };
            using var client = new HttpClient(handler);
            var runner = new Runner(); var service = Service(runner, wingetClient: client); await Prepare(service);
            await service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "winget", null, null,
                false, requireAuthentication: true);
            Assert.Equal(1, handler.Requests); Assert.Equal(0, runner.Calls);
            Assert.Equal("Prepared", service.Read(Output, ProfilePath, ReleasePath).Winget.State);
            Assert.DoesNotContain("publisher-from-env", File.ReadAllText(Path.Combine(Output, "catalog-update.json")));
        }
        finally { Environment.SetEnvironmentVariable(name, previous); }
    }

    [Fact]
    public async Task OrdinaryPreflightDoesNotRequirePublishingAuthentication()
    {
        var handler = new PublisherHandler { Status = HttpStatusCode.Unauthorized };
        using var client = new HttpClient(handler);
        var service = Service(wingetClient: client); await Prepare(service);
        await service.SubmitAsync(_profile, ProfilePath, _release, ReleasePath, Output, "winget", null, null, false);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task CancelledWinGetAuthenticationKeepsArchivedReservationReusable()
    {
        var runner = new Runner(); var service = Service(runner); await Prepare(service);
        var key = Guid.NewGuid().ToString("N");
        service.Reserve(Output, ProfilePath, ReleasePath, "winget", key);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SubmitAsync(_profile, ProfilePath,
            _release, ReleasePath, Output, "winget", null, null, true, cancellation.Token, key));
        var receipt = service.Read(Output, ProfilePath, ReleasePath);
        Assert.Equal("Reserved", receipt.Winget.State); Assert.Equal(key, receipt.Winget.ReservationKey);
        Assert.Equal(0, runner.Calls);
    }

    private static CatalogUpdateService Service(Runner? runner = null, StoreSubmissionService? store = null,
        Func<CatalogInstaller, bool, CancellationToken, Task>? verify = null, HttpClient? wingetClient = null) => new(
        new ReleaseCatalogPreparationService(path => new DotNetPublishMsiPackageMetadata
        { Path = path, ProductName = "Fixture App", ProductVersion = "1.2.3", Manufacturer = "Fixture", Architecture = "x64", Scope = "machine",
            ProductCode = "{11111111-1111-1111-1111-111111111111}", UpgradeCode = "{22222222-2222-2222-2222-222222222222}" }, _ => true),
        new WingetSubmissionService(processRunner: runner ?? new Runner(), httpClient: wingetClient ?? PublisherClient),
        store, verify ?? ((_, _, _) => Task.CompletedTask), (_, _) => Task.FromResult("open"));

    private static readonly HttpClient PublisherClient = new(new PublisherHandler());

    private sealed class PublisherHandler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string ExpectedToken = "test-token";
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.github.com/user", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(ExpectedToken, request.Headers.Authorization.Parameter);
            return Task.FromResult(new HttpResponseMessage(Status));
        }
    }

    private static StoreSubmissionSpec StoreSpec() => new()
    {
        Authentication = new() { SellerId = "seller", AccessTokenEnvVar = "CATALOG_TEST_STORE_TOKEN" },
        Targets = new[] { new StoreSubmissionTarget { Name = "App", Provider = StoreSubmissionProviderKind.DesktopInstaller, ApplicationId = "product" } }
    };

    private sealed class Runner : IProcessRunner
    {
        public int Calls; public bool Interrupt;
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Arguments[0] == "validate") return Task.FromResult(new ProcessRunResult(0, "valid", "", "winget", TimeSpan.Zero, false));
            Calls++; if (Interrupt) throw new IOException("interrupted after remote mutation");
            return Task.FromResult(new ProcessRunResult(0, "Created https://github.com/microsoft/winget-pkgs/pull/123", "", "wingetcreate", TimeSpan.Zero, false));
        }
    }

    private sealed class StoreHandler : HttpMessageHandler
    {
        public int Mutations;
        public int TokenRequests;
        public bool NotReady;
        public bool Active;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "login.microsoftonline.com")
            {
                Assert.Contains("tenant-from-env", request.RequestUri.AbsoluteUri);
                Assert.Contains("client_id=client-from-env", request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
                TokenRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"fixture-access-token\"}") });
            }
            if (request.Method != HttpMethod.Get) Mutations++;
            var path = request.RequestUri!.AbsolutePath;
            var body = path.EndsWith("/submit") ? "{\"submissionId\":\"submission-123\"}" :
                path.Contains("/submission/submission-123/") ? "{\"publishingStatus\":\"PUBLISHED\",\"hasFailed\":false}" : NotReady ? "{\"isReady\":false}" :
                Active ? "{\"isReady\":true,\"ongoingSubmissionId\":\"active-other-submission\"}" : "{\"isReady\":true}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"isSuccess\":true,\"errors\":[],\"responseData\":" + body + "}") });
        }
    }

    private readonly string? _previousToken = Environment.GetEnvironmentVariable("CATALOG_TEST_STORE_TOKEN");
    public void Dispose() { Environment.SetEnvironmentVariable("CATALOG_TEST_STORE_TOKEN", _previousToken); Directory.Delete(_root, true); }
}
