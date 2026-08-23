using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliPowerShellCompilationTests
{
    [Theory]
    [InlineData("powershell build missing.ps1 --kind exe --sing --output json", "powershell.build", "--sing")]
    [InlineData("powershell analyze missing.ps1 --recurs --output json", "powershell.analyze", "--recurs")]
    [InlineData("powershell build missing.ps1 --kind exe --mode 999 --output json", "powershell.build", "999")]
    public async Task Commands_RejectInvalidOptions(string arguments, string command, string errorFragment)
    {
        var result = await RunCliAsync(FindRepositoryRoot(), arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);
        using var document = JsonDocument.Parse(result.StdOut);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(command, document.RootElement.GetProperty("command").GetString());
        Assert.Contains(errorFragment, document.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildStrictTypedExecutable_ExposesRuntimeIndependentCliContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Typed Executable Tests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "compiled output");
        Directory.CreateDirectory(output);
        var source = Path.Combine(root, "Typed Script.ps1");
        File.WriteAllText(
            source,
            """
            param([int] $Count)
            [long] $total = 0
            for ([int] $value = 1; $value -le $Count; $value++) { $total += $value }
            return $total
            """);

        try
        {
            var build = await RunCliAsync(
                repositoryRoot,
                $"powershell build \"{source}\" --kind exe --mode Strict --framework net10.0 --out \"{output}\" --name TypedCliProof --output json");
            Assert.True(build.ExitCode == 0, FormatFailure("typed executable build", build));
            string artifactPath;
            using (var document = JsonDocument.Parse(build.StdOut))
            {
                Assert.True(document.RootElement.GetProperty("success").GetBoolean());
                var manifest = document.RootElement.GetProperty("result").GetProperty("manifest");
                Assert.Equal(1, manifest.GetProperty("compiledMethods").GetInt32());
                Assert.False(manifest.GetProperty("requiresPowerShellRuntime").GetBoolean());
                Assert.False(manifest.GetProperty("usesPowerShellRuntimeFallback").GetBoolean());
                artifactPath = manifest.GetProperty("artifactPath").GetString()!;
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = artifactPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("--Count=100");
            process.Start();
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, standardError);
            Assert.Equal("5050", standardOutput.Trim());
            Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AnalyzeAndBuildTypedLibrary_ExposeStableJsonCliContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Compilation Tests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "compiled output");
        Directory.CreateDirectory(output);
        var source = Path.Combine(root, "Typed Functions.psm1");
        File.WriteAllText(
            source,
            """
            function Get-AllowedAverageMs {
                param([double] $BaselineMs, [double] $RelativeTolerance, [double] $AbsoluteToleranceMs)
                $relativeCap = $BaselineMs * (1.0 + $RelativeTolerance)
                $absoluteCap = $BaselineMs + $AbsoluteToleranceMs
                if ($relativeCap -gt $absoluteCap) { return $relativeCap }
                return $absoluteCap
            }
            """);

        try
        {
            var analyze = await RunCliAsync(repositoryRoot, $"powershell analyze \"{source}\" --mode Strict --output json");
            Assert.True(analyze.ExitCode == 0, FormatFailure("analyze", analyze));
            using (var document = JsonDocument.Parse(analyze.StdOut))
            {
                Assert.True(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("powershell.analyze", document.RootElement.GetProperty("command").GetString());
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(1, result.GetProperty("totalUnits").GetInt32());
                Assert.Equal(1, result.GetProperty("compilableUnits").GetInt32());
            }

            var build = await RunCliAsync(
                repositoryRoot,
                $"powershell build \"{source}\" --kind library --mode Strict --framework net10.0 --out \"{output}\" --name CliProof --output json");
            Assert.True(build.ExitCode == 0, FormatFailure("build", build));
            using (var document = JsonDocument.Parse(build.StdOut))
            {
                Assert.True(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("powershell.build", document.RootElement.GetProperty("command").GetString());
                var manifest = document.RootElement.GetProperty("result").GetProperty("manifest");
                Assert.Equal(1, manifest.GetProperty("compiledMethods").GetInt32());
                Assert.Equal(0, manifest.GetProperty("runtimeFallbackUnits").GetInt32());
                Assert.Equal(0, manifest.GetProperty("omittedUnits").GetInt32());
                Assert.False(manifest.GetProperty("requiresPowerShellRuntime").GetBoolean());
                Assert.True(File.Exists(manifest.GetProperty("artifactPath").GetString()));
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Analyze_HonorsRequestedTargetFrameworkMemberSurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge CLI Target Analysis Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "Target Surface.ps1");
        File.WriteAllText(source, "function New-Version7Guid { return [System.Guid]::CreateVersion7() }");

        try
        {
            var net8 = await RunCliAsync(
                repositoryRoot,
                $"powershell analyze \"{source}\" --mode Strict --framework net8.0 --output json");
            Assert.Equal(1, net8.ExitCode);
            using (var document = JsonDocument.Parse(net8.StdOut))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal("net8.0", result.GetProperty("targetFramework").GetString());
                Assert.Equal(0, result.GetProperty("compilableUnits").GetInt32());
            }

            var net10 = await RunCliAsync(
                repositoryRoot,
                $"powershell analyze \"{source}\" --mode Strict --framework net10.0 --output json");
            Assert.Equal(0, net10.ExitCode);
            using var net10Document = JsonDocument.Parse(net10.StdOut);
            var net10Result = net10Document.RootElement.GetProperty("result");
            Assert.Equal("net10.0", net10Result.GetProperty("targetFramework").GetString());
            Assert.Equal(1, net10Result.GetProperty("compilableUnits").GetInt32());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(string repositoryRoot, string arguments)
    {
        var cli = Path.Combine(repositoryRoot, "PowerForge.Cli", "bin", "Release", "net10.0", "PowerForge.Cli.dll");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{cli}\" {arguments}",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        if (await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(120))) != exitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("PowerShell compilation CLI test timed out.");
        }
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName,
        string workingDirectory,
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge.Cli", "PowerForge.Cli.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Unable to locate the repository root for PowerForge CLI tests.");
    }

    private static string FormatFailure(string operation, (int ExitCode, string StdOut, string StdErr) result)
        => $"CLI {operation} exit code {result.ExitCode}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}";
}
