using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal static class AppleDeviceCommandSupport
{
    internal static ErrorRecord CreateProcessError(ProcessRunResult result, string errorId, string message)
    {
        var detail = string.Join(Environment.NewLine, new[] { result.StdErr, result.StdOut }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));
        var errorMessage = string.IsNullOrWhiteSpace(detail)
            ? $"{message} ExitCode={result.ExitCode}. TimedOut={result.TimedOut}."
            : $"{message} ExitCode={result.ExitCode}. TimedOut={result.TimedOut}.{Environment.NewLine}{detail}";

        return new ErrorRecord(
            new InvalidOperationException(errorMessage),
            errorId,
            ErrorCategory.OperationStopped,
            result);
    }
}
