using DBAClientX;
using System.Security.Cryptography;
using System.Text;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;

namespace PowerForgeStudio.Orchestrator.Storage;

public sealed class ReleaseStateDatabase
{
    private const string CurrentSchemaVersion = "15";
    private readonly SQLite _sqlite = new() {
        BusyTimeoutMs = 10_000
    };
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AllowedSchemaColumns =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase) {
            ["release_portfolio_view_state"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["preset_key"] = "TEXT NULL",
                ["family_key"] = "TEXT NULL"
            },
            ["release_queue_session"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["scope_key"] = "TEXT NULL",
                ["scope_display_name"] = "TEXT NULL"
            },
            ["release_portfolio_signal_snapshot"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["github_default_branch"] = "TEXT NULL",
                ["github_probed_branch"] = "TEXT NULL",
                ["github_is_default_branch"] = "INTEGER NULL",
                ["github_branch_protection_enabled"] = "INTEGER NULL"
            },
            ["release_publish_receipt"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["source_path"] = "TEXT NULL"
            }
        };

    public ReleaseStateDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
    }

    public string DatabasePath { get; }

    public static string GetDefaultDatabasePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "PowerForgeStudio", "releaseops.db");
    }

    public static async ValueTask<IAsyncDisposable> AcquireExclusiveAccessAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var semaphore = new Semaphore(initialCount: 1, maximumCount: 1, name: BuildMutexName(databasePath));
        var acquired = false;

        try
        {
            while (!acquired)
            {
                cancellationToken.ThrowIfCancellationRequested();
                acquired = semaphore.WaitOne(TimeSpan.FromMilliseconds(250));
            }

            return new AsyncSemaphoreHandle(semaphore);
        }
        catch
        {
            if (!acquired)
            {
                semaphore.Dispose();
            }

            throw;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var parentDirectory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        var commands = new[] {
            "PRAGMA journal_mode = WAL;",
            """
            CREATE TABLE IF NOT EXISTS app_schema (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS release_portfolio_snapshot (
                root_path TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                repository_kind TEXT NOT NULL,
                workspace_kind TEXT NOT NULL,
                module_build_script_path TEXT NULL,
                project_build_script_path TEXT NULL,
                is_worktree INTEGER NOT NULL,
                has_website_signals INTEGER NOT NULL,
                is_git_repository INTEGER NOT NULL,
                branch_name TEXT NULL,
                upstream_branch TEXT NULL,
                ahead_count INTEGER NOT NULL,
                behind_count INTEGER NOT NULL,
                tracked_change_count INTEGER NOT NULL,
                untracked_change_count INTEGER NOT NULL,
                readiness_kind TEXT NOT NULL,
                readiness_reason TEXT NOT NULL,
                scanned_at_utc TEXT NOT NULL
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS idx_release_portfolio_snapshot_kind
            ON release_portfolio_snapshot(readiness_kind, workspace_kind);
            """,
            """
            CREATE TABLE IF NOT EXISTS release_portfolio_signal_snapshot (
                root_path TEXT PRIMARY KEY,
                github_repository_slug TEXT NULL,
                github_status TEXT NULL,
                github_open_pr_count INTEGER NULL,
                github_latest_workflow_failed INTEGER NULL,
                github_latest_release_tag TEXT NULL,
                github_default_branch TEXT NULL,
                github_probed_branch TEXT NULL,
                github_is_default_branch INTEGER NULL,
                github_branch_protection_enabled INTEGER NULL,
                github_summary TEXT NULL,
                github_detail TEXT NULL,
                drift_status TEXT NULL,
                drift_summary TEXT NULL,
                drift_detail TEXT NULL,
                scanned_at_utc TEXT NOT NULL
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS idx_release_portfolio_signal_snapshot_status
            ON release_portfolio_signal_snapshot(github_status, drift_status);
            """,
            """
            CREATE TABLE IF NOT EXISTS release_portfolio_view_state (
                view_id TEXT PRIMARY KEY,
                preset_key TEXT NULL,
                focus_mode TEXT NOT NULL,
                search_text TEXT NULL,
                family_key TEXT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS release_plan_snapshot (
                root_path TEXT NOT NULL,
                adapter_kind TEXT NOT NULL,
                status TEXT NOT NULL,
                summary TEXT NOT NULL,
                plan_path TEXT NULL,
                exit_code INTEGER NOT NULL,
                duration_seconds REAL NOT NULL,
                output_tail TEXT NULL,
                error_tail TEXT NULL,
                scanned_at_utc TEXT NOT NULL,
                PRIMARY KEY (root_path, adapter_kind)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS release_queue_session (
                session_id TEXT PRIMARY KEY,
                workspace_root TEXT NOT NULL,
                scope_key TEXT NULL,
                scope_display_name TEXT NULL,
                total_items INTEGER NOT NULL,
                build_ready_items INTEGER NOT NULL,
                prepare_pending_items INTEGER NOT NULL,
                waiting_approval_items INTEGER NOT NULL,
                blocked_items INTEGER NOT NULL,
                verification_ready_items INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS release_queue_item (
                session_id TEXT NOT NULL,
                root_path TEXT NOT NULL,
                repository_name TEXT NOT NULL,
                repository_kind TEXT NOT NULL,
                workspace_kind TEXT NOT NULL,
                queue_order INTEGER NOT NULL,
                stage TEXT NOT NULL,
                status TEXT NOT NULL,
                summary TEXT NOT NULL,
                checkpoint_key TEXT NULL,
                checkpoint_state_json TEXT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (session_id, queue_order)
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS idx_release_queue_item_session
            ON release_queue_item(session_id, stage, status);
            """,
            """
            CREATE TABLE IF NOT EXISTS release_signing_receipt (
                session_id TEXT NOT NULL,
                root_path TEXT NOT NULL,
                repository_name TEXT NOT NULL,
                adapter_kind TEXT NOT NULL,
                artifact_path TEXT NOT NULL,
                artifact_kind TEXT NOT NULL,
                status TEXT NOT NULL,
                summary TEXT NOT NULL,
                signed_at_utc TEXT NOT NULL,
                PRIMARY KEY (session_id, artifact_path)
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS idx_release_signing_receipt_session
            ON release_signing_receipt(session_id, root_path, status);
            """,
            """
            CREATE TABLE IF NOT EXISTS release_publish_receipt (
                session_id TEXT NOT NULL,
                root_path TEXT NOT NULL,
                repository_name TEXT NOT NULL,
                adapter_kind TEXT NOT NULL,
                target_name TEXT NOT NULL,
                target_kind TEXT NOT NULL,
                destination TEXT NULL,
                source_path TEXT NULL,
                status TEXT NOT NULL,
                summary TEXT NOT NULL,
                published_at_utc TEXT NOT NULL,
                PRIMARY KEY (session_id, root_path, target_name, target_kind)
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS idx_release_publish_receipt_session
            ON release_publish_receipt(session_id, root_path, status);
            """,
            """
            CREATE TABLE IF NOT EXISTS release_verification_receipt (
                session_id TEXT NOT NULL,
                root_path TEXT NOT NULL,
                repository_name TEXT NOT NULL,
                adapter_kind TEXT NOT NULL,
                target_name TEXT NOT NULL,
                target_kind TEXT NOT NULL,
                destination TEXT NULL,
                status TEXT NOT NULL,
                summary TEXT NOT NULL,
                verified_at_utc TEXT NOT NULL,
                PRIMARY KEY (session_id, root_path, target_name, target_kind)
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS idx_release_verification_receipt_session
            ON release_verification_receipt(session_id, root_path, status);
            """,
            """
            CREATE TABLE IF NOT EXISTS release_git_quick_action_receipt (
                root_path TEXT PRIMARY KEY,
                action_title TEXT NOT NULL,
                action_kind TEXT NOT NULL,
                payload TEXT NOT NULL,
                succeeded INTEGER NOT NULL,
                summary TEXT NOT NULL,
                output_tail TEXT NULL,
                error_tail TEXT NULL,
                executed_at_utc TEXT NOT NULL
            );
            """
        };

        foreach (var command in commands)
        {
            await _sqlite.ExecuteNonQueryAsync(DatabasePath, command, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await EnsureColumnExistsAsync(
            tableName: "release_portfolio_view_state",
            columnName: "preset_key",
            columnDefinition: "TEXT NULL",
            cancellationToken).ConfigureAwait(false);

        await EnsureColumnExistsAsync(
            tableName: "release_portfolio_view_state",
            columnName: "family_key",
            columnDefinition: "TEXT NULL",
            cancellationToken).ConfigureAwait(false);

        await EnsureColumnExistsAsync(
            tableName: "release_queue_session",
            columnName: "scope_key",
            columnDefinition: "TEXT NULL",
            cancellationToken).ConfigureAwait(false);

        await EnsureColumnExistsAsync(
            tableName: "release_queue_session",
            columnName: "scope_display_name",
            columnDefinition: "TEXT NULL",
            cancellationToken).ConfigureAwait(false);

        await EnsureColumnExistsAsync(
            tableName: "release_portfolio_signal_snapshot",
            columnName: "github_default_branch",
            columnDefinition: "TEXT NULL",
            cancellationToken).ConfigureAwait(false);

        await EnsureColumnExistsAsync(
            tableName: "release_portfolio_signal_snapshot",
            columnName: "github_probed_branch",
            columnDefinition: "TEXT NULL",
            cancellationToken).ConfigureAwait(false);

        await EnsureColumnExistsAsync(
            tableName: "release_portfolio_signal_snapshot",
            columnName: "github_is_default_branch",
            columnDefinition: "INTEGER NULL",
            cancellationToken).ConfigureAwait(false);

        await EnsureColumnExistsAsync(
            tableName: "release_portfolio_signal_snapshot",
            columnName: "github_branch_protection_enabled",
            columnDefinition: "INTEGER NULL",
            cancellationToken).ConfigureAwait(false);

        await EnsureColumnExistsAsync(
            tableName: "release_publish_receipt",
            columnName: "source_path",
            columnDefinition: "TEXT NULL",
            cancellationToken).ConfigureAwait(false);

        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            """
            INSERT INTO app_schema(key, value, updated_at_utc)
            VALUES (@Key, @Value, @UpdatedAtUtc)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at_utc = excluded.updated_at_utc;
            """,
            new Dictionary<string, object?> {
                ["@Key"] = "schema_version",
                ["@Value"] = CurrentSchemaVersion,
                ["@UpdatedAtUtc"] = DateTime.UtcNow.ToString("O")
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string BuildMutexName(string databasePath)
    {
        var normalizedPath = Path.GetFullPath(databasePath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
        return $@"Global\PowerForgeStudio.ReleaseState.{hash}";
    }

    private sealed class AsyncSemaphoreHandle : IAsyncDisposable
    {
        private readonly Semaphore _semaphore;
        private bool _disposed;

        public AsyncSemaphoreHandle(Semaphore semaphore)
        {
            _semaphore = semaphore;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _semaphore.Release();
            _semaphore.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private async Task EnsureColumnExistsAsync(string tableName, string columnName, string columnDefinition, CancellationToken cancellationToken)
    {
        ValidateSchemaColumn(tableName, columnName, columnDefinition);
        var existingColumns = await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            $"PRAGMA table_info({tableName});",
            reader => reader.GetString(1),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (existingColumns.Any(existingColumn => string.Equals(existingColumn, columnName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateSchemaColumn(string tableName, string columnName, string columnDefinition)
    {
        if (!AllowedSchemaColumns.TryGetValue(tableName, out var allowedColumns) ||
            !allowedColumns.TryGetValue(columnName, out var expectedDefinition))
        {
            throw new InvalidOperationException($"Schema migration for {tableName}.{columnName} is not allowlisted.");
        }

        if (!string.Equals(expectedDefinition, columnDefinition, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Schema migration definition mismatch for {tableName}.{columnName}.");
        }
    }

    public async Task PersistPortfolioViewStateAsync(RepositoryPortfolioViewState state, string viewId = "default", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);

        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            """
            INSERT INTO release_portfolio_view_state(
                view_id,
                preset_key,
                focus_mode,
                search_text,
                family_key,
                updated_at_utc)
            VALUES (
                @ViewId,
                @PresetKey,
                @FocusMode,
                @SearchText,
                @FamilyKey,
                @UpdatedAtUtc)
            ON CONFLICT(view_id) DO UPDATE SET
                preset_key = excluded.preset_key,
                focus_mode = excluded.focus_mode,
                search_text = excluded.search_text,
                family_key = excluded.family_key,
                updated_at_utc = excluded.updated_at_utc;
            """,
            new Dictionary<string, object?> {
                ["@ViewId"] = viewId,
                ["@PresetKey"] = state.PresetKey,
                ["@FocusMode"] = state.FocusMode.ToString(),
                ["@SearchText"] = state.SearchText,
                ["@FamilyKey"] = state.FamilyKey,
                ["@UpdatedAtUtc"] = state.UpdatedAtUtc.ToString("O")
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<RepositoryPortfolioViewState?> LoadPortfolioViewStateAsync(string viewId = "default", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);

        var rows = await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            """
            SELECT preset_key,
                   focus_mode,
                   search_text,
                   family_key,
                   updated_at_utc
            FROM release_portfolio_view_state
            WHERE view_id = @ViewId
            LIMIT 1;
            """,
            reader => new PortfolioViewStateRow(
                PresetKey: reader.IsDBNull(0) ? null : reader.GetString(0),
                FocusMode: reader.GetString(1),
                SearchText: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                FamilyKey: reader.IsDBNull(3) ? null : reader.GetString(3),
                UpdatedAtUtc: DateTimeOffset.Parse(reader.GetString(4))),
            new Dictionary<string, object?> {
                ["@ViewId"] = viewId
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var row = rows.FirstOrDefault();
        if (row == default)
        {
            return null;
        }

        return new RepositoryPortfolioViewState(
            PresetKey: row.PresetKey,
            FocusMode: Enum.Parse<RepositoryPortfolioFocusMode>(row.FocusMode, ignoreCase: true),
            SearchText: row.SearchText,
            FamilyKey: row.FamilyKey,
            UpdatedAtUtc: row.UpdatedAtUtc);
    }

    public async Task PersistPortfolioSnapshotAsync(IEnumerable<RepositoryPortfolioItem> items, CancellationToken cancellationToken = default)
    {
        var materializedItems = items.ToArray();
        var scannedAtUtc = DateTime.UtcNow.ToString("O");
        await _sqlite.BeginTransactionAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
        try
        {
            await _sqlite.ExecuteNonQueryAsync(
                DatabasePath,
                "DELETE FROM release_portfolio_snapshot;",
                useTransaction: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _sqlite.ExecuteNonQueryAsync(
                DatabasePath,
                "DELETE FROM release_portfolio_signal_snapshot;",
                useTransaction: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var item in materializedItems)
            {
                await _sqlite.ExecuteNonQueryAsync(
                    DatabasePath,
                    """
                    INSERT INTO release_portfolio_snapshot(
                        root_path,
                        name,
                        repository_kind,
                        workspace_kind,
                        module_build_script_path,
                        project_build_script_path,
                        is_worktree,
                        has_website_signals,
                        is_git_repository,
                        branch_name,
                        upstream_branch,
                        ahead_count,
                        behind_count,
                        tracked_change_count,
                        untracked_change_count,
                        readiness_kind,
                        readiness_reason,
                        scanned_at_utc)
                    VALUES (
                        @RootPath,
                        @Name,
                        @RepositoryKind,
                        @WorkspaceKind,
                        @ModuleBuildScriptPath,
                        @ProjectBuildScriptPath,
                        @IsWorktree,
                        @HasWebsiteSignals,
                        @IsGitRepository,
                        @BranchName,
                        @UpstreamBranch,
                        @AheadCount,
                        @BehindCount,
                        @TrackedChangeCount,
                        @UntrackedChangeCount,
                        @ReadinessKind,
                        @ReadinessReason,
                        @ScannedAtUtc);
                    """,
                    new Dictionary<string, object?> {
                        ["@RootPath"] = item.RootPath,
                        ["@Name"] = item.Name,
                        ["@RepositoryKind"] = item.RepositoryKind.ToString(),
                        ["@WorkspaceKind"] = item.WorkspaceKind.ToString(),
                        ["@ModuleBuildScriptPath"] = item.Repository.ModuleBuildScriptPath,
                        ["@ProjectBuildScriptPath"] = item.Repository.ProjectBuildScriptPath,
                        ["@IsWorktree"] = item.Repository.IsWorktree ? 1 : 0,
                        ["@HasWebsiteSignals"] = item.Repository.HasWebsiteSignals ? 1 : 0,
                        ["@IsGitRepository"] = item.Git.IsGitRepository ? 1 : 0,
                        ["@BranchName"] = item.Git.BranchName,
                        ["@UpstreamBranch"] = item.Git.UpstreamBranch,
                        ["@AheadCount"] = item.Git.AheadCount,
                        ["@BehindCount"] = item.Git.BehindCount,
                        ["@TrackedChangeCount"] = item.Git.TrackedChangeCount,
                        ["@UntrackedChangeCount"] = item.Git.UntrackedChangeCount,
                        ["@ReadinessKind"] = item.Readiness.Kind.ToString(),
                        ["@ReadinessReason"] = item.Readiness.Reason,
                        ["@ScannedAtUtc"] = scannedAtUtc
                    },
                    useTransaction: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await _sqlite.ExecuteNonQueryAsync(
                    DatabasePath,
                    """
                    INSERT INTO release_portfolio_signal_snapshot(
                        root_path,
                        github_repository_slug,
                        github_status,
                        github_open_pr_count,
                        github_latest_workflow_failed,
                        github_latest_release_tag,
                        github_default_branch,
                        github_probed_branch,
                        github_is_default_branch,
                        github_branch_protection_enabled,
                        github_summary,
                        github_detail,
                        drift_status,
                        drift_summary,
                        drift_detail,
                        scanned_at_utc)
                    VALUES (
                        @RootPath,
                        @GitHubRepositorySlug,
                        @GitHubStatus,
                        @GitHubOpenPullRequestCount,
                        @GitHubLatestWorkflowFailed,
                        @GitHubLatestReleaseTag,
                        @GitHubDefaultBranch,
                        @GitHubProbedBranch,
                        @GitHubIsDefaultBranch,
                        @GitHubBranchProtectionEnabled,
                        @GitHubSummary,
                        @GitHubDetail,
                        @DriftStatus,
                        @DriftSummary,
                        @DriftDetail,
                        @ScannedAtUtc);
                    """,
                    new Dictionary<string, object?> {
                        ["@RootPath"] = item.RootPath,
                        ["@GitHubRepositorySlug"] = item.GitHubInbox?.RepositorySlug,
                        ["@GitHubStatus"] = item.GitHubInbox?.Status.ToString(),
                        ["@GitHubOpenPullRequestCount"] = item.GitHubInbox?.OpenPullRequestCount,
                        ["@GitHubLatestWorkflowFailed"] = item.GitHubInbox?.LatestWorkflowFailed is null ? null : item.GitHubInbox.LatestWorkflowFailed.Value ? 1 : 0,
                        ["@GitHubLatestReleaseTag"] = item.GitHubInbox?.LatestReleaseTag,
                        ["@GitHubDefaultBranch"] = item.GitHubInbox?.DefaultBranch,
                        ["@GitHubProbedBranch"] = item.GitHubInbox?.ProbedBranch,
                        ["@GitHubIsDefaultBranch"] = item.GitHubInbox?.IsDefaultBranch is null ? null : item.GitHubInbox.IsDefaultBranch.Value ? 1 : 0,
                        ["@GitHubBranchProtectionEnabled"] = item.GitHubInbox?.BranchProtectionEnabled is null ? null : item.GitHubInbox.BranchProtectionEnabled.Value ? 1 : 0,
                        ["@GitHubSummary"] = item.GitHubInbox?.Summary,
                        ["@GitHubDetail"] = item.GitHubInbox?.Detail,
                        ["@DriftStatus"] = item.ReleaseDrift?.Status.ToString(),
                        ["@DriftSummary"] = item.ReleaseDrift?.Summary,
                        ["@DriftDetail"] = item.ReleaseDrift?.Detail,
                        ["@ScannedAtUtc"] = scannedAtUtc
                    },
                    useTransaction: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await _sqlite.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (_sqlite.IsInTransaction)
            {
                await _sqlite.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<RepositoryPortfolioItem>> LoadPortfolioSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var portfolioRows = await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            """
            SELECT root_path,
                   name,
                   repository_kind,
                   workspace_kind,
                   module_build_script_path,
                   project_build_script_path,
                   is_worktree,
                   has_website_signals,
                   is_git_repository,
                   branch_name,
                   upstream_branch,
                   ahead_count,
                   behind_count,
                   tracked_change_count,
                   untracked_change_count,
                   readiness_kind,
                   readiness_reason
            FROM release_portfolio_snapshot
            ORDER BY name;
            """,
            reader => new PortfolioSnapshotRow(
                RootPath: reader.GetString(0),
                Name: reader.GetString(1),
                RepositoryKind: reader.GetString(2),
                WorkspaceKind: reader.GetString(3),
                ModuleBuildScriptPath: reader.IsDBNull(4) ? null : reader.GetString(4),
                ProjectBuildScriptPath: reader.IsDBNull(5) ? null : reader.GetString(5),
                IsWorktree: reader.GetInt32(6) == 1,
                HasWebsiteSignals: reader.GetInt32(7) == 1,
                IsGitRepository: reader.GetInt32(8) == 1,
                BranchName: reader.IsDBNull(9) ? null : reader.GetString(9),
                UpstreamBranch: reader.IsDBNull(10) ? null : reader.GetString(10),
                AheadCount: reader.GetInt32(11),
                BehindCount: reader.GetInt32(12),
                TrackedChangeCount: reader.GetInt32(13),
                UntrackedChangeCount: reader.GetInt32(14),
                ReadinessKind: reader.GetString(15),
                ReadinessReason: reader.GetString(16)),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var planRows = await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            """
            SELECT root_path,
                   adapter_kind,
                   status,
                   summary,
                   plan_path,
                   exit_code,
                   duration_seconds,
                   output_tail,
                   error_tail
            FROM release_plan_snapshot;
            """,
            reader => new PlanSnapshotRow(
                RootPath: reader.GetString(0),
                AdapterKind: reader.GetString(1),
                Status: reader.GetString(2),
                Summary: reader.GetString(3),
                PlanPath: reader.IsDBNull(4) ? null : reader.GetString(4),
                ExitCode: reader.GetInt32(5),
                DurationSeconds: reader.GetDouble(6),
                OutputTail: reader.IsDBNull(7) ? null : reader.GetString(7),
                ErrorTail: reader.IsDBNull(8) ? null : reader.GetString(8)),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var signalRows = await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            """
            SELECT root_path,
                   github_repository_slug,
                   github_status,
                   github_open_pr_count,
                   github_latest_workflow_failed,
                   github_latest_release_tag,
                   github_default_branch,
                   github_probed_branch,
                   github_is_default_branch,
                   github_branch_protection_enabled,
                   github_summary,
                   github_detail,
                   drift_status,
                   drift_summary,
                   drift_detail
            FROM release_portfolio_signal_snapshot;
            """,
            reader => new SignalSnapshotRow(
                RootPath: reader.GetString(0),
                GitHubRepositorySlug: reader.IsDBNull(1) ? null : reader.GetString(1),
                GitHubStatus: reader.IsDBNull(2) ? null : reader.GetString(2),
                GitHubOpenPullRequestCount: reader.IsDBNull(3) ? null : reader.GetInt32(3),
                GitHubLatestWorkflowFailed: reader.IsDBNull(4) ? null : reader.GetInt32(4) == 1,
                GitHubLatestReleaseTag: reader.IsDBNull(5) ? null : reader.GetString(5),
                GitHubDefaultBranch: reader.IsDBNull(6) ? null : reader.GetString(6),
                GitHubProbedBranch: reader.IsDBNull(7) ? null : reader.GetString(7),
                GitHubIsDefaultBranch: reader.IsDBNull(8) ? null : reader.GetInt32(8) == 1,
                GitHubBranchProtectionEnabled: reader.IsDBNull(9) ? null : reader.GetInt32(9) == 1,
                GitHubSummary: reader.IsDBNull(10) ? null : reader.GetString(10),
                GitHubDetail: reader.IsDBNull(11) ? null : reader.GetString(11),
                DriftStatus: reader.IsDBNull(12) ? null : reader.GetString(12),
                DriftSummary: reader.IsDBNull(13) ? null : reader.GetString(13),
                DriftDetail: reader.IsDBNull(14) ? null : reader.GetString(14)),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var planLookup = planRows
            .GroupBy(row => row.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RepositoryPlanResult>)group
                    .Select(row => new RepositoryPlanResult(
                        AdapterKind: Enum.Parse<RepositoryPlanAdapterKind>(row.AdapterKind, ignoreCase: true),
                        Status: Enum.Parse<RepositoryPlanStatus>(row.Status, ignoreCase: true),
                        Summary: row.Summary,
                        PlanPath: row.PlanPath,
                        ExitCode: row.ExitCode,
                        DurationSeconds: row.DurationSeconds,
                        OutputTail: row.OutputTail,
                        ErrorTail: row.ErrorTail))
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var signalLookup = signalRows.ToDictionary(row => row.RootPath, StringComparer.OrdinalIgnoreCase);
        var gitPreflightService = new RepositoryGitPreflightService();

        return portfolioRows
            .Select(row => {
                var hasSignal = signalLookup.TryGetValue(row.RootPath, out var signal);
                var repositoryEntry = new RepositoryCatalogEntry(
                    Name: row.Name,
                    RootPath: row.RootPath,
                    RepositoryKind: Enum.Parse<ReleaseRepositoryKind>(row.RepositoryKind, ignoreCase: true),
                    WorkspaceKind: Enum.Parse<ReleaseWorkspaceKind>(row.WorkspaceKind, ignoreCase: true),
                    ModuleBuildScriptPath: row.ModuleBuildScriptPath,
                    ProjectBuildScriptPath: row.ProjectBuildScriptPath,
                    IsWorktree: row.IsWorktree,
                    HasWebsiteSignals: row.HasWebsiteSignals);
                var gitSnapshot = new RepositoryGitSnapshot(
                    IsGitRepository: row.IsGitRepository,
                    BranchName: row.BranchName,
                    UpstreamBranch: row.UpstreamBranch,
                    AheadCount: row.AheadCount,
                    BehindCount: row.BehindCount,
                    TrackedChangeCount: row.TrackedChangeCount,
                    UntrackedChangeCount: row.UntrackedChangeCount);
                gitSnapshot = gitSnapshot with {
                    Diagnostics = gitPreflightService.Assess(repositoryEntry, gitSnapshot)
                };
                return new RepositoryPortfolioItem(
                    Repository: repositoryEntry,
                    Git: gitSnapshot,
                    Readiness: new RepositoryReadiness(
                        Enum.Parse<RepositoryReadinessKind>(row.ReadinessKind, ignoreCase: true),
                        row.ReadinessReason),
                    PlanResults: planLookup.GetValueOrDefault(row.RootPath, []),
                    GitHubInbox: !hasSignal || string.IsNullOrWhiteSpace(signal.GitHubStatus)
                        ? null
                        : new RepositoryGitHubInbox(
                            Status: Enum.Parse<RepositoryGitHubInboxStatus>(signal.GitHubStatus, ignoreCase: true),
                            RepositorySlug: signal.GitHubRepositorySlug,
                            OpenPullRequestCount: signal.GitHubOpenPullRequestCount,
                            LatestWorkflowFailed: signal.GitHubLatestWorkflowFailed,
                            LatestReleaseTag: signal.GitHubLatestReleaseTag,
                            DefaultBranch: signal.GitHubDefaultBranch,
                            ProbedBranch: signal.GitHubProbedBranch,
                            IsDefaultBranch: signal.GitHubIsDefaultBranch,
                            BranchProtectionEnabled: signal.GitHubBranchProtectionEnabled,
                            Summary: signal.GitHubSummary ?? "GitHub inbox snapshot loaded.",
                            Detail: signal.GitHubDetail ?? "No GitHub detail persisted."),
                    ReleaseDrift: !hasSignal || string.IsNullOrWhiteSpace(signal.DriftStatus)
                        ? null
                        : new RepositoryReleaseDrift(
                            Status: Enum.Parse<RepositoryReleaseDriftStatus>(signal.DriftStatus, ignoreCase: true),
                            Summary: signal.DriftSummary ?? "Release drift snapshot loaded.",
                            Detail: signal.DriftDetail ?? "No release drift detail persisted."));
            })
            .ToArray();
    }

    public async Task PersistPlanSnapshotsAsync(IEnumerable<RepositoryPortfolioItem> items, CancellationToken cancellationToken = default)
    {
        var scannedAtUtc = DateTime.UtcNow.ToString("O");
        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            "DELETE FROM release_plan_snapshot;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var item in items)
        {
            foreach (var result in item.PlanResults ?? [])
            {
                await _sqlite.ExecuteNonQueryAsync(
                    DatabasePath,
                    """
                    INSERT INTO release_plan_snapshot(
                        root_path,
                        adapter_kind,
                        status,
                        summary,
                        plan_path,
                        exit_code,
                        duration_seconds,
                        output_tail,
                        error_tail,
                        scanned_at_utc)
                    VALUES (
                        @RootPath,
                        @AdapterKind,
                        @Status,
                        @Summary,
                        @PlanPath,
                        @ExitCode,
                        @DurationSeconds,
                        @OutputTail,
                        @ErrorTail,
                        @ScannedAtUtc);
                    """,
                    new Dictionary<string, object?> {
                        ["@RootPath"] = item.RootPath,
                        ["@AdapterKind"] = result.AdapterKind.ToString(),
                        ["@Status"] = result.Status.ToString(),
                        ["@Summary"] = result.Summary,
                        ["@PlanPath"] = result.PlanPath,
                        ["@ExitCode"] = result.ExitCode,
                        ["@DurationSeconds"] = result.DurationSeconds,
                        ["@OutputTail"] = result.OutputTail,
                        ["@ErrorTail"] = result.ErrorTail,
                        ["@ScannedAtUtc"] = scannedAtUtc
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task PersistQueueSessionAsync(ReleaseQueueSession session, CancellationToken cancellationToken = default)
    {
        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            """
            INSERT INTO release_queue_session(
                session_id,
                workspace_root,
                scope_key,
                scope_display_name,
                total_items,
                build_ready_items,
                prepare_pending_items,
                waiting_approval_items,
                blocked_items,
                verification_ready_items,
                created_at_utc)
            VALUES (
                @SessionId,
                @WorkspaceRoot,
                @ScopeKey,
                @ScopeDisplayName,
                @TotalItems,
                @BuildReadyItems,
                @PreparePendingItems,
                @WaitingApprovalItems,
                @BlockedItems,
                @VerificationReadyItems,
                @CreatedAtUtc)
            ON CONFLICT(session_id) DO UPDATE SET
                workspace_root = excluded.workspace_root,
                scope_key = excluded.scope_key,
                scope_display_name = excluded.scope_display_name,
                total_items = excluded.total_items,
                build_ready_items = excluded.build_ready_items,
                prepare_pending_items = excluded.prepare_pending_items,
                waiting_approval_items = excluded.waiting_approval_items,
                blocked_items = excluded.blocked_items,
                verification_ready_items = excluded.verification_ready_items,
                created_at_utc = excluded.created_at_utc;
            """,
            new Dictionary<string, object?> {
                ["@SessionId"] = session.SessionId,
                ["@WorkspaceRoot"] = session.WorkspaceRoot,
                ["@ScopeKey"] = session.ScopeKey,
                ["@ScopeDisplayName"] = session.ScopeDisplayName,
                ["@TotalItems"] = session.Summary.TotalItems,
                ["@BuildReadyItems"] = session.Summary.BuildReadyItems,
                ["@PreparePendingItems"] = session.Summary.PreparePendingItems,
                ["@WaitingApprovalItems"] = session.Summary.WaitingApprovalItems,
                ["@BlockedItems"] = session.Summary.BlockedItems,
                ["@VerificationReadyItems"] = session.Summary.VerificationReadyItems,
                ["@CreatedAtUtc"] = session.CreatedAtUtc.ToString("O")
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            "DELETE FROM release_queue_item WHERE session_id = @SessionId;",
            new Dictionary<string, object?> {
                ["@SessionId"] = session.SessionId
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var item in session.Items)
        {
            await _sqlite.ExecuteNonQueryAsync(
                DatabasePath,
                """
                INSERT INTO release_queue_item(
                    session_id,
                    root_path,
                    repository_name,
                    repository_kind,
                    workspace_kind,
                    queue_order,
                    stage,
                    status,
                    summary,
                    checkpoint_key,
                    checkpoint_state_json,
                    updated_at_utc)
                VALUES (
                    @SessionId,
                    @RootPath,
                    @RepositoryName,
                    @RepositoryKind,
                    @WorkspaceKind,
                    @QueueOrder,
                    @Stage,
                    @Status,
                    @Summary,
                    @CheckpointKey,
                    @CheckpointStateJson,
                    @UpdatedAtUtc);
                """,
                new Dictionary<string, object?> {
                    ["@SessionId"] = session.SessionId,
                    ["@RootPath"] = item.RootPath,
                    ["@RepositoryName"] = item.RepositoryName,
                    ["@RepositoryKind"] = item.RepositoryKind.ToString(),
                    ["@WorkspaceKind"] = item.WorkspaceKind.ToString(),
                    ["@QueueOrder"] = item.QueueOrder,
                    ["@Stage"] = item.Stage.ToString(),
                    ["@Status"] = item.Status.ToString(),
                    ["@Summary"] = item.Summary,
                    ["@CheckpointKey"] = item.CheckpointKey,
                    ["@CheckpointStateJson"] = item.CheckpointStateJson,
                    ["@UpdatedAtUtc"] = item.UpdatedAtUtc.ToString("O")
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ReleaseQueueSession?> LoadLatestQueueSessionAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            """
            SELECT session_id,
                   workspace_root,
                   scope_key,
                   scope_display_name,
                   total_items,
                   build_ready_items,
                   prepare_pending_items,
                   waiting_approval_items,
                   blocked_items,
                   verification_ready_items,
                   created_at_utc
            FROM release_queue_session
            ORDER BY created_at_utc DESC
            LIMIT 1;
            """,
            reader => new QueueSessionRow(
                SessionId: reader.GetString(0),
                WorkspaceRoot: reader.GetString(1),
                ScopeKey: reader.IsDBNull(2) ? null : reader.GetString(2),
                ScopeDisplayName: reader.IsDBNull(3) ? null : reader.GetString(3),
                TotalItems: reader.GetInt32(4),
                BuildReadyItems: reader.GetInt32(5),
                PreparePendingItems: reader.GetInt32(6),
                WaitingApprovalItems: reader.GetInt32(7),
                BlockedItems: reader.GetInt32(8),
                VerificationReadyItems: reader.GetInt32(9),
                CreatedAtUtc: DateTimeOffset.Parse(reader.GetString(10))),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var sessionRow = sessions.FirstOrDefault();
        if (sessionRow == default)
        {
            return null;
        }

        var items = await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            """
            SELECT root_path,
                   repository_name,
                   repository_kind,
                   workspace_kind,
                   queue_order,
                   stage,
                   status,
                   summary,
                   checkpoint_key,
                   checkpoint_state_json,
                   updated_at_utc
            FROM release_queue_item
            WHERE session_id = @SessionId
            ORDER BY queue_order;
            """,
            reader => new ReleaseQueueItem(
                RootPath: reader.GetString(0),
                RepositoryName: reader.GetString(1),
                RepositoryKind: Enum.Parse<ReleaseRepositoryKind>(reader.GetString(2), ignoreCase: true),
                WorkspaceKind: Enum.Parse<ReleaseWorkspaceKind>(reader.GetString(3), ignoreCase: true),
                QueueOrder: reader.GetInt32(4),
                Stage: Enum.Parse<ReleaseQueueStage>(reader.GetString(5), ignoreCase: true),
                Status: Enum.Parse<ReleaseQueueItemStatus>(reader.GetString(6), ignoreCase: true),
                Summary: reader.GetString(7),
                CheckpointKey: reader.IsDBNull(8) ? null : reader.GetString(8),
                CheckpointStateJson: reader.IsDBNull(9) ? null : reader.GetString(9),
                UpdatedAtUtc: DateTimeOffset.Parse(reader.GetString(10))),
            new Dictionary<string, object?> {
                ["@SessionId"] = sessionRow.SessionId
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ReleaseQueueSession(
            SessionId: sessionRow.SessionId,
            WorkspaceRoot: sessionRow.WorkspaceRoot,
            CreatedAtUtc: sessionRow.CreatedAtUtc,
            Summary: new ReleaseQueueSummary(
                TotalItems: sessionRow.TotalItems,
                BuildReadyItems: sessionRow.BuildReadyItems,
                PreparePendingItems: sessionRow.PreparePendingItems,
                WaitingApprovalItems: sessionRow.WaitingApprovalItems,
                BlockedItems: sessionRow.BlockedItems,
                VerificationReadyItems: sessionRow.VerificationReadyItems),
            Items: items,
            ScopeKey: sessionRow.ScopeKey,
            ScopeDisplayName: sessionRow.ScopeDisplayName);
    }

    public async Task PersistSigningReceiptsAsync(string sessionId, IEnumerable<ReleaseSigningReceipt> receipts, CancellationToken cancellationToken = default)
    {
        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            "DELETE FROM release_signing_receipt WHERE session_id = @SessionId;",
            new Dictionary<string, object?> {
                ["@SessionId"] = sessionId
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var receipt in receipts)
        {
            await _sqlite.ExecuteNonQueryAsync(
                DatabasePath,
                """
                INSERT INTO release_signing_receipt(
                    session_id,
                    root_path,
                    repository_name,
                    adapter_kind,
                    artifact_path,
                    artifact_kind,
                    status,
                    summary,
                    signed_at_utc)
                VALUES (
                    @SessionId,
                    @RootPath,
                    @RepositoryName,
                    @AdapterKind,
                    @ArtifactPath,
                    @ArtifactKind,
                    @Status,
                    @Summary,
                    @SignedAtUtc);
                """,
                new Dictionary<string, object?> {
                    ["@SessionId"] = sessionId,
                    ["@RootPath"] = receipt.RootPath,
                    ["@RepositoryName"] = receipt.RepositoryName,
                    ["@AdapterKind"] = receipt.AdapterKind,
                    ["@ArtifactPath"] = receipt.ArtifactPath,
                    ["@ArtifactKind"] = receipt.ArtifactKind,
                    ["@Status"] = receipt.Status.ToString(),
                    ["@Summary"] = receipt.Summary,
                    ["@SignedAtUtc"] = receipt.SignedAtUtc.ToString("O")
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<ReleaseSigningReceipt>> LoadSigningReceiptsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            """
            SELECT root_path,
                   repository_name,
                   adapter_kind,
                   artifact_path,
                   artifact_kind,
                   status,
                   summary,
                   signed_at_utc
            FROM release_signing_receipt
            WHERE session_id = @SessionId
            ORDER BY signed_at_utc DESC, repository_name, artifact_path;
            """,
            reader => new ReleaseSigningReceipt(
                RootPath: reader.GetString(0),
                RepositoryName: reader.GetString(1),
                AdapterKind: reader.GetString(2),
                ArtifactPath: reader.GetString(3),
                ArtifactKind: reader.GetString(4),
                Status: Enum.Parse<ReleaseSigningReceiptStatus>(reader.GetString(5), ignoreCase: true),
                Summary: reader.GetString(6),
                SignedAtUtc: DateTimeOffset.Parse(reader.GetString(7))),
            new Dictionary<string, object?> {
                ["@SessionId"] = sessionId
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task PersistPublishReceiptsAsync(string sessionId, IEnumerable<ReleasePublishReceipt> receipts, CancellationToken cancellationToken = default)
    {
        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            "DELETE FROM release_publish_receipt WHERE session_id = @SessionId;",
            new Dictionary<string, object?> {
                ["@SessionId"] = sessionId
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var receipt in receipts)
        {
            await _sqlite.ExecuteNonQueryAsync(
                DatabasePath,
                """
                INSERT INTO release_publish_receipt(
                    session_id,
                    root_path,
                    repository_name,
                    adapter_kind,
                    target_name,
                    target_kind,
                    destination,
                    source_path,
                    status,
                    summary,
                    published_at_utc)
                VALUES (
                    @SessionId,
                    @RootPath,
                    @RepositoryName,
                    @AdapterKind,
                    @TargetName,
                    @TargetKind,
                    @Destination,
                    @SourcePath,
                    @Status,
                    @Summary,
                    @PublishedAtUtc);
                """,
                new Dictionary<string, object?> {
                    ["@SessionId"] = sessionId,
                    ["@RootPath"] = receipt.RootPath,
                    ["@RepositoryName"] = receipt.RepositoryName,
                    ["@AdapterKind"] = receipt.AdapterKind,
                    ["@TargetName"] = receipt.TargetName,
                    ["@TargetKind"] = receipt.TargetKind,
                    ["@Destination"] = receipt.Destination,
                    ["@SourcePath"] = receipt.SourcePath,
                    ["@Status"] = receipt.Status.ToString(),
                    ["@Summary"] = receipt.Summary,
                    ["@PublishedAtUtc"] = receipt.PublishedAtUtc.ToString("O")
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<ReleasePublishReceipt>> LoadPublishReceiptsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            """
            SELECT root_path,
                   repository_name,
                   adapter_kind,
                   target_name,
                   target_kind,
                   destination,
                   source_path,
                   status,
                   summary,
                   published_at_utc
            FROM release_publish_receipt
            WHERE session_id = @SessionId
            ORDER BY published_at_utc DESC, repository_name, target_name;
            """,
            reader => new ReleasePublishReceipt(
                RootPath: reader.GetString(0),
                RepositoryName: reader.GetString(1),
                AdapterKind: reader.GetString(2),
                TargetName: reader.GetString(3),
                TargetKind: reader.GetString(4),
                Destination: reader.IsDBNull(5) ? null : reader.GetString(5),
                SourcePath: reader.IsDBNull(6) ? null : reader.GetString(6),
                Status: Enum.Parse<ReleasePublishReceiptStatus>(reader.GetString(7), ignoreCase: true),
                Summary: reader.GetString(8),
                PublishedAtUtc: DateTimeOffset.Parse(reader.GetString(9))),
            new Dictionary<string, object?> {
                ["@SessionId"] = sessionId
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task PersistVerificationReceiptsAsync(string sessionId, IEnumerable<ReleaseVerificationReceipt> receipts, CancellationToken cancellationToken = default)
    {
        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            "DELETE FROM release_verification_receipt WHERE session_id = @SessionId;",
            new Dictionary<string, object?> {
                ["@SessionId"] = sessionId
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var receipt in receipts)
        {
            await _sqlite.ExecuteNonQueryAsync(
                DatabasePath,
                """
                INSERT INTO release_verification_receipt(
                    session_id,
                    root_path,
                    repository_name,
                    adapter_kind,
                    target_name,
                    target_kind,
                    destination,
                    status,
                    summary,
                    verified_at_utc)
                VALUES (
                    @SessionId,
                    @RootPath,
                    @RepositoryName,
                    @AdapterKind,
                    @TargetName,
                    @TargetKind,
                    @Destination,
                    @Status,
                    @Summary,
                    @VerifiedAtUtc);
                """,
                new Dictionary<string, object?> {
                    ["@SessionId"] = sessionId,
                    ["@RootPath"] = receipt.RootPath,
                    ["@RepositoryName"] = receipt.RepositoryName,
                    ["@AdapterKind"] = receipt.AdapterKind,
                    ["@TargetName"] = receipt.TargetName,
                    ["@TargetKind"] = receipt.TargetKind,
                    ["@Destination"] = receipt.Destination,
                    ["@Status"] = receipt.Status.ToString(),
                    ["@Summary"] = receipt.Summary,
                    ["@VerifiedAtUtc"] = receipt.VerifiedAtUtc.ToString("O")
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<ReleaseVerificationReceipt>> LoadVerificationReceiptsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            """
            SELECT root_path,
                   repository_name,
                   adapter_kind,
                   target_name,
                   target_kind,
                   destination,
                   status,
                   summary,
                   verified_at_utc
            FROM release_verification_receipt
            WHERE session_id = @SessionId
            ORDER BY verified_at_utc DESC, repository_name, target_name;
            """,
            reader => new ReleaseVerificationReceipt(
                RootPath: reader.GetString(0),
                RepositoryName: reader.GetString(1),
                AdapterKind: reader.GetString(2),
                TargetName: reader.GetString(3),
                TargetKind: reader.GetString(4),
                Destination: reader.IsDBNull(5) ? null : reader.GetString(5),
                Status: Enum.Parse<ReleaseVerificationReceiptStatus>(reader.GetString(6), ignoreCase: true),
                Summary: reader.GetString(7),
                VerifiedAtUtc: DateTimeOffset.Parse(reader.GetString(8))),
            new Dictionary<string, object?> {
                ["@SessionId"] = sessionId
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task PersistGitQuickActionReceiptAsync(RepositoryGitQuickActionReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        await _sqlite.ExecuteNonQueryAsync(
            DatabasePath,
            """
            INSERT INTO release_git_quick_action_receipt(
                root_path,
                action_title,
                action_kind,
                payload,
                succeeded,
                summary,
                output_tail,
                error_tail,
                executed_at_utc)
            VALUES (
                @RootPath,
                @ActionTitle,
                @ActionKind,
                @Payload,
                @Succeeded,
                @Summary,
                @OutputTail,
                @ErrorTail,
                @ExecutedAtUtc)
            ON CONFLICT(root_path) DO UPDATE SET
                action_title = excluded.action_title,
                action_kind = excluded.action_kind,
                payload = excluded.payload,
                succeeded = excluded.succeeded,
                summary = excluded.summary,
                output_tail = excluded.output_tail,
                error_tail = excluded.error_tail,
                executed_at_utc = excluded.executed_at_utc;
            """,
            new Dictionary<string, object?> {
                ["@RootPath"] = receipt.RootPath,
                ["@ActionTitle"] = receipt.ActionTitle,
                ["@ActionKind"] = receipt.ActionKind.ToString(),
                ["@Payload"] = receipt.Payload,
                ["@Succeeded"] = receipt.Succeeded ? 1 : 0,
                ["@Summary"] = receipt.Summary,
                ["@OutputTail"] = receipt.OutputTail,
                ["@ErrorTail"] = receipt.ErrorTail,
                ["@ExecutedAtUtc"] = receipt.ExecutedAtUtc.ToString("O")
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RepositoryGitQuickActionReceipt>> LoadGitQuickActionReceiptsAsync(CancellationToken cancellationToken = default)
    {
        return await _sqlite.QueryReadOnlyAsListAsync(
            DatabasePath,
            """
            SELECT root_path,
                   action_title,
                   action_kind,
                   payload,
                   succeeded,
                   summary,
                   output_tail,
                   error_tail,
                   executed_at_utc
            FROM release_git_quick_action_receipt
            ORDER BY executed_at_utc DESC;
            """,
            reader => new RepositoryGitQuickActionReceipt(
                RootPath: reader.GetString(0),
                ActionTitle: reader.GetString(1),
                ActionKind: Enum.Parse<RepositoryGitQuickActionKind>(reader.GetString(2), ignoreCase: true),
                Payload: reader.GetString(3),
                Succeeded: reader.GetInt32(4) == 1,
                Summary: reader.GetString(5),
                OutputTail: reader.IsDBNull(6) ? null : reader.GetString(6),
                ErrorTail: reader.IsDBNull(7) ? null : reader.GetString(7),
                ExecutedAtUtc: DateTimeOffset.Parse(reader.GetString(8))),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct QueueSessionRow(
        string SessionId,
        string WorkspaceRoot,
        string? ScopeKey,
        string? ScopeDisplayName,
        int TotalItems,
        int BuildReadyItems,
        int PreparePendingItems,
        int WaitingApprovalItems,
        int BlockedItems,
        int VerificationReadyItems,
        DateTimeOffset CreatedAtUtc);

    private readonly record struct PortfolioSnapshotRow(
        string RootPath,
        string Name,
        string RepositoryKind,
        string WorkspaceKind,
        string? ModuleBuildScriptPath,
        string? ProjectBuildScriptPath,
        bool IsWorktree,
        bool HasWebsiteSignals,
        bool IsGitRepository,
        string? BranchName,
        string? UpstreamBranch,
        int AheadCount,
        int BehindCount,
        int TrackedChangeCount,
        int UntrackedChangeCount,
        string ReadinessKind,
        string ReadinessReason);

    private readonly record struct PlanSnapshotRow(
        string RootPath,
        string AdapterKind,
        string Status,
        string Summary,
        string? PlanPath,
        int ExitCode,
        double DurationSeconds,
        string? OutputTail,
        string? ErrorTail);

    private readonly record struct SignalSnapshotRow(
        string RootPath,
        string? GitHubRepositorySlug,
        string? GitHubStatus,
        int? GitHubOpenPullRequestCount,
        bool? GitHubLatestWorkflowFailed,
        string? GitHubLatestReleaseTag,
        string? GitHubDefaultBranch,
        string? GitHubProbedBranch,
        bool? GitHubIsDefaultBranch,
        bool? GitHubBranchProtectionEnabled,
        string? GitHubSummary,
        string? GitHubDetail,
        string? DriftStatus,
        string? DriftSummary,
        string? DriftDetail);

    private readonly record struct PortfolioViewStateRow(
        string? PresetKey,
        string FocusMode,
        string SearchText,
        string? FamilyKey,
        DateTimeOffset UpdatedAtUtc);
}
