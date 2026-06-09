using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private sealed class NullDotNetPublishProgressReporter : IDotNetPublishProgressReporter
    {
        public static readonly NullDotNetPublishProgressReporter Instance = new();
        private NullDotNetPublishProgressReporter() { }
        public void StepStarting(DotNetPublishStep step) { }
        public void StepCompleted(DotNetPublishStep step) { }
        public void StepFailed(DotNetPublishStep step, Exception error) { }
    }

    private sealed class DotNetPublishStepException : Exception
    {
        public DotNetPublishStep Step { get; }

        public DotNetPublishStepException(DotNetPublishStep step, Exception inner)
            : base($"Step '{(step ?? throw new ArgumentNullException(nameof(step))).Key}' failed. {(inner ?? throw new ArgumentNullException(nameof(inner))).Message}", inner)
        {
            Step = step;
        }
    }

    private sealed class DotNetPublishCommandException : Exception
    {
        public int ExitCode { get; }
        public string FileName { get; }
        public string WorkingDirectory { get; }
        public string StdOut { get; }
        public string StdErr { get; }
        public string[] Args { get; }

        public string CommandLine
        {
            get
            {
#if NET472
                return FileName + " " + BuildWindowsArgumentString(Args);
#else
                return FileName + " " + string.Join(" ", Args.Select(EscapeForCommandLine));
#endif
            }
        }

        public DotNetPublishCommandException(
            string message,
            string fileName,
            string workingDirectory,
            IReadOnlyList<string> args,
            int exitCode,
            string stdOut,
            string stdErr)
            : base(message)
        {
            ExitCode = exitCode;
            FileName = fileName ?? string.Empty;
            WorkingDirectory = workingDirectory ?? string.Empty;
            StdOut = stdOut ?? string.Empty;
            StdErr = stdErr ?? string.Empty;
            Args = args is null ? Array.Empty<string>() : args.ToArray();
        }

#if !NET472
        private static string EscapeForCommandLine(string arg)
        {
            if (arg is null) return "\"\"";
            if (arg.Length == 0) return "\"\"";
            return arg.Any(char.IsWhiteSpace) || arg.Contains('"')
                ? "\"" + arg.Replace("\"", "\\\"") + "\""
                : arg;
        }
#endif
    }
}
