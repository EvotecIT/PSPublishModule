namespace PowerForgeStudio.Orchestrator.Storage;

public sealed partial class ReleaseStateDatabase
{
    private const string CreateVerificationReceiptTableSql = """
        CREATE TABLE IF NOT EXISTS release_verification_receipt (
            receipt_id INTEGER PRIMARY KEY,
            session_id TEXT NOT NULL,
            root_path TEXT NOT NULL,
            repository_name TEXT NOT NULL,
            adapter_kind TEXT NOT NULL,
            target_name TEXT NOT NULL,
            target_kind TEXT NOT NULL,
            destination TEXT NULL,
            status TEXT NOT NULL,
            summary TEXT NOT NULL,
            verified_at_utc TEXT NOT NULL
        );
        """;

    /// <summary>Preserves legacy evidence while removing the target-name uniqueness restriction.</summary>
    private async Task MigrateVerificationReceiptsAsync(CancellationToken cancellationToken)
    {
        await using var database = await _sqlite.OpenSessionAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
        await database.RunInTransactionAsync(async (transaction, token) =>
        {
            var columns = await transaction.QueryAsListAsync("PRAGMA table_info(release_verification_receipt);",
                reader => (Name: reader.GetString(1), Type: reader.GetString(2), PrimaryKey: reader.GetInt32(5)), cancellationToken: token).ConfigureAwait(false);
            if (columns.Any(column => column.Name == "receipt_id" && column.Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase) && column.PrimaryKey == 1))
                return;

            await transaction.ExecuteNonQueryAsync("ALTER TABLE release_verification_receipt RENAME TO release_verification_receipt_legacy_v19;", cancellationToken: token).ConfigureAwait(false);
            await transaction.ExecuteNonQueryAsync(CreateVerificationReceiptTableSql, cancellationToken: token).ConfigureAwait(false);
            await transaction.ExecuteNonQueryAsync("""
                INSERT INTO release_verification_receipt (
                    session_id, root_path, repository_name, adapter_kind, target_name, target_kind,
                    destination, status, summary, verified_at_utc)
                SELECT session_id, root_path, repository_name, adapter_kind, target_name, target_kind,
                    destination, status, summary, verified_at_utc
                FROM release_verification_receipt_legacy_v19;
                """, cancellationToken: token).ConfigureAwait(false);
            await transaction.ExecuteNonQueryAsync("DROP TABLE release_verification_receipt_legacy_v19;", cancellationToken: token).ConfigureAwait(false);
            await transaction.ExecuteNonQueryAsync("""
                CREATE INDEX idx_release_verification_receipt_session ON release_verification_receipt(session_id, root_path, status);
                """, cancellationToken: token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
