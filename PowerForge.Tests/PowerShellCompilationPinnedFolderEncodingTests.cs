using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void PinnedFolderEncoding_PreservesAccessMutationAndOfflineAnalysis(string framework, string host)
    {
        var sources = new[] { "Get-FolderEncoding.ps1", "Get-FileEncoding.ps1" }.Select(name =>
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "FilesFolders", name));
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, sources.Select(File.ReadAllText)), ".psm1");
        var root = Path.Combine(fixture.RootPath, "encoding-input");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "child"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        File.WriteAllText(Path.Combine(root, "a.ps1"), "Write-Output 'test'", new System.Text.UTF8Encoding(true));
        File.WriteAllText(Path.Combine(root, "b.ps1"), "Write-Output 'test'", new System.Text.UTF8Encoding(true));
        File.WriteAllText(Path.Combine(root, "child", "c.ps1"), "Write-Output 'test'", System.Text.Encoding.Unicode);
        File.WriteAllText(Path.Combine(root, "bin", "excluded.ps1"), "Write-Output 'test'", System.Text.Encoding.Unicode);
        File.WriteAllText(Path.Combine(root, "other.txt"), "text", new System.Text.UTF8Encoding(true));
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.PinnedFolderEncoding",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries,
            unit => unit.Name == "Get-FolderEncoding" && unit.EmittedClrMethod);
        var probe = "& (Get-Command Get-FolderEncoding).Module { param($root) " + """
            function Get-Date { $script:clock++; ([datetime]'2026-01-02T03:04:05').AddSeconds($script:clock) }
            function Get-MapSnapshot($map) {
                @($map.Keys|Sort-Object|ForEach-Object {[pscustomobject]@{key=$_;value=$map[$_]}})
            }
            foreach($case in 'extensions','preset','nonrecursive','no-details','depth') {
                $script:clock=0
                $parameters=@{Path=$root;GroupByExtension=$true}
                if($case -eq 'preset'){$parameters.FileType='PowerShell'}else{$parameters.Extensions='ps1'}
                if($case -eq 'nonrecursive'){$parameters.Recurse=$false}
                if($case -eq 'no-details'){$parameters.ShowFiles=$false;$parameters.RecommendTarget=$false}
                if($case -eq 'depth'){$parameters.MaxDepth=1}
                $result=Get-FolderEncoding @parameters
                [pscustomobject]@{case=$case;summary=$result.Summary
                    distribution=@(Get-MapSnapshot $result.EncodingDistribution)
                    extensions=@($result.ExtensionAnalysis.Keys|Sort-Object|ForEach-Object {
                        [pscustomobject]@{extension=$_;encodings=@(Get-MapSnapshot $result.ExtensionAnalysis[$_])}
                    })
                    files=@($result.Files|Sort-Object Path)
                    recommendations=@($result.Recommendations|Sort-Object Extension|ForEach-Object {
                        [pscustomobject]@{extension=$_.Extension;recommended=$_.RecommendedEncoding
                            nonCompliant=$_.NonCompliantFiles;total=$_.TotalFiles
                            current=@(Get-MapSnapshot $_.CurrentEncodings)}
                    });display=$result.ToString()
                }|ConvertTo-Json -Compress -Depth 12
            }
            try {Get-FolderEncoding -Path ($root+'-missing') -Extensions ps1 -ErrorAction Stop}
            catch { [pscustomobject]@{missing=$_.Exception.Message;id=$_.FullyQualifiedErrorId}|ConvertTo-Json -Compress }
            """ + " } '" + root.Replace("'", "''", StringComparison.Ordinal) + "'";
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Contains("\"TotalFiles\":3", generated);
        Assert.Contains("\"ProcessedDirectories\":1", generated);
        Assert.Contains("UTF8BOM", generated);
        Assert.Contains("Unicode", generated);
    }
}
