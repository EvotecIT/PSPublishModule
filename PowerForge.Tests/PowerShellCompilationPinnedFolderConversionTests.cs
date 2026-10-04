using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void PinnedFolderConversion_PreservesBytesBackupsWhatIfAndRollback(string framework, string host)
    {
        var sources = new[]
        {
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "FilesFolders", "Convert-FolderEncoding.ps1"),
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "FilesFolders", "Convert-FileEncoding.ps1"),
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "FilesFolders", "Get-FileEncoding.ps1"),
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Convert-FileEncodingSingle.ps1"),
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Resolve-Encoding.ps1")
        };
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, sources.Select(File.ReadAllText)), ".psm1");
        var root = Path.Combine(fixture.RootPath, "conversion-input");
        Directory.CreateDirectory(root);
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.PinnedFolderConversion",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries,
            unit => unit.Name == "Convert-FolderEncoding" && unit.EmittedClrMethod);
        var probe = "& (Get-Command Convert-FolderEncoding).Module { param($root) " + """
            $ProgressPreference='SilentlyContinue'
            $WarningPreference='SilentlyContinue'
            $content='Encoding '+[char]0x0141+[char]0x00e9+[char]0x03a9
            $unicode=[System.Text.Encoding]::Unicode
            $utf8=[System.Text.UTF8Encoding]::new($true)
            $expectedUnicode=[byte[]]($unicode.GetPreamble()+$unicode.GetBytes($content))
            $expectedUtf8=[byte[]]($utf8.GetPreamble()+$utf8.GetBytes($content))
            foreach($case in 'convert','preset','nonrecursive','whatif','mismatch','rollback','keep-loss') {
                # Reset the same owned paths before every original/generated invocation.
                Get-ChildItem -LiteralPath $root -File -Recurse|Remove-Item
                foreach($directory in 'child','bin') {
                    [void][System.IO.Directory]::CreateDirectory((Join-Path $root $directory))
                }
                foreach($relative in 'a.ps1','child/c.ps1','bin/excluded.ps1','other.txt') {
                    [System.IO.File]::WriteAllBytes((Join-Path $root $relative),$expectedUnicode)
                }
                $parameters=@{Path=$root;SourceEncoding='Unicode';TargetEncoding='UTF8BOM';CreateBackups=$true;PassThru=$true;Confirm=$false}
                if($case -eq 'preset'){$parameters.FileType='PowerShell'}else{$parameters.Extensions='ps1'}
                if($case -eq 'nonrecursive'){$parameters.Recurse=$false}
                if($case -eq 'whatif'){$parameters.WhatIf=$true}
                if($case -eq 'mismatch'){$parameters.SourceEncoding='UTF8BOM'}
                if($case -in 'rollback','keep-loss'){$parameters.TargetEncoding='Ascii'}
                if($case -eq 'keep-loss'){$parameters.NoRollbackOnMismatch=$true}
                $observedResults=@(Convert-FolderEncoding @parameters)
                $files=@(Get-ChildItem -LiteralPath $root -File -Recurse|Sort-Object FullName|ForEach-Object {
                    $bytes=[System.IO.File]::ReadAllBytes($_.FullName)
                    [pscustomobject]@{path=$_.FullName.Substring($root.Length);bytes=[Convert]::ToBase64String($bytes)
                        original=([Convert]::ToBase64String($bytes) -eq [Convert]::ToBase64String($expectedUnicode))
                        target=([Convert]::ToBase64String($bytes) -eq [Convert]::ToBase64String($expectedUtf8))
                        content=([System.IO.File]::ReadAllText($_.FullName) -eq $content)}
                })
                [pscustomobject]@{case=$case;results=@($observedResults|Sort-Object FilePath);files=$files}|ConvertTo-Json -Compress -Depth 8
            }
            try {Convert-FolderEncoding -Path ($root+'-missing') -Extensions ps1 -ErrorAction Stop}
            catch {[pscustomobject]@{missing=$_.Exception.Message;id=$_.FullyQualifiedErrorId}|ConvertTo-Json -Compress}
            """ + " } '" + root.Replace("'", "''", StringComparison.Ordinal) + "'";
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        var observations = generated.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("{\"case\":", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            Assert.Equal(7, observations.Length);
            foreach (var observation in observations)
            {
                var item = observation.RootElement;
                var scenario = item.GetProperty("case").GetString();
                var files = item.GetProperty("files").EnumerateArray().ToArray();
                var backups = files.Where(file => file.GetProperty("path").GetString()!.EndsWith(".bak", StringComparison.Ordinal)).ToArray();
                Assert.Equal(scenario == "whatif" ? 0 : scenario == "nonrecursive" ? 1 : 2, backups.Length);
                Assert.All(backups, file => Assert.True(file.GetProperty("original").GetBoolean()));
                foreach (var file in files.Except(backups))
                {
                    var path = file.GetProperty("path").GetString()!;
                    var selected = path.EndsWith("a.ps1", StringComparison.Ordinal) ||
                        (scenario != "nonrecursive" && path.EndsWith("c.ps1", StringComparison.Ordinal));
                    var converted = selected && scenario is "convert" or "preset" or "nonrecursive";
                    Assert.Equal(!converted && !(selected && scenario == "keep-loss"), file.GetProperty("original").GetBoolean());
                    Assert.Equal(converted, file.GetProperty("target").GetBoolean());
                    Assert.Equal(!(selected && scenario == "keep-loss"), file.GetProperty("content").GetBoolean());
                }
                Assert.Equal(scenario == "whatif" ? 0 : scenario == "nonrecursive" ? 1 : 2,
                    item.GetProperty("results").GetArrayLength());
            }
        }
        finally { foreach (var observation in observations) observation.Dispose(); }
    }
}
