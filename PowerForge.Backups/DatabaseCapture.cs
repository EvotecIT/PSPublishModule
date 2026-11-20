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

    // Negative: kibibytes, as SQLite's cache_size reads it.
    internal const int CheckCacheKibibytes = -256 * 1024;

    // Read-only, so the checked file stays byte-identical to its recorded SHA-256. The default 2 MB page cache makes
    // integrity_check re-read index pages from disk for every row: a 56 GB file with 108 indexes read about 365 GB in four
    // hours without finishing. A 256 MB cache keeps the pages it revisits; SQLite allocates it only as pages are read, and
    // an unpooled connection frees it (and the file) when each statement's connection closes.
    internal static SQLite CheckClient() => new()
    {
        ConnectionOptions = new SQLiteConnectionOptions { ReadOnly = true, Pooling = false, CacheSize = CheckCacheKibibytes, UseMemoryTempStore = false }
    };

    internal static async Task VerifySqliteAsync(string path, string[] queries, CapturedDatabase captured,
        bool full, CancellationToken token)
    {
        // Only standalone backup or restored files reach this method, never the live source.
        using var client = CheckClient();
        var check = Convert.ToString(await client.ExecuteScalarAsync(path,
            full ? "PRAGMA integrity_check" : "PRAGMA quick_check", cancellationToken: token));
        if (check != "ok") throw new InvalidDataException("SQLite integrity verification failed.");
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
