using System.Diagnostics;
using System.Text;

namespace PowerForge.Tests;

public sealed partial class PowerForgeCliPowerShellCompilationTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task ProjectRunCli_PreservesBinaryStdinStdoutArgumentsStderrAndExitCode()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "PFC", Guid.NewGuid().ToString("N")[..12], "source space ż");
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "input.ps1");
            File.WriteAllText(source, """
                [Console]::OpenStandardInput().CopyTo([Console]::OpenStandardOutput())
                foreach ($item in $args) { [Console]::Error.Write(('<{0}>' -f $item)) }
                exit 23
                """);
            var project = Path.Combine(root, "powerforge.psproject.json");
            var service = new PowerShellCompilationProjectManifestService();
            var target = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Package, "net10.0", "win-x64", false, true,
                PowerShellCompilationExecutableOptimization.None, true);
            service.Save(project, service.Create(project, source, "Streams", target));
            var workflow = new PowerShellCompilationProjectWorkflowService();
            var locked = workflow.Lock(project);
            Assert.True(locked.Succeeded, string.Join("\n", locked.Targets.Select(item => item.Message)));
            var restored = workflow.Restore(project);
            Assert.True(restored.Succeeded, string.Join("\n", restored.Targets.Select(item => item.Message)));

            // The first separator belongs to the CLI; the second is the packaged EXE's
            // documented positional-only delimiter, so dash-prefixed strings enter $args.
            var appArguments = new[] { "--", "", "two words", "embedded\"quote", "trailing\\", "żółć", "--help", "--json", "--verbose", "--view", "application-value" };
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet", WorkingDirectory = FindRepositoryRoot(), UseShellExecute = false,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8, CreateNoWindow = true
                }
            };
            foreach (var argument in new[] { Path.Combine(FindRepositoryRoot(), "PowerForge.Cli", "bin", "Release", "net10.0", "PowerForge.Cli.dll"),
                         "powershell", "project", "run", project, "--" }.Concat(appArguments))
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            try
            {
                var input = new byte[] { 0, 1, 255, 13, 10, 65, 128 };
                using var output = new MemoryStream();
                var stdout = process.StandardOutput.BaseStream.CopyToAsync(output);
                var stderr = process.StandardError.ReadToEndAsync();
                await process.StandardInput.BaseStream.WriteAsync(input);
                process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
                await process.WaitForExitAsync(timeout.Token);
                await stdout;
                var errorOutput = await stderr;
                Assert.True(process.ExitCode == 23, "Exit " + process.ExitCode + ": " + errorOutput + "\n" + Encoding.UTF8.GetString(output.ToArray()));
                Assert.Equal(input, output.ToArray());
                Assert.Equal(string.Concat(appArguments.Skip(1).Select(argument => "<" + argument + ">")), errorOutput);
            }
            finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        }
        finally { Directory.Delete(Directory.GetParent(root)!.FullName, recursive: true); }
    }
}
