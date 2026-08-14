using System.Formats.Tar;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class CloudflareIncrementalCachePurgeTests
{
    private const string ZoneId = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void CreateManifest_ShouldHashTheExactTarAndMapIndexAliases()
    {
        var root = NewTempDirectory();
        try
        {
            var artifactPath = Path.Combine(root, "artifact.tar");
            WriteTar(artifactPath,
                (".nojekyll", Array.Empty<byte>()),
                ("index.html", Encoding.UTF8.GetBytes("home")),
                ("docs/index.html", Encoding.UTF8.GetBytes("docs")),
                ("assets/a file.js", Encoding.UTF8.GetBytes("asset")));

            var firstPath = Path.Combine(root, "first.json");
            var secondPath = Path.Combine(root, "second.json");
            var first = CloudflareDeploymentManifestStore.CreateFromTar(artifactPath, "https://example.test/project", firstPath);
            var second = CloudflareDeploymentManifestStore.CreateFromTar(artifactPath, "https://example.test/project/", secondPath);

            Assert.Equal(4, first.ArtifactFileCount);
            Assert.Equal(6, first.UrlPathCount);
            Assert.Equal("https://example.test/project/", first.BaseUrl);
            Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
            Assert.Equal(first.ContentBytes, second.ContentBytes);

            var manifest = CloudflareDeploymentManifestStore.LoadRequired(firstPath);
            Assert.Equal(
                ["", ".nojekyll", "assets/a%20file.js", "docs/", "docs/index.html", "index.html"],
                manifest.Files.Select(entry => entry.Path).ToArray());
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("home"))).ToLowerInvariant(),
                manifest.Files.Single(entry => entry.Path == string.Empty).Sha256);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateManifest_ShouldRejectLinksInsteadOfHashingOutsideArtifactContent()
    {
        var root = NewTempDirectory();
        try
        {
            var artifactPath = Path.Combine(root, "artifact.tar");
            using (var stream = File.Create(artifactPath))
            using (var writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: false))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "outside") { LinkName = "../secret" });

            var exception = Assert.Throws<InvalidDataException>(() => CloudflareDeploymentManifestStore.CreateFromTar(
                artifactPath,
                "https://example.test/",
                Path.Combine(root, "manifest.json")));

            Assert.Contains("unsupported type", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateManifest_ShouldOnlyMapLowercaseIndexFilesToCleanAliases()
    {
        var root = NewTempDirectory();
        try
        {
            var artifactPath = Path.Combine(root, "artifact.tar");
            WriteTar(artifactPath,
                ("docs/index.html", Encoding.UTF8.GetBytes("clean")),
                ("docs/INDEX.HTML", Encoding.UTF8.GetBytes("case-sensitive")));

            var result = CloudflareDeploymentManifestStore.CreateFromTar(
                artifactPath,
                "https://example.test/",
                Path.Combine(root, "manifest.json"));
            var manifest = CloudflareDeploymentManifestStore.LoadRequired(result.ManifestPath);

            Assert.Equal(3, result.UrlPathCount);
            Assert.Equal(
                ["docs/", "docs/INDEX.HTML", "docs/index.html"],
                manifest.Files.Select(entry => entry.Path).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldBatchChangedUrlsInCloudflareSizedRequests()
    {
        var root = NewTempDirectory();
        try
        {
            var previousPath = WriteManifest(root, "previous.json", Enumerable.Range(0, 101)
                .Select(index => Entry($"docs/{index}.html", 'a')).ToArray());
            var currentPath = WriteManifest(root, "current.json", Enumerable.Range(0, 101)
                .Select(index => Entry($"docs/{index}.html", 'b')).ToArray());
            var handler = new RecordingHandler(SuccessResponse(), SuccessResponse());
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/",
                currentPath,
                previousPath,
                dryRun: false,
                logger: null,
                client);

            Assert.True(result.Success, result.Message);
            Assert.False(result.UsedFallback);
            Assert.Equal(101, result.TargetCount);
            Assert.Equal(2, result.RequestCount);
            Assert.Equal([100, 1], handler.Bodies.Select(CountFileTargets).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldUseHostnameFallbackWithoutPreviousBaseline()
    {
        var root = NewTempDirectory();
        try
        {
            var currentPath = WriteManifest(root, "current.json", [Entry("index.html", 'a')]);
            var handler = new RecordingHandler(SuccessResponse());
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/",
                currentPath,
                previousManifestPath: null,
                dryRun: false,
                logger: null,
                client);

            Assert.True(result.Success, result.Message);
            Assert.True(result.UsedFallback);
            Assert.Equal(CloudflareCachePurgeMode.Hostname, result.ActualMode);
            var body = JsonNode.Parse(Assert.Single(handler.Bodies))!.AsObject();
            Assert.Equal("example.test", Assert.Single(body["hosts"]!.AsArray())!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldUseHostnameFallbackForOversizedDiff()
    {
        var root = NewTempDirectory();
        try
        {
            var previousPath = WriteManifest(root, "previous.json", Array.Empty<CloudflareDeploymentManifestEntry>());
            var currentPath = WriteManifest(root, "current.json", Enumerable.Range(0, CloudflareIncrementalCachePurger.MaxIncrementalTargets + 1)
                .Select(index => Entry($"docs/{index}.html", 'a')).ToArray());
            var handler = new RecordingHandler(SuccessResponse());
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/",
                currentPath,
                previousPath,
                dryRun: false,
                logger: null,
                client);

            Assert.True(result.Success, result.Message);
            Assert.True(result.UsedFallback);
            Assert.Contains("exceeding", result.FallbackReason, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(JsonNode.Parse(Assert.Single(handler.Bodies))!["hosts"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldSkipApiWhenManifestIsUnchanged()
    {
        var root = NewTempDirectory();
        try
        {
            var entries = new[] { Entry(string.Empty, 'a'), Entry("index.html", 'a') };
            var previousPath = WriteManifest(root, "previous.json", entries);
            var currentPath = WriteManifest(root, "current.json", entries);
            var handler = new RecordingHandler();
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/",
                currentPath,
                previousPath,
                dryRun: false,
                logger: null,
                client);

            Assert.True(result.Success, result.Message);
            Assert.Equal(0, result.TargetCount);
            Assert.Empty(handler.Bodies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldRejectUnsafeCurrentManifestWithoutPurging()
    {
        var root = NewTempDirectory();
        try
        {
            var currentPath = WriteManifest(root, "current.json", [Entry("../admin", 'a')], validate: false);
            var handler = new RecordingHandler();
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/",
                currentPath,
                previousManifestPath: null,
                dryRun: false,
                logger: null,
                client);

            Assert.False(result.Success);
            Assert.Contains("unsafe", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Bodies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldRejectNullCurrentHashWithoutPurging()
    {
        var root = NewTempDirectory();
        try
        {
            var currentPath = WriteManifest(root, "current.json", [Entry("index.html", 'a')], validate: false);
            var json = JsonNode.Parse(File.ReadAllText(currentPath))!.AsObject();
            json["files"]![0]!["sha256"] = null;
            File.WriteAllText(currentPath, json.ToJsonString());
            var handler = new RecordingHandler();
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/",
                currentPath,
                previousManifestPath: null,
                dryRun: false,
                logger: null,
                client);

            Assert.False(result.Success);
            Assert.Contains("invalid SHA-256", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Bodies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurge_ShouldFallBackWhenPreviousHashAlgorithmIsNull()
    {
        var root = NewTempDirectory();
        try
        {
            var currentPath = WriteManifest(root, "current.json", [Entry("index.html", 'a')]);
            var previousPath = WriteManifest(root, "previous.json", [Entry("index.html", 'b')], validate: false);
            var json = JsonNode.Parse(File.ReadAllText(previousPath))!.AsObject();
            json["hashAlgorithm"] = null;
            File.WriteAllText(previousPath, json.ToJsonString());
            var handler = new RecordingHandler(SuccessResponse());
            using var client = NewClient(handler);

            var result = CloudflareIncrementalCachePurger.Purge(
                ZoneId,
                "secret-token",
                "https://example.test/",
                currentPath,
                previousPath,
                dryRun: false,
                logger: null,
                client);

            Assert.True(result.Success, result.Message);
            Assert.True(result.UsedFallback);
            Assert.Equal(CloudflareCachePurgeMode.Hostname, result.ActualMode);
            Assert.NotNull(JsonNode.Parse(Assert.Single(handler.Bodies))!["hosts"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncrementalPurgeJson_ShouldPreserveSchemaOnePurgeFields()
    {
        var result = WebCliCommandHandlers.BuildCloudflarePurgeResult(
            "site.json",
            ZoneId,
            "https://example.test/",
            CloudflareCachePurgeMode.Incremental,
            urlCount: 3,
            targetCount: 3,
            dryRun: false,
            message: "ok",
            actualMode: CloudflareCachePurgeMode.Files,
            requestCount: 1,
            usedFallback: false);

        Assert.False(result.GetProperty("purgeEverything").GetBoolean());
        Assert.Equal(3, result.GetProperty("urlCount").GetInt32());
        Assert.Equal("incremental", result.GetProperty("purgeMode").GetString());
        Assert.Equal("files", result.GetProperty("actualMode").GetString());
        Assert.Equal(1, result.GetProperty("requestCount").GetInt32());
    }

    [Fact]
    public void PipelineIncrementalPurge_ShouldResolveManifestPathsAndUseSafeFirstDeployFallback()
    {
        var root = NewTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "site.json"),
                """
                {
                  "Name": "Incremental Test",
                  "BaseUrl": "https://example.test/",
                  "Cloudflare": { "PurgeMode": "incremental" }
                }
                """);
            WriteManifest(root, "current.json", [Entry("index.html", 'a')]);
            File.WriteAllText(Path.Combine(root, "pipeline.json"),
                """
                {
                  "steps": [
                    {
                      "task": "cloudflare",
                      "operation": "purge",
                      "siteConfig": "./site.json",
                      "currentManifestPath": "./current.json",
                      "zoneId": "0123456789abcdef0123456789abcdef",
                      "token": "test-token",
                      "dryRun": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null);

            Assert.True(result.Success, result.Steps.Single().Message);
            Assert.Contains("hostname fallback", result.Steps.Single().Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReusableDeployWorkflow_ShouldKeepManifestPrivateAndCommitBaselineAfterPurge()
    {
        var runWorkflow = ReadRepoFile(".github", "workflows", "powerforge-website-run.yml");
        var deployWorkflow = ReadRepoFile(".github", "workflows", "powerforge-website-deploy.yml");
        var action = ReadRepoFile(".github", "actions", "powerforge-cloudflare-site-policy", "action.yml");

        Assert.Contains("cloudflare manifest create", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-cloudflare-cache-manifest", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("cloudflare_cache_manifest_artifact_name", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("inputs.site_artifact_name != '' || inputs.generate_cloudflare_cache_manifest", runWorkflow, StringComparison.Ordinal);
        foreach (var operatingSystem in new[] { "Linux", "macOS", "Windows" })
            Assert.Contains($"inputs.generate_cloudflare_cache_manifest) && runner.os == '{operatingSystem}'", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("generate_cloudflare_cache_manifest", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("manifest-artifact-name: ${{ needs.build.outputs.cloudflare_cache_manifest_artifact_name }}", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("deployment-run-id: ${{ needs.deploy.outputs.run_id }}", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("deployment-run-attempt: ${{ needs.deploy.outputs.run_attempt }}", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("Cache GitHub Pages deployment receipt", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("powerforge-cloudflare-deployment-v1-${{ github.repository_id }}-${{ needs.build.outputs.cloudflare_cache_manifest_scope }}", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("actions: write", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("manage-incremental-purge: \"true\"", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("deployment-id: ${{ needs.build.outputs.source_sha }}", deployWorkflow, StringComparison.Ordinal);
        Assert.Contains("actions/cache/restore@", action, StringComparison.Ordinal);
        Assert.Contains("actions/cache/save@", action, StringComparison.Ordinal);
        Assert.Contains("${{ github.run_id }}-${{ github.run_attempt }}", action, StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_CLOUDFLARE_DRY_RUN: ${{ inputs.dry-run }}", action, StringComparison.Ordinal);
        Assert.Contains("$arguments += '--dry-run'", ReadRepoFile(".github", "actions", "powerforge-cloudflare-site-policy", "Invoke-PowerForgeCloudflareIncrementalPurge.ps1"), StringComparison.Ordinal);
        Assert.Contains("POWERFORGE_CLOUDFLARE_USE_PREVIOUS", action, StringComparison.Ordinal);
        Assert.Contains("Resolve-PowerForgeCloudflareBaselineOrder.ps1", action, StringComparison.Ordinal);
        Assert.Contains("Restore latest GitHub Pages deployment receipt", action, StringComparison.Ordinal);
        Assert.Contains("steps.baseline_order.outputs.stale != 'true'", action, StringComparison.Ordinal);
        Assert.Contains("inputs.dry-run != 'true'", action, StringComparison.Ordinal);
        Assert.Contains("retention-days: 1", runWorkflow, StringComparison.Ordinal);
        Assert.Contains("Purge changed Cloudflare URLs", action, StringComparison.Ordinal);
        Assert.True(
            runWorkflow.IndexOf("Archive site artifact", StringComparison.Ordinal) <
            runWorkflow.IndexOf("Create incremental Cloudflare deployment manifest", StringComparison.Ordinal));
        Assert.True(
            runWorkflow.IndexOf("Create incremental Cloudflare deployment manifest", StringComparison.Ordinal) <
            runWorkflow.IndexOf("Upload Pages artifact", StringComparison.Ordinal));
        Assert.True(
            action.IndexOf("Purge changed Cloudflare URLs", StringComparison.Ordinal) <
            action.IndexOf("Cache deployed manifest baseline", StringComparison.Ordinal));
    }

    [Fact]
    public void BaselineOrder_ShouldRejectOlderJobOnlyRerunsButAllowIntentionalRedeploys()
    {
        if (!CommandExists("pwsh"))
            return;

        var root = NewTempDirectory();
        try
        {
            var previousManifest = Path.Combine(root, "manifest.json");
            var baselineState = Path.Combine(root, "state.json");
            var deploymentReceipt = Path.Combine(root, "deployment.json");
            File.WriteAllText(previousManifest, "{}");
            File.WriteAllText(baselineState,
                """
                { "schemaVersion": 1, "deploymentRunId": "200", "deploymentRunAttempt": 1 }
                """);
            File.WriteAllText(deploymentReceipt,
                """
                { "schemaVersion": 1, "deploymentRunId": "200", "deploymentRunAttempt": 1 }
                """);

            var stale = RunBaselineOrder(root, previousManifest, baselineState, deploymentReceipt, deploymentRunId: 100, deploymentRunAttempt: 1);
            Assert.Equal("true", stale["stale"]);
            Assert.Equal("false", stale["use_previous"]);

            File.WriteAllText(deploymentReceipt,
                """
                { "schemaVersion": 1, "deploymentRunId": "100", "deploymentRunAttempt": 2 }
                """);
            var redeployed = RunBaselineOrder(root, previousManifest, baselineState, deploymentReceipt, deploymentRunId: 100, deploymentRunAttempt: 2);
            Assert.Equal("false", redeployed["stale"]);
            Assert.Equal("true", redeployed["use_previous"]);

            File.Delete(baselineState);
            File.WriteAllText(deploymentReceipt,
                """
                { "schemaVersion": 1, "deploymentRunId": "300", "deploymentRunAttempt": 1 }
                """);
            var legacy = RunBaselineOrder(root, previousManifest, baselineState, deploymentReceipt, deploymentRunId: 300, deploymentRunAttempt: 1);
            Assert.Equal("false", legacy["stale"]);
            Assert.Equal("false", legacy["use_previous"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, "unavailable")]
    [InlineData("not-json", "invalid")]
    public void BaselineOrder_ShouldFailClosedWithoutAValidLatestDeploymentReceipt(string? receiptContent, string expectedError)
    {
        if (!CommandExists("pwsh"))
            return;

        var root = NewTempDirectory();
        try
        {
            var previousManifest = Path.Combine(root, "manifest.json");
            var baselineState = Path.Combine(root, "state.json");
            var deploymentReceipt = Path.Combine(root, "deployment.json");
            File.WriteAllText(previousManifest, "{}");
            File.WriteAllText(baselineState, "{}");
            if (receiptContent is not null)
                File.WriteAllText(deploymentReceipt, receiptContent);

            var failure = RunBaselineOrderProcess(root, previousManifest, baselineState, deploymentReceipt, deploymentRunId: 100, deploymentRunAttempt: 1);
            Assert.NotEqual(0, failure.ExitCode);
            Assert.Contains(expectedError, failure.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CloudflareDeploymentManifestEntry Entry(string path, char hashCharacter) => new()
    {
        Path = path,
        Length = 1,
        Sha256 = new string(hashCharacter, 64)
    };

    private static string WriteManifest(
        string root,
        string name,
        CloudflareDeploymentManifestEntry[] entries,
        bool validate = true)
    {
        var manifest = new CloudflareDeploymentManifest
        {
            BaseUrl = "https://example.test/",
            Files = entries
        };
        if (validate)
            CloudflareDeploymentManifestStore.Validate(manifest);
        var path = Path.Combine(root, name);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, manifest, WebCliJson.Context.CloudflareDeploymentManifest);
        return path;
    }

    private static void WriteTar(string path, params (string Name, byte[] Content)[] files)
    {
        using var stream = File.Create(path);
        using var writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: false);
        foreach (var file in files)
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, file.Name)
            {
                DataStream = new MemoryStream(file.Content, writable: false)
            });
        }
    }

    private static HttpClient NewClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.cloudflare.com/client/v4/")
    };

    private static HttpResponseMessage SuccessResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"success\":true,\"errors\":[],\"messages\":[],\"result\":{}}", Encoding.UTF8, "application/json")
    };

    private static int CountFileTargets(string body) => JsonNode.Parse(body)!["files"]!.AsArray().Count;

    private static string NewTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-cloudflare-incremental-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ReadRepoFile(params string[] segments)
    {
        return File.ReadAllText(RepoPath(segments));
    }

    private static string RepoPath(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine([root, .. segments]);
    }

    private static Dictionary<string, string> RunBaselineOrder(
        string root,
        string previousManifest,
        string baselineState,
        string deploymentReceipt,
        long deploymentRunId,
        int deploymentRunAttempt)
    {
        var result = RunBaselineOrderProcess(root, previousManifest, baselineState, deploymentReceipt, deploymentRunId, deploymentRunAttempt);
        Assert.True(result.ExitCode == 0, $"Baseline-order validation failed ({result.ExitCode}). stdout: {result.StandardOutput} stderr: {result.StandardError}");

        return File.ReadAllLines(result.OutputPath)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
    }

    private static (int ExitCode, string StandardOutput, string StandardError, string OutputPath) RunBaselineOrderProcess(
        string root,
        string previousManifest,
        string baselineState,
        string deploymentReceipt,
        long deploymentRunId,
        int deploymentRunAttempt)
    {
        var outputPath = Path.Combine(root, $"output-{Guid.NewGuid():N}.txt");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(RepoPath(".github", "actions", "powerforge-cloudflare-site-policy", "Resolve-PowerForgeCloudflareBaselineOrder.ps1"));
        startInfo.Environment["GITHUB_OUTPUT"] = outputPath;
        startInfo.Environment["POWERFORGE_CLOUDFLARE_PREVIOUS_MANIFEST"] = previousManifest;
        startInfo.Environment["POWERFORGE_CLOUDFLARE_BASELINE_STATE"] = baselineState;
        startInfo.Environment["POWERFORGE_CLOUDFLARE_DEPLOYMENT_RECEIPT"] = deploymentReceipt;
        startInfo.Environment["POWERFORGE_DEPLOYMENT_RUN_ID"] = deploymentRunId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["POWERFORGE_DEPLOYMENT_RUN_ATTEMPT"] = deploymentRunAttempt.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start PowerShell baseline-order validation.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, standardOutput, standardError, outputPath);
    }

    private static bool CommandExists(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo(command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("exit 0");
            using var process = Process.Start(startInfo);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        internal List<string> Bodies { get; } = new();

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => CaptureAndRespond(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(CaptureAndRespond(request));

        private HttpResponseMessage CaptureAndRespond(HttpRequestMessage request)
        {
            Bodies.Add(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty);
            if (_responses.Count == 0)
                throw new InvalidOperationException("No response configured.");
            return _responses.Dequeue();
        }
    }
}
