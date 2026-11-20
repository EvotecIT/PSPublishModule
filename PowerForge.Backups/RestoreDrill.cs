using DBAClientX;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Backups;

/// <summary>Verifies selected native NAS files and restores SQL Server into an owned disposable database.</summary>
public sealed class RestoreDrill
{
    public Task VerifyAsync(BackupOptions options, string selectedFolder, string expectedCaptureSha256,
        Action<string>? log = null, CancellationToken cancellationToken = default)
        => VerifyAsync(options, selectedFolder, expectedCaptureSha256, null, log, cancellationToken);

    /// <summary>
    /// Runs the drill, reporting each step to <paramref name="progress"/>. Cancelling
    /// <paramref name="cancellationToken"/> records the attempt as cancelled by the user, not as a failure.
    /// </summary>
    public async Task VerifyAsync(BackupOptions options, string selectedFolder, string expectedCaptureSha256,
        IRestoreDrillProgress? progress, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var steps = new RestoreDrillSteps(progress ?? NoRestoreDrillProgress.Instance);
        var report = steps.Progress;
        options.Validate();
        options.DemandSeparatedStorage([Path.GetFullPath(selectedFolder)]);
        log ??= _ => { };
        Safety.DemandPhysical(options.WorkRoot);
        FileStream? runLock = null;
        var run = Path.Combine(options.WorkRoot, "restore-" + Guid.NewGuid().ToString("N"));
        var statusPath = Path.Combine(Path.GetDirectoryName(options.StatePath)!, "restore-drill.json");
        var state = new RestoreDrillState { StartedAtUtc = DateTimeOffset.UtcNow, Status = "running", CaptureSha256 = expectedCaptureSha256 };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.RestoreDrillTimeoutMinutes is { } limit) timeout.CancelAfter(TimeSpan.FromMinutes(limit));
        var token = timeout.Token;
        var stateGate = new object();
        void Record()
        {
            lock (stateGate)
                try { Safety.WriteJson(statusPath, state); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        // A closing console allows only a few seconds, and SQL Server cleanup can take longer: record the cancellation
        // the moment it is requested, on the cancelling thread, before any cleanup starts.
        using var recordCancellation = cancellationToken.Register(() =>
        {
            lock (stateGate)
            {
                if (state.Status != "running") return;
                state.Status = "cancelled";
                state.ErrorType = "CancelledByUser";
                state.CompletedAtUtc = DateTimeOffset.UtcNow;
            }
            Record();
        });
        try
        {
            Directory.CreateDirectory(options.WorkRoot);
            lock (stateGate) Safety.WriteJson(statusPath, state);
            runLock = new FileStream(Path.Combine(options.WorkRoot, ".run.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            Safety.DemandPhysical(selectedFolder);
            report.Plan("capture", "Check the capture record", 0, RestoreDrillUnit.None);
            // capture.json is small and names every database, so it is read before any step starts: the whole plan is
            // reported first, as IRestoreDrillProgress promises. A failure while reading it still ends the capture step.
            steps.OwnBeforeStart("capture");
            var capturePath = Path.Combine(selectedFolder, "capture.json");
            if (!Regex.IsMatch(expectedCaptureSha256, "\\A[a-f0-9]{64}\\z") || await Safety.HashAsync(capturePath, token) != expectedCaptureSha256)
                throw new InvalidDataException("Selected native NAS capture digest differs.");
            var header = JsonSerializer.Deserialize<NativeTransferHeader>(File.ReadAllText(capturePath), BackupOptions.Json)
                ?? throw new InvalidDataException("Selected native capture is empty.");
            if (header.SchemaVersion != 2 || header.Format != "native" || header.HostId != options.HostId)
                throw new InvalidDataException("Native capture format or source differs.");
            foreach (var file in header.Files)
                if (!Regex.IsMatch(file.Entry, "\\A([a-z][a-z0-9-]{0,31}\\.(sqlite|bak)|config/[a-z][a-z0-9-]{0,31}\\.json|manifest\\.json)\\z"))
                    throw new InvalidDataException("Unsafe native file entry.");
            var databaseFiles = header.Files.Where(IsDatabaseEntry).ToArray();
            foreach (var file in databaseFiles)
                report.Plan("checksum:" + file.Entry, "Checksum " + file.Entry, file.SizeBytes, RestoreDrillUnit.Bytes);
            foreach (var file in databaseFiles)
            {
                var id = Path.GetFileNameWithoutExtension(file.Entry);
                if (file.Entry.EndsWith(".bak", StringComparison.Ordinal))
                {
                    report.Plan("copy:" + id, "Copy " + file.Entry + " for SQL Server", file.SizeBytes, RestoreDrillUnit.Bytes);
                    report.Plan("restore:" + id, "Restore " + id + " into SQL Server", 100, RestoreDrillUnit.Percent);
                    report.Plan("check:" + id, "Check " + id + " (DBCC CHECKDB)", 100, RestoreDrillUnit.Percent);
                }
                else report.Plan("check:" + id, "Check " + id + " (SQLite integrity)", 0, RestoreDrillUnit.None);
            }
            steps.Start("capture", "the recovery files capture.json lists");
            foreach (var file in header.Files.Where(f => !IsDatabaseEntry(f)))
                await DemandFileAsync(selectedFolder, file, null, token);
            steps.Finish("capture");
            foreach (var file in databaseFiles)
            {
                var key = "checksum:" + file.Entry;
                steps.Start(key);
                await DemandFileAsync(selectedFolder, file, done => report.Report(key, done), token);
                steps.Finish(key);
            }
            var manifest = JsonSerializer.Deserialize<CaptureManifest>(File.ReadAllText(Path.Combine(selectedFolder, "manifest.json")), BackupOptions.Json)
                ?? throw new InvalidDataException("Native recovery manifest is empty.");
            if (manifest.SchemaVersion != 2 || manifest.Format != "native" || manifest.HostId != options.HostId ||
                manifest.Databases.Count != options.Databases.Length ||
                !manifest.Databases.Select(d => d.Id).Order().SequenceEqual(options.Databases.Select(d => d.Id).Order()) ||
                !manifest.Files.Select(f => f.Entry).Order().SequenceEqual(options.RecoveryFiles.Select(f => "config/" + f.Id + ".json").Order()))
                throw new InvalidDataException("Native recovery inventory differs.");
            if (manifest.Databases.Select(d => d.Entry).Concat(manifest.Files.Select(f => f.Entry)).Append("manifest.json").Order()
                .SequenceEqual(header.Files.Select(f => f.Entry).Order()) is false)
                throw new InvalidDataException("Native capture and recovery inventories differ.");
            Directory.CreateDirectory(run);
            File.WriteAllText(Path.Combine(run, ".powerforge-backup-run"), "powerforge-database-backup-v1\n");
            foreach (var captured in manifest.Databases)
            {
                log("Restore verification " + captured.Id);
                var checkKey = "check:" + captured.Id;
                var source = options.Databases.Single(d => d.Id == captured.Id);
                var expectedEntry = source.Id + (source.Provider == "sqlite" ? ".sqlite" : ".bak");
                if (captured.Provider != source.Provider || captured.Entry != expectedEntry || !captured.Verified)
                    throw new InvalidDataException("Recovery database metadata differs.");
                var file = header.Files.Single(f => f.Entry == expectedEntry);
                if (file.SizeBytes != captured.SizeBytes || file.Sha256 != captured.Sha256)
                    throw new InvalidDataException("Database recovery inventory differs.");
                var path = Path.Combine(selectedFolder, expectedEntry);
                if (source.Provider == "sqlite")
                {
                    steps.Start(checkKey, "working; SQLite gives no percentage");
                    await DatabaseCapture.VerifySqliteAsync(path, source.ValidationQueries, captured, true, token);
                    steps.Finish(checkKey);
                }
                else
                {
                    await DemandCopySpaceAsync(options, source, captured.SizeBytes, log, token);
                    var copy = Path.Combine(run, expectedEntry);
                    var copyKey = "copy:" + captured.Id;
                    steps.Start(copyKey);
                    await Safety.CopyAsync(path, copy, done => report.Report(copyKey, done), token);
                    steps.Finish(copyKey);
                    await VerifySqlServerAsync(source, captured, copy, run, options.MinimumFreeBytes, steps, log, token);
                }
            }
            lock (stateGate)
            {
                // A cancellation recorded after the last check (during its cleanup, for example) wins over success; the
                // cancellation callback holds this lock, so one of the two outcomes is recorded, never both.
                cancellationToken.ThrowIfCancellationRequested();
                state.Stamp = manifest.Stamp;
                state.DatabaseIds = manifest.Databases.Select(d => d.Id).ToArray();
                state.Status = "success";
                state.ErrorType = null;
            }
        }
        catch (Exception error)
        {
            bool cancelled;
            lock (stateGate)
            {
                // Read under the lock the cancellation callback takes, so a cancellation it recorded is never overwritten.
                cancelled = cancellationToken.IsCancellationRequested || state.Status == "cancelled";
                state.Status = cancelled ? "cancelled" : "failed";
                // The drill's own limit cancels the linked token; name that, so it is not mistaken for a stop by the user.
                state.ErrorType = cancelled ? "CancelledByUser"
                    : timeout.IsCancellationRequested && error is OperationCanceledException ? "TimedOut" : error.GetType().Name;
                state.CompletedAtUtc = DateTimeOffset.UtcNow;
            }
            steps.FailRunning(cancelled ? "cancelled by the user" : state.ErrorType == "TimedOut" ? "stopped at the drill's time limit" : error.GetType().Name);
            Record();
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(run))
                {
                    try { state.CleanupLogicalBytes = Safety.RemoveOwnedRun(options.WorkRoot, run); }
                    catch (Exception error) { state.CleanupErrorType = error.GetType().Name; if (state.Status != "cancelled") state.Status = "failed"; }
                }
                lock (stateGate)
                {
                    state.Healthy = state.Status == "success";
                    state.CompletedAtUtc = DateTimeOffset.UtcNow;
                    Safety.WriteJson(statusPath, state);
                }
                if (state.Healthy) Safety.WriteJson(Path.Combine(Path.GetDirectoryName(statusPath)!, "restore-drill-success.json"), state);
            }
            finally { runLock?.Dispose(); }
        }
        if (!state.Healthy) throw new IOException("Restore drill cleanup failed.");
    }

    private static bool IsDatabaseEntry(CapturedFile file)
        => file.Entry.EndsWith(".sqlite", StringComparison.Ordinal) || file.Entry.EndsWith(".bak", StringComparison.Ordinal);

    private static async Task DemandFileAsync(string folder, CapturedFile file, Action<long>? progress, CancellationToken token)
    {
        var path = Path.Combine(folder, file.Entry.Replace('/', Path.DirectorySeparatorChar));
        Safety.DemandPhysical(path);
        if (new FileInfo(path).Length != file.SizeBytes ||
            (progress is null ? await Safety.HashAsync(path, token) : await Safety.HashAsync(path, progress, token)) != file.Sha256)
            throw new InvalidDataException("Selected native backup file digest differs.");
    }

    private static string MasterConnection(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        builder.Remove("Database"); builder.Remove("Initial Catalog");
        builder["Initial Catalog"] = "master";
        builder["Pooling"] = false;
        return builder.ConnectionString;
    }

    // Space for the backup copy is checked before SQL Server is contacted. When it is short, a database left by a stopped drill may
    // hold it: remove those (only this drill's own) and check once more. When SQL Server cannot be reached, the shortage stands.
    private static async Task DemandCopySpaceAsync(BackupOptions options, DatabaseSource source, long requiredBytes, Action<string> log, CancellationToken token)
    {
        try
        {
            Safety.DemandFreeSpace(options.WorkRoot, options.MinimumFreeBytes, requiredBytes);
            return;
        }
        catch (IOException shortage)
        {
            try { await RemoveLeftoverRestoresAsync(MasterConnection(source.ConnectionString), options.WorkRoot, log, token); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                log("Leftover restores could not be checked: " + error.GetType().Name);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(shortage).Throw();
            }
        }
        Safety.DemandFreeSpace(options.WorkRoot, options.MinimumFreeBytes, requiredBytes);
    }

    // A drill stopped while SQL Server held its disposable database (a closed console allows only seconds) leaves that
    // database behind. Remove only databases with the drill's generated name whose every file is in one of its run folders.
    private static async Task RemoveLeftoverRestoresAsync(string master, string workRoot, Action<string> log, CancellationToken token)
    {
        using var client = new SqlServer();
        var files = await client.QueryAsListAsync(master,
            "SELECT d.name, f.physical_name FROM sys.databases d JOIN sys.master_files f ON f.database_id = d.database_id WHERE d.name LIKE 'PowerForgeRestore[_]%'",
            row => (Name: row.GetString(0), Path: row.GetString(1)), cancellationToken: token);
        var root = Path.GetFullPath(workRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var database in files.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var owned = Regex.IsMatch(database.Key, "\\APowerForgeRestore_[0-9a-f]{32}\\z") && database.All(f =>
            {
                var full = Path.GetFullPath(f.Path);
                if (!full.StartsWith(root, Safety.PathComparison)) return false;
                var folder = full.Substring(root.Length).Split(Path.DirectorySeparatorChar)[0];
                return folder.StartsWith("restore-", StringComparison.Ordinal) && Path.GetFileName(full).StartsWith(database.Key + "-", StringComparison.Ordinal);
            });
            if (!owned) { log("Leaving " + database.Key + ": its files are not all in a drill run folder."); continue; }
            await client.ExecuteNonQueryAsync(master, "DROP DATABASE [" + database.Key + "]", cancellationToken: token);
            log("Removed " + database.Key + ", left by an earlier drill that was stopped.");
        }
    }

    // SQL Server reports RESTORE and DBCC CHECKDB progress in sys.dm_exec_requests; polled on its own connection and
    // matched to this drill's statement by its generated database name. Without VIEW SERVER STATE the step still shows
    // its running time.
    private static async Task TrackServerPercentAsync(string master, string command, string target, string key, IRestoreDrillProgress progress,
        Func<Task> work, CancellationToken token)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var poll = Task.Run(async () =>
        {
            using var watcher = new SqlServer();
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);
                var values = await watcher.QueryAsListAsync(master,
                    "SELECT MAX(r.percent_complete) FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t " +
                    "WHERE r.command LIKE @command AND t.text LIKE @target",
                    row => row.IsDBNull(0) ? (double?)null : Convert.ToDouble(row.GetValue(0)),
                    new Dictionary<string, object?> { ["@command"] = command, ["@target"] = "%" + target.Replace("_", "[_]") + "%" },
                    cancellationToken: stop.Token);
                if (values.FirstOrDefault() is { } percent)
                    progress.Report(key, (long)Math.Floor(percent), "SQL Server reports " + percent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%");
            }
        }, CancellationToken.None);
        try { await work(); }
        finally
        {
            stop.Cancel();
            try { await poll; } catch { /* progress polling never decides the drill's outcome */ }
        }
    }

    private static async Task VerifySqlServerAsync(DatabaseSource source, CapturedDatabase captured,
        string path, string run, long minimumFreeBytes, RestoreDrillSteps steps, Action<string> log, CancellationToken token)
    {
        var expected = captured.SqlIdentity ?? throw new InvalidDataException("SQL backup identity absent.");
        using var client = new SqlServer();
        var files = await client.ReadDiskBackupFileListAsync(source.ConnectionString, path, 7200, token);
        var master = MasterConnection(source.ConnectionString);
        // SQL Server is in use by now: remove databases left by a stopped drill before they count against this restore.
        await RemoveLeftoverRestoresAsync(master, Path.GetDirectoryName(run)!, log, token);
        Safety.DemandFreeSpace(run, minimumFreeBytes, files.Aggregate(0L, (total, file) => checked(total + file.SizeBytes)));
        var target = "PowerForgeRestore_" + Guid.NewGuid().ToString("N");
        var moves = files.Select((f, index) => (f.LogicalName, Path: Path.Combine(run, target + "-" + index + (f.FileType == "L" ? ".ldf" : ".mdf"))))
            .ToDictionary(f => f.LogicalName, f => f.Path);
        var builder = new DbConnectionStringBuilder { ConnectionString = master };
        try
        {
            var restoreKey = "restore:" + captured.Id;
            var checkKey = "check:" + captured.Id;
            steps.Start(restoreKey, "as disposable database " + target);
            await TrackServerPercentAsync(master, "RESTORE%", target, restoreKey, steps.Progress, async () =>
                await client.RestoreDatabaseAsNewAsync(master, path, target,
                    new SqlServerBackupIdentity(expected.Database, expected.BackupSetGuid, expected.MediaName, expected.MediaSetId), moves, 7200, token), token);
            steps.Finish(restoreKey);
            steps.Start(checkKey);
            var errorCount = 0;
            await TrackServerPercentAsync(master, "DBCC%", target, checkKey, steps.Progress, async () =>
                errorCount = (await client.QueryAsListAsync(master,
                    "DBCC CHECKDB ([" + target + "]) WITH TABLERESULTS, NO_INFOMSGS", _ => true, cancellationToken: token)).Count, token);
            if (errorCount != 0) throw new InvalidDataException("Restored SQL Server integrity check failed.");
            builder["Initial Catalog"] = target;
            foreach (var query in source.ValidationQueries)
                await client.ExecuteScalarAsync(builder.ConnectionString, query, cancellationToken: token);
            steps.Finish(checkKey, "integrity check and validation queries passed");
        }
        finally
        {
            var paths = await client.QueryAsListAsync(master,
                "SELECT physical_name FROM sys.master_files WHERE database_id=DB_ID(@name)", row => row.GetString(0),
                new Dictionary<string, object?> { ["@name"] = target }, cancellationToken: CancellationToken.None);
            if (paths.Count > 0)
            {
                if (paths.Any(p => !moves.Values.Any(owned => string.Equals(owned, p, Safety.PathComparison))))
                    throw new IOException("Disposable SQL database ownership could not be established; cleanup held.");
                await client.ExecuteNonQueryAsync(master, "DROP DATABASE [" + target + "]", cancellationToken: CancellationToken.None);
            }
        }
    }
}

internal sealed class NativeTransferHeader
{
    public int SchemaVersion { get; set; }
    public string Format { get; set; } = "";
    public string HostId { get; set; } = "";
    public List<CapturedFile> Files { get; set; } = [];
}

internal sealed class RestoreDrillState
{
    public bool Healthy { get; set; }
    public string Status { get; set; } = "";
    public string? Stamp { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public string CaptureSha256 { get; set; } = "";
    public string[] DatabaseIds { get; set; } = [];
    public string? ErrorType { get; set; }
    public string? CleanupErrorType { get; set; }
    public long CleanupLogicalBytes { get; set; }
}
