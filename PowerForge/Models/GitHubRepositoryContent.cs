namespace PowerForge;

/// <summary>
/// Source or manually assigned status of a sponsor.
/// </summary>
public enum GitHubSponsorStatus
{
    /// <summary>The sponsor is currently active.</summary>
    Current,

    /// <summary>The sponsor is no longer active.</summary>
    Former
}

/// <summary>
/// Entity type represented by a sponsor record.
/// </summary>
public enum GitHubSponsorEntityType
{
    /// <summary>A GitHub user account.</summary>
    User,

    /// <summary>A GitHub organization account.</summary>
    Organization,

    /// <summary>A sponsor supplied only through local configuration.</summary>
    Manual
}

/// <summary>
/// Markdown layout used for a generated Sponsors block.
/// </summary>
public enum GitHubSponsorsMarkdownLayout
{
    /// <summary>Tiered roster with optional former-sponsor recognition.</summary>
    Full,

    /// <summary>Compact avatar row suitable for a project README.</summary>
    Compact
}

/// <summary>
/// Top-level configuration for generated GitHub repository content.
/// </summary>
public sealed class GitHubRepositoryContentSpec
{
    /// <summary>Optional GitHub GraphQL endpoint.</summary>
    public string? GraphQlEndpoint { get; set; }

    /// <summary>Optional GitHub token. Prefer <see cref="TokenEnvName"/> in checked-in configuration.</summary>
    public string? Token { get; set; }

    /// <summary>Environment variable used to resolve the token when <see cref="Token"/> is empty.</summary>
    public string TokenEnvName { get; set; } = "GITHUB_TOKEN";

    /// <summary>GitHub Sponsors content settings.</summary>
    public GitHubSponsorsContentSpec Sponsors { get; set; } = new();
}

/// <summary>
/// Query used to retrieve public current and former GitHub sponsors.
/// </summary>
public sealed class GitHubSponsorsQuery
{
    /// <summary>GitHub user or organization login receiving sponsorships.</summary>
    public string SponsorableLogin { get; set; } = string.Empty;

    /// <summary>GitHub token used for GraphQL requests.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Optional GitHub GraphQL endpoint.</summary>
    public string? GraphQlEndpoint { get; set; }

    /// <summary>Whether public former sponsors should also be retrieved.</summary>
    public bool IncludeFormer { get; set; } = true;

    /// <summary>GraphQL records requested per page.</summary>
    public int PageSize { get; set; } = 100;
}

/// <summary>
/// Configuration for GitHub Sponsors retrieval, recognition, and document output.
/// </summary>
public sealed class GitHubSponsorsContentSpec
{
    /// <summary>Whether Sponsors content generation is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>GitHub user or organization login receiving sponsorships.</summary>
    public string SponsorableLogin { get; set; } = string.Empty;

    /// <summary>Whether public former sponsors should also be retrieved.</summary>
    public bool IncludeFormer { get; set; } = true;

    /// <summary>Whether generation fails when no current sponsors are returned.</summary>
    public bool FailOnEmpty { get; set; } = true;

    /// <summary>Whether tiered generation fails when GitHub withholds funding-tier data for every current GitHub sponsor.</summary>
    public bool RequireFundingTierData { get; set; }

    /// <summary>GraphQL records requested per page.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Opt-in recognition-tier settings.</summary>
    public GitHubSponsorTierRecognitionSpec TierRecognition { get; set; } = new();

    /// <summary>Per-account recognition and presentation overrides.</summary>
    public GitHubSponsorOverrideSpec[] Overrides { get; set; } = Array.Empty<GitHubSponsorOverrideSpec>();

    /// <summary>Manually maintained sponsors, including non-GitHub people or companies.</summary>
    public GitHubManualSponsorSpec[] ManualEntries { get; set; } = Array.Empty<GitHubManualSponsorSpec>();

    /// <summary>Markdown documents or blocks produced by the run.</summary>
    public GitHubSponsorsOutputSpec[] Outputs { get; set; } = Array.Empty<GitHubSponsorsOutputSpec>();
}

