using System.Diagnostics;

namespace PowerForge;

internal sealed partial class PowerForgeToolReleaseService
{
    internal static ProcessExecutionResult RunProcess(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo);
        if (process is null)
            return new ProcessExecutionResult(1, string.Empty, "Failed to start process.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        using var cancellationRegistration = cancellationToken.Register(
            static state => TryKillProcessTree((Process)state!),
            process);
        process.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessExecutionResult(
            process.ExitCode,
            stdOutTask.GetAwaiter().GetResult(),
            stdErrTask.GetAwaiter().GetResult());
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (process.HasExited)
                return;
#if NET472
            process.Kill();
#else
            process.Kill(entireProcessTree: true);
#endif
        }
        catch
        {
            // Best-effort cancellation cleanup.
        }
    }

    private const int MessageHeadCharacters = 500;
    private const int MessageTailCharacters = 2500;

    /// <summary>
    /// Bounds failed process output for an exception message. dotnet and MSBuild print the actual
    /// error and the build summary last, so the tail is kept and only a short head is retained.
    /// </summary>
    internal static string TrimForMessage(string? stdErr, string? stdOut)
    {
        var combined = string.Join(
            Environment.NewLine,
            new[] { stdErr?.Trim(), stdOut?.Trim() }.Where(text => !string.IsNullOrWhiteSpace(text)));
        if (combined.Length <= MessageHeadCharacters + MessageTailCharacters)
            return combined;

        var omitted = combined.Length - MessageHeadCharacters - MessageTailCharacters;
        return combined.Substring(0, MessageHeadCharacters) +
               $"{Environment.NewLine}... {omitted} character(s) omitted ...{Environment.NewLine}" +
               combined.Substring(combined.Length - MessageTailCharacters);
    }

    internal readonly struct ProcessExecutionResult
    {
        public ProcessExecutionResult(int exitCode, string stdOut, string stdErr)
        {
            ExitCode = exitCode;
            StdOut = stdOut ?? string.Empty;
            StdErr = stdErr ?? string.Empty;
        }

        public int ExitCode { get; }

        public string StdOut { get; }

        public string StdErr { get; }
    }
}
