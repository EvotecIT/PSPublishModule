using System.Net;
using System.Text;

namespace PowerForge.Web;

/// <summary>Lightweight static file server for local preview.</summary>
public static class WebStaticServer
{
    /// <summary>Starts a blocking static file server.</summary>
    /// <param name="rootPath">Root directory to serve.</param>
    /// <param name="host">Host/IP to bind.</param>
    /// <param name="port">Port to bind.</param>
    /// <param name="token">Cancellation token to stop the server.</param>
    /// <param name="log">Optional log callback.</param>
    public static void Serve(string rootPath, string host, int port, CancellationToken token, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        var basePath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(basePath))
            throw new DirectoryNotFoundException($"Directory does not exist: {basePath}");

        var prefix = $"http://{host}:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        log?.Invoke($"Serving {basePath}");
        log?.Invoke($"Listening on {prefix} (Ctrl+C to stop)");

        token.Register(() =>
        {
            try { listener.Close(); } catch { }
        });

        while (!token.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = listener.GetContext();
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (context is null)
                continue;

            _ = Task.Run(() => HandleRequest(context, basePath), token);
        }
    }

    private static void HandleRequest(HttpListenerContext context, string basePath)
    {
        var request = context.Request;
        var response = context.Response;

        if (request.HttpMethod != "GET" && request.HttpMethod != "HEAD")
        {
            response.StatusCode = 405;
            response.Close();
            return;
        }

        var rawPath = request.Url?.AbsolutePath ?? "/";
        var path = Uri.UnescapeDataString(rawPath);
        var filePath = ResolveFilePath(basePath, path);

        if (filePath is null || !File.Exists(filePath))
        {
            var spaFallback = ResolveSpaFallback(basePath, path);
            if (!string.IsNullOrWhiteSpace(spaFallback) && File.Exists(spaFallback))
            {
                WriteFile(response, spaFallback, 200, request.HttpMethod == "HEAD");
                return;
            }

            var notFound = Path.Combine(basePath, "404.html");
            if (File.Exists(notFound))
            {
                WriteFile(response, notFound, 404);
                return;
            }

            var payload = Encoding.UTF8.GetBytes("404 - Not Found");
            response.StatusCode = 404;
            response.ContentType = "text/plain; charset=utf-8";
            response.OutputStream.Write(payload, 0, payload.Length);
            response.Close();
            return;
        }

        WriteFile(response, filePath, 200, request.HttpMethod == "HEAD");
    }

    private static string? ResolveSpaFallback(string basePath, string urlPath)
    {
        if (string.IsNullOrWhiteSpace(urlPath)) return null;
        var normalized = urlPath.TrimEnd('/');
        if (normalized.StartsWith("/docs", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(basePath, "docs", "index.html");
        if (normalized.StartsWith("/playground", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(basePath, "playground", "index.html");
        return null;
    }

    private static string? ResolveFilePath(string basePath, string urlPath)
    {
        var relative = urlPath.TrimStart('/');
        relative = relative.Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative))
            relative = "index.html";

        var candidate = Path.GetFullPath(Path.Combine(basePath, relative));
        if (!candidate.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            return null;

        if (Directory.Exists(candidate))
        {
            var indexPath = Path.Combine(candidate, "index.html");
            return File.Exists(indexPath) ? indexPath : null;
        }

        if (!Path.HasExtension(candidate))
        {
            var indexPath = Path.Combine(candidate, "index.html");
            if (File.Exists(indexPath))
                return indexPath;
        }

        return candidate;
    }

    private static void WriteFile(HttpListenerResponse response, string filePath, int statusCode, bool headOnly = false)
    {
        var bytes = File.ReadAllBytes(filePath);
        response.StatusCode = statusCode;
        response.ContentType = GetContentType(Path.GetExtension(filePath));
        response.ContentLength64 = bytes.Length;
        if (!headOnly)
        {
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        response.Close();
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".wasm" => "application/wasm",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream"
        };
    }
}
