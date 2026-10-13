using DBAClientX;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Backups;

/// <summary>Verifies selected native NAS files and restores SQL Server into an owned disposable database.</summary>
public sealed class RestoreDrill
{
    public async Task VerifyAsync(BackupOptions options, string selectedFolder, string expectedCaptureSha256,
        Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        options.Validate();
        log ??= _ => { };
        Safety.DemandPhysical(options.WorkRoot);
        Directory.CreateDirectory(options.WorkRoot);
        using var runLock = new FileStream(Path.Combine(options.WorkRoot, ".run.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var run = Path.Combine(options.WorkRoot, "restore-" + Guid.NewGuid().ToString("N"));
        var statusPath = Path.Combine(Path.GetDirectoryName(options.StatePath)!, "restore-drill.json");
        var state = new RestoreDrillState { StartedAtUtc = DateTimeOffset.UtcNow, Status = "running", CaptureSha256 = expectedCaptureSha256 };
        Safety.WriteJson(statusPath, state);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(options.TimeoutMinutes));
        var token = timeout.Token;
        try
        {
            Safety.DemandPhysical(selectedFolder);
            var capturePath = Path.Combine(selectedFolder, "capture.json");
            if (!Regex.IsMatch(expectedCaptureSha256, "^[a-f0-9]{64}$") || await Safety.HashAsync(capturePath, token) != expectedCaptureSha256)
                throw new InvalidDataException("Selected native NAS capture digest differs.");
            var header = JsonSerializer.Deserialize<NativeTransferHeader>(File.ReadAllText(capturePath), BackupOptions.Json)
                ?? throw new InvalidDataException("Selected native capture is empty.");
            if (header.SchemaVersion != 2 || header.Format != "native" || header.HostId != options.HostId)
                throw new InvalidDataException("Native capture format or source differs.");
            foreach (var file in header.Files)
            {
                if (!Regex.IsMatch(file.Entry, "^([a-z][a-z0-9-]{0,31}\\.(sqlite|bak)|config/[a-z][a-z0-9-]{0,31}\\.json|manifest\\.json)$"))
                    throw new InvalidDataException("Unsafe native file entry.");
                var path = Path.Combine(selectedFolder, file.Entry.Replace('/', Path.DirectorySeparatorChar));
                Safety.DemandPhysical(path);
                if (new FileInfo(path).Length != file.SizeBytes || await Safety.HashAsync(path, token) != file.Sha256)
                    throw new InvalidDataException("Selected native backup file digest differs.");
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
                var source = options.Databases.Single(d => d.Id == captured.Id);
                var expectedEntry = source.Id + (source.Provider == "sqlite" ? ".sqlite" : ".bak");
                if (captured.Provider != source.Provider || captured.Entry != expectedEntry || !captured.Verified)
                    throw new InvalidDataException("Recovery database metadata differs.");
                var file = header.Files.Single(f => f.Entry == expectedEntry);
                if (file.SizeBytes != captured.SizeBytes || file.Sha256 != captured.Sha256)
                    throw new InvalidDataException("Database recovery inventory differs.");
                var path = Path.Combine(selectedFolder, expectedEntry);
                if (source.Provider == "sqlite")
                    await DatabaseCapture.VerifySqliteAsync(path, source.ValidationQueries, captured, true, token);
                else
                {
                    var copy = Path.Combine(run, expectedEntry);
                    File.Copy(path, copy);
                    await VerifySqlServerAsync(source, captured, copy, run, token);
                }
            }
            state.Stamp = manifest.Stamp;
            state.DatabaseIds = manifest.Databases.Select(d => d.Id).ToArray();
            state.Status = "success";
        }
        catch (Exception error) { state.Status = "failed"; state.ErrorType = error.GetType().Name; throw; }
        finally
        {
            if (Directory.Exists(run))
            {
                try { state.CleanupLogicalBytes = Safety.RemoveOwnedRun(options.WorkRoot, run); }
                catch (Exception error) { state.CleanupErrorType = error.GetType().Name; state.Status = "failed"; }
            }
            state.Healthy = state.Status == "success";
            state.CompletedAtUtc = DateTimeOffset.UtcNow;
            Safety.WriteJson(statusPath, state);
            if (state.Healthy) Safety.WriteJson(Path.Combine(Path.GetDirectoryName(statusPath)!, "restore-drill-success.json"), state);
        }
        if (!state.Healthy) throw new IOException("Restore drill cleanup failed.");
    }

    private static async Task VerifySqlServerAsync(DatabaseSource source, CapturedDatabase captured,
        string path, string run, CancellationToken token)
    {
        var expected = captured.SqlIdentity ?? throw new InvalidDataException("SQL backup identity absent.");
        using var client = new SqlServer();
        var files = await client.ReadDiskBackupFileListAsync(source.ConnectionString, path, 7200, token);
        var target = "PowerForgeRestore_" + Guid.NewGuid().ToString("N");
        var moves = files.Select((f, index) => (f.LogicalName, Path: Path.Combine(run, target + "-" + index + (f.FileType == "L" ? ".ldf" : ".mdf"))))
            .ToDictionary(f => f.LogicalName, f => f.Path);
        var builder = new DbConnectionStringBuilder { ConnectionString = source.ConnectionString };
        builder.Remove("Database"); builder.Remove("Initial Catalog");
        builder["Initial Catalog"] = "master";
        builder["Pooling"] = false;
        var master = builder.ConnectionString;
        try
        {
            await client.RestoreDatabaseAsNewAsync(master, path, target,
                new SqlServerBackupIdentity(expected.Database, expected.BackupSetGuid, expected.MediaName, expected.MediaSetId), moves, 7200, token);
            var errors = await client.QueryAsListAsync(master,
                "DBCC CHECKDB ([" + target + "]) WITH TABLERESULTS, NO_INFOMSGS", _ => true, cancellationToken: token);
            if (errors.Count != 0) throw new InvalidDataException("Restored SQL Server integrity check failed.");
            builder["Initial Catalog"] = target;
            foreach (var query in source.ValidationQueries)
                await client.ExecuteScalarAsync(builder.ConnectionString, query, cancellationToken: token);
        }
        finally
        {
            var paths = await client.QueryAsListAsync(master,
                "SELECT physical_name FROM sys.master_files WHERE database_id=DB_ID(@name)", row => row.GetString(0),
                new Dictionary<string, object?> { ["@name"] = target }, cancellationToken: CancellationToken.None);
            if (paths.Count > 0)
            {
                if (paths.Any(p => !moves.Values.Contains(p, StringComparer.OrdinalIgnoreCase)))
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
