using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Scans final machine-facing artifacts without executing referenced instructions.</summary>
public sealed partial class WebAgentContentSecurityScanner : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;
    private readonly bool _pinVerifiedExternalHostAddress;

    /// <summary>Creates a scanner using a bounded public-registry HTTP client.</summary>
    public WebAgentContentSecurityScanner()
        : this(CreateDefaultHttpClient(), disposeClient: true, pinVerifiedExternalHostAddress: true)
    {
    }

    internal WebAgentContentSecurityScanner(
        HttpClient httpClient,
        bool disposeClient = false,
        bool pinVerifiedExternalHostAddress = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _disposeClient = disposeClient;
        _pinVerifiedExternalHostAddress = pinVerifiedExternalHostAddress;
    }

    /// <summary>Scans configured artifacts and verifies extracted package and host references.</summary>
    public WebAgentContentSecurityResult Scan(WebAgentContentSecurityOptions options)
        => Scan(options, options?.SiteRoot ?? string.Empty);

    /// <summary>Scans configured artifacts using an explicit site root without mutating the supplied options.</summary>
    public WebAgentContentSecurityResult Scan(WebAgentContentSecurityOptions options, string siteRoot)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(siteRoot))
            throw new ArgumentException("SiteRoot is required.", nameof(siteRoot));
        if (options.RequestTimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "RequestTimeoutSeconds must be greater than zero.");
        if (options.MaxArtifactBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxArtifactBytes must be greater than zero.");
        if (options.PublicationCatalogMaxAgeHours < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "PublicationCatalogMaxAgeHours must be zero or greater.");
        if (options.MaxPackageReferences <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxPackageReferences must be greater than zero.");
        if (options.MaxExternalHosts <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxExternalHosts must be greater than zero.");
        if (options.MaxRegistryResponseBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRegistryResponseBytes must be greater than zero.");
        if (options.MaxNetworkDurationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxNetworkDurationSeconds must be greater than zero.");

        siteRoot = Path.GetFullPath(siteRoot);
        if (!Directory.Exists(siteRoot))
            throw new DirectoryNotFoundException($"Site root not found: {siteRoot}");

        var findings = new List<WebAgentContentSecurityFinding>();
        var packages = new List<WebAgentPackageReference>();
        var urls = new HashSet<Uri>(UriComparer.Instance);
        var artifactCount = 0;
        var configuredPaths = NormalizeArtifactPaths(options.Files).ToArray();
        if (configuredPaths.Length == 0)
            throw new ArgumentException("At least one agent-content artifact path is required.", nameof(options));

        foreach (var configuredPath in configuredPaths)
        {
            var fullPath = ResolveArtifactPath(siteRoot, configuredPath);
            if (!File.Exists(fullPath))
            {
                AddFinding(findings, "error", "PFAGENT.ARTIFACT.MISSING", configuredPath, null,
                    "Configured agent-facing artifact does not exist.");
                continue;
            }

            var length = new FileInfo(fullPath).Length;
            if (length > options.MaxArtifactBytes)
            {
                AddFinding(findings, "error", "PFAGENT.ARTIFACT.TOO_LARGE", configuredPath, null,
                    $"Artifact is {length} bytes; the configured maximum is {options.MaxArtifactBytes} bytes.");
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(fullPath, new UTF8Encoding(false, true));
            }
            catch (DecoderFallbackException ex)
            {
                AddFinding(findings, "error", "PFAGENT.TEXT.INVALID_UTF8", configuredPath, null,
                    $"Artifact is not valid UTF-8: {ex.Message}");
                continue;
            }

            artifactCount++;
            var segments = ExtractTextSegments(content, Path.GetExtension(fullPath), configuredPath, findings);
            foreach (var segment in segments)
            {
                ScanPackageSourceEnvironmentOverrides(segment.Text, configuredPath, segment.LineOffset, segment.CountLogicalLines, findings);
                ScanInvisibleUnicode(segment.Text, configuredPath, findings, segment.LineOffset, segment.CountLogicalLines);
                if (options.CheckPromptInjection)
                    ScanPromptInjection(segment.Text, configuredPath, findings, segment.LineOffset, segment.CountLogicalLines);
                packages.AddRange(ExtractPackageReferences(segment.Text, configuredPath, segment.LineOffset, segment.CountLogicalLines, findings));
                ExtractUrls(segment.Text, urls);
            }
        }

        packages = packages
            .GroupBy(PackageIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static package => package.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Line)
            .ToList();

        using var networkBudget = new CancellationTokenSource(TimeSpan.FromSeconds(options.MaxNetworkDurationSeconds));
        var verifiedPackages = 0;
        if (packages.Count > options.MaxPackageReferences)
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.LIMIT_EXCEEDED", null, null,
                $"Artifacts contain {packages.Count} unique package references; the configured maximum is {options.MaxPackageReferences}. No registry requests were sent.");
        }
        else if (options.VerifyPackages && !findings.Any(static finding =>
                     finding.Code is "PFAGENT.PACKAGE.UNTRUSTED_SOURCE" or
                         "PFAGENT.PACKAGE.UNVERIFIABLE_ENVIRONMENT"))
        {
            var catalog = LoadOwnerCatalog(options, findings);
            var verificationCache = new Dictionary<string, PackageVerificationOutcome>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in packages)
            {
                if (networkBudget.IsCancellationRequested)
                {
                    AddFinding(findings, "error", "PFAGENT.NETWORK.TIME_BUDGET", null, null,
                        $"Network verification exceeded the configured {options.MaxNetworkDurationSeconds}-second total time budget.");
                    break;
                }
                if (VerifyPackage(package, options, catalog, verificationCache, findings, networkBudget.Token))
                    verifiedPackages++;
            }
        }

        var externalHostCount = 0;
        if (options.VerifyExternalHosts)
        {
            if (networkBudget.IsCancellationRequested)
            {
                AddFinding(findings, "error", "PFAGENT.NETWORK.TIME_BUDGET", null, null,
                    $"Network verification exceeded the configured {options.MaxNetworkDurationSeconds}-second total time budget before external hosts could be checked.");
            }
            else
            {
                externalHostCount = VerifyExternalHosts(urls, options, findings, networkBudget.Token);
            }
        }

        return new WebAgentContentSecurityResult
        {
            Success = findings.All(static finding => !finding.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)),
            ArtifactCount = artifactCount,
            PackageReferenceCount = packages.Count,
            VerifiedPackageCount = verifiedPackages,
            ExternalHostCount = externalHostCount,
            Findings = findings.ToArray()
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposeClient)
            _httpClient.Dispose();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxAutomaticRedirections = 5,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge.Web-AgentContentSecurity/1.0");
        return client;
    }

    private static IEnumerable<string> NormalizeArtifactPaths(IEnumerable<string>? paths)
        => (paths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim().Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string ResolveArtifactPath(string siteRoot, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException($"Agent-content artifact paths must be relative to siteRoot: {relativePath}");

        var fullPath = Path.GetFullPath(Path.Combine(siteRoot, relativePath));
        var relative = Path.GetRelativePath(siteRoot, fullPath);
        if (relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException($"Agent-content artifact path escapes siteRoot: {relativePath}");
        }
        return fullPath;
    }

    private static IReadOnlyList<TextSegment> ExtractTextSegments(
        string content,
        string extension,
        string path,
        List<WebAgentContentSecurityFinding> findings)
    {
        if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return new[] { new TextSegment(content, 0, true) };

        var segments = new List<TextSegment>();
        try
        {
            var utf8 = Encoding.UTF8.GetBytes(content);
            var reader = new Utf8JsonReader(utf8, isFinalBlock: true, state: default);
            var scannedThrough = 0;
            var physicalLineOffset = 0;
            while (reader.Read())
            {
                if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName))
                    continue;
                var value = reader.GetString();
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                var tokenStart = checked((int)reader.TokenStartIndex);
                for (var index = scannedThrough; index < tokenStart; index++)
                {
                    if (utf8[index] == (byte)'\n')
                        physicalLineOffset++;
                }
                scannedThrough = tokenStart;
                segments.Add(new TextSegment(value, physicalLineOffset, false));
            }
        }
        catch (JsonException ex)
        {
            AddFinding(findings, "error", "PFAGENT.ARTIFACT.INVALID_JSON", path, ex.LineNumber is null ? null : checked((int)ex.LineNumber.Value + 1),
                $"Configured JSON artifact is invalid: {ex.Message}");
        }
        return segments;
    }

    private static void AddFinding(
        ICollection<WebAgentContentSecurityFinding> findings,
        string severity,
        string code,
        string? path,
        int? line,
        string message)
        => findings.Add(new WebAgentContentSecurityFinding
        {
            Severity = severity,
            Code = code,
            Path = path,
            Line = line,
            Message = message
        });

    private static string PackageIdentityKey(WebAgentPackageReference package)
        => string.Create(CultureInfo.InvariantCulture, $"{package.Ecosystem}|{package.Id}|{package.Version}");

    private sealed record TextSegment(string Text, int LineOffset, bool CountLogicalLines);

    private sealed class UriComparer : IEqualityComparer<Uri>
    {
        public static UriComparer Instance { get; } = new();
        public bool Equals(Uri? x, Uri? y) => string.Equals(x?.AbsoluteUri, y?.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(Uri obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(obj.AbsoluteUri);
    }
}
