using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;

namespace PowerForge.Backups;

internal static class EncryptedArchive
{
    internal static async Task CreateAsync(BackupOptions options, CaptureManifest manifest, string run,
        string archive, CancellationToken token)
    {
        var recoverySecrets = new Dictionary<string, Dictionary<string, string>>();
        var copies = new Dictionary<string, string>();
        foreach (var file in options.RecoveryFiles)
        {
            Safety.DemandPhysical(file.Path);
            var copy = Path.Combine(run, "config-" + file.Id + ".json");
            File.Copy(file.Path, copy);
            copies.Add(file.Id, copy);
            if (file.RecoverDpapiSecrets) recoverySecrets[file.Id] = RecoverySecrets.Read(copy);
            manifest.Files.Add(new("config/" + file.Id + ".json", new FileInfo(copy).Length,
                await Safety.HashAsync(copy, token)));
        }
        manifest.RecoveredSecretCount = recoverySecrets.Sum(x => x.Value.Count);
        // Sensitive recovered values only enter the encryption stream; no plaintext recovery file is written.
        await ProcessRunner.RunAsync(options.AgeExecutable, ["--encrypt", "--recipient", options.Recipient, "--output", archive], async input =>
        {
            await using var gzip = new GZipStream(input, CompressionLevel.Fastest, leaveOpen: true);
            await using var writer = new TarWriter(gzip, leaveOpen: true);
            foreach (var db in manifest.Databases)
                await writer.WriteEntryAsync(Path.Combine(run, db.Entry), db.Entry, token);
            foreach (var file in options.RecoveryFiles)
                await writer.WriteEntryAsync(copies[file.Id], "config/" + file.Id + ".json", token);
            await WriteJsonEntryAsync(writer, "recovery/secrets.json", recoverySecrets, token);
            await WriteJsonEntryAsync(writer, "manifest.json", manifest, token);
        }, token);
    }

    private static async Task WriteJsonEntryAsync(TarWriter writer, string name, object value, CancellationToken token)
    {
        await using var bytes = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(value, BackupOptions.Json));
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = bytes };
        await writer.WriteEntryAsync(entry, token);
    }
}
