using PowerForge;

namespace PowerForge.Tests;

public sealed class RunProfileServiceTests
{
    [Fact]
    public void Prepare_ProjectProfile_BuildsDotNetRunCommand()
    {
        var root = CreateTempRoot();
        try
        {
            var projectPath = Path.GetFullPath(CreateFile(root, "src/Tray/Tray.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"));
            var spec = new RunProfileSpec
            {
                ProjectRoot = root,
                Profiles = new[]
                {
                    new RunProfile
                    {
                        Name = "Tray",
                        Kind = RunProfileKind.Project,
                        ProjectPath = "src/Tray/Tray.csproj",
                        Framework = "net10.0-windows",
                        PassAllowRoot = true,
                        PassExtraArgs = true,
                        PassIncludePrivateToolPacks = true,
                        MsBuildProperties = new Dictionary<string, string?>
                        {
                            ["UseLocalOfficeImoCheckout"] = "true"
                        }
                    }
                }
            };

            var service = new RunProfileService();
            var prepared = service.Prepare(spec, Path.Combine(root, "run.profiles.json"), new RunProfileExecutionRequest
            {
                TargetName = "Tray",
                Configuration = "Release",
                NoBuild = true,
                NoRestore = true,
                AllowRoot = new[] { @"C:\Support\GitHub" },
                IncludePrivateToolPacks = true,
                TestimoXRoot = @"C:\Support\GitHub\TestimoX",
                ExtraArgs = new[] { "--minimized" }
            });

            Assert.Equal("dotnet", prepared.Executable);
            Assert.Equal(root, prepared.WorkingDirectory);
            Assert.Contains("--project", prepared.Arguments);
            Assert.Contains(prepared.Arguments, arg => string.Equals(arg, projectPath, StringComparison.OrdinalIgnoreCase));
            Assert.Contains("--framework", prepared.Arguments);
            Assert.Contains("net10.0-windows", prepared.Arguments);
            Assert.Contains("--no-build", prepared.Arguments);
            Assert.Contains("--no-restore", prepared.Arguments);
            Assert.Contains("/p:UseLocalOfficeImoCheckout=true", prepared.Arguments);
            Assert.Contains("/p:IncludePrivateToolPacks=true", prepared.Arguments);
            Assert.Contains(prepared.Arguments, arg => arg.StartsWith("/p:TestimoXRoot=", StringComparison.Ordinal));
            Assert.Contains("--", prepared.Arguments);
            Assert.Contains("-AllowRoot", prepared.Arguments);
            Assert.Contains(@"C:\Support\GitHub", prepared.Arguments);
            Assert.Contains("--minimized", prepared.Arguments);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Prepare_ScriptProfile_BuildsPowerShellInvocation()
    {
        var root = CreateTempRoot();
        try
        {
            var scriptPath = Path.GetFullPath(CreateFile(root, "Build/Chat/Run-Chat.ps1", "param([string[]]$AllowRoot,[string[]]$ExtraArgs)"));
            var spec = new RunProfileSpec
            {
                ProjectRoot = root,
                Profiles = new[]
                {
                    new RunProfile
                    {
                        Name = "Chat.Host",
                        Kind = RunProfileKind.Script,
                        Path = "Build/Chat/Run-Chat.ps1",
                        Framework = "net10.0-windows",
                        PreferPwsh = true,
                        PassFramework = true,
                        PassNoRestore = true,
                        PassAllowRoot = true,
                        PassExtraArgs = true
                    }
                }
            };

            var service = new RunProfileService();
            var prepared = service.Prepare(spec, Path.Combine(root, "run.profiles.json"), new RunProfileExecutionRequest
            {
                TargetName = "Chat.Host",
                NoRestore = true,
                AllowRoot = new[] { @"C:\Support\GitHub" },
                ExtraArgs = new[] { "--echo-tool-outputs" }
            });

            Assert.EndsWith("pwsh.exe", prepared.Executable, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-File", prepared.Arguments);
            Assert.Contains(prepared.Arguments, arg => string.Equals(arg, scriptPath, StringComparison.OrdinalIgnoreCase));
            Assert.Contains("-Framework", prepared.Arguments);
            Assert.Contains("net10.0-windows", prepared.Arguments);
            Assert.Contains("-NoRestore", prepared.Arguments);
            Assert.Contains("-AllowRoot", prepared.Arguments);
            Assert.Contains(@"C:\Support\GitHub", prepared.Arguments);
            Assert.Contains("-ExtraArgs", prepared.Arguments);
            Assert.Contains("--echo-tool-outputs", prepared.Arguments);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Prepare_ProjectProfile_CanPassExtraArgsDirectly()
    {
        var root = CreateTempRoot();
        try
        {
            var projectPath = Path.GetFullPath(CreateFile(root, "src/Cli/Cli.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"));
            var spec = new RunProfileSpec
            {
                ProjectRoot = root,
                Profiles = new[]
                {
                    new RunProfile
                    {
                        Name = "PowerForge",
                        Kind = RunProfileKind.Project,
                        ProjectPath = "src/Cli/Cli.csproj",
                        Framework = "net10.0",
                        PassExtraArgsDirect = true
                    }
                }
            };

            var service = new RunProfileService();
            var prepared = service.Prepare(spec, Path.Combine(root, "run.profiles.json"), new RunProfileExecutionRequest
            {
                TargetName = "PowerForge",
                ExtraArgs = new[] { "release", "--help" }
            });

            Assert.Contains("--", prepared.Arguments);
            Assert.Contains("release", prepared.Arguments);
            Assert.Contains("--help", prepared.Arguments);
            Assert.DoesNotContain("-ExtraArgs", prepared.Arguments);
            Assert.Contains(prepared.Arguments, arg => string.Equals(arg, projectPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task RunAsync_CommandProfile_UsesProcessRunner()
    {
        ProcessRunRequest? captured = null;
        var runner = new StubProcessRunner(request =>
        {
            captured = request;
            return new ProcessRunResult(0, "ok", string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });

        var service = new RunProfileService(runner, new PowerShellRunner(runner));
        var spec = new RunProfileSpec
        {
            Profiles = new[]
            {
                new RunProfile
                {
                    Name = "echo",
                    Kind = RunProfileKind.Command,
                    Executable = "dotnet",
                    Arguments = new[] { "--info" }
                }
            }
        };

        var result = await service.RunAsync(spec, null, new RunProfileExecutionRequest
        {
            TargetName = "echo",
            CaptureOutput = true,
            CaptureError = true
        });

        Assert.NotNull(captured);
        Assert.Equal("dotnet", captured!.FileName);
        Assert.Equal(new[] { "--info" }, captured.Arguments);
        Assert.True(result.Succeeded);
        Assert.Equal("ok", result.StdOut);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateFile(string root, string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
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
