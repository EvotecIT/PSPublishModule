namespace PowerForge;

/// <summary>
/// Orchestrates config-driven GitHub repository content generation.
/// </summary>
public sealed class GitHubRepositoryContentService
{
    private readonly ILogger _logger;
    private readonly GitHubSponsorsClient _sponsorsClient;
    private readonly GitHubSponsorRecognitionService _recognitionService;
    private readonly GitHubSponsorsMarkdownRenderer _renderer;
    private readonly ManagedMarkdownDocumentUpdater _documentUpdater;

    /// <summary>
    /// Creates a repository-content service.
    /// </summary>
    /// <param name="logger">Optional progress logger.</param>
    /// <param name="sponsorsClient">Optional Sponsors API client.</param>
    /// <param name="recognitionService">Optional recognition service.</param>
    /// <param name="renderer">Optional Markdown renderer.</param>
    /// <param name="documentUpdater">Optional managed-document updater.</param>
    public GitHubRepositoryContentService(
        ILogger? logger = null,
        GitHubSponsorsClient? sponsorsClient = null,
        GitHubSponsorRecognitionService? recognitionService = null,
        GitHubSponsorsMarkdownRenderer? renderer = null,
        ManagedMarkdownDocumentUpdater? documentUpdater = null)
    {
        _logger = logger ?? new NullLogger();
        _sponsorsClient = sponsorsClient ?? new GitHubSponsorsClient();
        _recognitionService = recognitionService ?? new GitHubSponsorRecognitionService();
        _renderer = renderer ?? new GitHubSponsorsMarkdownRenderer();
        _documentUpdater = documentUpdater ?? new ManagedMarkdownDocumentUpdater();
    }

    /// <summary>
    /// Synchronizes all enabled repository-content sections.
    /// </summary>
    /// <param name="spec">Repository-content specification.</param>
    /// <param name="baseDirectory">Base directory used for relative output paths.</param>
    /// <param name="restrictedOutputRoot">Optional root that every output must remain within. Reparse-point output paths are rejected.</param>
    /// <returns>Synchronization result.</returns>
    public GitHubRepositoryContentResult Sync(
        GitHubRepositoryContentSpec spec,
        string? baseDirectory = null,
        string? restrictedOutputRoot = null)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        var sponsorsSpec = spec.Sponsors ?? throw new InvalidOperationException("Sponsors configuration is required.");
        if (!sponsorsSpec.Enabled)
            throw new InvalidOperationException("No GitHub repository-content sections are enabled. Set sponsors.enabled to true.");

        var login = ResolveSponsorableLogin(sponsorsSpec.SponsorableLogin);
        var token = ResolveToken(spec.Token, spec.TokenEnvName);
        var outputs = sponsorsSpec.Outputs ?? Array.Empty<GitHubSponsorsOutputSpec>();
        if (outputs.Length == 0)
            throw new InvalidOperationException("At least one Sponsors output is required.");

        var includeFormer = sponsorsSpec.IncludeFormer && outputs.Any(output => output.IncludeFormer);
        _logger.Info($"Retrieving public GitHub Sponsors for {login}.");
        var tierRecognitionEnabled = sponsorsSpec.TierRecognition?.Enabled ?? false;
        var source = _sponsorsClient.GetSponsorSources(new GitHubSponsorsQuery
        {
            SponsorableLogin = login,
            Token = token,
            GraphQlEndpoint = spec.GraphQlEndpoint,
            IncludeFormer = includeFormer,
            PageSize = sponsorsSpec.PageSize
        }, includeFundingTierData: tierRecognitionEnabled);

        var currentGitHubSponsors = source.Where(sponsor => sponsor.Sponsor.Status == GitHubSponsorStatus.Current).ToArray();
        if (tierRecognitionEnabled && sponsorsSpec.RequireFundingTierData &&
            currentGitHubSponsors.Length > 0 && currentGitHubSponsors.All(sponsor => sponsor.FundingTierMonthlyDollars is null))
        {
            throw new InvalidOperationException(
                "GitHub withheld funding-tier data for every current sponsor. No documents were modified. " +
                "Use a maintainer-authorized token or disable sponsors.requireFundingTierData.");
        }

        var recognition = _recognitionService.Prepare(source, sponsorsSpec);
        var current = recognition.Sponsors.Where(sponsor => sponsor.Status == GitHubSponsorStatus.Current).ToArray();
        var former = recognition.Sponsors.Where(sponsor => sponsor.Status == GitHubSponsorStatus.Former).ToArray();
        if (sponsorsSpec.FailOnEmpty && current.Length == 0)
            throw new InvalidOperationException("GitHub Sponsors returned no public current sponsors. No documents were modified.");

        var basePath = ResolveBaseDirectory(baseDirectory);
        var planned = BuildPlans(outputs, recognition, basePath, restrictedOutputRoot);
        foreach (var plan in planned)
            _documentUpdater.ValidateUpdate(plan.Request);

        var documentResults = new List<GitHubSponsorsDocumentResult>();
        foreach (var plan in planned)
        {
            var update = _documentUpdater.Update(plan.Request);
            documentResults.Add(new GitHubSponsorsDocumentResult
            {
                Path = update.Path,
                BlockId = update.BlockId,
                Layout = plan.Output.Layout,
                Changed = update.Changed,
                Created = update.Created,
                Appended = update.Appended
            });
        }

