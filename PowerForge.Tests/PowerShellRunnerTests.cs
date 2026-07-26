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

    [Fact]
    public void Run_UsesCurrentUserWindowsAppsPowerShellAliasesWhenResolvingPath()
    {
        if (Path.DirectorySeparatorChar != '\\')
            return;

        using var directory = new TemporaryDirectory();
        var windowsApps = Directory.CreateDirectory(Path.Combine(directory.Path, "Microsoft", "WindowsApps"));
        var realPowerShell = Directory.CreateDirectory(Path.Combine(directory.Path, "PowerShell", "7"));
        var aliasPath = Path.Combine(windowsApps.FullName, "pwsh.exe");
        var realPath = Path.Combine(realPowerShell.FullName, "pwsh.exe");
        File.WriteAllText(aliasPath, string.Empty);
        File.WriteAllText(realPath, string.Empty);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable(
                "PATH",
                string.Join(Path.PathSeparator, windowsApps.FullName, realPowerShell.FullName));

            ProcessRunRequest? captured = null;
            var processRunner = new StubProcessRunner(request => {
                captured = request;
                return new ProcessRunResult(0, "ok", string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
            });

            var runner = new PowerShellRunner(processRunner);
            var result = runner.Run(PowerShellRunRequest.ForCommand(
                "Get-ChildItem",
                TimeSpan.FromSeconds(5),
                preferPwsh: true));

            Assert.NotNull(captured);
        Assert.Equal(aliasPath, captured!.FileName);
        Assert.Equal(aliasPath, result.Executable);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public void Run_CompatibleRequest_RejectsIncompatibleExecutableOverride()
    {
        var executablePath = CreateStubExecutablePath();
        var requests = new List<ProcessRunRequest>();
        var processRunner = new StubProcessRunner(request => {
            requests.Add(request);
            return new ProcessRunResult(0, "Core|7", string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });
        var runner = new PowerShellRunner(processRunner);

        var result = runner.Run(PowerShellRunRequest.ForCompatibleCommand(
            commandText: "Get-ChildItem",
            timeout: TimeSpan.FromMinutes(1),
            requiredRuntimeMajor: 8,
            executableOverride: executablePath));

        Assert.Equal(127, result.ExitCode);
        Assert.Empty(result.Executable);
        Assert.Contains("requires PowerShell Core running on .NET 8 or later", result.StdErr, StringComparison.Ordinal);
        Assert.Single(requests);
        Assert.Contains("$PSVersionTable.PSEdition", requests[0].Arguments[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Run_CompatibleRequest_DoesNotFallBackToWindowsPowerShell()
    {
        using var directory = new TemporaryDirectory();
        var executableName = Path.DirectorySeparatorChar == '\\' ? "powershell.exe" : "powershell";
        File.WriteAllText(Path.Combine(directory.Path, executableName), string.Empty);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory.Path);
            var processRunner = new StubProcessRunner(request =>
                throw new InvalidOperationException($"Unexpected process invocation: {request.FileName}"));
            var runner = new PowerShellRunner(processRunner);

            var result = runner.Run(PowerShellRunRequest.ForCompatibleCommand(
                commandText: "Get-ChildItem",
                timeout: TimeSpan.FromMinutes(1),
                requiredRuntimeMajor: 8));

            Assert.Equal(127, result.ExitCode);
            Assert.Empty(result.Executable);
            Assert.Contains("No compatible pwsh executable was found", result.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public void Run_CompatibleRequest_SkipsOlderRuntimeAndUsesCompatiblePwsh()
    {
        using var directory = new TemporaryDirectory();
        var firstDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "pwsh-8"));
        var secondDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "pwsh-10"));
        var executableName = Path.DirectorySeparatorChar == '\\' ? "pwsh.exe" : "pwsh";
        var firstExecutable = Path.Combine(firstDirectory.FullName, executableName);
        var secondExecutable = Path.Combine(secondDirectory.FullName, executableName);
        File.WriteAllText(firstExecutable, string.Empty);
        File.WriteAllText(secondExecutable, string.Empty);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, firstDirectory.FullName, secondDirectory.FullName));
            var requests = new List<ProcessRunRequest>();
            var processRunner = new StubProcessRunner(request => {
                requests.Add(request);
                var isProbe = request.Arguments[^1].Contains("$PSVersionTable.PSEdition", StringComparison.Ordinal);
                var output = isProbe
                    ? (string.Equals(request.FileName, firstExecutable, StringComparison.OrdinalIgnoreCase) ? "Core|8" : "Core|10")
                    : "ok";
                return new ProcessRunResult(0, output, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
            });
            var runner = new PowerShellRunner(processRunner);

            var result = runner.Run(PowerShellRunRequest.ForCompatibleCommand(
                commandText: "Get-ChildItem",
                timeout: TimeSpan.FromMinutes(1),
                requiredRuntimeMajor: 10));

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(secondExecutable, result.Executable);
            Assert.Collection(
                requests,
                request => Assert.Equal(firstExecutable, request.FileName),
                request => Assert.Equal(secondExecutable, request.FileName),
                request => Assert.Equal(secondExecutable, request.FileName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
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
