using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeCliPowerShellCompilationTests
{
    [WindowsFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task ProjectRunCli_PreservesBinaryStdinStdoutArgumentsStderrAndExitCode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var cli = PowerForgeCliTestHost.ResolveAssemblyPath(repositoryRoot,
            Environment.GetEnvironmentVariable(PowerForgeCliTestHost.AssemblyPathEnvironmentVariable));
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

            var environmentPath = Path.Combine(root, ".powerforge", "environment", "environment.json");
            var environmentText = File.ReadAllText(environmentPath);
            var environment = JsonSerializer.Deserialize<PowerShellCompilationProjectEnvironment>(
                environmentText, PowerShellCompilationProjectManifestService.JsonOptions)!;
            Assert.Equal(3, environment.SchemaVersion);
            // Inspect the real SDK runtime downloads acquired for this RID, using their selected
            // versions rather than assuming the SDK patch installed on the validation machine.
            var sdkDownloads = Directory.EnumerateDirectories(environment.PackageRoot)
                .Where(path => Path.GetFileName(path).EndsWith(".app.runtime." + target.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase))
                .SelectMany(path => Directory.EnumerateDirectories(path).Select(version => new
                {
                    Id = Path.GetFileName(path), Version = Path.GetFileName(version), Root = version
                })).ToArray();
            Assert.NotEmpty(sdkDownloads);
            foreach (var download in sdkDownloads)
            {
                var recorded = Assert.Single(environment.Packages, package =>
                    package.Id.Equals(download.Id, StringComparison.OrdinalIgnoreCase) && package.Version == download.Version);
                Assert.False(string.IsNullOrWhiteSpace(recorded.ArchiveSha512));
                Assert.False(string.IsNullOrWhiteSpace(recorded.ExtractedFilesSha256));
                _ = PowerShellCompilationNuGetPackageVerifier.Verify(environment.PackageRoot, recorded);
            }
            environment.SchemaVersion = 2;
            File.WriteAllText(environmentPath, JsonSerializer.Serialize(environment, PowerShellCompilationProjectManifestService.JsonOptions));
            try
            {
                Assert.Contains("project restore", Assert.Throws<InvalidDataException>(() => workflow.Build(project)).Message);
                var staleRestore = workflow.Restore(project, offline: true);
                Assert.False(staleRestore.Succeeded);
                Assert.Contains("project restore", Assert.Single(staleRestore.Targets).Message);
            }
            finally { File.WriteAllText(environmentPath, environmentText); }

            // The first separator belongs to the CLI; the second is the packaged EXE's
            // documented positional-only delimiter, so dash-prefixed strings enter $args.
            var appArguments = new[] { "--", "", "two words", "embedded\"quote", "trailing\\", "żółć", "--help", "--json", "--verbose", "--view", "application-value" };
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet", WorkingDirectory = repositoryRoot, UseShellExecute = false,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8, CreateNoWindow = true
                }
            };
            foreach (var argument in new[] { cli,
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
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
                await process.WaitForExitAsync(timeout.Token);
                await stdout;
                var errorOutput = await stderr;
                Assert.True(process.ExitCode == 23, "Exit " + process.ExitCode + ": " + errorOutput + "\n" + Encoding.UTF8.GetString(output.ToArray()));
                Assert.Equal(input, output.ToArray());
                Assert.Equal(string.Concat(appArguments.Skip(1).Select(argument => "<" + argument + ">")), errorOutput);
            }
            finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }

            // Both entrypoints share the pre-SDK environment gate. A recorded SDK download
            // cannot disappear while the environment still claims an intact acquired closure.
            var removed = sdkDownloads[0];
            File.Delete(Path.Combine(removed.Root, ".nupkg.metadata"));
            var missing = Assert.Throws<FileNotFoundException>(() => workflow.Build(project));
            Assert.Contains(removed.Id + "/" + removed.Version, missing.Message, StringComparison.OrdinalIgnoreCase);
            var missingRestore = workflow.Restore(project, offline: true);
            Assert.False(missingRestore.Succeeded);
            Assert.Contains(removed.Id + "/" + removed.Version, Assert.Single(missingRestore.Targets).Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(Directory.GetParent(root)!.FullName, recursive: true); }
    }
}