/// <summary>
/// Opt-in mapping from GitHub funding tiers to public recognition tiers.
/// </summary>
public sealed class GitHubSponsorTierRecognitionSpec
{
    /// <summary>Whether public output is grouped by recognition tier.</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether the standard Principal/Platinum/Gold/Silver/Bronze bands are used when <see cref="Tiers"/> is empty.</summary>
    public bool UseDefaultTiers { get; set; } = true;

    /// <summary>Recognition tier used when GitHub does not expose a selected tier or no band matches.</summary>
    public string UnmappedTierKey { get; set; } = "Sponsors";

    /// <summary>Custom recognition tier bands. Higher minimum amounts are matched first.</summary>
    public GitHubSponsorRecognitionTierSpec[] Tiers { get; set; } = Array.Empty<GitHubSponsorRecognitionTierSpec>();
}

/// <summary>
/// One public sponsor-recognition tier.
/// </summary>
public sealed class GitHubSponsorRecognitionTierSpec
{
    /// <summary>Stable key referenced by overrides and result records.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Heading shown in a full Sponsors roster.</summary>
    public string Heading { get; set; } = string.Empty;

    /// <summary>Minimum GitHub monthly tier price mapped to this recognition tier.</summary>
    public int? MinimumMonthlyDollars { get; set; }

    /// <summary>Sort order in generated output. Lower values appear first.</summary>
    public int Order { get; set; }

    /// <summary>Avatar size in pixels for this recognition tier.</summary>
    public int AvatarSize { get; set; } = 64;
}

/// <summary>
/// Per-account override applied after GitHub data is retrieved.
/// </summary>
public sealed class GitHubSponsorOverrideSpec
{
    /// <summary>GitHub login or manual-entry key matched case-insensitively.</summary>
    public string Login { get; set; } = string.Empty;

    /// <summary>Optional recognition tier key. This always overrides the GitHub funding tier mapping.</summary>
    public string? RecognitionTierKey { get; set; }

    /// <summary>Optional public display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Optional public profile or company URL.</summary>
    public string? ProfileUrl { get; set; }

    /// <summary>Optional avatar or logo URL.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>Whether the matched sponsor is omitted from generated content.</summary>
    public bool Exclude { get; set; }
}

/// <summary>
/// Manually maintained sponsor used for companies, external funding, or explicit recognition.
/// </summary>
public sealed class GitHubManualSponsorSpec
{
    /// <summary>Stable key used for sorting and overrides.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Public display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional GitHub login. When present, GitHub profile and avatar defaults can be derived.</summary>
    public string? Login { get; set; }

    /// <summary>Optional public profile or company URL.</summary>
    public string? ProfileUrl { get; set; }

    /// <summary>Optional avatar or logo URL.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>Optional recognition tier key.</summary>
    public string? RecognitionTierKey { get; set; }

    /// <summary>Whether the manual entry is a former sponsor.</summary>
    public bool Former { get; set; }
}

/// <summary>
/// One generated Sponsors Markdown destination.
/// </summary>
public sealed class GitHubSponsorsOutputSpec
{
    /// <summary>Destination Markdown path, relative to the repository root unless absolute.</summary>
    public string Path { get; set; } = "SPONSORS.md";

    /// <summary>Marker block identifier.</summary>
    public string BlockId { get; set; } = "sponsors";

    /// <summary>Generated layout.</summary>
    public GitHubSponsorsMarkdownLayout Layout { get; set; } = GitHubSponsorsMarkdownLayout.Full;

    /// <summary>Heading used when creating a new document.</summary>
    public string Title { get; set; } = "Sponsors";

    /// <summary>Optional Markdown placed before generated sponsor groups.</summary>
    public string? Introduction { get; set; }

    /// <summary>Optional Markdown placed after generated sponsor groups.</summary>
    public string? Closing { get; set; }

    /// <summary>Whether former sponsors are included in this output.</summary>
    public bool IncludeFormer { get; set; } = true;

    /// <summary>Whether a missing destination document may be created.</summary>
    public bool CreateIfMissing { get; set; }

