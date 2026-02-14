using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Collections.Concurrent;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebStaticServerTests
{
    [Fact]
    public async Task ServeWithPortFallback_UsesNextAvailablePort_AndServesContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-static-server-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var indexPath = Path.Combine(root, "index.html");
        File.WriteAllText(indexPath, "<!doctype html><html><body>hello fallback</body></html>");

        HttpListener? blocker = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;

        try
        {
            var busyPort = GetFreePort();
            blocker = new HttpListener();
            blocker.Prefixes.Add($"http://localhost:{busyPort}/");
            blocker.Start();

            var logs = new ConcurrentQueue<string>();
            cts = new CancellationTokenSource();
            serverTask = Task.Run(() =>
            {
                WebStaticServer.ServeWithPortFallback(
                    root,
                    "localhost",
                    busyPort,
                    cts.Token,
                    message => logs.Enqueue(message),
                    maxPortAttempts: 10);
            });

            var listeningLog = await WaitForLogAsync(
                logs,
                message => message.StartsWith("Listening on http://localhost:", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(5));
            if (listeningLog is null && serverTask.IsCompleted)
            {
                if (serverTask.Exception is not null)
                    throw new Xunit.Sdk.XunitException($"Server task faulted before listening: {serverTask.Exception.GetBaseException().Message}");
            }
            Assert.NotNull(listeningLog);
            Assert.True(
                logs.Any(message =>
                    message.Contains($"Requested port {busyPort} is busy. Using", StringComparison.OrdinalIgnoreCase)),
                "Expected fallback log message about busy requested port.");

            var boundPort = ExtractPortFromListeningLog(listeningLog!);
            Assert.True(boundPort > busyPort, "Expected fallback to a later available port.");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var html = await http.GetStringAsync($"http://localhost:{boundPort}/");
            Assert.Contains("hello fallback", html, StringComparison.OrdinalIgnoreCase);

            cts.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            if (cts is not null)
            {
                try { cts.Cancel(); } catch { }
            }
            if (serverTask is not null)
            {
                try { await serverTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
            }
            if (blocker is not null)
            {
                try { blocker.Stop(); } catch { }
                try { blocker.Close(); } catch { }
            }
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }

    [Fact]
    public void Serve_StrictMode_ThrowsWhenRequestedPortIsBusy()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-static-server-strict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), "<html><body>strict</body></html>");

        HttpListener? blocker = null;
        try
        {
            var busyPort = GetFreePort();
            blocker = new HttpListener();
            blocker.Prefixes.Add($"http://localhost:{busyPort}/");
            blocker.Start();

            using var cts = new CancellationTokenSource();
            var exception = Assert.ThrowsAny<Exception>(() =>
                WebStaticServer.Serve(root, "localhost", busyPort, cts.Token));

            Assert.False(string.IsNullOrWhiteSpace(exception.Message));
            Assert.True(
                exception is HttpListenerException || exception is IOException,
                $"Expected HttpListenerException/IOException, got {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            if (blocker is not null)
            {
                try { blocker.Stop(); } catch { }
                try { blocker.Close(); } catch { }
            }
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ServeWithPortFallback_DoesNotServeFilesOutsideRoot_ForTraversalPath()
    {
        var parent = Path.Combine(Path.GetTempPath(), "pf-web-static-server-traversal-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "site");
        var sibling = Path.Combine(parent, "site2");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><body>root</body></html>");
        File.WriteAllText(Path.Combine(sibling, "secret.html"), "<!doctype html><html><body>TOP SECRET</body></html>");

        CancellationTokenSource? cts = null;
        Task? serverTask = null;
        try
        {
            var logs = new ConcurrentQueue<string>();
            cts = new CancellationTokenSource();
            var preferredPort = GetFreePort();
            serverTask = Task.Run(() =>
            {
                WebStaticServer.ServeWithPortFallback(
                    root,
                    "localhost",
                    preferredPort,
                    cts.Token,
                    message => logs.Enqueue(message),
                    maxPortAttempts: 10);
            });

            var listeningLog = await WaitForLogAsync(
                logs,
                message => message.StartsWith("Listening on http://localhost:", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(5));
            if (listeningLog is null && serverTask.IsCompleted && serverTask.Exception is not null)
                throw new Xunit.Sdk.XunitException($"Server task faulted before listening: {serverTask.Exception.GetBaseException().Message}");
            Assert.NotNull(listeningLog);
            var boundPort = ExtractPortFromListeningLog(listeningLog!);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.GetAsync($"http://localhost:{boundPort}/..%2Fsite2%2Fsecret.html");
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Forbidden,
                $"Expected NotFound or Forbidden, got {(int)response.StatusCode} ({response.StatusCode}).");
            Assert.DoesNotContain("TOP SECRET", body, StringComparison.OrdinalIgnoreCase);

            cts.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            if (cts is not null)
            {
                try { cts.Cancel(); } catch { }
            }
            if (serverTask is not null)
            {
                try { await serverTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
            }
            if (Directory.Exists(parent))
            {
                try { Directory.Delete(parent, true); } catch { }
            }
        }
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

    private static int ExtractPortFromListeningLog(string listeningLog)
    {
        var marker = "http://localhost:";
        var markerIndex = listeningLog.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        Assert.True(markerIndex >= 0, $"Unexpected listening log format: {listeningLog}");
        var start = markerIndex + marker.Length;
        var end = listeningLog.IndexOf('/', start);
        if (end < 0)
            end = listeningLog.Length;
        var portText = listeningLog[start..end];
        Assert.True(int.TryParse(portText, out var port), $"Could not parse port from: {listeningLog}");
        return port;
    }

    private static async Task<string?> WaitForLogAsync(
        ConcurrentQueue<string> logs,
        Func<string, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = logs.ToArray();
            var match = snapshot.FirstOrDefault(predicate);
            if (!string.IsNullOrWhiteSpace(match))
                return match;

            await Task.Delay(50);
        }

        return logs.ToArray().FirstOrDefault(predicate);
    }
}