        _logger.Info($"Generated {documentResults.Count} Sponsors output(s): {current.Length} current, {former.Length} former.");
        return new GitHubRepositoryContentResult
        {
            Success = true,
            SponsorableLogin = login,
            CurrentSponsors = current,
            FormerSponsors = former,
            Documents = documentResults.ToArray()
        };
    }

    private List<OutputPlan> BuildPlans(
        GitHubSponsorsOutputSpec[] outputs,
        GitHubSponsorRecognitionResult recognition,
        string basePath,
        string? restrictedOutputRoot)
    {
        var plans = new List<OutputPlan>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in outputs)
        {
            if (output is null) throw new InvalidOperationException("Sponsors outputs cannot contain null entries.");
            if (string.IsNullOrWhiteSpace(output.Path)) throw new InvalidOperationException("Sponsors output path is required.");
            if (string.IsNullOrWhiteSpace(output.BlockId)) throw new InvalidOperationException("Sponsors output block id is required.");

            var path = System.IO.Path.IsPathRooted(output.Path)
                ? System.IO.Path.GetFullPath(output.Path)
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(basePath, output.Path));
            ValidateRestrictedOutputPath(path, restrictedOutputRoot);
            var destinationKey = path + "|" + output.BlockId.Trim();
            if (!destinations.Add(destinationKey))
                throw new InvalidOperationException($"Sponsors output '{output.BlockId}' is configured more than once for '{path}'.");

            var markdown = _renderer.Render(recognition, output);
            plans.Add(new OutputPlan(output, new ManagedMarkdownUpdateRequest
            {
                Path = path,
                BlockId = output.BlockId,
                Markdown = markdown,
                CreateIfMissing = output.CreateIfMissing,
                MissingBlockBehavior = output.MissingBlockBehavior,
                NewDocumentTitle = output.Title
            }));
        }
        return plans;
    }

    private static string ResolveSponsorableLogin(string? configured)
    {
        var login = string.IsNullOrWhiteSpace(configured)
            ? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY_OWNER")
            : configured;
        if (string.IsNullOrWhiteSpace(login))
            throw new InvalidOperationException("Sponsorable login is required. Configure sponsors.sponsorableLogin or set GITHUB_REPOSITORY_OWNER.");
        return login!.Trim();
    }

    private static string ResolveToken(string? configured, string? tokenEnvironmentName)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured!.Trim();
        var environmentName = string.IsNullOrWhiteSpace(tokenEnvironmentName) ? "GITHUB_TOKEN" : tokenEnvironmentName!.Trim();
        var token = Environment.GetEnvironmentVariable(environmentName);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"GitHub token is required. Set {environmentName} or provide a token at runtime.");
        return token!.Trim();
    }

    private static string ResolveBaseDirectory(string? baseDirectory)
    {
        var value = string.IsNullOrWhiteSpace(baseDirectory) ? Directory.GetCurrentDirectory() : baseDirectory!;
        return System.IO.Path.GetFullPath(value);
    }

    private static void ValidateRestrictedOutputPath(string outputPath, string? restrictedOutputRoot)
    {
        if (string.IsNullOrWhiteSpace(restrictedOutputRoot))
            return;

        var fullRoot = System.IO.Path.GetFullPath(restrictedOutputRoot!.Trim().Trim('"'));
        var volumeRoot = System.IO.Path.GetPathRoot(fullRoot) ?? string.Empty;
        var root = fullRoot.Length > volumeRoot.Length
            ? fullRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            : fullRoot;
        var comparison = System.IO.Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPrefix = root.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                         root.EndsWith(System.IO.Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? root
            : root + System.IO.Path.DirectorySeparatorChar;
        if (!outputPath.Equals(root, comparison) && !outputPath.StartsWith(rootPrefix, comparison))
            throw new InvalidOperationException($"Managed output is outside the restricted output root: {outputPath}");

        if (outputPath.Equals(root, comparison))
            return;

        var relative = outputPath.Substring(rootPrefix.Length);
        var current = root;
        foreach (var segment in relative.Split(new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = System.IO.Path.Combine(current, segment);
            var existingEntry = FindPathEntryWithoutFollowing(current);
            if (existingEntry is null)
                continue;

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(existingEntry);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new InvalidOperationException(
                    $"Managed output path contains an existing entry that cannot be safely inspected: {existingEntry}",
                    exception);
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Managed output path traverses a symbolic link or reparse point and cannot be safely restricted: {existingEntry}");
            }
        }
    }

    private static string? FindPathEntryWithoutFollowing(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            return path;

        var parent = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            return null;

        string? exact = null;
        var aliases = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(parent))
        {
            var candidate = System.IO.Path.GetFullPath(entry);
            if (candidate.Equals(path, StringComparison.Ordinal))
                exact = candidate;
            else if (candidate.Equals(path, StringComparison.OrdinalIgnoreCase))
                aliases.Add(candidate);
        }

        if (exact is not null)
            return exact;
        if (aliases.Count == 1)
            return aliases[0];
        if (aliases.Count > 1)
        {
            throw new InvalidOperationException(
                $"Managed output path has ambiguous case-colliding filesystem entries and cannot be safely restricted: {path}");
        }
        return null;
    }

    private sealed class OutputPlan
    {
        internal OutputPlan(GitHubSponsorsOutputSpec output, ManagedMarkdownUpdateRequest request)
        {
            Output = output;
            Request = request;
        }

        internal GitHubSponsorsOutputSpec Output { get; }
        internal ManagedMarkdownUpdateRequest Request { get; }
    }
}