    /// <summary>Behavior when the document exists but the marker block is missing.</summary>
    public ManagedMarkdownMissingBlockBehavior MissingBlockBehavior { get; set; } = ManagedMarkdownMissingBlockBehavior.Fail;

    /// <summary>Default avatar size in pixels when recognition tiers do not provide one.</summary>
    public int AvatarSize { get; set; } = 64;

    /// <summary>Maximum current sponsors shown by a compact output. Zero includes all.</summary>
    public int MaxEntries { get; set; }

    /// <summary>Optional link added after a compact output, such as <c>SPONSORS.md</c>.</summary>
    public string? MoreLink { get; set; }
}

/// <summary>
/// Normalized public sponsor record used for results and rendering.
/// </summary>
public sealed class GitHubSponsorRecord
{
    /// <summary>GitHub login or manual-entry key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>GitHub login when available.</summary>
    public string? Login { get; set; }

    /// <summary>Public display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Public profile or company URL.</summary>
    public string? ProfileUrl { get; set; }

    /// <summary>Avatar or logo URL.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>Current or former status.</summary>
    public GitHubSponsorStatus Status { get; set; }

    /// <summary>GitHub user, organization, or manual entity type.</summary>
    public GitHubSponsorEntityType EntityType { get; set; }

    /// <summary>Public recognition tier after mapping and manual overrides.</summary>
    public string? RecognitionTierKey { get; set; }
}

/// <summary>
/// Internal sponsor source data. Funding amounts are used only for opt-in recognition mapping and are never exposed in public results.
/// </summary>
internal sealed class GitHubSponsorSourceRecord
{
    internal GitHubSponsorRecord Sponsor { get; set; } = new();
    internal int? FundingTierMonthlyDollars { get; set; }
}

/// <summary>
/// Sponsors and recognition tiers prepared for deterministic rendering.
/// </summary>
public sealed class GitHubSponsorRecognitionResult
{
    /// <summary>Whether tier grouping is enabled.</summary>
    public bool TierRecognitionEnabled { get; set; }

    /// <summary>Normalized tiers in display order.</summary>
    public GitHubSponsorRecognitionTierSpec[] Tiers { get; set; } = Array.Empty<GitHubSponsorRecognitionTierSpec>();

    /// <summary>Normalized, overridden, and sorted sponsor records.</summary>
    public GitHubSponsorRecord[] Sponsors { get; set; } = Array.Empty<GitHubSponsorRecord>();
}

/// <summary>
/// Summary of one generated Sponsors document update.
/// </summary>
public sealed class GitHubSponsorsDocumentResult
{
    /// <summary>Absolute output path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Managed block identifier.</summary>
    public string BlockId { get; set; } = string.Empty;

    /// <summary>Generated layout.</summary>
    public GitHubSponsorsMarkdownLayout Layout { get; set; }

    /// <summary>Whether the file content changed.</summary>
    public bool Changed { get; set; }

    /// <summary>Whether a new file was created.</summary>
    public bool Created { get; set; }

    /// <summary>Whether a new managed block was appended.</summary>
    public bool Appended { get; set; }
}

/// <summary>
/// Result of a GitHub repository-content synchronization run.
/// </summary>
public sealed class GitHubRepositoryContentResult
{
    /// <summary>Whether all requested content was generated successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Sponsorable account used for the run.</summary>
    public string SponsorableLogin { get; set; } = string.Empty;

    /// <summary>Normalized current sponsors.</summary>
    public GitHubSponsorRecord[] CurrentSponsors { get; set; } = Array.Empty<GitHubSponsorRecord>();

    /// <summary>Normalized former sponsors.</summary>
    public GitHubSponsorRecord[] FormerSponsors { get; set; } = Array.Empty<GitHubSponsorRecord>();

    /// <summary>Generated document updates.</summary>
    public GitHubSponsorsDocumentResult[] Documents { get; set; } = Array.Empty<GitHubSponsorsDocumentResult>();

    /// <summary>Optional warning or failure message.</summary>
    public string? Message { get; set; }
}
