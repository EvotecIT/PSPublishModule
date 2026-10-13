using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Backups;

/// <summary>Captures, verifies and publishes native backup files through a restricted SSH receiver.</summary>
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
        state.Stamp = state.StartedAtUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        var run = Path.Combine(options.WorkRoot, state.Stamp);
        void Stage(string value) { state.Stage = value; Safety.WriteJson(options.StatePath, state); log(value); }
        try
        {
            Stage("preflight");
            var held = new List<string>();
            foreach (var stale in Directory.EnumerateDirectories(options.WorkRoot))
            {
                if (Regex.IsMatch(Path.GetFileName(stale), "^\\d{8}T\\d{6}Z-[a-f0-9]{8}$") &&
                    File.Exists(Path.Combine(stale, ".powerforge-backup-run")))
                {
                    try { state.CleanupLogicalBytes += Safety.RemoveOwnedRun(options.WorkRoot, stale); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { held.Add(stale); }
                }
                else if (Path.GetFileName(stale).StartsWith("restore-", StringComparison.Ordinal)) held.Add(stale);
            }
            state.HeldStagingPaths = held.ToArray();
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
            Stage("configuration");
            Directory.CreateDirectory(Path.Combine(run, "config"));
            foreach (var file in options.RecoveryFiles)
            {
                Safety.DemandPhysical(file.Path);
                var entry = "config/" + file.Id + ".json";
                var destination = Path.Combine(run, "config", file.Id + ".json");
                File.Copy(file.Path, destination);
                File.SetAttributes(destination, File.GetAttributes(destination) & ~FileAttributes.ReadOnly);
                manifest.Files.Add(new(entry, new FileInfo(destination).Length, await Safety.HashAsync(destination, token)));
            }
            Safety.WriteJson(Path.Combine(run, "manifest.json"), manifest);
            var files = manifest.Databases.Select(d => new CapturedFile(d.Entry, d.SizeBytes, d.Sha256)).Concat(manifest.Files).ToList();
            files.Add(new("manifest.json", new FileInfo(Path.Combine(run, "manifest.json")).Length,
                await Safety.HashAsync(Path.Combine(run, "manifest.json"), token)));
            state.TransferBytes = files.Sum(f => f.SizeBytes);
            var header = new { schemaVersion = 2, format = "native", hostId = options.HostId, stamp = state.Stamp,
                createdAtUtc = state.StartedAtUtc, sizeBytes = state.TransferBytes, databases = manifest.Databases, files };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(header, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            state.CaptureSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Stage("transfer");
            var response = await ProcessRunner.RunAsync(options.Destination.SshExecutable,
                ProcessRunner.SshArguments(options.Destination, "receive"), async input =>
                {
                    await input.WriteAsync(bytes, token);
                    await input.WriteAsync(new byte[] { 10 }, token);
                    foreach (var file in files)
                    {
                        await using var source = File.OpenRead(Path.Combine(run, file.Entry.Replace('/', Path.DirectorySeparatorChar)));
                        await source.CopyToAsync(input, token);
                        log("Transferred " + file.Entry);
                    }
                }, token);
            using var receipt = JsonDocument.Parse(response);
            var value = receipt.RootElement;
            if (value.GetProperty("stamp").GetString() != state.Stamp || value.GetProperty("captureSha256").GetString() != state.CaptureSha256 ||
                value.GetProperty("sizeBytes").GetInt64() != state.TransferBytes || !value.GetProperty("verified").GetBoolean())
                throw new InvalidDataException("NAS receipt does not match this capture.");
            state.NasVerifiedAtUtc = value.GetProperty("verifiedAtUtc").GetDateTimeOffset();
            state.Status = "success";
            state.CompletedAtUtc = DateTimeOffset.UtcNow;
            Stage("complete");
        }
        catch (Exception exception)
        {
            state.Status = "failed"; state.ErrorType = exception.GetType().Name; state.CompletedAtUtc = DateTimeOffset.UtcNow;
            throw;
        }
        finally
        {
            if (Directory.Exists(run))
            {
                try { state.CleanupLogicalBytes += Safety.RemoveOwnedRun(options.WorkRoot, run); }
                catch (Exception exception) { state.CleanupErrorType = exception.GetType().Name; }
            }
            Safety.WriteJson(options.StatePath, state);
            if (state.Status == "success") Safety.WriteJson(Path.Combine(Path.GetDirectoryName(options.StatePath)!, "last-success.json"), state);
        }
        return state;
    }
}
