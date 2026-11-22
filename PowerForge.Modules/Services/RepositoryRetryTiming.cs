using System.Net;

namespace PowerForge;

internal static class RepositoryRetryTiming
{
    internal static bool IsTransientStatus(HttpStatusCode status, bool includeNotFound = true)
        => (includeNotFound && status == HttpStatusCode.NotFound) ||
           status == HttpStatusCode.RequestTimeout || (int)status == 429 || (int)status >= 500;

    internal static TimeSpan Backoff(TimeSpan initialDelay, int attempt)
        => TimeSpan.FromMilliseconds(Math.Min(30000, Math.Max(0, initialDelay.TotalMilliseconds) * Math.Pow(2, Math.Min(attempt - 1, 10))));
}
