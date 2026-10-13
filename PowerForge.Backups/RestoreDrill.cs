using DBAClientX;
using System.Data.Common;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Backups;

/// <summary>Decrypts an explicitly selected archive and verifies disposable restored databases.</summary>
public sealed class RestoreDrill
{
    public async Task VerifyAsync(BackupOptions options, string archive, string identityFile,
        string expectedArchiveSha256, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        options.Validate();
        log ??= _ => { };
        if (!Regex.IsMatch(expectedArchiveSha256, "^[a-f0-9]{64}$") ||
            await Safety.HashAsync(archive, cancellationToken) != expectedArchiveSha256)
            throw new InvalidDataException("Selected NAS archive digest differs.");
        Safety.DemandPhysical(options.WorkRoot);
        using var runLock = new FileStream(Path.Combine(options.WorkRoot, ".run.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var run = Path.Combine(options.WorkRoot, "restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        File.WriteAllText(Path.Combine(run, ".powerforge-backup-run"), "powerforge-database-backup-v1\n");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(options.TimeoutMinutes));
        var token = timeout.Token;
        try
        {
            var (manifest, recoveredSecrets) = await ExtractAsync(options, archive, identityFile, run, token);
            if (manifest.HostId != options.HostId || manifest.Databases.Count != options.Databases.Length ||
                !manifest.Databases.Select(d => d.Id).Order().SequenceEqual(options.Databases.Select(d => d.Id).Order()))
                throw new InvalidDataException("Recovery archive does not contain the configured database set.");
            if (recoveredSecrets != manifest.RecoveredSecretCount)
                throw new InvalidDataException("Recovered secret inventory differs.");
            foreach (var captured in manifest.Databases)
            {
                log("Restore verification " + captured.Id);
                var source = options.Databases.Single(d => d.Id == captured.Id);
                var expectedEntry = source.Id + (source.Provider == "sqlite" ? ".sqlite" : ".bak");
                if (captured.Provider != source.Provider || captured.Entry != expectedEntry || !captured.Verified)
                    throw new InvalidDataException("Recovery database metadata differs.");
                var path = Path.Combine(run, expectedEntry);
                if (new FileInfo(path).Length != captured.SizeBytes || await Safety.HashAsync(path, token) != captured.Sha256)
                    throw new InvalidDataException("Recovered database digest differs.");
                if (source.Provider == "sqlite")
                    await DatabaseCapture.VerifySqliteAsync(path, source.ValidationQueries, captured, true, token);
                else await VerifySqlServerAsync(source, captured, path, run, token);
            }
            foreach (var file in manifest.Files)
            {
                if (!Regex.IsMatch(file.Entry, "^config/[a-z][a-z0-9-]{0,31}\\.json$"))
                    throw new InvalidDataException("Unexpected recovered configuration entry.");
                var path = Path.Combine(run, file.Entry.Replace('/', Path.DirectorySeparatorChar));
                if (new FileInfo(path).Length != file.SizeBytes || await Safety.HashAsync(path, token) != file.Sha256)
                    throw new InvalidDataException("Recovered configuration digest differs.");
            }
            Safety.WriteJson(Path.Combine(Path.GetDirectoryName(options.StatePath)!, "restore-drill.json"),
                new { healthy = true, stamp = manifest.Stamp, archiveSha256 = expectedArchiveSha256,
                    completedAtUtc = DateTimeOffset.UtcNow, databaseIds = manifest.Databases.Select(d => d.Id),
                    recoveredSecretCount = recoveredSecrets });
        }
        finally { Safety.RemoveOwnedRun(options.WorkRoot, run); }
    }

    private static async Task<(CaptureManifest Manifest, int SecretCount)> ExtractAsync(BackupOptions options,
        string archive, string identity, string run, CancellationToken token)
    {
        using var process = ProcessRunner.Start(options.AgeExecutable, ["--decrypt", "--identity", identity, archive]);
        using var registration = token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        process.StandardInput.Close();
        var errors = process.StandardError.ReadToEndAsync(token);
        CaptureManifest? manifest = null;
        var secretCount = -1;
        var names = new HashSet<string>();
        long extractedBytes = 0;
        try
        {
            await using (var gzip = new GZipStream(process.StandardOutput.BaseStream, CompressionMode.Decompress, leaveOpen: true))
            await using (var tar = new TarReader(gzip, leaveOpen: true))
            {
                TarEntry? entry;
                while ((entry = await tar.GetNextEntryAsync(false, token)) != null)
                {
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || !names.Add(entry.Name) ||
                        !Regex.IsMatch(entry.Name, "^([a-z][a-z0-9-]{0,31}\\.(sqlite|bak)|config/[a-z][a-z0-9-]{0,31}\\.json|manifest\\.json|recovery/secrets\\.json)$"))
                        throw new InvalidDataException("Unsafe archive entry.");
                    extractedBytes = checked(extractedBytes + entry.Length);
                    if (extractedBytes > 200L * 1024 * 1024 * 1024) throw new InvalidDataException("Recovery extraction exceeds budget.");
                    if (entry.Name is "manifest.json" or "recovery/secrets.json")
                    {
                        if (entry.Length > 1024 * 1024) throw new InvalidDataException("Recovery metadata exceeds budget.");
                        if (entry.Name == "manifest.json")
                            manifest = await JsonSerializer.DeserializeAsync<CaptureManifest>(entry.DataStream!, BackupOptions.Json, token);
                        else
                        {
                            var secrets = await JsonSerializer.DeserializeAsync<Dictionary<string, Dictionary<string, string>>>(entry.DataStream!, BackupOptions.Json, token);
                            secretCount = secrets?.Sum(x => x.Value.Count) ?? -1;
                        }
                    }
                    else
                    {
                        var path = Path.Combine(run, entry.Name.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await using var destination = new FileStream(path, FileMode.CreateNew);
                        if (entry.DataStream != null) await entry.DataStream.CopyToAsync(destination, token);
                    }
                }
                // Consume to EOF so age authenticates every encrypted chunk, including trailing data.
                await gzip.CopyToAsync(Stream.Null, token);
            }
            await process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, token);
            await process.WaitForExitAsync(token);
            await errors;
            if (process.ExitCode != 0 || manifest == null || secretCount < 0)
                throw new InvalidDataException("Archive decryption or recovery metadata verification failed.");
            return (manifest, secretCount);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }

    private static async Task VerifySqlServerAsync(DatabaseSource source, CapturedDatabase captured,
        string path, string run, CancellationToken token)
    {
        var expected = captured.SqlIdentity ?? throw new InvalidDataException("SQL backup identity absent.");
        using var client = new SqlServer();
        var files = await client.ReadDiskBackupFileListAsync(source.ConnectionString, path, 7200, token);
        var target = "PowerForgeRestore_" + Guid.NewGuid().ToString("N");
        var moves = files.ToDictionary(f => f.LogicalName, f => Path.Combine(run, target + "-" + Array.IndexOf(files.ToArray(), f) + (f.FileType == "L" ? ".ldf" : ".mdf")));
        // The pinned DbaClientX restore refuses existing database names and allocated file paths; never REPLACE.
        var builder = new DbConnectionStringBuilder { ConnectionString = source.ConnectionString };
        builder.Remove("Database"); builder.Remove("Initial Catalog"); builder["Initial Catalog"] = "master";
        var masterConnection = builder.ConnectionString;
        try
        {
            await client.RestoreDatabaseAsNewAsync(source.ConnectionString, path, target,
                new SqlServerBackupIdentity(expected.Database, expected.BackupSetGuid, expected.MediaName, expected.MediaSetId), moves, 7200, token);
            var errors = await client.QueryAsListAsync(masterConnection,
                "DBCC CHECKDB ([" + target + "]) WITH TABLERESULTS, NO_INFOMSGS", _ => true, cancellationToken: token);
            if (errors.Count != 0) throw new InvalidDataException("Restored SQL Server integrity check failed.");
            builder["Initial Catalog"] = target;
            foreach (var query in source.ValidationQueries)
                await client.ExecuteScalarAsync(builder.ConnectionString, query, cancellationToken: token);
        }
        finally
        {
            // Failed restores can leave RESTORING databases. Prove every file belongs to this drill before removing it.
            var paths = await client.QueryAsListAsync(masterConnection,
                "SELECT physical_name FROM sys.master_files WHERE database_id=DB_ID(@name)", row => row.GetString(0),
                new Dictionary<string, object?> { ["@name"] = target }, cancellationToken: CancellationToken.None);
            if (paths.Count > 0)
            {
                if (paths.Any(p => !moves.Values.Contains(p, StringComparer.OrdinalIgnoreCase)))
                    throw new IOException("Disposable SQL database ownership could not be established; cleanup held.");
                await client.ExecuteNonQueryAsync(masterConnection,
                    "DROP DATABASE [" + target + "]", cancellationToken: CancellationToken.None);
            }
        }
    }
}
