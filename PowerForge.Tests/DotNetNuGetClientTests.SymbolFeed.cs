using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class DotNetNuGetClientTests
{
    [Theory]
    [InlineData(201, true)]
    [InlineData(409, false)]
    public async Task PushPackageAsync_RetriesSymbolsThroughAdvertisedEndpoint(int symbolStatus, bool expectedSuccess)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-symbol-feed-" + Guid.NewGuid().ToString("N")));
        using var listener = new HttpListener();
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        var feed = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(feed);
        listener.Start();
        var uploads = new ConcurrentQueue<string>();
        var server = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (HttpListenerException) when (!listener.IsListening) { break; }
                catch (ObjectDisposedException) { break; }
                await context.Request.InputStream.CopyToAsync(Stream.Null);
                var path = context.Request.Url!.AbsolutePath;
                string body;
                if (context.Request.HttpMethod == "GET" && path == "/v3/index.json")
                {
                    context.Response.ContentType = "application/json";
                    body = $$"""{"version":"3.0.0","resources":[{"@id":"{{feed}}publish","@type":"PackagePublish/2.0.0"},{"@id":"{{feed}}symbols","@type":"SymbolPackagePublish/4.9.0"}]}""";
                }
                else if (context.Request.HttpMethod == "PUT" && path is "/publish/" or "/symbols/")
                {
                    uploads.Enqueue(path);
                    context.Response.StatusCode = path == "/publish/" ? 409 : symbolStatus;
                    context.Response.StatusDescription = path == "/publish/"
                        ? "Package already exists and cannot be modified"
                        : symbolStatus == 409 ? "This package ID has been reserved" : "Created";
                    body = string.Empty;
                }
                else
                {
                    context.Response.StatusCode = 404;
                    body = string.Empty;
                }
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });

        try
        {
            var packageDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "packages"));
            var packagePath = Path.Combine(packageDirectory.FullName, "Sample.1.0.0.nupkg");
            foreach (var path in new[] { packagePath, Path.ChangeExtension(packagePath, ".snupkg") })
            {
                using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
                using var writer = new StreamWriter(archive.CreateEntry("Sample.nuspec").Open());
                writer.Write("<package><metadata><id>Sample</id><version>1.0.0</version><authors>Test</authors><description>Synthetic local fixture</description></metadata></package>");
            }
            // The source exists only in the nested config. Both attempts must preserve this context.
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "NuGet.config"),
                "<configuration><packageSources><clear /></packageSources></configuration>");
            await File.WriteAllTextAsync(Path.Combine(packageDirectory.FullName, "NuGet.config"),
                $"<configuration><packageSources><clear /><add key=\"LocalNuGet\" value=\"{feed}v3/index.json\" allowInsecureConnections=\"true\" /></packageSources></configuration>");
            var client = new DotNetNuGetClient(
                new IsolatedNuGetProcessRunner(Path.Combine(root.FullName, "http-cache")),
                runtimeDirectoryRoot: Path.Combine(root.FullName, "runtime"));
            var result = await client.PushPackageAsync(new DotNetNuGetPushRequest(
                packagePath, "synthetic-key", "LocalNuGet", true, root.FullName, TimeSpan.FromSeconds(30)));

            Assert.Equal(new[] { "/publish/", "/symbols/" }, uploads.ToArray());
            Assert.Equal(expectedSuccess, result.Succeeded);
            var overall = DotNetRepositoryReleaseService.ClassifyNuGetPushOutcome(result);
            var outcomes = DotNetRepositoryReleaseService.ClassifyPublishedArtifacts(
                new[] { packagePath, Path.ChangeExtension(packagePath, ".snupkg") }, overall, true);
            Assert.Equal(DotNetRepositoryReleaseService.PackagePushOutcome.SkippedDuplicate, outcomes[packagePath]);
            Assert.Equal(expectedSuccess ? DotNetRepositoryReleaseService.PackagePushOutcome.Published : DotNetRepositoryReleaseService.PackagePushOutcome.Failed,
                outcomes[Path.ChangeExtension(packagePath, ".snupkg")]);
        }
        finally
        {
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
            root.Delete(recursive: true);
        }
    }

    private sealed class IsolatedNuGetProcessRunner(string httpCache) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => new ProcessRunner().RunAsync(new ProcessRunRequest(
                request.FileName, request.WorkingDirectory, request.Arguments, request.Timeout,
                new Dictionary<string, string?> { ["NUGET_HTTP_CACHE_PATH"] = httpCache }), cancellationToken);
    }
}
