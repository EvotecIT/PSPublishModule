using System.Text;
using System.Text.Json;

namespace PowerForge.Backups;

/// <summary>Captures, verifies, encrypts and publishes a complete set through a restricted SSH receiver.</summary>
public sealed class DatabaseBackupRunner
{
    public async Task<BackupRunState> RunAsync(BackupOptions options, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        options.Validate();
        log ??= _ => { };
        Safety.DemandPhysical(options.WorkRoot);
        Directory.CreateDirectory(options.WorkRoot);
        using var runLock = new FileStream(Path.Combine(options.WorkRoot, ".run.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(options.TimeoutMinutes));
        var token = timeout.Token;
        var state = new BackupRunState { HostId = options.HostId, StartedAtUtc = DateTimeOffset.UtcNow, Status = "running", Stage = "preflight" };
        state.Stamp = state.StartedAtUtc.ToString("yyyyMMdd'T'HHmmss'Z'") + "-" + Guid.NewGuid().ToString("N")[..8];
        var run = Path.Combine(options.WorkRoot, state.Stamp);
        void Stage(string value) { state.Stage = value; Safety.WriteJson(options.StatePath, state); log(value); }
        try
        {
            Stage("preflight");
            if (new DriveInfo(Path.GetPathRoot(options.WorkRoot)!).AvailableFreeSpace < options.MinimumFreeBytes)
                throw new IOException("Insufficient free staging space.");
            foreach (var file in options.RecoveryFiles)
                if (!File.Exists(file.Path)) throw new FileNotFoundException("Required recovery configuration is missing.");
            Directory.CreateDirectory(run);
            File.WriteAllText(Path.Combine(run, ".powerforge-backup-run"), "powerforge-database-backup-v1\n");
            var manifest = new CaptureManifest { HostId = options.HostId, Stamp = state.Stamp, CreatedAtUtc = state.StartedAtUtc };
            state.Manifest = manifest;
            foreach (var source in options.Databases)
            {
                Stage("capture-" + source.Id);
                manifest.Databases.Add(await DatabaseCapture.CaptureAsync(source, run, log, token));
            }
            Stage("encrypt");
            var archive = Path.Combine(run, "recovery.tar.gz.age");
            await EncryptedArchive.CreateAsync(options, manifest, run, archive, token);
            Stage("hash-archive");
            state.ArchiveBytes = new FileInfo(archive).Length;
            state.ArchiveSha256 = await Safety.HashAsync(archive, token);
            Stage("transfer");
            var header = new { schemaVersion = 1, hostId = options.HostId, stamp = state.Stamp,
                createdAtUtc = state.StartedAtUtc, sizeBytes = state.ArchiveBytes, sha256 = state.ArchiveSha256,
                databases = manifest.Databases.Select(db => new { db.Id, db.Provider, db.CapturedAtUtc, db.Verified, db.SizeBytes }),
                recoveredSecretCount = manifest.RecoveredSecretCount };
            var response = await ProcessRunner.RunAsync(options.Destination.SshExecutable,
                ProcessRunner.SshArguments(options.Destination, "receive"), async input =>
                {
                    await input.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header, BackupOptions.Json).Replace("\n", "") + "\n"), token);
                    await using var source = File.OpenRead(archive);
                    await source.CopyToAsync(input, token);
                }, token);
            using var receipt = JsonDocument.Parse(response);
            var value = receipt.RootElement;
            if (value.GetProperty("stamp").GetString() != state.Stamp || value.GetProperty("sha256").GetString() != state.ArchiveSha256 ||
                value.GetProperty("sizeBytes").GetInt64() != state.ArchiveBytes || !value.GetProperty("verified").GetBoolean())
                throw new InvalidDataException("NAS receipt does not match this capture.");
            state.NasVerifiedAtUtc = value.GetProperty("verifiedAtUtc").GetDateTimeOffset();
            state.Status = "success";
            state.CompletedAtUtc = DateTimeOffset.UtcNow;
            Stage("complete");
            Safety.WriteJson(Path.Combine(Path.GetDirectoryName(options.StatePath)!, "last-success.json"), state);
        }
        catch (Exception exception)
        {
            state.Status = "failed"; state.ErrorType = exception.GetType().Name; state.CompletedAtUtc = DateTimeOffset.UtcNow;
            Safety.WriteJson(options.StatePath, state);
            throw;
        }
        finally
        {
            if (Directory.Exists(run))
            {
                try { Safety.RemoveOwnedRun(options.WorkRoot, run); }
                catch (Exception exception) { state.CleanupErrorType = exception.GetType().Name; }
            }
            Safety.WriteJson(options.StatePath, state);
        }
        return state;
    }
}
