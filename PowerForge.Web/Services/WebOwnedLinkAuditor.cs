using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using PowerForge.Web.Protocols.Sitemaps;

namespace PowerForge.Web;

/// <summary>Run-scoped sitemap cache and bounded HTTP fallback for owned destinations.</summary>
internal sealed class WebOwnedLinkAuditor : IDisposable
{
    internal sealed record Result(bool Broken, string? Unknown);
    private readonly WebOwnedLinkAuditOptions _options;
    private readonly Action<string, string, string?, string, string?> _addIssue;
    private readonly Dictionary<string, Uri> _domains = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loadedDomains = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sitemaps = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Result> _links = new(StringComparer.Ordinal);
    private readonly HttpClient _client;
    private readonly CancellationTokenSource _budget;

    internal WebOwnedLinkAuditor(WebOwnedLinkAuditOptions options,
        Action<string, string, string?, string, string?> addIssue)
    {
        _options = options;
        _addIssue = addIssue;
        if (options.Severity is not ("warning" or "error") || options.Domains is null || options.Domains.Length is < 1 or > 20 ||
            options.TimeoutSeconds is < 1 or > 120 || options.MaxDurationSeconds is < 1 or > 600 ||
            options.MaxSitemapBytes is < 1 or > 52428800 || options.MaxSitemaps is < 1 or > 100 ||
            options.MaxLinkChecks is < 0 or > 10000)
            throw new ArgumentException("Invalid OwnedLinks severity, domains or network limits.");
        foreach (var domain in options.Domains)
        {
            if (domain is null || string.IsNullOrWhiteSpace(domain.Domain) ||
                !Uri.TryCreate("https://" + domain.Domain, UriKind.Absolute, out var origin) ||
                domain.Domain.IndexOfAny(['/', '\\', '?', '#', '@']) >= 0 || origin.AbsolutePath != "/" ||
                origin.UserInfo.Length > 0 || origin.Query.Length > 0 || origin.Fragment.Length > 0)
                throw new ArgumentException("OwnedLinks domains must be exact hostnames with an optional port.");
            var sitemap = string.IsNullOrWhiteSpace(domain.SitemapUrl) ? new Uri(origin, "/sitemap.xml") :
                Uri.TryCreate(domain.SitemapUrl, UriKind.Absolute, out var explicitUri) ? explicitUri : null;
            if (sitemap is null || !IsHttp(sitemap) || sitemap.Authority != origin.Authority ||
                sitemap.UserInfo.Length > 0 || sitemap.Fragment.Length > 0)
                throw new ArgumentException("OwnedLinks sitemap URLs must use HTTP(S) on their configured domain.");
            if (!_domains.TryAdd(origin.Authority, sitemap))
                throw new ArgumentException("OwnedLinks domains must be unique.");
        }
        _client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            UseCookies = false
        }) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge.Web-OwnedLinks/1.0");
        _budget = new CancellationTokenSource();
    }

    /// <summary>Returns false for unowned/non-HTTP URLs; emits stable findings for checked unknown/broken URLs.</summary>
    internal bool Check(string href, string source, bool useSitemap, out bool broken)
    {
        broken = false;
        if (!Uri.TryCreate(href.StartsWith("//", StringComparison.Ordinal) ? "https:" + href : href,
                UriKind.Absolute, out var uri) || !IsOwned(uri)) return false;
        var key = Key(uri);
        if (_loadedDomains.Count == 0) _budget.CancelAfter(TimeSpan.FromSeconds(_options.MaxDurationSeconds));
        if (_loadedDomains.Add(uri.Authority))
        {
            try { LoadSitemapAsync(_domains[uri.Authority], 0).GetAwaiter().GetResult(); }
            catch (Exception ex) when (IsNetworkFailure(ex))
            {
                // Sitemap failure is visible even if an individual fallback later succeeds.
                _addIssue("warning", "owned-link", null,
                    $"Owned-domain sitemap unavailable/unknown for '{uri.Authority}': {Reason(ex)}.",
                    "sitemap-unavailable:" + IssueIdentity(uri.Authority));
            }
        }
        Result result;
        if (useSitemap && _pages.Contains(key)) result = new(false, null);
        else if (!_links.TryGetValue(key, out result!))
        {
            if (_links.Count >= _options.MaxLinkChecks)
                result = new(false, "HTTP fallback URL limit reached");
            else
            {
                try { result = ProbeAsync(uri).GetAwaiter().GetResult(); }
                catch (Exception ex) when (IsNetworkFailure(ex)) { result = new(false, Reason(ex)); }
                _links[key] = result;
            }
        }
        broken = result.Broken;
        if (broken)
            _addIssue(_options.Severity, "owned-link", source,
                $"Broken owned-domain link '{href}' (HTTP 404/410).", "broken:" + IssueIdentity(key, source));
        else if (result.Unknown is not null)
            _addIssue("warning", "owned-link", source,
                $"Owned-domain link unknown '{href}': {result.Unknown}.", "unknown:" + IssueIdentity(key, source));
        return true;
    }

    private async Task LoadSitemapAsync(Uri uri, int depth)
    {
        if (_sitemaps.Contains(Key(uri))) return;
        if (depth > 5 || _sitemaps.Count >= _options.MaxSitemaps)
            throw new InvalidOperationException("Sitemap document/depth limit reached");
        _sitemaps.Add(Key(uri));
        using var timeout = RequestTimeout();
        using var response = await GetAsync(uri, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > _options.MaxSitemapBytes)
            throw new InvalidOperationException("Sitemap byte limit exceeded");
        using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var data = await ReadBoundedAsync(input, timeout.Token).ConfigureAwait(false);
        // .xml.gz files may be served without Content-Encoding. HTTP decompression has
        // already handled encoded responses; the magic bytes avoid decoding twice.
        using var expanded = data.Length >= 2 && data.GetBuffer()[0] == 0x1f && data.GetBuffer()[1] == 0x8b
            ? await ExpandGzipAsync(data, timeout.Token).ConfigureAwait(false) : null;
        var document = SitemapReader.Read(expanded ?? data, strict: true);
        var finalUri = response.RequestMessage!.RequestUri!;
        foreach (var entry in document.Entries)
        {
            if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var target) || !IsOwned(target))
                throw new InvalidOperationException("Sitemap contains an invalid or unowned location");
            if (document.IsIndex)
            {
                if (target.Authority != finalUri.Authority)
                    throw new InvalidOperationException("Sitemap index child is outside its domain");
                await LoadSitemapAsync(target, depth + 1).ConfigureAwait(false);
            }
            else _pages.Add(Key(target));
        }
    }

    private async Task<MemoryStream> ReadBoundedAsync(Stream input, CancellationToken token)
    {
        var data = new MemoryStream();
        try
        {
            var buffer = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if (data.Length + read > _options.MaxSitemapBytes)
                    throw new InvalidOperationException("Sitemap byte limit exceeded");
                data.Write(buffer, 0, read);
            }
            data.Position = 0;
            return data;
        }
        catch { data.Dispose(); throw; }
    }

    private async Task<MemoryStream> ExpandGzipAsync(Stream input, CancellationToken token)
    {
        using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
        return await ReadBoundedAsync(gzip, token).ConfigureAwait(false);
    }

    private async Task<Result> ProbeAsync(Uri uri)
    {
        using var timeout = RequestTimeout();
        using var response = await GetAsync(uri, timeout.Token).ConfigureAwait(false);
        return response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone ? new(true, null) :
            response.IsSuccessStatusCode ? new(false, null) : new(false, $"HTTP {(int)response.StatusCode}");
    }

    /// <summary>Follows at most five redirects and never requests an unconfigured destination.</summary>
    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (var redirects = 0; ; redirects++)
        {
            token.ThrowIfCancellationRequested();
            if (!IsOwned(uri)) throw new InvalidOperationException("Redirect leaves configured owned domains");
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Key(uri)));
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (redirects >= 5 || location is null || !Uri.TryCreate(uri, location, out var next))
                throw new InvalidOperationException("Redirect limit reached or invalid redirect location");
            uri = next;
        }
    }

    private CancellationTokenSource RequestTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(_budget.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        return timeout;
    }
    private bool IsOwned(Uri uri) => IsHttp(uri) && uri.UserInfo.Length == 0 && _domains.ContainsKey(uri.Authority);
    private static bool IsHttp(Uri uri) => uri.Scheme is "http" or "https";
    // Audit hints are lowercased and punctuation-normalized. Hash exact URI bytes
    // into a hex alphabet first so case, separators and query values stay distinct.
    private static string IssueIdentity(string value, string? source = null) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source is null ? value : source + "\0" + value)));
    private static string Key(Uri uri) => new UriBuilder(uri) { Fragment = string.Empty }.Uri.AbsoluteUri;
    private static bool IsNetworkFailure(Exception ex) => ex is HttpRequestException or IOException or
        OperationCanceledException or InvalidOperationException or InvalidDataException or System.Xml.XmlException;
    private static string Reason(Exception ex) => ex is OperationCanceledException ? "Network timeout or total duration limit reached" :
        ex is HttpRequestException request && request.StatusCode.HasValue ? $"HTTP {(int)request.StatusCode}" :
        ex is HttpRequestException or IOException ? "Network request failed" : ex.Message;
    public void Dispose() { _client.Dispose(); _budget.Dispose(); }
}
