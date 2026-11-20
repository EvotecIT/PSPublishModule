using DBAClientX;

namespace PowerForge.Backups;

internal static class DatabaseCapture
{
    internal static async Task<CapturedDatabase> CaptureAsync(DatabaseSource source, string run,
        Action<string> log, CancellationToken token)
    {
        var destination = Path.Combine(run, source.Id + (source.Provider == "sqlite" ? ".sqlite" : ".bak"));
        var result = new CapturedDatabase { Id = source.Id, Provider = source.Provider, CapturedAtUtc = DateTimeOffset.UtcNow };
        if (source.Provider == "sqlite")
        {
            Safety.DemandPhysical(source.Database);
            if (!File.Exists(source.Database)) throw new FileNotFoundException("Source database is missing.");
            using var client = new SQLite();
            var last = DateTimeOffset.MinValue;
            var progress = new Progress<SqliteBackupProgress>(_ =>
            {
                if (DateTimeOffset.UtcNow - last > TimeSpan.FromSeconds(30))
                { last = DateTimeOffset.UtcNow; log("Capturing " + source.Id); }
            });
            await client.BackupDatabaseAsync(source.Database, destination, SqliteBackupMethod.Auto,
                new SqliteBackupOptions { PagesPerStep = 1024 }, progress, token);
            await VerifySqliteAsync(destination, source.ValidationQueries, result, false, token);
        }
        else
        {
            using var client = new SqlServer();
            var backup = await client.BackupDatabaseCopyOnlyToDiskAsync(source.ConnectionString, source.Database,
                run, 7200, token);
            File.Move(backup.ServerBackupPath, destination);
            var identity = backup.Header.Identity;
            result.SqlIdentity = new(identity.SourceDatabaseName, identity.BackupSetGuid, identity.MediaName, identity.MediaSetId);
        }
        result.Entry = Path.GetFileName(destination);
        result.SizeBytes = new FileInfo(destination).Length;
        if (result.SizeBytes <= 0) throw new InvalidDataException("Database capture is empty.");
        log("Hashing " + source.Id);
        result.Sha256 = await Safety.HashAsync(destination, token);
        result.Verified = true;
        return result;
    }

    // Read-only and unpooled: the checked file stays byte-identical to its recorded SHA-256, and nothing keeps it open.
    internal static SQLite ReadOnlyClient() => new()
    {
        ConnectionOptions = new SQLiteConnectionOptions { ReadOnly = true, Pooling = false, UseMemoryTempStore = false }
    };

    /// <param name="full">Compare the table count and representative counts with the capture (a restore drill).</param>
    /// <param name="fullIntegrity">Run <c>integrity_check</c> rather than <c>quick_check</c>; defaults to <paramref name="full"/>.</param>
    internal static async Task VerifySqliteAsync(string path, string[] queries, CapturedDatabase captured,
        bool full, CancellationToken token, bool? fullIntegrity = null)
    {
        // Only standalone backup or restored files reach this method, never the live source. DbaClientX runs the check
        // read-only on its own thread with a page cache sized for it.
        using var client = ReadOnlyClient();
        var integrity = await client.CheckIntegrityAsync(path, fullIntegrity ?? full, cancellationToken: token);
        if (!integrity.IsHealthy) throw new InvalidDataException("SQLite integrity verification failed.");
        var tables = Convert.ToInt64(await client.ExecuteScalarAsync(path,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type='table'", cancellationToken: token));
        var counts = new List<long>();
        foreach (var query in queries)
            counts.Add(Convert.ToInt64(await client.ExecuteScalarAsync(path, query, cancellationToken: token)));
        if (full && (tables != captured.TableCount || !counts.SequenceEqual(captured.ValidationCounts)))
            throw new InvalidDataException("Restored SQLite metadata or representative counts differ.");
        captured.TableCount = tables;
        captured.ValidationCounts = counts;
    }
}
