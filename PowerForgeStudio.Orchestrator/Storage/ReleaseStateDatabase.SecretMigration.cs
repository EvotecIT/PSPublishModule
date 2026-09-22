using DBAClientX;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Storage;

public sealed partial class ReleaseStateDatabase
{
    /// <summary>Scrubs older release evidence once, before advancing the schema version.</summary>
    private async Task MigrateStoredReleaseSecretsAsync(CancellationToken cancellationToken)
    {
        await using var database = await _sqlite.OpenSessionAsync(DatabasePath, cancellationToken).ConfigureAwait(false);
        await database.RunInTransactionAsync(async (transaction, token) =>
        {
            var version = await transaction.ExecuteScalarAsync(
                "SELECT value FROM app_schema WHERE key = 'schema_version';", cancellationToken: token).ConfigureAwait(false);
            if (string.Equals(version as string, CurrentSchemaVersion, StringComparison.Ordinal)) return;

            var publications = await transaction.QueryAsListAsync(
                "SELECT receipt_id, destination, summary, destination_credentials_omitted FROM release_publish_receipt;",
                reader => (Id: reader.GetInt64(0), Destination: reader.IsDBNull(1) ? null : reader.GetString(1),
                    Summary: reader.GetString(2), Omitted: reader.GetInt32(3) != 0), cancellationToken: token).ConfigureAwait(false);
            foreach (var row in publications)
            {
                var destination = StudioOutputSanitizer.SanitizeDestination(row.Destination);
                var summary = StudioOutputSanitizer.Sanitize(row.Summary);
                var omitted = row.Omitted || StudioOutputSanitizer.DestinationCredentialsOmitted(row.Destination);
                if (destination == row.Destination && summary == row.Summary && omitted == row.Omitted) continue;
                await transaction.ExecuteNonQueryAsync(
                    "UPDATE release_publish_receipt SET destination = @Destination, summary = @Summary, destination_credentials_omitted = @Omitted WHERE receipt_id = @Id;",
                    new Dictionary<string, object?> { ["@Destination"] = destination, ["@Summary"] = summary, ["@Omitted"] = omitted, ["@Id"] = row.Id }, token).ConfigureAwait(false);
            }

            var verifications = await transaction.QueryAsListAsync(
                "SELECT receipt_id, destination, summary FROM release_verification_receipt;",
                reader => (Id: reader.GetInt64(0), Destination: reader.IsDBNull(1) ? null : reader.GetString(1),
                    Summary: reader.GetString(2)), cancellationToken: token).ConfigureAwait(false);
            foreach (var row in verifications)
            {
                var destination = StudioOutputSanitizer.SanitizeDestination(row.Destination);
                var summary = StudioOutputSanitizer.Sanitize(row.Summary);
                if (destination == row.Destination && summary == row.Summary) continue;
                await transaction.ExecuteNonQueryAsync(
                    "UPDATE release_verification_receipt SET destination = @Destination, summary = @Summary WHERE receipt_id = @Id;",
                    new Dictionary<string, object?> { ["@Destination"] = destination, ["@Summary"] = summary, ["@Id"] = row.Id }, token).ConfigureAwait(false);
            }

            var queueItems = await transaction.QueryAsListAsync(
                "SELECT session_id, queue_order, summary, checkpoint_state_json FROM release_queue_item;",
                reader => (SessionId: reader.GetString(0), Order: reader.GetInt32(1), Summary: reader.GetString(2),
                    Checkpoint: reader.IsDBNull(3) ? null : reader.GetString(3)), cancellationToken: token).ConfigureAwait(false);
            foreach (var row in queueItems)
            {
                var summary = StudioOutputSanitizer.Sanitize(row.Summary);
                var checkpoint = ReleaseCheckpointEvidenceSanitizer.Sanitize(row.Checkpoint);
                if (summary == row.Summary && checkpoint == row.Checkpoint) continue;
                await transaction.ExecuteNonQueryAsync(
                    "UPDATE release_queue_item SET summary = @Summary, checkpoint_state_json = @Checkpoint WHERE session_id = @SessionId AND queue_order = @Order;",
                    new Dictionary<string, object?> { ["@Summary"] = summary, ["@Checkpoint"] = checkpoint, ["@SessionId"] = row.SessionId, ["@Order"] = row.Order }, token).ConfigureAwait(false);
            }

            var progress = await transaction.QueryAsListAsync(
                "SELECT progress_id, item_name, item_path, state, detail FROM release_execution_progress;",
                reader => (Id: reader.GetInt64(0), Name: reader.GetString(1), Path: reader.IsDBNull(2) ? null : reader.GetString(2),
                    State: reader.GetString(3), Detail: reader.GetString(4)), cancellationToken: token).ConfigureAwait(false);
            foreach (var row in progress)
            {
                var name = StudioOutputSanitizer.Sanitize(row.Name);
                var path = row.Path is null ? null : StudioOutputSanitizer.Sanitize(row.Path);
                var state = StudioOutputSanitizer.Sanitize(row.State);
                var detail = StudioOutputSanitizer.Sanitize(row.Detail);
                if (name == row.Name && path == row.Path && state == row.State && detail == row.Detail) continue;
                await transaction.ExecuteNonQueryAsync(
                    "UPDATE release_execution_progress SET item_name = @Name, item_path = @Path, state = @State, detail = @Detail WHERE progress_id = @Id;",
                    new Dictionary<string, object?> { ["@Name"] = name, ["@Path"] = path, ["@State"] = state, ["@Detail"] = detail, ["@Id"] = row.Id }, token).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
