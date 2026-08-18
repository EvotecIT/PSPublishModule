using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Json.Schema;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class DeploymentArtifactVerifierTests
{
    [Fact]
    public void Verify_RetriesTheCompleteGraphWithAFreshCacheIdentity()
    {
        var expectedA = Encoding.UTF8.GetBytes("current-a");
        var expectedB = Encoding.UTF8.GetBytes("current-b");
        var staleB = Encoding.UTF8.GetBytes("stale-b");
        string? firstIdentity = null;
        var requests = new List<string>();
        using var client = new HttpClient(new CallbackHandler(request =>
        {
            var identity = ParseQuery(request.RequestUri!, "_powerforge_verify");
            firstIdentity ??= identity;
            requests.Add($"{identity}:{request.RequestUri!.AbsolutePath}");
            var bytes = identity == firstIdentity && request.RequestUri.AbsolutePath.EndsWith("/b.js", StringComparison.Ordinal)
                ? staleB
                : request.RequestUri.AbsolutePath.EndsWith("/a.js", StringComparison.Ordinal) ? expectedA : expectedB;
            return BytesResponse(bytes);
        }));

        var result = DeploymentArtifactVerifier.Verify(
            Manifest(("assets/a.js", expectedA), ("assets/b.js", expectedB)),
            Options(attempts: 2),
            client,
            delay: _ => { },
            cacheIdentityFactory: () => "graph");

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.AttemptsCompleted);
        Assert.False(result.Attempts[0].Success);
        Assert.True(result.Attempts[1].Success);
        Assert.Equal(
            [
                "graph-1:/assets/a.js",
                "graph-1:/assets/b.js",
                "graph-2:/assets/a.js",
                "graph-2:/assets/b.js"
            ],
            requests);
    }

    [Fact]
    public void Verify_FailsClosedWhenEveryCacheIdentityReturnsStaleBytes()
    {
        var expected = Encoding.UTF8.GetBytes("current");
        var stale = Encoding.UTF8.GetBytes("stale!!");
        using var client = new HttpClient(new CallbackHandler(_ => BytesResponse(stale)));

        var result = DeploymentArtifactVerifier.Verify(
            Manifest(("assets/app.js", expected)),
            Options(attempts: 3),
            client,
            delay: _ => { },
            cacheIdentityFactory: () => "persistent");

        Assert.False(result.Success);
        Assert.Equal(3, result.AttemptsCompleted);
        Assert.All(result.Attempts, attempt => Assert.False(attempt.Success));
        Assert.Contains("SHA-256 mismatch", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_AppliesPathSelectionAndDeclaredByteBoundsBeforeNetworkAccess()
    {
        var bytes = Encoding.UTF8.GetBytes("asset");
        var requestCount = 0;
        using var client = new HttpClient(new CallbackHandler(_ =>
        {
            requestCount++;
            return BytesResponse(bytes);
        }));
        var manifest = Manifest(("assets/app.js", bytes), ("other/app.js", bytes));

        var result = DeploymentArtifactVerifier.Verify(
            manifest,
            Options(attempts: 1, prefixes: ["/assets/"]),
            client,
            delay: _ => { });

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.SelectedFileCount);
        Assert.Equal(1, requestCount);

        var bounded = Options(attempts: 1, prefixes: ["assets/"]);
        bounded.MaxResponseBytes = bytes.Length - 1;
        var error = Assert.Throws<InvalidOperationException>(() =>
            DeploymentArtifactVerifier.Verify(manifest, bounded, client, delay: _ => { }));
        Assert.Contains("per-response limit", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task RunPipeline_DeploymentVerify_UsesManifestEnvironmentAndWritesReports()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-deployment-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousManifest = Environment.GetEnvironmentVariable("POWERFORGE_DEPLOYMENT_MANIFEST");
        HttpListener? listener = null;
        CancellationTokenSource? cancellation = null;
        Task? server = null;
        try
        {
            var bytes = Encoding.UTF8.GetBytes("deployed");
            var port = GetFreePort();
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            cancellation = new CancellationTokenSource();
            server = Task.Run(async () =>
            {
                while (!cancellation.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync().WaitAsync(cancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    context.Response.StatusCode = 200;
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            });

            var manifestPath = Path.Combine(root, "manifest.json");
            File.WriteAllText(manifestPath,
                $$"""
                {
                  "SchemaVersion": 1,
                  "HashAlgorithm": "sha256",
                  "BaseUrl": "http://127.0.0.1:{{port}}/",
                  "CachePolicyFingerprint": "",
                  "Files": [
                    { "Path": "apps/demo/app.js", "Length": {{bytes.Length}}, "Sha256": "{{Sha256(bytes)}}" }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(root, "pipeline.json"),
                """
                {
                  "steps": [
                    {
                      "task": "deployment-verify",
                      "pathPrefixes": "/apps/demo/",
                      "attempts": 1,
                      "requestAttempts": 1,
                      "reportPath": "./_reports/deployment.json",
                      "summaryPath": "./_reports/deployment.md"
                    }
                  ]
                }
                """);
            Environment.SetEnvironmentVariable("POWERFORGE_DEPLOYMENT_MANIFEST", manifestPath);

            var result = WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null);

            Assert.True(result.Success, result.Steps.Single().Message);
            Assert.Contains("\"Success\": true", File.ReadAllText(Path.Combine(root, "_reports", "deployment.json")), StringComparison.Ordinal);
            Assert.Contains("Result: pass", File.ReadAllText(Path.Combine(root, "_reports", "deployment.md")), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POWERFORGE_DEPLOYMENT_MANIFEST", previousManifest);
            cancellation?.Cancel();
            listener?.Stop();
            if (server is not null)
            {
                try { await server; } catch (OperationCanceledException) { }
            }
            listener?.Close();
            cancellation?.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PipelineSchema_AcceptsBoundedDeploymentVerifyTask()
    {
        var repoRoot = FindRepoRoot();
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(repoRoot, "Schemas", "powerforge.web.pipelinespec.schema.json")));
        var pipeline = JsonNode.Parse(
            """
            {
              "steps": [
                {
                  "task": "deployment-verify",
                  "pathPrefixes": ["/apps/converter/"],
                  "attempts": 12,
                  "delayMs": 30000,
                  "requestAttempts": 2,
                  "timeoutMs": 30000,
                  "maxFiles": 1024,
                  "maxResponseBytes": 67108864,
                  "maxTotalBytes": 268435456,
                  "reportPath": "./_reports/deployment.json",
                  "summaryPath": "./_reports/deployment.md"
                }
              ]
            }
            """)!;

        var evaluation = schema.Evaluate(pipeline, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(evaluation.IsValid, evaluation.ToString());
    }

    private static DeploymentArtifactVerificationOptions Options(int attempts, string[]? prefixes = null) => new()
    {
        Attempts = attempts,
        DelayMilliseconds = 0,
        RequestAttempts = 1,
        RequestRetryDelayMilliseconds = 0,
        TimeoutMilliseconds = 5000,
        PathPrefixes = prefixes ?? ["assets/"]
    };

    private static CloudflareDeploymentManifest Manifest(params (string Path, byte[] Bytes)[] files) => new()
    {
        BaseUrl = "https://example.test/",
        Files = files.Select(file => new CloudflareDeploymentManifestEntry
        {
            Path = file.Path,
            Length = file.Bytes.Length,
            Sha256 = Sha256(file.Bytes)
        }).ToArray()
    };

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static HttpResponseMessage BytesResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes)
    };

    private static string ParseQuery(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var components = pair.Split('=', 2);
            if (Uri.UnescapeDataString(components[0]).Equals(name, StringComparison.Ordinal))
                return components.Length == 2 ? Uri.UnescapeDataString(components[1]) : string.Empty;
        }
        return string.Empty;
    }

    private static int GetFreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        try { return ((System.Net.IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 12 && current is not null; index++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                return current.FullName;
        }
        throw new DirectoryNotFoundException("Unable to locate the PowerForge repository root.");
    }

    private sealed class CallbackHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(callback(request));
    }
}
