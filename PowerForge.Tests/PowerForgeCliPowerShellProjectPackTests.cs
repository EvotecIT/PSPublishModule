using System.Text.Json;
using NuGet.Packaging;

namespace PowerForge.Tests;

public sealed partial class PowerForgeCliPowerShellCompilationTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task ProjectPackCli_CreatesNuGetFromQualifiedLibraryAndPreservesZipDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "PFC", Guid.NewGuid().ToString("N")[..12], "package space");
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "Functions.psm1");
            File.WriteAllText(source, "function Get-Answer { return 42 }");
            var project = Path.Combine(root, "powerforge.psproject.json");
            await Invoke($"init \"{source}\" --name CliLibrary --kind library --mode Strict --framework net10.0 --emit-source");
            var service = new PowerShellCompilationProjectManifestService();
            var manifest = service.Load(project);
            Assert.True(manifest.Artifacts.Single().EmitSource);
            manifest.NuGet = new PowerShellCompilationProjectNuGetPackage
            {
                PackageId = "Cli.Compiled.Library", PackageVersion = "1.2.3", Authors = "CLI fixture",
                Description = "Qualified project package.", LicenseExpression = "MIT"
            };
            service.Save(project, manifest);
            foreach (var command in new[] { "lock", "restore", "restore --offline", "build", "test" })
                await Invoke($"{command} \"{project}\"");
            var packed = await Invoke($"pack \"{project}\" --format nuget --target {manifest.Artifacts.Single().Name}");
            var result = packed.GetProperty("result").GetProperty("targets")[0];
            var path = result.GetProperty("path").GetString()!;
            Assert.EndsWith(".nupkg", path);
            Assert.Equal(PowerShellCompilationProjectManifestService.ComputeSha256(path), result.GetProperty("packageSha256").GetString());
            Assert.Equal(64, result.GetProperty("publicAbiSha256").GetString()!.Length);
            using (var reader = new PackageArchiveReader(path))
            {
                Assert.Equal("Cli.Compiled.Library", reader.NuspecReader.GetId());
                Assert.Equal("1.2.3", reader.NuspecReader.GetVersion().ToNormalizedString());
            }
            var zipped = await Invoke($"pack \"{project}\"");
            Assert.EndsWith(".zip", zipped.GetProperty("result").GetProperty("targets")[0].GetProperty("path").GetString());
            var hash = PowerShellCompilationProjectManifestService.ComputeSha256(path);
            var rejected = await RunCliAsync(FindRepositoryRoot(), $"powershell project pack \"{project}\" --format tar --output json");
            Assert.Equal(2, rejected.ExitCode);
            using var error = JsonDocument.Parse(rejected.StdOut);
            Assert.False(error.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(hash, PowerShellCompilationProjectManifestService.ComputeSha256(path));
        }
        finally { Directory.Delete(Directory.GetParent(root)!.FullName, recursive: true); }

        async Task<JsonElement> Invoke(string command)
        {
            var result = await RunCliAsync(FindRepositoryRoot(), "powershell project " + command + " --output json");
            Assert.True(result.ExitCode == 0, FormatFailure(command, result));
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            return document.RootElement.Clone();
        }
    }
}
