using DBAClientX;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Orchestrator.Storage;

/// <summary>A queue checkpoint and its receipts observed within one database transaction.</summary>
public sealed record ReleaseCheckpointSnapshot(ReleaseQueueSession Session,
    IReadOnlyList<ReleaseSigningReceipt> SigningReceipts,
    IReadOnlyList<ReleasePublishReceipt> PublishReceipts,
    IReadOnlyList<ReleaseVerificationReceipt> VerificationReceipts);

public sealed partial class ReleaseStateDatabase
{
    public Task PersistQueueSessionAsync(ReleaseQueueSession session, CancellationToken cancellationToken = default)
        => PersistReleaseCheckpointAsync(session, cancellationToken: cancellationToken);

    /// <summary>Commits queue state and supplied receipt sets together. Null receipt sets preserve prior evidence. An explicit receipt root limits replacement to that working copy.</summary>
    public async Task PersistReleaseCheckpointAsync(ReleaseQueueSession session,
        IReadOnlyList<ReleaseSigningReceipt>? signingReceipts = null,
        IReadOnlyList<ReleasePublishReceipt>? publishReceipts = null,
        IReadOnlyList<ReleaseVerificationReceipt>? verificationReceipts = null,
        CancellationToken cancellationToken = default,
        string? receiptRootPath = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var database = await _sqlite.OpenSessionAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
        await database.RunInTransactionAsync(async (transaction, token) =>
        {
            await PersistQueueSessionCoreAsync(transaction, session, token).ConfigureAwait(false);
            if (signingReceipts is not null) await PersistReceiptSetAsync(transaction, session.SessionId, signingReceipts, SigningReceiptTable, token, receiptRootPath).ConfigureAwait(false);
            if (publishReceipts is not null) await PersistReceiptSetAsync(transaction, session.SessionId, publishReceipts, PublishReceiptTable, token, receiptRootPath).ConfigureAwait(false);
            if (verificationReceipts is not null) await PersistReceiptSetAsync(transaction, session.SessionId, verificationReceipts, VerificationReceiptTable, token, receiptRootPath).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<ReleaseQueueSession?> LoadLatestQueueSessionAsync(CancellationToken cancellationToken = default)
        => LoadQueueSessionAsync(null, cancellationToken);

    /// <summary>Loads a specific session; null selects the most recently created session.</summary>
    public async Task<ReleaseQueueSession?> LoadQueueSessionAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        await using var database = await _sqlite.OpenSessionAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
        return await database.RunInTransactionAsync((transaction, token) => LoadQueueSessionCoreAsync(transaction, sessionId, token), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the requested session and all stage receipts from one consistent snapshot.</summary>
    public async Task<ReleaseCheckpointSnapshot?> LoadReleaseCheckpointAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await using var database = await _sqlite.OpenSessionAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
        return await database.RunInTransactionAsync(async (transaction, token) =>
        {
            var session = await LoadQueueSessionCoreAsync(transaction, sessionId, token).ConfigureAwait(false);
            if (session is null) return null;
            var signing = await ReadReceiptSetAsync(transaction, sessionId, SigningReceiptTable, token).ConfigureAwait(false);
            var publish = await ReadReceiptSetAsync(transaction, sessionId, PublishReceiptTable, token).ConfigureAwait(false);
            var verify = await ReadReceiptSetAsync(transaction, sessionId, VerificationReceiptTable, token).ConfigureAwait(false);
            return new ReleaseCheckpointSnapshot(session, signing, publish, verify);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Task PersistReceiptSetAsync<T>(SQLiteAsyncSession database, string sessionId, IEnumerable<T> receipts,
        ReceiptTableDefinition<T> table, CancellationToken token, string? rootPath = null)
        => ReplaceSessionRowsAsync(database, $"DELETE FROM {table.TableName} WHERE session_id = @SessionId AND (@RootPath IS NULL OR root_path = @RootPath);",
            new Dictionary<string, object?> { ["@SessionId"] = sessionId, ["@RootPath"] = rootPath }, table.InsertSql, receipts,
            receipt =>
            {
                var parameters = table.BuildParameters(sessionId, receipt);
                if (rootPath is not null && !string.Equals(rootPath, parameters["@RootPath"] as string, StringComparison.Ordinal))
                    throw new InvalidOperationException("Receipt working copy does not match its replacement scope.");
                return parameters;
            }, token);

    private static Task<IReadOnlyList<T>> ReadReceiptSetAsync<T>(SQLiteAsyncSession database, string sessionId,
        ReceiptTableDefinition<T> table, CancellationToken token)
        => database.QueryAsListAsync(table.QuerySql, table.Map,
            new Dictionary<string, object?> { ["@SessionId"] = sessionId }, cancellationToken: token);

    private static async Task ReplaceSessionRowsAsync<T>(SQLiteAsyncSession database, string deleteSql,
        Dictionary<string, object?>? deleteParameters, string insertSql, IEnumerable<T> rows,
        Func<T, Dictionary<string, object?>> buildParameters, CancellationToken cancellationToken)
    {
        await database.ExecuteNonQueryAsync(deleteSql, deleteParameters, cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await database.ExecuteNonQueryAsync(insertSql, buildParameters(row), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PersistQueueSessionCoreAsync(SQLiteAsyncSession database, ReleaseQueueSession session, CancellationToken cancellationToken)
    {
        await database.ExecuteNonQueryAsync(
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

        await ReplaceSessionRowsAsync(
            database,
            deleteSql: "DELETE FROM release_queue_item WHERE session_id = @SessionId;",
            deleteParameters: new Dictionary<string, object?> {
                ["@SessionId"] = session.SessionId
            },
            insertSql:
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
            rows: session.Items,
            buildParameters: item => new Dictionary<string, object?> {
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

    private static async Task<ReleaseQueueSession?> LoadQueueSessionCoreAsync(SQLiteAsyncSession database, string? sessionId, CancellationToken cancellationToken)
    {
        var sessions = await database.QueryAsListAsync(
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
            WHERE @SessionId IS NULL OR session_id = @SessionId
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
            new Dictionary<string, object?> { ["@SessionId"] = sessionId },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var sessionRow = sessions.FirstOrDefault();
        if (sessionRow == default)
        {
            return null;
        }

        var items = await database.QueryAsListAsync(
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

        return ReleaseQueueSessionFactory.Create(
            workspaceRoot: sessionRow.WorkspaceRoot,
            items: items,
            createdAtUtc: sessionRow.CreatedAtUtc,
            scopeKey: sessionRow.ScopeKey,
            scopeDisplayName: sessionRow.ScopeDisplayName,
            sessionId: sessionRow.SessionId);
    }

}
