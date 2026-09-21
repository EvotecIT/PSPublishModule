using DBAClientX;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Orchestrator.Storage;

public sealed partial class ReleaseStateDatabase
{
    private const int MaximumProgressRowsPerSession = 500;

    public async Task AppendReleaseProgressAsync(
        string sessionId,
        ReleaseArtifactProgress progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(progress);
        await using var database = await _sqlite.OpenSessionAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
        await database.RunInTransactionAsync(async (transaction, token) =>
        {
            var sessionValue = await transaction.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM release_queue_session WHERE session_id = @SessionId;",
                new Dictionary<string, object?> { ["@SessionId"] = sessionId }, token).ConfigureAwait(false);
            var sessionExists = Convert.ToInt64(sessionValue, System.Globalization.CultureInfo.InvariantCulture);
            if (sessionExists != 1)
                throw new InvalidOperationException("Release progress requires an existing durable session claim.");

            await transaction.ExecuteNonQueryAsync(
                """
                INSERT INTO release_execution_progress(
                    session_id, stage, item_name, item_path, state, completed_items, total_items, detail, observed_at_utc)
                VALUES (
                    @SessionId, @Stage, @ItemName, @ItemPath, @State, @CompletedItems, @TotalItems, @Detail, @ObservedAtUtc);
                """,
                BuildProgressParameters(sessionId, progress), token).ConfigureAwait(false);
            await transaction.ExecuteNonQueryAsync(
                """
                DELETE FROM release_execution_progress
                WHERE session_id = @SessionId
                  AND progress_id NOT IN (
                      SELECT progress_id
                      FROM release_execution_progress
                      WHERE session_id = @SessionId
                      ORDER BY progress_id DESC
                      LIMIT @Limit);
                """,
                new Dictionary<string, object?> {
                    ["@SessionId"] = sessionId,
                    ["@Limit"] = MaximumProgressRowsPerSession
                }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReleaseArtifactProgress>> LoadReleaseProgressAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await using var database = await _sqlite.OpenSessionAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
        return await ReadReleaseProgressCoreAsync(database, sessionId, cancellationToken).ConfigureAwait(false);
    }

    internal static Task<IReadOnlyList<ReleaseArtifactProgress>> ReadReleaseProgressCoreAsync(
        SQLiteAsyncSession database,
        string sessionId,
        CancellationToken cancellationToken)
        => database.QueryAsListAsync(
            """
            SELECT stage, item_name, item_path, state, completed_items, total_items, detail, observed_at_utc
            FROM release_execution_progress
            WHERE session_id = @SessionId
            ORDER BY progress_id;
            """,
            static reader => new ReleaseArtifactProgress(
                Enum.Parse<Domain.Queue.ReleaseQueueStage>(reader.GetString(0), ignoreCase: true),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7))),
            new Dictionary<string, object?> { ["@SessionId"] = sessionId },
            cancellationToken: cancellationToken);

    private static Dictionary<string, object?> BuildProgressParameters(string sessionId, ReleaseArtifactProgress progress)
        => new() {
            ["@SessionId"] = sessionId,
            ["@Stage"] = progress.Stage.ToString(),
            ["@ItemName"] = progress.ItemName,
            ["@ItemPath"] = progress.ItemPath,
            ["@State"] = progress.State,
            ["@CompletedItems"] = progress.CompletedItems,
            ["@TotalItems"] = progress.TotalItems,
            ["@Detail"] = progress.Detail,
            ["@ObservedAtUtc"] = progress.ObservedAtUtc.ToString("O")
        };
}
