using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void TypeFactoryArgument_PreservesEvaluationCatchAndOfflineDeletion(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "FilesFolders", "Remove-FileItem.ps1");
        Assert.Equal("E1CE8D334214A50E92F269312B597A32B62726A26F4D0BD0ED7810671426FEEF",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + """

            function New-FromTypeName {
                [CmdletBinding()]param($Name,[bool]$Throw,$Trace)
                $value='prior'
                try {
                    $value=[Activator]::CreateInstance([Type]::GetType($($Trace.Add('name');$Name),$($Trace.Add('throw');$Throw)))
                    $Trace.Add('constructed')
                } catch {
                    [pscustomobject]@{kind='error';id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}
                } finally {$Trace.Add('finally')}
                [pscustomobject]@{kind='value';type=$value.GetType().FullName;value=[string]$value;trace=@($Trace.ToArray())}
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TypeFactory", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, entry => {
            Assert.True(entry.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(entry));
            Assert.True(entry.UsesNativeFunctionBinding);
            Assert.False(entry.RetainedHostedSource);
        });
        var probe = """
            foreach($name in 'System.Text.StringBuilder','System.Int32','System.String','System.IO.Stream','Missing.Authored.Type','System.Int32[','',$null){
                foreach($throw in $false,$true){
                    $trace=[Collections.Generic.List[string]]::new()
                    [pscustomobject]@{kind='factory';name=$name;throw=$throw;records=@(New-FromTypeName -Name $name -Throw $throw -Trace $trace)}|ConvertTo-Json -Depth 8 -Compress
                }
            }
            Add-Type -TypeDefinition 'public sealed class FactoryNameProbe { public System.Collections.Generic.List<string> Trace; public bool Failure; public override string ToString() { Trace.Add("stringify"); if (Failure) throw new System.InvalidOperationException("name-stringification"); return "System.Int32"; } }'
            foreach($failure in $false,$true){
                $trace=[Collections.Generic.List[string]]::new()
                $name=[FactoryNameProbe]::new();$name.Trace=$trace;$name.Failure=$failure
                [pscustomobject]@{kind='callback';failure=$failure;records=@(New-FromTypeName -Name $name -Throw $true -Trace $trace)}|ConvertTo-Json -Depth 8 -Compress
            }
            foreach($case in 'file','empty-directory','nonempty-directory','recursive','whatif','missing','zero-retries'){
                $root='OWNED_ROOT'
                if(Test-Path -LiteralPath $root){[IO.Directory]::Delete($root,$true)}
                [void][IO.Directory]::CreateDirectory($root)
                $path=Join-Path $root 'target'
                if($case -in 'empty-directory','nonempty-directory','recursive'){
                    [void][IO.Directory]::CreateDirectory($path)
                    if($case -ne 'empty-directory'){[IO.File]::WriteAllText((Join-Path $path 'child.txt'),'owned')}
                }elseif($case -ne 'missing'){[IO.File]::WriteAllText($path,'owned')}
                $warnings=@()
                $records=@(Remove-FileItem -Paths $path -DeleteMethod DotNetDelete -Confirm:$false -Recursive:($case -eq 'recursive') -WhatIf:($case -eq 'whatif') -Retries $(if($case -eq 'zero-retries'){0}else{1}) -Passthru -WarningVariable warnings 3>$null)
                [pscustomobject]@{kind='delete';case=$case;records=$records;exists=(Test-Path -LiteralPath $path);warnings=@($warnings|ForEach-Object {[string]$_})}|ConvertTo-Json -Depth 8 -Compress
            }
            """.Replace("OWNED_ROOT", EscapeStatementErrorPath(Path.Combine(fixture.RootPath, "owned-delete")), StringComparison.Ordinal);
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Equal(25, generated.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Count(static line => line.StartsWith('{')));
        Assert.Contains("constructed", generated);
        Assert.Contains("stringify", generated);
        Assert.Contains("prior", generated);
        Assert.Contains("\"exists\":false", generated);
        Assert.Contains("\"exists\":true", generated);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void TypeFactoryArgument_KeepsUnknownTransformsTypedCatchesAndStrictClosed(string framework)
    {
        var source = PowerShellSourceParser.Parse("""
            function New-Closed {[CmdletBinding()]param([string]$Name) try {[Activator]::CreateInstance([Type]::GetType($Name))}catch{$_.FullyQualifiedErrorId}}
            function New-TypedCatch {[CmdletBinding()]param([string]$Name) try {[Activator]::CreateInstance([Type]::GetType($Name))}catch [Exception] {$_.FullyQualifiedErrorId}}
            function New-Transform {[CmdletBinding()]param() try {[Activator]::CreateInstance([int]::MaxValue.GetType())}catch{$_.FullyQualifiedErrorId}}
            function New-Cast {[CmdletBinding()]param([string]$Name) try {[Activator]::CreateInstance([Type]([Type]::GetType($Name)))}catch{$_.FullyQualifiedErrorId}}
            function New-DelegateFactory {[CmdletBinding()]param([string]$Name) try {[Activator]::CreateInstance([Type]::GetType($Name,$null,$null))}catch{$_.FullyQualifiedErrorId}}
            """, Path.Combine(Path.GetTempPath(), "type-factory-boundaries.ps1"));
        var hybrid = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal("New_Closed", Assert.Single(hybrid.Emitted.Methods).GeneratedName);
        var strict = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, PowerShellCompilationCapabilities.TypedLibrary);
        Assert.Empty(strict.Emitted.Methods);
    }
}
