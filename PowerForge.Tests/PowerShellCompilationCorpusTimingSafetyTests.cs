using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void TimingEvidenceCreationUsesPowerShellLocationAndPreservesExistingInput()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge timing [output]", Guid.NewGuid().ToString("N"));
        var current = Path.Combine(root, "current");
        Directory.CreateDirectory(current);
        try
        {
            var input = Path.Combine(current, "input[before].json");
            const string original = "{\"sample\":\"original\"}";
            File.WriteAllText(input, original);
            File.WriteAllText(Path.Combine(root, "input[before].json"), "{\"sample\":\"wrong directory\"}");
            var helper = Path.Combine(FindCorpusRepositoryRoot(), "Benchmarks", "PowerShellCompilation", "Corpus", "Corpus.TimingRecords.ps1");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{helper.Replace("'", "''", StringComparison.Ordinal)}}'
                $root = '{{root.Replace("'", "''", StringComparison.Ordinal)}}'
                [Environment]::CurrentDirectory = $root
                Set-Location -LiteralPath (Join-Path $root 'current')
                $inputRecord = Read-CorpusTimingRecord -Path './input[before].json'
                if ($inputRecord.value.sample -cne 'original') { throw 'Read the process directory instead of the PowerShell location' }
                try {
                    $null = New-CorpusTimingEvidence -Path './input[before].json' -Value @{sample='replacement'}
                    throw 'Overwrote existing evidence'
                } catch [IO.IOException] { }
                $output = New-CorpusTimingEvidence -Path './new[after].json' -Value @{sample='new observation'}
                $record = Read-CorpusTimingRecord -Path $output
                if ($record.value.sample -cne 'new observation') { throw 'Wrong new observation' }
                if ($record.sha256 -cne (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Wrong observation identity' }
                [Console]::Out.Write('created new evidence')
                """;
            var result = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", command);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("created new evidence", result.StandardOutput);
            Assert.Equal(original, File.ReadAllText(input));
            Assert.True(File.Exists(Path.Combine(current, "new[after].json")));
            Assert.False(File.Exists(Path.Combine(root, "new[after].json")));
            Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void CorpusCompilerSnapshotExecutesOriginalFilesAfterTheBuildDirectoryChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge compiler snapshot", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "build");
        Directory.CreateDirectory(source);
        try
        {
            var launcher = Path.Combine(source, "compiler.ps1");
            File.WriteAllText(launcher, "[Console]::Out.Write('revision-A|' + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'dependency.txt')))");
            File.WriteAllText(Path.Combine(source, "dependency.txt"), "dependency-A");
            var corpus = Path.Combine(FindCorpusRepositoryRoot(), "Benchmarks", "PowerShellCompilation", "Corpus");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{Path.Combine(corpus, "Corpus.Runner.Common.ps1").Replace("'", "''", StringComparison.Ordinal)}}'
                . '{{Path.Combine(corpus, "Corpus.CompilerSnapshot.ps1").Replace("'", "''", StringComparison.Ordinal)}}'
                [IO.Directory]::CreateDirectory('{{Path.Combine(root, "launch").Replace("'", "''", StringComparison.Ordinal)}}') | Out-Null
                [Environment]::CurrentDirectory = '{{Path.Combine(root, "launch").Replace("'", "''", StringComparison.Ordinal)}}'
                Set-Location -LiteralPath '{{root.Replace("'", "''", StringComparison.Ordinal)}}'
                $snapshot = New-CorpusCompilerSnapshot -CliAssemblyPath './build/compiler.ps1' -SnapshotPath './run/compiler'
                [IO.File]::WriteAllText('{{launcher.Replace("'", "''", StringComparison.Ordinal)}}', "[Console]::Out.Write('revision-B')")
                [IO.File]::WriteAllText('{{Path.Combine(source, "dependency.txt").Replace("'", "''", StringComparison.Ordinal)}}', 'dependency-B')
                $hostPath = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
                $observed = Invoke-OwnedProcess -FileName $hostPath -Arguments @('-NoProfile','-NonInteractive','-File',$snapshot.cliAssemblyPath) `
                    -WorkingDirectory '{{root.Replace("'", "''", StringComparison.Ordinal)}}' -TimeoutSeconds 30
                if ($observed.ExitCode -ne 0) { throw $observed.StandardError }
                [Console]::Out.Write($observed.StandardOutput)
                """;
            var result = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", command);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("revision-A|dependency-A", result.StandardOutput);
            Assert.True(string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
