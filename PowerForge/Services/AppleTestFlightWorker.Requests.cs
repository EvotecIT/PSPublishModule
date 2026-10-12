#if NET8_0_OR_GREATER
using System.Text.Json;

namespace PowerForge;

internal sealed partial class AppleTestFlightWorker
{
    private static int RequestAttempt(JsonElement run) => run.TryGetProperty("run_attempt", out var attempt) ? attempt.GetInt32() : 1;
    private static DateTimeOffset RequestStartedAt(JsonElement run) =>
        run.TryGetProperty("run_started_at", out var started) && started.ValueKind == JsonValueKind.String
            ? started.GetDateTimeOffset() : run.GetProperty("created_at").GetDateTimeOffset();

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
            if (state.Requests.Any(value => value.Key == key || value.Key == rejectedKey)) continue;
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
            state.LastRequestRun = Math.Max(state.LastRequestRun, id);
            persist();
        }
    }
}
#endif
