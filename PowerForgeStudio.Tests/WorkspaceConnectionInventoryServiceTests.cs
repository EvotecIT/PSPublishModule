using System.Net;
using System.IO.Pipes;
using System.Text;
using PowerForge;
using PowerForgeStudio.Domain.Connections;
using PowerForgeStudio.Orchestrator.Connections;

namespace PowerForgeStudio.Tests;

public sealed class WorkspaceConnectionInventoryServiceTests : IDisposable
{
    private readonly string _fixture = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "studio-connections-" + Guid.NewGuid().ToString("N"))).FullName;

    [Theory]
    [InlineData("https://user:password@example.test/path?token=secret#fragment", "https://example.test/path")]
    [InlineData("http://example.test/path", "Insecure endpoint blocked")]
    [InlineData("http://127.0.0.1:5000/path?secret=yes", "http://127.0.0.1:5000/path")]
    [InlineData("named-pipe://./intelligencex.chat", "named-pipe://./intelligencex.chat")]
    public void EndpointSanitizerNeverDisplaysCredentialsOrQueryValues(string input, string expected)
        => Assert.Equal(expected, ConnectionEndpointSanitizer.Sanitize(input));

    [Fact]
    public async Task GitHubProbeDiscardsAllCommandOutput()
    {
        const string sentinel = "super-secret-token-value";
        var source = new GitHubConnectionSource(new ResultRunner(0, sentinel, sentinel));

        var result = await source.ReadAsync(_fixture, CancellationToken.None);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Verified", entry.State);
        Assert.DoesNotContain(sentinel, entry.ToString(), StringComparison.Ordinal);
        Assert.Equal("GitHub CLI credential store", entry.CredentialReference);
    }

    [Fact]
    public async Task GitHubTimeoutIsNotReportedAsMissingAuthentication()
    {
        var source = new GitHubConnectionSource(new ResultRunner(-1, "", "", timedOut: true));

        var result = await source.ReadAsync(_fixture, CancellationToken.None);

        Assert.Equal("Unavailable", Assert.Single(result.Entries).State);
    }

    [Fact]
    public async Task LicensingProbeCountsLockedProfilesWithoutOpeningThem()
    {
        var profiles = Directory.CreateDirectory(Path.Combine(_fixture, "profiles")).FullName;
        var profile = Path.Combine(profiles, "Production.json");
        await File.WriteAllTextAsync(profile, "{\"protectedAdminApiKey\":\"super-secret-sentinel\"}");
        await using var locked = new FileStream(profile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var client = new HttpClient(new StaticHandler(HttpStatusCode.OK));
        var source = new LicensingConnectionSource(client, profiles);

        var result = await source.ReadAsync(_fixture, CancellationToken.None);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Reachable", entry.State);
        Assert.Equal("1 Licensing.Admin protected-profile reference", entry.CredentialReference);
        Assert.DoesNotContain("super-secret-sentinel", entry.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReachableLicensingOwnerWithoutProfileIsUnconfigured()
    {
        var profiles = Directory.CreateDirectory(Path.Combine(_fixture, "empty-profiles")).FullName;
        using var client = new HttpClient(new StaticHandler(HttpStatusCode.OK));
        var source = new LicensingConnectionSource(client, profiles);

        var result = await source.ReadAsync(_fixture, CancellationToken.None);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Unconfigured", entry.State);
        Assert.False(entry.NeedsAttention);
        Assert.Equal(["Public health"], entry.Capabilities);
    }

    [Fact]
    public async Task ProviderFailureDoesNotHideHealthyProviderEvidence()
    {
        var service = new WorkspaceConnectionInventoryService([
            new ThrowingSource(),
            new StaticSource()
        ]);

        var snapshot = await service.InspectAsync(_fixture);

        Assert.Single(snapshot.Entries);
        Assert.Contains(snapshot.Sources, source => source.Provider == "Broken" && source.State == "Unavailable");
        Assert.Contains(snapshot.Sources, source => source.Provider == "Healthy" && source.State == "Available");
    }

    [Fact]
    public async Task ProviderOwnedHttpTimeoutBecomesUnavailableEvidence()
    {
        using var client = new HttpClient(new TimeoutHandler());
        var source = new RegistryConnectionSource(client);

        var result = await source.ReadAsync(_fixture, CancellationToken.None);

        Assert.Equal("Unavailable", result.State.State);
        Assert.All(result.Entries, entry => Assert.Equal("Unavailable", entry.State));
    }

    [Fact]
    public async Task IntelligenceXHandshakeAcceptsOnlyCorrelatedOwnerHelloAndDoesNotCopyPayload()
    {
        var pipeName = "studio-ix-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, new UTF8Encoding(false), false, 1024, leaveOpen: true);
            await using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            var request = await reader.ReadLineAsync();
            Assert.Contains("powerforge-studio-capability-check", request, StringComparison.Ordinal);
            await writer.WriteLineAsync("{\"type\":\"hello\",\"requestId\":\"powerforge-studio-capability-check\",\"name\":\"IntelligenceX.Chat.Service\",\"version\":\"1.2.3-preview+local\",\"secret\":\"payload-sentinel\"}");
        });
        var source = new IntelligenceXConnectionSource(pipeName);

        var result = await source.ReadAsync(_fixture, CancellationToken.None);
        await serverTask;

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Verified", entry.State);
        Assert.Contains("1.2.3-preview+local", entry.Evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("payload-sentinel", entry.ToString(), StringComparison.Ordinal);
        Assert.Equal(["Service handshake"], entry.Capabilities);
    }

    [Fact]
    public async Task IntelligenceXOwnerDiscoverySupportsWorktreeWorkspaceTopology()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(_fixture, "_worktrees", "PowerForge-feature")).FullName;
        var project = Path.Combine(_fixture, "IntelligenceX", "IntelligenceX.Chat", "IntelligenceX.Chat.Service", "IntelligenceX.Chat.Service.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        await File.WriteAllTextAsync(project, "<Project />");
        var source = new IntelligenceXConnectionSource("missing-" + Guid.NewGuid().ToString("N"), TimeSpan.FromMilliseconds(20));

        var result = await source.ReadAsync(workspace, CancellationToken.None);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Available", entry.State);
        Assert.Equal(["Owner source detected"], entry.Capabilities);
    }

    public void Dispose() => Directory.Delete(_fixture, recursive: true);

    private sealed class ResultRunner(int exitCode, string output, string error, bool timedOut = false) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            request.InvokePreStartBoundary();
            request.InvokeStartedProcessBoundary(1);
            request.InvokeStartBoundary();
            var result = new ProcessRunResult(exitCode, output, error, request.FileName, TimeSpan.Zero, timedOut);
            request.InvokeCompletionBoundary(result);
            return Task.FromResult(result);
        }
    }

    private sealed class StaticHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new TaskCanceledException("provider timeout");
    }

    private sealed class ThrowingSource : IWorkspaceConnectionSource
    {
        public string Provider => "Broken";
        public Task<ConnectionSourceResult> ReadAsync(string workspaceRoot, CancellationToken cancellationToken)
            => throw new IOException("unavailable");
    }

    private sealed class StaticSource : IWorkspaceConnectionSource
    {
        public string Provider => "Healthy";
        public Task<ConnectionSourceResult> ReadAsync(string workspaceRoot, CancellationToken cancellationToken)
        {
            WorkspaceConnectionEntry entry = new("healthy", "Healthy", Provider, "Service", "Verified",
                "https://example.test", "No credential", ["Read"], DateTimeOffset.UtcNow, "Verified", "Test");
            return Task.FromResult(new ConnectionSourceResult([entry],
                new WorkspaceConnectionSourceState(Provider, "Available", 1, "Available")));
        }
    }
}
