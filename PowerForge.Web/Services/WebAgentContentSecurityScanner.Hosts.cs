using System.Net;
using System.Net.Sockets;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private int VerifyExternalHosts(
        IEnumerable<Uri> urls,
        WebAgentContentSecurityOptions options,
        List<WebAgentContentSecurityFinding> findings,
        CancellationToken networkBudget)
    {
        var hosts = urls
            .Where(static uri => !string.IsNullOrWhiteSpace(uri.IdnHost))
            .Where(uri => !IsTrustedDomain(uri.IdnHost.TrimEnd('.'), options.TrustedDomains))
            .GroupBy(static uri => uri.IdnHost.TrimEnd('.'), StringComparer.OrdinalIgnoreCase)
            .Select(static group => new HostTargets(
                group.Key,
                group.Select(static uri => new UriBuilder(uri.Scheme, uri.IdnHost, uri.IsDefaultPort ? -1 : uri.Port).Uri)
                    .Distinct(UriComparer.Instance)
                    .ToArray()))
            .OrderBy(static target => target.Host, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (hosts.Length > options.MaxExternalHosts)
        {
            AddFinding(findings, "error", "PFAGENT.HOST.LIMIT_EXCEEDED", null, null,
                $"Artifacts contain {hosts.Length} unique external hosts; the configured maximum is {options.MaxExternalHosts}. No host requests were sent.");
            return 0;
        }

        var checkedHosts = 0;
        foreach (var target in hosts)
        {
            if (networkBudget.IsCancellationRequested)
            {
                AddFinding(findings, "error", "PFAGENT.NETWORK.TIME_BUDGET", null, null,
                    $"Network verification exceeded the configured {options.MaxNetworkDurationSeconds}-second total time budget.");
                break;
            }
            checkedHosts++;
            var host = target.Host;
            try
            {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(networkBudget);
                cancellation.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
                var addresses = Dns.GetHostAddressesAsync(host, cancellation.Token).GetAwaiter().GetResult();
                if (addresses.Length == 0)
                {
                    AddFinding(findings, "error", "PFAGENT.HOST.UNRESOLVED", null, null,
                        $"External hostname '{host}' did not resolve.");
                    continue;
                }
                if (addresses.Any(static address => !IsPublicAddress(address)))
                {
                    AddFinding(findings, "error", "PFAGENT.HOST.NON_PUBLIC", null, null,
                        $"External hostname '{host}' resolves to a loopback, private, link-local, or otherwise non-public address; it was not fetched.");
                    continue;
                }

                foreach (var endpoint in target.Endpoints)
                    VerifyTakeoverFingerprint(endpoint, addresses, options.RequestTimeoutSeconds, findings, networkBudget);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or HttpRequestException)
            {
                AddFinding(findings, "error", "PFAGENT.HOST.UNRESOLVED", null, null,
                    $"External hostname '{host}' could not be verified: {ex.Message}");
            }
        }
        return checkedHosts;
    }

    private void VerifyTakeoverFingerprint(
        Uri endpoint,
        IReadOnlyList<IPAddress> verifiedAddresses,
        int timeoutSeconds,
        List<WebAgentContentSecurityFinding> findings,
        CancellationToken networkBudget)
    {
        Exception? lastError = null;
        var responseCount = 0;
        var addresses = _pinVerifiedExternalHostAddress
            ? verifiedAddresses
            : new[] { verifiedAddresses[0] };
        foreach (var verifiedAddress in addresses)
        {
            try
            {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(networkBudget);
                cancellation.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                using var pinnedClient = _pinVerifiedExternalHostAddress
                    ? CreatePinnedHttpClient(verifiedAddress, timeoutSeconds)
                    : null;
                var client = pinnedClient ?? _httpClient;
                using var response = client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellation.Token)
                    .GetAwaiter().GetResult();
                responseCount++;
                InspectTakeoverResponse(endpoint, response, timeoutSeconds, findings, networkBudget);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or HttpRequestException)
            {
                lastError = ex;
                if (networkBudget.IsCancellationRequested)
                    throw;
            }
        }
        if (responseCount == 0)
            throw new HttpRequestException($"No verified public address accepted the connection to {endpoint}.", lastError);
    }

    private static void InspectTakeoverResponse(
        Uri endpoint,
        HttpResponseMessage response,
        int timeoutSeconds,
        List<WebAgentContentSecurityFinding> findings,
        CancellationToken networkBudget)
    {
        if ((int)response.StatusCode < 400)
            return;

        using var bodyCancellation = CancellationTokenSource.CreateLinkedTokenSource(networkBudget);
        bodyCancellation.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var stream = response.Content.ReadAsStream(bodyCancellation.Token);
        using var reader = new StreamReader(stream);
        var buffer = new char[16 * 1024];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = reader.ReadAsync(buffer.AsMemory(count, buffer.Length - count), bodyCancellation.Token)
                .AsTask().GetAwaiter().GetResult();
            if (read == 0)
                break;
            count += read;
        }
        var body = new string(buffer, 0, count);
        var fingerprints = new[]
        {
            "There isn't a GitHub Pages site here",
            "No such app",
            "DEPLOYMENT_NOT_FOUND",
            "404 Web Site not found",
            "The specified bucket does not exist",
            "Fastly error: unknown domain"
        };
        var fingerprint = fingerprints.FirstOrDefault(value => body.Contains(value, StringComparison.OrdinalIgnoreCase));
        if (fingerprint is not null)
        {
            AddFinding(findings, "error", "PFAGENT.HOST.DANGLING_SERVICE", null, null,
                $"External endpoint '{endpoint}' returned a known unclaimed-service fingerprint: {fingerprint}.");
        }
    }

    private static HttpClient CreatePinnedHttpClient(IPAddress address, int timeoutSeconds)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(
                        new IPEndPoint(address, context.DnsEndPoint.Port),
                        cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        var client = new HttpClient(handler, disposeHandler: true);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge.Web-AgentContentSecurity/1.0");
        return client;
    }

    private static bool IsTrustedDomain(string host, IEnumerable<string>? trustedDomains)
    {
        foreach (var configured in trustedDomains ?? Array.Empty<string>())
        {
            var configuredValue = configured.Trim();
            var includeSubdomains = configuredValue.StartsWith(".", StringComparison.Ordinal);
            var trusted = configuredValue.TrimEnd('.').TrimStart('.');
            if (string.IsNullOrWhiteSpace(trusted))
                continue;
            if (host.Equals(trusted, StringComparison.OrdinalIgnoreCase) ||
                includeSubdomains &&
                host.EndsWith("." + trusted, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6None))
            return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(bytes[0] == 10 ||
                     bytes[0] == 127 ||
                     bytes[0] == 0 ||
                     bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
                     bytes[0] == 169 && bytes[1] == 254 ||
                     bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                     bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2 ||
                     bytes[0] == 192 && bytes[1] == 168 ||
                     bytes[0] == 198 && bytes[1] is 18 or 19 ||
                     bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100 ||
                     bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113 ||
                     bytes[0] >= 224);
        }

        if (address.IsIPv4MappedToIPv6)
            return IsPublicAddress(address.MapToIPv4());
        if (TryExtractEmbeddedIPv4(bytes, out var embeddedAddress))
            return IsPublicAddress(embeddedAddress);
        return !(address.IsIPv6LinkLocal ||
                 address.IsIPv6SiteLocal ||
                 address.IsIPv6Multicast ||
                 bytes.Length >= 4 && bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00 ||
                 bytes.Length >= 6 && bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x02 && bytes[4] == 0x00 && bytes[5] == 0x00 ||
                 bytes.Length >= 4 && bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8 ||
                 bytes.Length >= 6 && bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B && bytes[4] == 0x00 && bytes[5] == 0x01 ||
                 bytes[0] == 0xFC ||
                 bytes[0] == 0xFD);
    }

    private sealed record HostTargets(string Host, Uri[] Endpoints);

    private static bool TryExtractEmbeddedIPv4(byte[] bytes, out IPAddress address)
    {
        address = IPAddress.None;
        if (bytes.Length != 16)
            return false;

        var offset = -1;
        // IPv4-compatible ::/96 and NAT64 64:ff9b::/96.
        if (bytes.Take(12).All(static value => value == 0) ||
            bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B &&
            bytes.Skip(4).Take(8).All(static value => value == 0))
        {
            offset = 12;
        }
        // 6to4 2002::/16 stores the IPv4 address immediately after the prefix.
        else if (bytes[0] == 0x20 && bytes[1] == 0x02)
        {
            offset = 2;
        }

        if (offset < 0)
            return false;
        address = new IPAddress(bytes.AsSpan(offset, 4));
        return true;
    }
}
