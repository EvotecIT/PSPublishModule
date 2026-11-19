using System.Collections.Concurrent;
using System.Text.Json;
using DBAClientX;
using PowerForge.Backups;

namespace PowerForge.Backups.Tests;

public sealed partial class DatabaseBackupContracts
{
    [Fact]
    public async Task RestoreDrillPlansEveryStepBeforeRunningAndFinishesEachOne()
    {
        var (options, selected, captureHash) = await SelectedSqliteSetAsync();
        var progress = new RecordingProgress();
        await new RestoreDrill().VerifyAsync(options, selected, captureHash, progress);

        var planned = progress.Events.Where(e => e.Kind == "plan").Select(e => e.Key).ToArray();
        Assert.Equal(["capture", "checksum:sample.sqlite", "check:sample"], planned);
        var firstStart = progress.Events.FindIndex(e => e.Kind == "start");
        Assert.True(progress.Events.FindLastIndex(e => e.Kind == "plan") < firstStart, "Steps were planned after the first step began.");
        Assert.All(planned, key => Assert.Contains(progress.Events, e => e.Kind == "finish" && e.Key == key && e.Succeeded));
        var checksum = progress.Events.Last(e => e.Kind == "report" && e.Key == "checksum:sample.sqlite");
        Assert.Equal(new FileInfo(Path.Combine(selected, "sample.sqlite")).Length, checksum.Done);
    }

