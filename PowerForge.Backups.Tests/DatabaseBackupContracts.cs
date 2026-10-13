using DBAClientX;
using PowerForge.Backups;

namespace PowerForge.Backups.Tests;

public sealed class DatabaseBackupContracts : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForgeBackupTest-" + Guid.NewGuid().ToString("N"));

    public DatabaseBackupContracts() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task WalCaptureContainsCommittedDataAndLeavesLiveDatabaseWritable()
    {
        var source = Path.Combine(_root, "live.sqlite");
        using var client = new SQLite();
        await client.ExecuteNonQueryAsync(source, "PRAGMA journal_mode=WAL; CREATE TABLE facts(id INTEGER PRIMARY KEY, value TEXT); INSERT INTO facts VALUES(1,'committed');");
        var run = Path.Combine(_root, "run"); Directory.CreateDirectory(run);
        var spec = new DatabaseSource { Id = "sample", Provider = "sqlite", Database = source, ValidationQueries = ["SELECT COUNT(*) FROM facts"] };
        var captured = await DatabaseCapture.CaptureAsync(spec, run, _ => { }, CancellationToken.None);
        await client.ExecuteNonQueryAsync(source, "INSERT INTO facts VALUES(2,'after-capture');");
        Assert.True(captured.Verified);
        Assert.Equal(1, Assert.Single(captured.ValidationCounts));
        Assert.Equal(2L, Convert.ToInt64(await client.ExecuteScalarAsync(source, "SELECT COUNT(*) FROM facts")));
        await DatabaseCapture.VerifySqliteAsync(Path.Combine(run, captured.Entry), spec.ValidationQueries, captured, true, CancellationToken.None);
    }

    [Fact]
    public async Task RestoreVerificationRejectsChangedRepresentativeData()
    {
        var db = Path.Combine(_root, "copy.sqlite");
        using var client = new SQLite();
        await client.ExecuteNonQueryAsync(db, "CREATE TABLE facts(id INTEGER); INSERT INTO facts VALUES(1);");
        var captured = new CapturedDatabase { TableCount = 1, ValidationCounts = [2] };
        await Assert.ThrowsAsync<InvalidDataException>(() => DatabaseCapture.VerifySqliteAsync(db,
            ["SELECT COUNT(*) FROM facts"], captured, true, CancellationToken.None));
    }

    [Fact]
    public void CleanupRefusesUnmarkedDirectoryAndPreservesContents()
    {
        var run = Path.Combine(_root, "valuable"); Directory.CreateDirectory(run);
        File.WriteAllText(Path.Combine(run, "file"), "preserve");
        Assert.Throws<IOException>(() => Safety.RemoveOwnedRun(_root, run));
        Assert.True(File.Exists(Path.Combine(run, "file")));
    }

    [Fact]
    public void OverlapRecognizesContainmentAndDistinguishesSiblingPrefixes()
    {
        Assert.True(Safety.Overlaps(_root, Path.Combine(_root, "live.sqlite")));
        Assert.False(Safety.Overlaps(_root, _root + "-sibling"));
    }

    [Fact]
    public async Task WrongSelectedArchiveDigestFailsBeforeCreatingRestoreOutput()
    {
        var archive = Path.Combine(_root, "archive.age"); File.WriteAllText(archive, "synthetic");
        var options = ValidOptions();
        await Assert.ThrowsAsync<InvalidDataException>(() => new RestoreDrill().VerifyAsync(options, archive,
            Path.Combine(_root, "identity"), new string('0', 64)));
        Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [Fact]
    public void InvalidHostIdentityCannotEnterSshOrDestinationProtocol()
    {
        var options = ValidOptions(); options.HostId = "../../other";
        Assert.Throws<InvalidDataException>(options.Validate);
    }

    private BackupOptions ValidOptions() => new()
    {
        HostId = "sample", WorkRoot = Path.Combine(_root, "work"), StatePath = Path.Combine(_root, "state.json"),
        AgeExecutable = Path.Combine(_root, "age"), Recipient = "age1" + new string('q', 58),
        Destination = new() { Host = "nas.example", IdentityFile = Path.Combine(_root, "key"),
            KnownHostsFile = Path.Combine(_root, "known_hosts"), SshExecutable = Path.Combine(_root, "ssh") },
        Databases = [new() { Id = "sample", Provider = "sqlite", Database = Path.Combine(_root, "live.sqlite") }]
    };
}
