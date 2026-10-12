#if NET8_0_OR_GREATER
using System.Text.Json;

namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    private static int RequestAttempt(JsonElement run) => run.TryGetProperty("run_attempt", out var attempt) ? attempt.GetInt32() : 1;
    private static DateTimeOffset RequestStartedAt(JsonElement run) =>
        run.TryGetProperty("run_started_at", out var started) && started.ValueKind == JsonValueKind.String
            ? started.GetDateTimeOffset() : run.GetProperty("created_at").GetDateTimeOffset();

    /// <summary>Discovers only the new run prefix and polls saved pending/failed observations at real GitHub read boundaries.</summary>
    internal static JsonElement[] ObserveRequestRuns(AppleTestFlightWorkerState state, DateTimeOffset acceptAfter,
        Func<int, JsonElement[]> readPage, Func<long, JsonElement> readRun, Action persist, Func<JsonElement[]>? readOutstanding = null)
    {
        var observations = new Dictionary<long, JsonElement>();
        // Schema-1 journals predating the discovery cursor already retain a
        // consumed frontier. Preserve saved pending ids and poll old rejections
        // independently rather than rescanning all enrolled history on upgrade.
        if (state.LastScannedRequestRun == 0 && state.LastRequestRun > 0)
        {
            // The older journal did not persist queued observations. Recover
            // still-outstanding runs once before migrating its consumed frontier.
            foreach (var run in readOutstanding?.Invoke() ?? Array.Empty<JsonElement>())
                if (run.GetProperty("created_at").GetDateTimeOffset() >= acceptAfter)
                    observations[run.GetProperty("id").GetInt64()] = run;
            state.PendingRequestRuns = state.PendingRequestRuns.Concat(observations.Keys).Distinct().ToList();
            state.LastScannedRequestRun = state.LastRequestRun;
        }
        var highWater = state.LastScannedRequestRun;
        for (var page = 1; ; page++)
        {
            if (page > 100) throw new InvalidOperationException("New request pagination exceeded the worker bound; narrow initial enrollment before continuing.");
            var runs = readPage(page);
            var reachedHistory = false;
            foreach (var run in runs)
            {
                var id = run.GetProperty("id").GetInt64();
                // GitHub lists workflow runs newest-created first; reruns keep
                // their original position and are polled independently below.
                if (run.GetProperty("created_at").GetDateTimeOffset() < acceptAfter || id <= state.LastScannedRequestRun)
                { reachedHistory = true; break; }
                observations[id] = run;
                highWater = Math.Max(highWater, id);
            }
            if (reachedHistory || runs.Length < 100) break;
        }
        // Persist every discovered id before advancing the prefix cursor. A
        // failed read, delayed workflow or process stop cannot create a hole.
        state.PendingRequestRuns = state.PendingRequestRuns.Concat(observations.Keys).Distinct().ToList();
        state.LastScannedRequestRun = highWater;
        persist();
        var accepted = new HashSet<string>(state.Requests.Where(value => value.Operation != "Rejected").Select(value => value.Key), StringComparer.Ordinal);
        JsonElement Read(long id)
        {
            var run = readRun(id);
            if (run.GetProperty("id").GetInt64() != id) throw new AppleTestFlightToolException("Authoritative request run identity changed during observation.");
            return run;
        }
        foreach (var id in state.PendingRequestRuns)
            if (!observations.ContainsKey(id)) observations[id] = Read(id);
        // Failed attempts may be rerun long after their original creation date.
        // Rotate a bounded batch so old rejections neither grow pagination nor
        // monopolize a pass ahead of fresh Off/pause requests.
        var retryIds = state.Requests.Where(value => value.Operation == "Rejected")
            .Select(value => value.Key.Split(':')).Where(parts => parts.Length == 4 && parts[0] == "run" && parts[2] == "attempt")
            .Select(parts => long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)).Distinct()
            .Where(id => !accepted.Contains("run:" + id) && !observations.ContainsKey(id)).OrderBy(id => id).ToArray();
        var retries = retryIds.Where(id => id > state.RejectedRequestScanCursor)
            .Concat(retryIds.Where(id => id <= state.RejectedRequestScanCursor)).Take(20).ToArray();
        foreach (var id in retries) observations[id] = Read(id);
        if (retries.Length > 0)
        {
            state.PendingRequestRuns = state.PendingRequestRuns.Concat(retries).Distinct().ToList();
            state.RejectedRequestScanCursor = retries.Last();
            persist();
        }
        return observations.Values.ToArray();
    }

    /// <summary>Consumes immutable run observations in intent order. Only external reads and durable persistence are supplied by the host.</summary>
    internal static void ConsumeRequestRuns(AppleTestFlightWorkerState state, IEnumerable<JsonElement> runs, DateTimeOffset now,
        Func<JsonElement, AppleTestFlightRequestRecord> readValidated, Action persist)
    {
        foreach (var run in runs.OrderBy(RequestStartedAt).ThenBy(value => value.GetProperty("id").GetInt64()))
        {
            var id = run.GetProperty("id").GetInt64();
            var key = "run:" + id;
            var attempt = RequestAttempt(run);
            var rejectedKey = key + ":attempt:" + attempt;
            if (state.Requests.Any(value => value.Key == key || value.Key == rejectedKey))
            {
                if (state.PendingRequestRuns.Remove(id)) persist();
                continue;
            }
            // A queued request cannot hold intake forever. A later successful
            // rerun has a new attempt and remains eligible after this rejection.
            if (Text(run, "status") != "completed" && now - RequestStartedAt(run) < TimeSpan.FromMinutes(10)) break;
            try
            {
                if (Text(run, "status") != "completed") throw new InvalidOperationException("Request attempt did not finish within the ten-minute intake window.");
                if (Text(run, "conclusion") != "success") throw new InvalidOperationException("Request attempt did not complete successfully.");
                var request = readValidated(run);
                if (request.Key != key) throw new InvalidOperationException("Validated request does not match the observed run.");
                ApplyRequest(state, request);
            }
            catch (Exception exception) when (exception is InvalidOperationException or JsonException or FormatException or OverflowException or KeyNotFoundException)
            {
                state.Requests.Add(new AppleTestFlightRequestRecord
                {
                    Key = rejectedKey, Attempt = attempt, Operation = "Rejected", RecordedAt = RequestStartedAt(run),
                    Url = Text(run, "html_url"), Diagnostic = SafeDiagnostic(exception.Message)
                });
            }
            // Tool/network/filesystem failures escape without discarding intent.
            // The active mutation heartbeat closes its gate until observation succeeds.
            state.PendingRequestRuns.Remove(id);
            state.LastRequestRun = Math.Max(state.LastRequestRun, id);
            persist();
        }
    }
}
#endif