    [Fact]
    public async Task CancellingTheDrillRecordsItAsCancelledByTheUser()
    {
        var (options, selected, captureHash) = await SelectedSqliteSetAsync();
        using var cancellation = new CancellationTokenSource();
        string? recordedOnCancel = null;
        var progress = new RecordingProgress
        {
            OnStart = key =>
            {
                if (!key.StartsWith("check:", StringComparison.Ordinal)) return;
                cancellation.Cancel();
                // A closing console allows only seconds: the record must already say cancelled before any cleanup runs.
                recordedOnCancel = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "restore-drill.json"))).RootElement.GetProperty("status").GetString();
            }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RestoreDrill().VerifyAsync(options, selected, captureHash, progress, null, cancellation.Token));

        Assert.Equal("cancelled", recordedOnCancel);
        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "restore-drill.json")));
        Assert.Equal("cancelled", state.RootElement.GetProperty("status").GetString());
        Assert.Equal("CancelledByUser", state.RootElement.GetProperty("errorType").GetString());
        Assert.False(state.RootElement.GetProperty("healthy").GetBoolean());
        Assert.Contains(progress.Events, e => e.Kind == "finish" && e.Key == "check:sample" && !e.Succeeded && e.Detail == "cancelled by the user");
        Assert.False(File.Exists(Path.Combine(_root, "restore-drill-success.json")));
        Assert.Empty(Directory.EnumerateDirectories(options.WorkRoot));
    }

    [Fact]
    public async Task CancellingAsTheLastCheckFinishesIsNotReportedAsSuccess()
    {
        var (options, selected, captureHash) = await SelectedSqliteSetAsync();
        using var cancellation = new CancellationTokenSource();
        // After the last check's own cancellation checks and before the drill records its result.
        var progress = new RecordingProgress { OnFinish = key => { if (key == "check:sample") cancellation.Cancel(); } };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RestoreDrill().VerifyAsync(options, selected, captureHash, progress, null, cancellation.Token));

        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "restore-drill.json")));
        Assert.Equal("cancelled", state.RootElement.GetProperty("status").GetString());
        Assert.False(state.RootElement.GetProperty("healthy").GetBoolean());
        Assert.False(File.Exists(Path.Combine(_root, "restore-drill-success.json")));
    }

    [Fact]
    public void TextProgressShowsAmountTimeLeftAndKeepsQuietStepsVisible()
    {
        var now = TimeSpan.Zero;
        var output = new StringWriter();
        var text = new RestoreDrillTextProgress(output, () => now) { Interval = TimeSpan.FromSeconds(10) };
        text.Plan("checksum:big.sqlite", "Checksum big.sqlite", 40_000_000_000, RestoreDrillUnit.Bytes);
        text.Plan("check:big", "Check big (SQLite integrity)", 0, RestoreDrillUnit.None);
        text.Start("checksum:big.sqlite");
        now = TimeSpan.FromSeconds(5);
        text.Report("checksum:big.sqlite", 5_000_000_000);
        now = TimeSpan.FromSeconds(60);
        text.Report("checksum:big.sqlite", 10_000_000_000);
        text.Finish("checksum:big.sqlite", true);
        text.Start("check:big");
        now = TimeSpan.FromSeconds(75);
        text.Heartbeat();
        text.Finish("check:big", false, "cancelled by the user");

        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line => line[10..]).ToArray();
        Assert.Equal(
        [
            "Started  Checksum big.sqlite",
            "Working  Checksum big.sqlite: 10.0 of 40.0 GB (25%), 1m 00s elapsed, about 3m 00s left",
            "Passed   Checksum big.sqlite in 1m 00s",
            "Started  Check big (SQLite integrity)",
            "Working  Check big (SQLite integrity): 15s elapsed",
            "Failed   Check big (SQLite integrity) in 15s (cancelled by the user)"
        ], lines);
    }

    [Fact]
    public void TextProgressStopsWritingWhenItsOutputFails()
    {
        var text = new RestoreDrillTextProgress(new FailingWriter(), () => TimeSpan.Zero);
        text.Plan("check:big", "Check big", 0, RestoreDrillUnit.None);
        text.Start("check:big");
        text.Heartbeat();
        text.Finish("check:big", true);
    }

    private sealed class FailingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("There is not enough space on the disk.");
    }

    private async Task<(BackupOptions Options, string Selected, string CaptureHash)> SelectedSqliteSetAsync()
    {
        var source = Path.Combine(_root, "live.sqlite");
        using var client = new SQLite();
        await client.ExecuteNonQueryAsync(source, "CREATE TABLE facts(id INTEGER PRIMARY KEY, value TEXT); INSERT INTO facts VALUES(1,'committed');");
        var run = Path.Combine(_root, "selected"); Directory.CreateDirectory(run);
        var spec = new DatabaseSource { Id = "sample", Provider = "sqlite", Database = source, ValidationQueries = ["SELECT COUNT(*) FROM facts"] };
        var captured = await DatabaseCapture.CaptureAsync(spec, run, _ => { }, CancellationToken.None);
        var options = ValidOptions(); options.Databases = [spec];
        var manifest = new CaptureManifest { HostId = options.HostId, Stamp = "synthetic-capture", CreatedAtUtc = DateTimeOffset.UtcNow };
        manifest.Databases.Add(captured);
        var manifestPath = Path.Combine(run, "manifest.json"); Safety.WriteJson(manifestPath, manifest);
        var header = new { schemaVersion = 2, format = "native", hostId = options.HostId, stamp = manifest.Stamp,
            createdAtUtc = manifest.CreatedAtUtc, sizeBytes = captured.SizeBytes + new FileInfo(manifestPath).Length,
            databases = manifest.Databases, files = new[] { new CapturedFile(captured.Entry, captured.SizeBytes, captured.Sha256),
                new CapturedFile("manifest.json", new FileInfo(manifestPath).Length, await Safety.HashAsync(manifestPath, CancellationToken.None)) } };
        var capturePath = Path.Combine(run, "capture.json"); Safety.WriteJson(capturePath, header);
        return (options, run, await Safety.HashAsync(capturePath, CancellationToken.None));
    }

    private sealed class RecordingProgress : IRestoreDrillProgress
    {
        private readonly ConcurrentQueue<(string Kind, string Key, long Done, bool Succeeded, string? Detail)> _events = new();
        internal Action<string>? OnStart { get; init; }
        internal Action<string>? OnFinish { get; init; }
        internal List<(string Kind, string Key, long Done, bool Succeeded, string? Detail)> Events => _events.ToList();
        public void Plan(string key, string label, long total, RestoreDrillUnit unit) => _events.Enqueue(("plan", key, total, false, label));
        public void Start(string key, string? detail = null) { _events.Enqueue(("start", key, 0, false, detail)); OnStart?.Invoke(key); }
        public void Report(string key, long done, string? detail = null) => _events.Enqueue(("report", key, done, false, detail));
        public void Finish(string key, bool succeeded, string? detail = null) { _events.Enqueue(("finish", key, 0, succeeded, detail)); OnFinish?.Invoke(key); }
    }
}
