using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeArrayReceiver_PreservesSingleFileConversionAndCaughtFailure(string framework, string host)
    {
        var reader = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "FilesFolders", "Get-FileEncoding.ps1");
        var converter = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Convert-FileEncodingSingle.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(reader) + Environment.NewLine + File.ReadAllText(converter), ".psm1");
        File.WriteAllText(fixture.ScriptPath, File.ReadAllText(fixture.ScriptPath), new System.Text.UTF8Encoding(true));
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeArrayReceiver", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "Get-FileEncoding", "Convert-FileEncodingSingle" })
            Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name && unit.EmittedClrMethod && unit.UsesNativeFunctionBinding);
        var data = Path.Combine(fixture.RootPath, "array-receiver-proof");
        Directory.CreateDirectory(data);
        var probe = """
            $WarningPreference='SilentlyContinue'
            $root='__ROOT__'
            $source=[Text.Encoding]::Unicode
            $utf8=[Text.UTF8Encoding]::new($true)
            $text='plain '+[char]0x0141+[char]0x03a9
            foreach($case in 'convert','backup-collision','mismatch','same','whatif','rollback','keep-loss','missing') {
                foreach($file in Get-ChildItem -LiteralPath $root -File) { Remove-Item -LiteralPath $file.FullName }
                $path=Join-Path $root 'input.txt'
                if($case -ne 'missing') { [IO.File]::WriteAllText($path,$text,$source) }
                if($case -eq 'backup-collision') { [IO.File]::WriteAllText(($path+'.backup'),'existing',[Text.Encoding]::ASCII) }
                $parameters=@{FilePath=$path;SourceEncoding=$source;TargetEncoding=$utf8;CreateBackup=$true;Confirm=$false}
                if($case -eq 'mismatch') { $parameters.SourceEncoding=[Text.Encoding]::ASCII }
                if($case -eq 'same') { $parameters.TargetEncoding=$source }
                if($case -eq 'whatif') { $parameters.WhatIf=$true }
                if($case -in 'rollback','keep-loss') { $parameters.TargetEncoding=[Text.Encoding]::ASCII }
                if($case -eq 'keep-loss') { $parameters.NoRollbackOnMismatch=$true }
                if($case -eq 'missing') { $parameters.Force=$true }
                $result=@(Convert-FileEncodingSingle @parameters)
                $files=@(Get-ChildItem -LiteralPath $root -File | Sort-Object Name | ForEach-Object {
                    $stream=[IO.File]::Open($_.FullName,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); $stream.Dispose()
                    [pscustomobject]@{name=$_.Name;bytes=[Convert]::ToBase64String([IO.File]::ReadAllBytes($_.FullName))}
                })
                [pscustomobject]@{case=$case;result=$result;files=$files} | ConvertTo-Json -Depth 6 -Compress
            }
            """;
        probe = probe.Replace("__ROOT__", EscapeStatementErrorPath(data), StringComparison.Ordinal);
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        var expectedLines = original.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var actualLines = generated.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expectedLines.Length, actualLines.Length);
        for (var index = 0; index < expectedLines.Length; index++)
        {
            if (expectedLines[index].StartsWith("{", StringComparison.Ordinal))
                Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                    System.Text.Json.Nodes.JsonNode.Parse(expectedLines[index]),
                    System.Text.Json.Nodes.JsonNode.Parse(actualLines[index])),
                    "Original: " + expectedLines[index] + Environment.NewLine + "Generated: " + actualLines[index]);
            else Assert.Equal(expectedLines[index], actualLines[index]);
        }
        Assert.Contains("\"Status\":\"Converted\"", generated);
        Assert.Contains("\"Status\":\"Failed\"", generated);
        Assert.Contains("\"Status\":\"Error\"", generated);
        Assert.Contains("input.txt.backup1", generated);
    }
}
