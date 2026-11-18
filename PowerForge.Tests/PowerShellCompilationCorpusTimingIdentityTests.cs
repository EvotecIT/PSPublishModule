using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Theory]
    [InlineData("source")]
    [InlineData("root-module")]
    [InlineData("dependency")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ModuleTimingRejectsChangedSourceOrExecutablePayloadWithUnchangedManifest(string changed)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge timing identity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var package = Path.Combine(root, "original.nupkg");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("Demo.psm1").Open(), new UTF8Encoding(false)))
                writer.Write("function Get-Demo { 42 }");
            var packageHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))).ToLowerInvariant();
            var source = Path.Combine(root, "extract", packageHash);
            Directory.CreateDirectory(Path.Combine(root, "packages"));
            File.Move(package, Path.Combine(root, "packages", packageHash + ".nupkg"));
            ZipFile.ExtractToDirectory(Path.Combine(root, "packages", packageHash + ".nupkg"), source);
            var artifacts = Path.Combine(root, "output");
            var module = Path.Combine(artifacts, "Demo");
            Directory.CreateDirectory(module);
            File.WriteAllText(Path.Combine(module, "Demo.psd1"), "@{RootModule='Demo.psm1'}");
            File.WriteAllText(Path.Combine(module, "Demo.psm1"), "function Get-Demo { 42 }");
            File.WriteAllText(Path.Combine(module, "Dependency.dll"), "original dependency");
            var inventory = Directory.GetFiles(module).Select(path => new
            {
                relativePath = Path.GetRelativePath(artifacts, path).Replace('\\', '/'),
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
            }).ToArray();
            var input = JsonSerializer.Serialize(new
            {
                entry = new { id = "Demo", sha256 = packageHash },
                build = new { artifactFiles = inventory }
            });
            var changedPath = changed switch
            {
                "source" => Path.Combine(source, "Demo.psm1"),
                "root-module" => Path.Combine(module, "Demo.psm1"),
                _ => Path.Combine(module, "Dependency.dll")
            };
            var common = Path.Combine(FindCorpusRepositoryRoot(), "Benchmarks", "PowerShellCompilation", "Corpus");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{Path.Combine(common, "Corpus.Runner.Common.ps1").Replace("'", "''", StringComparison.Ordinal)}}'
                . '{{Path.Combine(common, "Corpus.TimingIdentity.ps1").Replace("'", "''", StringComparison.Ordinal)}}'
                $inputData = @'
                {{input}}
                '@ | ConvertFrom-Json
                $arguments = @{
                    Entry = $inputData.entry
                    Build = $inputData.build
                    WorkspacePath = '{{root.Replace("'", "''", StringComparison.Ordinal)}}'
                    ArtifactRoot = '{{artifacts.Replace("'", "''", StringComparison.Ordinal)}}'
                }
                Assert-CorpusTimingIdentity @arguments
                [IO.File]::WriteAllText('{{changedPath.Replace("'", "''", StringComparison.Ordinal)}}', 'changed payload')
                try {
                    Assert-CorpusTimingIdentity @arguments
                    [Console]::Out.Write('accepted changed payload')
                } catch {
                    if ($_.Exception.Message -notmatch 'payload hash mismatch') { throw }
                    [Console]::Out.Write('rejected changed payload')
                }
                """;
            var result = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", command);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("rejected changed payload", result.StandardOutput);
            Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
