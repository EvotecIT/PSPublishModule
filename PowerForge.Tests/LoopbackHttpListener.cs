using System.Net;
using System.Net.Sockets;

namespace PowerForge.Tests;

// Test fixtures need the bound port, not the result of an earlier unreserved probe.
internal static class LoopbackHttpListener
{
    internal static HttpListener Start(ref int port, string host = "127.0.0.1", string path = "/")
    {
        for (var attempt = 0; ; attempt++)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://{host}:{port}{path}");
            try
            {
                listener.Start();
                return listener;
            }
            catch (HttpListenerException error) when (attempt < 9 && IsPortConflict(error))
            {
                listener.Close();
                port = GetCandidatePort();
            }
            catch
            {
                listener.Close();
                throw;
            }
        }
    }

    internal static int GetCandidatePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static bool IsPortConflict(HttpListenerException error)
        // Unix uses errno 98/48 for occupied sockets and 400 for duplicate HttpListener prefixes.
        // Windows HTTP.sys reports a sharing violation (32) or existing registration (183).
        => error.NativeErrorCode is 98 or 48 or 10048 ||
           (!OperatingSystem.IsWindows() && error.NativeErrorCode == 400) ||
           (OperatingSystem.IsWindows() && error.NativeErrorCode is 32 or 183);
}
