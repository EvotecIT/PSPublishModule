using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Retains useful, bounded Apple tool errors without retaining credential values.</summary>
internal static class AppleToolFailureFormatter
{
    private static readonly Regex ErrorLine = new(@"\berror:|\berrSec\w+|\bITMS-\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Redacts complete streams before choosing error lines and bounded excerpts.</summary>
    internal static string Format(ProcessRunResult failed, IEnumerable<string> sensitiveValues, string? summary = null)
    {
        var secrets = sensitiveValues.ToArray();
        var status = failed.StartFailed ? "could not start" : failed.TimedOut ? "timed out" :
            failed.StandardOutputLimitExceeded || failed.StandardErrorLimitExceeded ? "exceeded its output limit" : "failed";
        summary = ReleaseCredentialRedactor.RedactText(
            summary ?? $"{failed.Executable} {status} (exit code {failed.ExitCode}).", secrets);
        var stdout = ReleaseCredentialRedactor.RedactText(failed.StdOut, secrets).Trim();
        var stderr = ReleaseCredentialRedactor.RedactText(failed.StdErr, secrets).Trim();
        var streams = new List<string>();
        const int outputBudget = 1760;
        var stderrBudget = string.IsNullOrWhiteSpace(stdout) ? outputBudget : Math.Min(stderr.Length, outputBudget / 2);
        if (!string.IsNullOrWhiteSpace(stdout))
            streams.Add("stdout:\n" + Compact(stdout, outputBudget - stderrBudget));
        if (!string.IsNullOrWhiteSpace(stderr))
            streams.Add("stderr:\n" + Compact(stderr, stderrBudget));
        return Compact(summary, 200) + (streams.Count == 0 ? string.Empty : "\n" + string.Join("\n", streams));
    }

    private static string Compact(string text, int maximumLength)
    {
        // Xcode writes provisioning and compiler errors to stdout after its build preamble.
        // Prefer those observed errors over truncating them out of the retained evidence.
        var errors = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => ErrorLine.IsMatch(line)).Reverse().Take(8).Reverse().ToArray();
        if (errors.Length > 0)
            text = string.Join(Environment.NewLine, errors);
        if (text.Length <= maximumLength)
            return text;
        const string separator = "\n... output truncated ...\n";
        var headLength = (maximumLength - separator.Length) / 2;
        return text.Substring(0, headLength) + separator + text.Substring(text.Length - (maximumLength - separator.Length - headLength));
    }
}
