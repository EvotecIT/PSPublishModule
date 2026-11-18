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
    [InlineData("source-hidden")]
    [InlineData("artifact-hidden")]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ModuleTimingRejectsChangedSourceOrExecutablePayloadWithUnchangedManifest(string changed)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge timing identity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var package = Path.Combine(root, "original.nupkg");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Demo.psm1").Open(), new UTF8Encoding(false)))
                    writer.Write("function Get-Demo { 42 }");
                using var nested = new StreamWriter(archive.CreateEntry("nested//helper.ps1").Open(), new UTF8Encoding(false));
                nested.Write("'normalized source path'");
            }
            var packageHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))).ToLowerInvariant();
            var source = Path.Combine(root, "extract", packageHash);
            Directory.CreateDirectory(Path.Combine(root, "packages"));
            File.Move(package, Path.Combine(root, "packages", packageHash + ".nupkg"));
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
                "source-hidden" => Path.Combine(source, ".unlisted.ps1"),
                "artifact-hidden" => Path.Combine(module, ".unlisted.ps1"),
                _ => Path.Combine(module, "Dependency.dll")
            };
            var expectedError = changed switch
            {
                "source-hidden" => "Unexpected timing source payload",
                "artifact-hidden" => "Unexpected timing artifact payload",
                _ => "payload hash mismatch"
            };
            var common = Path.Combine(FindCorpusRepositoryRoot(), "Benchmarks", "PowerShellCompilation", "Corpus");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{Path.Combine(common, "Corpus.Runner.Common.ps1").Replace("'", "''", StringComparison.Ordinal)}}'
                . '{{Path.Combine(common, "Corpus.TimingIdentity.ps1").Replace("'", "''", StringComparison.Ordinal)}}'
                Expand-VerifiedCorpusArchive -PayloadPath '{{Path.Combine(root, "packages", packageHash + ".nupkg").Replace("'", "''", StringComparison.Ordinal)}}' `
                    -Target '{{source.Replace("'", "''", StringComparison.Ordinal)}}' `
                    -ContainmentRoot '{{Path.Combine(root, "extract").Replace("'", "''", StringComparison.Ordinal)}}' -Label 'timing source'
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
                if ('{{changed}}' -like '*-hidden' -and $IsWindows) {
                    [IO.File]::SetAttributes('{{changedPath.Replace("'", "''", StringComparison.Ordinal)}}', [IO.FileAttributes]::Hidden)
                }
                try {
                    Assert-CorpusTimingIdentity @arguments
                    [Console]::Out.Write('accepted changed payload')
                } catch {
                    if ($_.Exception.Message -notmatch '{{expectedError}}') { throw }
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

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ModuleTimingSelectsTheRecordedPayloadWhenRetainedManifestsMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge timing [retained]", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "packages"));
        try
        {
            var package = Path.Combine(root, "input.nupkg");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("Demo.psm1").Open()))
                writer.Write("function Get-Demo { 42 }");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))).ToLowerInvariant();
            var storedPackage = Path.Combine(root, "packages", hash + ".nupkg");
            File.Move(package, storedPackage);
            var before = Path.Combine(root, "runs", "before", "modules", "Demo");
            var after = Path.Combine(root, "runs", "after", "modules", "Demo");
            foreach (var module in new[] { before, after })
            {
                Directory.CreateDirectory(module);
                File.WriteAllText(Path.Combine(module, "Demo.psd1"), "@{RootModule='Demo.psm1'}");
                File.WriteAllText(Path.Combine(module, "Demo.psm1"), "function Get-Demo { 42 }");
                File.WriteAllText(Path.Combine(module, "Demo.dll"), module == before ? "before assembly" : "after assembly");
            }
            var inventory = Directory.GetFiles(after).Select(path => new
            {
                relativePath = Path.GetRelativePath(Path.GetDirectoryName(after)!, path).Replace('\\', '/'),
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
            }).ToArray();
            var input = JsonSerializer.Serialize(new
            {
                entry = new { id = "Demo", sha256 = hash },
                build = new
                {
                    artifactSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(after, "Demo.psd1")))).ToLowerInvariant(),
                    artifactFiles = inventory
                }
            });
            var common = Path.Combine(FindCorpusRepositoryRoot(), "Benchmarks", "PowerShellCompilation", "Corpus");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{Path.Combine(common, "Corpus.Runner.Common.ps1").Replace("'", "''", StringComparison.Ordinal)}}'
                . '{{Path.Combine(common, "Corpus.TimingIdentity.ps1").Replace("'", "''", StringComparison.Ordinal)}}'
                $data = @'
                {{input}}
                '@ | ConvertFrom-Json
                $root = '{{root.Replace("'", "''", StringComparison.Ordinal)}}'
                Expand-VerifiedCorpusArchive -PayloadPath '{{storedPackage.Replace("'", "''", StringComparison.Ordinal)}}' `
                    -Target (Join-Path $root 'extract/{{hash}}') -ContainmentRoot (Join-Path $root 'extract') -Label 'timing source'
                $selected = Find-CorpusTimingArtifact -Entry $data.entry -Build $data.build -WorkspacePath $root
                if ($selected.FullName -cne '{{Path.Combine(after, "Demo.psd1").Replace("'", "''", StringComparison.Ordinal)}}') { throw 'Wrong retained payload' }
                [IO.File]::WriteAllText('{{Path.Combine(after, "Demo.dll").Replace("'", "''", StringComparison.Ordinal)}}', 'tampered assembly')
                try {
                    $null = Find-CorpusTimingArtifact -Entry $data.entry -Build $data.build -WorkspacePath $root
                    throw 'Accepted a changed assembly'
                } catch {
                    if ($_.Exception.Message -notmatch 'inventory-matching timing artifact') { throw }
                }
                [Console]::Out.Write('selected recorded payload and rejected tampering')
                """;
            var result = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", command);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("selected recorded payload and rejected tampering", result.StandardOutput);
            Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
