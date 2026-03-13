using PowerForge;

namespace PowerForge.Tests;

public sealed class PowerShellRunnerTests
{
    [Fact]
    public void Run_CommandRequest_UsesStructuredProcessRunnerWithCommandInvocation()
    {
        var executablePath = CreateStubExecutablePath();
        ProcessRunRequest? captured = null;
        var processRunner = new StubProcessRunner(request => {
            captured = request;
            return new ProcessRunResult(0, "ok", string.Empty, request.FileName, TimeSpan.FromSeconds(1), timedOut: false);
        });
        var runner = new PowerShellRunner(processRunner);
        var environmentVariables = new Dictionary<string, string?> {
            ["PF_TEST"] = "1"
        };

        var result = runner.Run(PowerShellRunRequest.ForCommand(
            commandText: "Get-ChildItem",
            timeout: TimeSpan.FromMinutes(1),
            preferPwsh: true,
            workingDirectory: @"C:\repo",
            environmentVariables: environmentVariables,
            executableOverride: executablePath));

        Assert.NotNull(captured);
        Assert.Equal(executablePath, captured!.FileName);
        Assert.Equal(@"C:\repo", captured.WorkingDirectory);
        Assert.Equal(environmentVariables, captured.EnvironmentVariables);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "Get-ChildItem"], captured.Arguments);
        Assert.Equal(executablePath, result.Executable);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Run_FileRequest_UsesStructuredProcessRunnerWithFileInvocation()
    {
        var executablePath = CreateStubExecutablePath();
        ProcessRunRequest? captured = null;
        var processRunner = new StubProcessRunner(request => {
            captured = request;
            return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });
        var runner = new PowerShellRunner(processRunner);

        _ = runner.Run(new PowerShellRunRequest(
            scriptPath: @"C:\repo\Build\Build-Module.ps1",
            arguments: ["-Configuration", "Release"],
            timeout: TimeSpan.FromMinutes(2),
            preferPwsh: true,
            workingDirectory: @"C:\repo",
            executableOverride: executablePath));

        Assert.NotNull(captured);
        Assert.Equal(
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", @"C:\repo\Build\Build-Module.ps1", "-Configuration", "Release"],
            captured!.Arguments);
    }

    private static string CreateStubExecutablePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "powerforge-pwsh-stub.exe");
        if (!File.Exists(path))
            File.WriteAllText(path, string.Empty);

        return path;
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_execute(request));
    }
}
