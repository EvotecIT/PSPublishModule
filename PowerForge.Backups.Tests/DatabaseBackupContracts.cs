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
        var options = ValidOptions(); options.Databases = [spec];
        var manifest = new CaptureManifest { HostId = options.HostId, Stamp = "synthetic-capture", CreatedAtUtc = DateTimeOffset.UtcNow };
        manifest.Databases.Add(captured);
        var manifestPath = Path.Combine(run, "manifest.json"); Safety.WriteJson(manifestPath, manifest);
        var header = new { schemaVersion = 2, format = "native", hostId = options.HostId, stamp = manifest.Stamp,
            createdAtUtc = manifest.CreatedAtUtc, sizeBytes = captured.SizeBytes + new FileInfo(manifestPath).Length,
            databases = manifest.Databases, files = new[] { new CapturedFile(captured.Entry, captured.SizeBytes, captured.Sha256),
                new CapturedFile("manifest.json", new FileInfo(manifestPath).Length, await Safety.HashAsync(manifestPath, CancellationToken.None)) } };
        var capturePath = Path.Combine(run, "capture.json"); Safety.WriteJson(capturePath, header);
        await new RestoreDrill().VerifyAsync(options, run, await Safety.HashAsync(capturePath, CancellationToken.None));
        using var restored = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "restore-drill.json")));
        Assert.True(restored.RootElement.GetProperty("healthy").GetBoolean());
        Assert.Empty(Directory.EnumerateDirectories(options.WorkRoot));
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
    public async Task FailedRestoreAttemptReplacesPreviouslyHealthyAttemptStatus()
    {
        var options = ValidOptions();
        var selected = Path.Combine(_root, "download"); Directory.CreateDirectory(selected);
        File.WriteAllText(Path.Combine(selected, "capture.json"), "synthetic");
        File.WriteAllText(Path.Combine(_root, "restore-drill.json"), "{\"healthy\":true}");
        await Assert.ThrowsAsync<InvalidDataException>(() => new RestoreDrill().VerifyAsync(options, selected, new string('0', 64)));
        using var state = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "restore-drill.json")));
        Assert.False(state.RootElement.GetProperty("healthy").GetBoolean());
        Assert.Equal("failed", state.RootElement.GetProperty("status").GetString());
        Assert.Empty(Directory.EnumerateDirectories(options.WorkRoot));
    }

    [Fact]
    public void InvalidHostIdentityCannotEnterSshOrDestinationProtocol()
    {
        var options = ValidOptions(); options.HostId = "../../other";
        Assert.Throws<InvalidDataException>(options.Validate);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("sqlite-wal")]
    [InlineData("sqlite-shm")]
    [InlineData("sqlite-journal")]
    [InlineData("recovery")]
    [InlineData("ssh-key")]
    [InlineData("ssh-pins")]
    [InlineData("ssh-executable")]
    [InlineData("success-companion")]
    [InlineData("restore-companion")]
    public async Task ProtectedInputsCannotBeReplacedByAnyRunStateOutput(string alias)
    {
        var options = ValidOptions();
        var input = alias switch
        {
            "sqlite" => options.Databases[0].Database,
            "sqlite-wal" => options.Databases[0].Database + "-wal",
            "sqlite-shm" => options.Databases[0].Database + "-shm",
            "sqlite-journal" => options.Databases[0].Database + "-journal",
            "ssh-key" => options.Destination.IdentityFile,
            "ssh-pins" => options.Destination.KnownHostsFile,
            "ssh-executable" => options.Destination.SshExecutable,
            "success-companion" => Path.Combine(_root, "last-success.json"),
            "restore-companion" => Path.Combine(_root, "restore-drill.json"),
            _ => Path.Combine(_root, "service.json")
        };
        if (alias is "recovery" or "success-companion" or "restore-companion")
            options.RecoveryFiles = [new() { Id = "service", Path = input }];
        if (alias is not ("success-companion" or "restore-companion")) options.StatePath = input;
        File.WriteAllText(input, "preserve the protected input");
        await Assert.ThrowsAsync<InvalidDataException>(() => new DatabaseBackupRunner().RunAsync(options));
        Assert.Equal("preserve the protected input", File.ReadAllText(input));
        Assert.False(Directory.Exists(options.WorkRoot));
    }

    [Fact]
    public void LoadingConfigurationRejectsOutputsThatWouldReplaceThatConfiguration()
    {
        var options = ValidOptions();
        var path = Path.Combine(_root, "config.json"); options.StatePath = path;
        var original = System.Text.Json.JsonSerializer.Serialize(options, BackupOptions.Json);
        File.WriteAllText(path, original);
        Assert.Throws<InvalidDataException>(() => BackupOptions.Load(path));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public async Task RestoreDrillCannotWriteIntoTheSelectedRecoveryFolder()
    {
        var options = ValidOptions();
        var selected = Path.Combine(_root, "download"); Directory.CreateDirectory(selected);
        options.StatePath = Path.Combine(selected, "state.json");
        var receipt = Path.Combine(selected, "restore-drill.json");
        File.WriteAllText(receipt, "preserve selected recovery files");
        await Assert.ThrowsAsync<InvalidDataException>(() => new RestoreDrill().VerifyAsync(options, selected, new string('0', 64)));
        Assert.Equal("preserve selected recovery files", File.ReadAllText(receipt));
        Assert.False(Directory.Exists(options.WorkRoot));
    }

    [Fact]
    public void StagingCannotUseAnEntireMountedVolume()
    {
        var options = ValidOptions(); options.WorkRoot = Path.GetPathRoot(_root)!;
        Assert.Throws<InvalidDataException>(options.Validate);
    }

    private BackupOptions ValidOptions() => new()
    {
        HostId = "sample", WorkRoot = Path.Combine(_root, "work"), StatePath = Path.Combine(_root, "state.json"),
        Destination = new() { Host = "nas.example", IdentityFile = Path.Combine(_root, "key"),
            KnownHostsFile = Path.Combine(_root, "known_hosts"), SshExecutable = Path.Combine(_root, "ssh") },
        Databases = [new() { Id = "sample", Provider = "sqlite", Database = Path.Combine(_root, "live.sqlite") }]
    };
}
