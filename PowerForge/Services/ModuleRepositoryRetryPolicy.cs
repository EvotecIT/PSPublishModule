using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Shared retry decisions for repository reads and module/tool installation.</summary>
internal sealed class ModuleRepositoryRetryPolicy
{
    internal const int MaximumAttempts = 4;
    private readonly ILogger _logger;
    private readonly Action<TimeSpan> _delay;

    internal ModuleRepositoryRetryPolicy(ILogger logger, Action<TimeSpan>? delay = null)
    {
        _logger = logger;
        _delay = delay ?? (duration => System.Threading.Thread.Sleep(duration));
    }

    internal static bool IsTransientStatus(HttpStatusCode status, bool includeNotFound = true)
        => (includeNotFound && status == HttpStatusCode.NotFound) ||
           status == HttpStatusCode.RequestTimeout || (int)status == 429 || (int)status >= 500;

    internal static TimeSpan Backoff(TimeSpan initialDelay, int attempt)
        => TimeSpan.FromMilliseconds(Math.Min(30000, Math.Max(0, initialDelay.TotalMilliseconds) * Math.Pow(2, Math.Min(attempt - 1, 10))));

    /// <summary>Retries the existing child-process protocol without retrying missing tools or user cancellation.</summary>
    internal PowerShellRunResult RunPowerShell(string operation, Func<PowerShellRunResult> run)
        => Execute(operation, () =>
        {
            var result = run();
            if (result.ExitCode != 0 && result.ExitCode != 3 && IsTransientError(ReadError(result)))
                throw new TransientRepositoryFailure(ReadError(result));
            return result;
        });

    /// <summary>Also covers the installer's direct repository bootstrap, before a PowerShell backend exists.</summary>
    internal T Execute<T>(string operation, Func<T> run)
    {
        for (var attempt = 1; ; attempt++)
        {
            _logger.Info($"{operation}: attempt {attempt}/{MaximumAttempts}.");
            try { return run(); }
            catch (Exception ex) when (IsTransientException(ex))
            {
                if (attempt == MaximumAttempts)
                    throw new InvalidOperationException($"{operation} failed after {attempt} attempts. Verify the repository, package name and requested version; for Gallery outages retry later. Last error: {ex.Message}", ex);
                Wait(operation, attempt, ex is TimeoutException ? "timeout" : "transient repository failure (404/408/429/5xx or transport error)");
            }
        }
    }

    private static bool IsTransientException(Exception exception)
    {
        if (exception is TransientRepositoryFailure or TimeoutException) return true;
        if (exception is System.Net.Http.HttpRequestException httpException)
        {
#if !NET472
            if (httpException.StatusCode.HasValue)
                return IsTransientStatus(httpException.StatusCode.Value);
#endif
            return !Regex.IsMatch(httpException.Message, @"(?<!\d)(400|401|403)(?!\d)");
        }
        return exception is WebException webException && webException.Status == WebExceptionStatus.Timeout;
    }

    internal sealed class TransientRepositoryFailure(string message) : Exception(message);

    private void Wait(string operation, int attempt, string reason)
    {
        var delay = Backoff(TimeSpan.FromSeconds(1), attempt);
        _logger.Warn($"{operation}: {reason} on attempt {attempt}/{MaximumAttempts}; retrying in {delay.TotalSeconds:0} seconds.");
        _delay(delay);
    }

    private static string ReadError(PowerShellRunResult result)
    {
        foreach (var line in result.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var marker = line.IndexOf("::ERROR::", StringComparison.Ordinal);
            if (marker < 0) continue;
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(marker + 9))); }
            catch (FormatException) { /* A non-protocol line may contain the same text. */ }
        }
        return result.StdErr;
    }

    internal static bool IsTransientError(string message)
        => Regex.IsMatch(message ?? string.Empty,
            @"(?i)(?<!\d)(404|408|429|5\d\d)(?!\d)|\btoo many requests\b|\b(timed?\s*out|timeout)\b|temporarily unavailable|service unavailable|bad gateway|gateway timeout");
}
