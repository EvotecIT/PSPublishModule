namespace PowerForgeStudio.Orchestrator.Storage;

public sealed partial class ReleaseStateDatabase
{
    private const string CreatePublicationReceiptTableSql = """
        CREATE TABLE IF NOT EXISTS release_publish_receipt (
            receipt_id INTEGER PRIMARY KEY,
            session_id TEXT NOT NULL,
            root_path TEXT NOT NULL,
            repository_name TEXT NOT NULL,
            adapter_kind TEXT NOT NULL,
            target_name TEXT NOT NULL,
            target_kind TEXT NOT NULL,
            destination TEXT NULL,
            source_path TEXT NULL,
            package_id TEXT NULL,
            package_version TEXT NULL,
            destination_credentials_omitted INTEGER NOT NULL DEFAULT 0,
            status TEXT NOT NULL,
            summary TEXT NOT NULL,
            published_at_utc TEXT NOT NULL
        );
        """;

    /// <summary>Preserves legacy evidence while removing the target-name uniqueness restriction.</summary>
    private async Task MigratePublicationReceiptsAsync(CancellationToken cancellationToken)
    {
        await using var database = await _sqlite.OpenSessionAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
        await database.RunInTransactionAsync(async (transaction, token) =>
        {
            var columns = await transaction.QueryAsListAsync("PRAGMA table_info(release_publish_receipt);",
                reader => (Name: reader.GetString(1), Type: reader.GetString(2), PrimaryKey: reader.GetInt32(5)), cancellationToken: token).ConfigureAwait(false);
            if (!columns.Any(column => column.Name == "source_path"))
                await transaction.ExecuteNonQueryAsync("ALTER TABLE release_publish_receipt ADD COLUMN source_path TEXT NULL;", cancellationToken: token).ConfigureAwait(false);
            if (!columns.Any(column => column.Name == "package_id"))
                await transaction.ExecuteNonQueryAsync("ALTER TABLE release_publish_receipt ADD COLUMN package_id TEXT NULL;", cancellationToken: token).ConfigureAwait(false);
            if (!columns.Any(column => column.Name == "package_version"))
                await transaction.ExecuteNonQueryAsync("ALTER TABLE release_publish_receipt ADD COLUMN package_version TEXT NULL;", cancellationToken: token).ConfigureAwait(false);
            if (!columns.Any(column => column.Name == "destination_credentials_omitted"))
                await transaction.ExecuteNonQueryAsync("ALTER TABLE release_publish_receipt ADD COLUMN destination_credentials_omitted INTEGER NOT NULL DEFAULT 0;", cancellationToken: token).ConfigureAwait(false);
            if (columns.Any(column => column.Name == "receipt_id" && column.Type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase) && column.PrimaryKey == 1))
                return;

            await transaction.ExecuteNonQueryAsync("ALTER TABLE release_publish_receipt RENAME TO release_publish_receipt_legacy_v18;", cancellationToken: token).ConfigureAwait(false);
            await transaction.ExecuteNonQueryAsync(CreatePublicationReceiptTableSql, cancellationToken: token).ConfigureAwait(false);
            await transaction.ExecuteNonQueryAsync("""
                INSERT INTO release_publish_receipt (
                    session_id, root_path, repository_name, adapter_kind, target_name, target_kind,
                    destination, source_path, status, summary, published_at_utc)
                SELECT session_id, root_path, repository_name, adapter_kind, target_name, target_kind,
                    destination, source_path, status, summary, published_at_utc
                FROM release_publish_receipt_legacy_v18;
                """, cancellationToken: token).ConfigureAwait(false);
            await transaction.ExecuteNonQueryAsync("DROP TABLE release_publish_receipt_legacy_v18;", cancellationToken: token).ConfigureAwait(false);
            await transaction.ExecuteNonQueryAsync("""
                CREATE INDEX idx_release_publish_receipt_session ON release_publish_receipt(session_id, root_path, status);
                """, cancellationToken: token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
