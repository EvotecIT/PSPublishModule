using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerGitHubArtifactsTests
{
    [Fact]
    public async Task RunPipeline_GitHubArtifactsPrune_DryRun_WritesReports()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-gh-artifacts-dryrun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;
        RequestCounter? counter = null;

        try
        {
            var port = GetFreePort();
            var artifacts = new[]
            {
                Artifact(101, "test-results", 20),
                Artifact(102, "test-results", 16),
                Artifact(103, "test-results", 2),
                Artifact(104, "github-pages", 30)
            };
            (listener, cts, serverTask, counter) = StartGitHubArtifactsServer(port, artifacts, failDeleteIds: null);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "github-artifacts-prune",
                      "repository": "EvotecIT/IntelligenceX",
                      "token": "test-token",
                      "apiBaseUrl": "http://127.0.0.1:{{port}}/",
                      "name": "test-results*",
                      "keep": 1,
                      "maxAgeDays": 7,
                      "maxDelete": 20,
                      "dryRun": true,
                      "reportPath": "./_reports/gh-artifacts.json",
                      "summaryPath": "./_reports/gh-artifacts.md"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);
            Assert.Contains("dry-run", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var reportPath = Path.Combine(root, "_reports", "gh-artifacts.json");
            var summaryPath = Path.Combine(root, "_reports", "gh-artifacts.md");
            Assert.True(File.Exists(reportPath));
            Assert.True(File.Exists(summaryPath));

            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal(2, report.RootElement.GetProperty("plannedDeletes").GetInt32());
            Assert.True(report.RootElement.GetProperty("dryRun").GetBoolean());
            Assert.NotNull(counter);
            Assert.True(counter!.ListCount >= 1);
            Assert.Equal(0, counter.DeleteCount);
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunPipeline_GitHubArtifactsPrune_Apply_FailsWhenDeleteFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-gh-artifacts-apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;
        RequestCounter? counter = null;

        try
        {
            var port = GetFreePort();
            var artifacts = new[]
            {
                Artifact(201, "test-results", 40),
                Artifact(202, "test-results", 35)
            };
            (listener, cts, serverTask, counter) = StartGitHubArtifactsServer(port, artifacts, failDeleteIds: new[] { 202L });

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "github-artifacts-prune",
                      "repository": "EvotecIT/CodeGlyphX",
                      "token": "test-token",
                      "apiBaseUrl": "http://127.0.0.1:{{port}}/",
                      "name": "test-results*",
                      "keep": 0,
                      "maxAgeDays": 0,
                      "maxDelete": 20,
                      "apply": true,
                      "failOnDeleteError": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("failed", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(counter);
            Assert.True(counter!.DeleteCount >= 2);
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunPipeline_GitHubArtifactsPrune_CanUseEnvironmentFallbacks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-gh-artifacts-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;
        RequestCounter? counter = null;

        var originalRepo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        var originalToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        try
        {
            var port = GetFreePort();
            var artifacts = new[]
            {
                Artifact(301, "github-pages", 90)
            };
            (listener, cts, serverTask, counter) = StartGitHubArtifactsServer(port, artifacts, failDeleteIds: null);

            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", "EvotecIT/HtmlForgeX.Website");
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "test-token");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "github-artifacts-prune",
                      "apiBaseUrl": "http://127.0.0.1:{{port}}/",
                      "name": "github-pages",
                      "keep": 0,
                      "dryRun": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);
            Assert.NotNull(counter);
            Assert.True(counter!.ListCount >= 1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", originalRepo);
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", originalToken);
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    private static FakeArtifact Artifact(long id, string name, int daysAgo)
    {
        var timestamp = DateTimeOffset.UtcNow.AddDays(-daysAgo);
        return new FakeArtifact
        {
            Id = id,
            Name = name,
            SizeInBytes = 1024 + id,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    private static (HttpListener listener, CancellationTokenSource cts, Task serverTask, RequestCounter counter) StartGitHubArtifactsServer(
        int port,
        IReadOnlyList<FakeArtifact> artifacts,
        IEnumerable<long>? failDeleteIds)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var cts = new CancellationTokenSource();
        var counter = new RequestCounter();
        var failures = new HashSet<long>(failDeleteIds ?? Array.Empty<long>());

        var serverTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException) when (cts.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cts.IsCancellationRequested)
                {
                    break;
                }

                if (context is null)
                    continue;

                try
                {
                    var path = context.Request.Url?.AbsolutePath ?? "/";
                    if (context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                        path.EndsWith("/actions/artifacts", StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Increment(ref counter.ListCount);
                        var payload = new
                        {
                            total_count = artifacts.Count,
                            artifacts = artifacts.Select(a => new
                            {
                                id = a.Id,
                                name = a.Name,
                                size_in_bytes = a.SizeInBytes,
                                expired = false,
                                created_at = a.CreatedAt.ToString("O"),
                                updated_at = a.UpdatedAt.ToString("O"),
                                workflow_run = new { id = 1000 + a.Id }
                            }).ToArray()
                        };
                        var json = JsonSerializer.Serialize(payload);
                        WriteResponse(context.Response, statusCode: 200, json);
                        continue;
                    }

                    if (context.Request.HttpMethod.Equals("DELETE", StringComparison.OrdinalIgnoreCase) &&
                        path.Contains("/actions/artifacts/", StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Increment(ref counter.DeleteCount);
                        var idText = path.Split('/').LastOrDefault();
                        _ = long.TryParse(idText, out var artifactId);
                        if (failures.Contains(artifactId))
                        {
                            WriteResponse(context.Response, statusCode: 500, "{\"message\":\"delete failed\"}");
                            continue;
                        }

                        context.Response.StatusCode = 204;
                        context.Response.Close();
                        continue;
                    }

                    WriteResponse(context.Response, statusCode: 404, "{\"message\":\"not found\"}");
                }
                catch
                {
                    try
                    {
                        if (context.Response.OutputStream.CanWrite)
                            WriteResponse(context.Response, statusCode: 500, "{\"message\":\"server error\"}");
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
        }, cts.Token);

        return (listener, cts, serverTask, counter);
    }

    private static void WriteResponse(HttpListenerResponse response, int statusCode, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentEncoding = Encoding.UTF8;
        response.OutputStream.Write(payload, 0, payload.Length);
        response.Close();
    }

    private sealed class FakeArtifact
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public long SizeInBytes { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class RequestCounter
    {
        public int ListCount;
        public int DeleteCount;
    }

    private static async Task StopServerAsync(HttpListener? listener, CancellationTokenSource? cts, Task? serverTask)
    {
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
        }

        if (listener is not null)
        {
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
        }

        if (serverTask is not null)
        {
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }

        cts?.Dispose();
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}
