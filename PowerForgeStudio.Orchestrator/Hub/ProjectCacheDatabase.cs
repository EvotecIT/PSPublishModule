using DBAClientX;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed class ProjectCacheDatabase
{
    private const string CurrentSchemaVersion = "1";
    private readonly SQLite _sqlite = new() { BusyTimeoutMs = 10_000 };

    public ProjectCacheDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
    }

    public string DatabasePath { get; }

    public static string GetDefaultDatabasePath()
    {
        var studioRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerForgeStudio");
        return Path.Combine(studioRoot, "hub-cache.db");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var parentDirectory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        var commands = new[]
        {
            "PRAGMA journal_mode = WAL;",
            """
            CREATE TABLE IF NOT EXISTS hub_schema(
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS hub_project_entry(
                name TEXT NOT NULL,
                root_path TEXT PRIMARY KEY,
                github_slug TEXT NULL,
                azure_devops_slug TEXT NULL,
                category TEXT NOT NULL,
                kind TEXT NOT NULL,
                repository_kind TEXT NOT NULL,
                workspace_kind TEXT NOT NULL,
                build_script_kind TEXT NOT NULL,
                primary_build_script_path TEXT NULL,
                has_powerforge_json INTEGER NOT NULL DEFAULT 0,
                has_project_build_json INTEGER NOT NULL DEFAULT 0,
                has_solution INTEGER NOT NULL DEFAULT 0,
                has_github_workflows INTEGER NOT NULL DEFAULT 0,
                has_azure_pipelines INTEGER NOT NULL DEFAULT 0,
                last_commit_utc TEXT NULL,
                last_scan_utc TEXT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS hub_github_cache(
                slug TEXT NOT NULL,
                open_pr_count INTEGER NULL,
                open_issue_count INTEGER NULL,
                latest_workflow_failed INTEGER NULL,
                latest_release_tag TEXT NULL,
                cached_at_utc TEXT NOT NULL,
                PRIMARY KEY (slug)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS hub_build_result(
                project_name TEXT NOT NULL,
                script_kind TEXT NOT NULL,
                script_path TEXT NOT NULL,
                succeeded INTEGER NOT NULL,
                output_hash TEXT NOT NULL,
                error_snippet TEXT NULL,
                duration_seconds REAL NOT NULL,
                completed_at_utc TEXT NOT NULL
            );
            """
        };

        foreach (var command in commands)
        {
            await _sqlite.ExecuteNonQueryAsync(DatabasePath, command, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            """
            INSERT INTO hub_schema(key, value, updated_at_utc)
            VALUES ('schema_version', @Version, @Now)
            ON CONFLICT(key) DO UPDATE SET value = @Version, updated_at_utc = @Now;
            """,
            new Dictionary<string, object?>
            {
                ["@Version"] = CurrentSchemaVersion,
                ["@Now"] = DateTimeOffset.UtcNow.ToString("O")
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // ─── Project Entry Cache ───

    public async Task SaveProjectEntriesAsync(
        IReadOnlyList<ProjectEntry> entries,
        CancellationToken cancellationToken = default)
    {
        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            "DELETE FROM hub_project_entry;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            await _sqlite.ExecuteNonQueryAsync(
                DatabasePath,
                """
                INSERT INTO hub_project_entry(
                    name, root_path, github_slug, azure_devops_slug,
                    category, kind, repository_kind, workspace_kind,
                    build_script_kind, primary_build_script_path,
                    has_powerforge_json, has_project_build_json, has_solution,
                    has_github_workflows, has_azure_pipelines,
                    last_commit_utc, last_scan_utc)
                VALUES (
                    @Name, @RootPath, @GitHubSlug, @AzureDevOpsSlug,
                    @Category, @Kind, @RepositoryKind, @WorkspaceKind,
                    @BuildScriptKind, @PrimaryBuildScriptPath,
                    @HasPowerForgeJson, @HasProjectBuildJson, @HasSolution,
                    @HasGitHubWorkflows, @HasAzurePipelines,
                    @LastCommitUtc, @LastScanUtc);
                """,
                new Dictionary<string, object?>
                {
                    ["@Name"] = entry.Name,
                    ["@RootPath"] = entry.RootPath,
                    ["@GitHubSlug"] = entry.GitHubSlug,
                    ["@AzureDevOpsSlug"] = entry.AzureDevOpsSlug,
                    ["@Category"] = entry.Category.ToString(),
                    ["@Kind"] = entry.Kind.ToString(),
                    ["@RepositoryKind"] = entry.RepositoryKind.ToString(),
                    ["@WorkspaceKind"] = entry.WorkspaceKind.ToString(),
                    ["@BuildScriptKind"] = entry.BuildScriptKind.ToString(),
                    ["@PrimaryBuildScriptPath"] = entry.PrimaryBuildScriptPath,
                    ["@HasPowerForgeJson"] = entry.HasPowerForgeJson ? 1 : 0,
                    ["@HasProjectBuildJson"] = entry.HasProjectBuildJson ? 1 : 0,
                    ["@HasSolution"] = entry.HasSolution ? 1 : 0,
                    ["@HasGitHubWorkflows"] = entry.HasGitHubWorkflows ? 1 : 0,
                    ["@HasAzurePipelines"] = entry.HasAzurePipelines ? 1 : 0,
                    ["@LastCommitUtc"] = entry.LastCommitUtc?.ToString("O"),
                    ["@LastScanUtc"] = entry.LastScanUtc?.ToString("O")
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<ProjectEntry>> LoadProjectEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            """
            SELECT name, root_path, github_slug, azure_devops_slug,
                   category, kind, repository_kind, workspace_kind,
                   build_script_kind, primary_build_script_path,
                   has_powerforge_json, has_project_build_json, has_solution,
                   has_github_workflows, has_azure_pipelines,
                   last_commit_utc, last_scan_utc
            FROM hub_project_entry
            ORDER BY name COLLATE NOCASE;
            """,
            reader => new ProjectEntry(
                Name: reader.GetString(0),
                RootPath: reader.GetString(1),
                GitHubSlug: reader.IsDBNull(2) ? null : reader.GetString(2),
                AzureDevOpsSlug: reader.IsDBNull(3) ? null : reader.GetString(3),
                Category: Enum.TryParse<ProjectCategory>(reader.GetString(4), true, out var cat) ? cat : ProjectCategory.Unknown,
                Kind: Enum.TryParse<ProjectKind>(reader.GetString(5), true, out var kind) ? kind : ProjectKind.Primary,
                RepositoryKind: Enum.TryParse<Domain.Catalog.ReleaseRepositoryKind>(reader.GetString(6), true, out var rk) ? rk : Domain.Catalog.ReleaseRepositoryKind.Unknown,
                WorkspaceKind: Enum.TryParse<Domain.Catalog.ReleaseWorkspaceKind>(reader.GetString(7), true, out var wk) ? wk : Domain.Catalog.ReleaseWorkspaceKind.PrimaryRepository,
                BuildScriptKind: Enum.TryParse<BuildScriptKind>(reader.GetString(8), true, out var bsk) ? bsk : BuildScriptKind.None,
                PrimaryBuildScriptPath: reader.IsDBNull(9) ? null : reader.GetString(9),
                HasPowerForgeJson: reader.GetInt32(10) == 1,
                HasProjectBuildJson: reader.GetInt32(11) == 1,
                HasSolution: reader.GetInt32(12) == 1,
                HasGitHubWorkflows: reader.GetInt32(13) == 1,
                HasAzurePipelines: reader.GetInt32(14) == 1,
                LastCommitUtc: reader.IsDBNull(15) ? null : DateTimeOffset.Parse(reader.GetString(15)),
                LastScanUtc: reader.IsDBNull(16) ? null : DateTimeOffset.Parse(reader.GetString(16))),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetLastScanTimeAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            "SELECT MAX(last_scan_utc) FROM hub_project_entry;",
            reader => reader.IsDBNull(0) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(0)),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return rows.Count > 0 ? rows[0] : null;
    }

    // ─── GitHub Cache ───

    public async Task SaveGitHubCacheAsync(
        string slug,
        int? openPrCount,
        int? openIssueCount,
        bool? latestWorkflowFailed,
        string? latestReleaseTag,
        CancellationToken cancellationToken = default)
    {
        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            """
            INSERT INTO hub_github_cache(slug, open_pr_count, open_issue_count, latest_workflow_failed, latest_release_tag, cached_at_utc)
            VALUES (@Slug, @PrCount, @IssueCount, @WorkflowFailed, @ReleaseTag, @CachedAt)
            ON CONFLICT(slug) DO UPDATE SET
                open_pr_count = @PrCount,
                open_issue_count = @IssueCount,
                latest_workflow_failed = @WorkflowFailed,
                latest_release_tag = @ReleaseTag,
                cached_at_utc = @CachedAt;
            """,
            new Dictionary<string, object?>
            {
                ["@Slug"] = slug,
                ["@PrCount"] = openPrCount,
                ["@IssueCount"] = openIssueCount,
                ["@WorkflowFailed"] = latestWorkflowFailed.HasValue ? (latestWorkflowFailed.Value ? 1 : 0) : null,
                ["@ReleaseTag"] = latestReleaseTag,
                ["@CachedAt"] = DateTimeOffset.UtcNow.ToString("O")
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitHubCacheEntry?> LoadGitHubCacheAsync(
        string slug,
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        var rows = await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            """
            SELECT open_pr_count, open_issue_count, latest_workflow_failed, latest_release_tag, cached_at_utc
            FROM hub_github_cache
            WHERE slug = @Slug
            LIMIT 1;
            """,
            reader =>
            {
                var cachedAt = DateTimeOffset.Parse(reader.GetString(4));
                if (DateTimeOffset.UtcNow - cachedAt > maxAge)
                {
                    return null;
                }

                return new GitHubCacheEntry(
                    OpenPrCount: reader.IsDBNull(0) ? null : reader.GetInt32(0),
                    OpenIssueCount: reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    LatestWorkflowFailed: reader.IsDBNull(2) ? null : reader.GetInt32(2) == 1,
                    LatestReleaseTag: reader.IsDBNull(3) ? null : reader.GetString(3),
                    CachedAtUtc: cachedAt);
            },
            new Dictionary<string, object?> { ["@Slug"] = slug },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return rows.Count > 0 ? rows[0] : null;
    }

    // ─── Build Results ───

    public async Task SaveBuildResultAsync(
        ProjectBuildResult result,
        CancellationToken cancellationToken = default)
    {
        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            """
            INSERT INTO hub_build_result(
                project_name, script_kind, script_path, succeeded,
                output_hash, error_snippet, duration_seconds, completed_at_utc)
            VALUES (
                @Name, @Kind, @Path, @Succeeded,
                @OutputHash, @ErrorSnippet, @Duration, @CompletedAt);
            """,
            new Dictionary<string, object?>
            {
                ["@Name"] = result.ProjectName,
                ["@Kind"] = result.ScriptKind.ToString(),
                ["@Path"] = result.ScriptPath,
                ["@Succeeded"] = result.Succeeded ? 1 : 0,
                ["@OutputHash"] = result.Output.Length > 200 ? result.Output[..200] : result.Output,
                ["@ErrorSnippet"] = result.Error?.Length > 200 == true ? result.Error[..200] : result.Error,
                ["@Duration"] = result.DurationSeconds,
                ["@CompletedAt"] = result.CompletedAtUtc.ToString("O")
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

public sealed record GitHubCacheEntry(
    int? OpenPrCount,
    int? OpenIssueCount,
    bool? LatestWorkflowFailed,
    string? LatestReleaseTag,
    DateTimeOffset CachedAtUtc);
