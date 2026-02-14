using System.Net;
using System.Text;

namespace PowerForge.Web;

/// <summary>Lightweight static file server for local preview.</summary>
public static class WebStaticServer
{
    private static readonly StringComparison FileSystemPathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>Starts a blocking static file server.</summary>
    /// <param name="rootPath">Root directory to serve.</param>
    /// <param name="host">Host/IP to bind.</param>
    /// <param name="port">Port to bind.</param>
    /// <param name="token">Cancellation token to stop the server.</param>
    /// <param name="log">Optional log callback.</param>
    public static void Serve(string rootPath, string host, int port, CancellationToken token, Action<string>? log = null)
        => ServeCore(rootPath, host, port, token, log, autoPortFallback: false, maxPortAttempts: 1);

    /// <summary>
    /// Starts a blocking static file server and, when the requested port is busy, tries subsequent ports.
    /// </summary>
    /// <param name="rootPath">Root directory to serve.</param>
    /// <param name="host">Host/IP to bind.</param>
    /// <param name="port">Preferred port to bind.</param>
    /// <param name="token">Cancellation token to stop the server.</param>
    /// <param name="log">Optional log callback.</param>
    /// <param name="maxPortAttempts">Maximum number of sequential ports to try.</param>
    public static void ServeWithPortFallback(
        string rootPath,
        string host,
        int port,
        CancellationToken token,
        Action<string>? log = null,
        int maxPortAttempts = 20)
        => ServeCore(rootPath, host, port, token, log, autoPortFallback: true, maxPortAttempts: maxPortAttempts);

    private static void ServeCore(
        string rootPath,
        string host,
        int port,
        CancellationToken token,
        Action<string>? log,
        bool autoPortFallback,
        int maxPortAttempts)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        var basePath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(basePath))
            throw new DirectoryNotFoundException($"Directory does not exist: {basePath}");

        var attempts = Math.Max(1, maxPortAttempts);
        var requestedPort = port <= 0 ? 8080 : port;
        if (requestedPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");

        var (listener, boundPort) = StartListener(host, requestedPort, autoPortFallback, attempts);
        using (listener)
        {
            var prefix = $"http://{host}:{boundPort}/";
            log?.Invoke($"Serving {basePath}");
            if (boundPort != requestedPort)
                log?.Invoke($"Requested port {requestedPort} is busy. Using {boundPort}.");
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
    }

    private static (HttpListener Listener, int BoundPort) StartListener(string host, int requestedPort, bool autoPortFallback, int attempts)
    {
        HttpListenerException? lastBindException = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var candidatePort = requestedPort + attempt;
            if (candidatePort > 65535)
                break;

            var listener = new HttpListener();
            var prefix = $"http://{host}:{candidatePort}/";
            listener.Prefixes.Add(prefix);

            try
            {
                listener.Start();
                return (listener, candidatePort);
            }
            catch (HttpListenerException ex)
            {
                var portUnavailable = IsPortUnavailableForBinding(ex);
                if (autoPortFallback && portUnavailable)
                {
                    lastBindException = ex;
                    try { listener.Close(); } catch { }
                    if (attempt + 1 < attempts)
                        continue;
                    break;
                }

                try { listener.Close(); } catch { }
                throw;
            }
            catch
            {
                try { listener.Close(); } catch { }
                throw;
            }
        }

        if (lastBindException is not null)
        {
            throw new IOException(
                $"Could not start server on {host}:{requestedPort} after trying {attempts} ports. " +
                "Use --port to select a different range.",
                lastBindException);
        }

        throw new IOException($"Could not start server on {host}:{requestedPort}.");
    }

    private static bool IsPortUnavailableForBinding(HttpListenerException ex)
    {
        var code = ex.NativeErrorCode;
        if (code == 5 || code == 183 || code == 10048 || code == 98)
            return true;

        var message = ex.Message ?? string.Empty;
        if (message.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("in use", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("address already", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("conflict", StringComparison.OrdinalIgnoreCase) >= 0;
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
        var normalizedRoot = NormalizeRootPath(basePath);
        var relative = urlPath.TrimStart('/');
        relative = relative.Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative))
            relative = "index.html";

        var candidate = Path.GetFullPath(Path.Combine(basePath, relative));
        if (!IsPathWithinRoot(normalizedRoot, candidate))
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
            var htmlPath = candidate + ".html";
            if (File.Exists(htmlPath))
                return htmlPath;
            var htmPath = candidate + ".htm";
            if (File.Exists(htmPath))
                return htmPath;
        }

        return candidate;
    }

    private static string NormalizeRootPath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool IsPathWithinRoot(string normalizedRoot, string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);
        return full.StartsWith(normalizedRoot, FileSystemPathComparison);
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
