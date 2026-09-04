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
