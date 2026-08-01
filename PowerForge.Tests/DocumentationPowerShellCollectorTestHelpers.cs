namespace PowerForge.Tests;

public sealed partial class DocumentationPowerShellCollectorTests
{
    private static string NestedExpression(int depth, string value)
    {
        if (depth <= 0) return value;
        var result = value;
        for (var index = 0; index < depth; index++)
            result = "& { $collection = [System.Object[]]::new(1); $collection.SetValue((" + result +
                     "), 0); return ,$collection }";
        return result;
    }

    private sealed class ExecutablePowerShellRunner : IPowerShellRunner
    {
        private readonly string _executable;
        private readonly string _workingDirectory;
        private readonly PowerShellRunner _inner = new();

        public ExecutablePowerShellRunner(string executable, string workingDirectory)
        {
            _executable = executable;
            _workingDirectory = workingDirectory;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _inner.Run(new PowerShellRunRequest(
                request.ScriptPath!,
                request.Arguments,
                request.Timeout,
                request.PreferPwsh,
                request.WorkingDirectory ?? _workingDirectory,
                request.EnvironmentVariables,
                _executable,
                request.CaptureOutput,
                request.CaptureError,
                request.OutputLineReceived,
                request.ErrorLineReceived));
    }
}
