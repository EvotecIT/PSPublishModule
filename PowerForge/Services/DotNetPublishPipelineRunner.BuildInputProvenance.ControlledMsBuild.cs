using System.Text;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static (int ExitCode, string StdOut, string StdErr, bool TimedOut)
        RunControlledMsBuildEvaluationProcess(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string?> environmentVariables,
            TimeSpan timeout,
            string responseFileDirectory)
    {
        if (arguments.Count == 0 ||
            !arguments[0].Equals("msbuild", StringComparison.OrdinalIgnoreCase))
        {
            return (-1, string.Empty, "Controlled MSBuild arguments must begin with the msbuild command.", false);
        }

        if (arguments.Any(argument =>
                argument.IndexOfAny(['\r', '\n', '\0']) >= 0))
        {
            return (-1, string.Empty, "Controlled MSBuild arguments contain unsupported response-file characters.", false);
        }

        Directory.CreateDirectory(responseFileDirectory);
        bool inspectContext = arguments.Any(argument => argument.StartsWith("-p:DirectoryBuildPropsPath=",
                StringComparison.OrdinalIgnoreCase) &&
                argument.Contains("PowerForge.ControlledRestoreContexts.", StringComparison.Ordinal));
        if (inspectContext && arguments.Contains("-restore", StringComparer.OrdinalIgnoreCase))
        {
            // Restore can make new package imports available. Inspect again before
            // Build rather than treating a pre-restore inventory as its proof.
            string[] restoreArguments = arguments.Where(argument =>
                    !argument.Equals("-restore", StringComparison.OrdinalIgnoreCase) &&
                    !argument.StartsWith("-target:", StringComparison.OrdinalIgnoreCase) &&
                    !argument.StartsWith("-getItem:", StringComparison.OrdinalIgnoreCase) &&
                    !argument.StartsWith("-getProperty:", StringComparison.OrdinalIgnoreCase))
                .Append("-target:Restore").ToArray();
            var restore = RunControlledMsBuildEvaluationProcess(workingDirectory, restoreArguments,
                environmentVariables, timeout, responseFileDirectory);
            if (restore.ExitCode != 0 || restore.TimedOut) return restore;
            return RunControlledMsBuildEvaluationProcess(workingDirectory,
                arguments.Where(argument => !argument.Equals("-restore", StringComparison.OrdinalIgnoreCase)).ToArray(),
                environmentVariables, timeout, responseFileDirectory);
        }
        if (inspectContext &&
            !TryInspectControlledContextInvocation(workingDirectory, arguments, environmentVariables,
                responseFileDirectory, out string? inspectionFailure))
            return (-1, string.Empty, inspectionFailure ?? "Controlled context inspection failed.", false);

        string responseFilePath = Path.Combine(
            responseFileDirectory,
            "controlled-msbuild-" + Guid.NewGuid().ToString("N") + ".rsp");
        try
        {
            File.WriteAllLines(
                responseFilePath,
                arguments,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return RunBuildInputEvaluationProcess(
                "dotnet",
                workingDirectory,
                ["@" + responseFilePath],
                environmentVariables,
                timeout);
        }
        finally
        {
            TryDeleteFile(responseFilePath);
        }
    }
}
