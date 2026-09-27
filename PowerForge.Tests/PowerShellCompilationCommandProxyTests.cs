using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void CommandProxy_PreservesAuthoredSourceAndActiveHostMetadata(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSScriptTools", "CommandProxy", "Copy-Command.ps1");
        Assert.Equal("9FE8DAE88FAC427C5950685A3CA33F13CBAEB0DF9B0A3DF8E8968F0848831728",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + """

            function Read-CommandMetadata {
                [CmdletBinding()]param([Management.Automation.CommandMetadata]$Value,$Caller)
                [pscustomobject]@{same=[object]::ReferenceEquals($Value,$Caller);bound=[object]::ReferenceEquals($Value,$PSBoundParameters.Value);name=$Value.Name;keys=@($Value.Parameters.Keys|Sort-Object)}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CommandProxy", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        var ledger = built.Manifest!.UnitDispositionLedger!.Entries;
        Assert.True(Assert.Single(ledger, entry => entry.Name == "Copy-Command").EmittedClrMethod);
        Assert.True(Assert.Single(ledger, entry => entry.Name == "Read-CommandMetadata").UsesNativeFunctionBinding);
        const string probe = """
            $ErrorActionPreference='Stop'
            function global:Invoke-OfflineTarget {
                [CmdletBinding()]param([Parameter(ValueFromPipeline)][Alias('n')][int]$Number=3,[switch]$Fail)
                process {if($Fail){throw 'offline-proxy-failure'};$Number+10}
            }
            Set-Alias -Name OfflineTargetAlias -Value Invoke-OfflineTarget -Scope Global
            $metadata=[Management.Automation.CommandMetadata]::new((Get-Command Invoke-OfflineTarget))
            Read-CommandMetadata -Value $metadata -Caller $metadata|ConvertTo-Json -Compress
            foreach($alias in $false,$true){foreach($proxy in $false,$true){
                $name=if($alias){'OfflineTargetAlias'}else{'Invoke-OfflineTarget'}
                $text=Copy-Command -Command $name -NewName Invoke-OfflineProxy -AsProxy:$proxy -UseForwardHelp
                $tokens=$null;$errors=$null
                [void][Management.Automation.Language.Parser]::ParseInput($text,[ref]$tokens,[ref]$errors)
                if($errors.Count){throw ($errors|Out-String)}
                $result=& {
                    . ([scriptblock]::Create($text))
                    $failure=$null
                    try{Invoke-OfflineProxy -Fail}catch{$failure=[pscustomobject]@{message=$_.Exception.Message;id=$_.FullyQualifiedErrorId}}
                    [pscustomobject]@{default=@(Invoke-OfflineProxy);alias=@(Invoke-OfflineProxy -n 5);pipeline=@(1,2|Invoke-OfflineProxy);failure=$failure;parameterType=(Get-Command Invoke-OfflineProxy).Parameters.Number.ParameterType.FullName}
                }
                [pscustomobject]@{case='proxy';alias=$alias;proxy=$proxy;result=$result;text=$text}|ConvertTo-Json -Depth 6 -Compress
            }}
            function global:Invoke-OfflineTarget {[CmdletBinding()]param([string]$Word='rebound');$Word.ToUpperInvariant()}
            $text=Copy-Command Invoke-OfflineTarget Invoke-OfflineRebound -AsProxy -UseForwardHelp
            $result=& {. ([scriptblock]::Create($text));Invoke-OfflineRebound -Word local}
            [pscustomobject]@{case='rebound';result=@($result);text=$text}|ConvertTo-Json -Compress
            $warnings=@();$records=@(Copy-Command -Command MissingOfflineCommand_29a -WarningVariable warnings 3>$null)
            [pscustomobject]@{case='missing';count=$records.Count;warnings=@($warnings|ForEach-Object {[string]$_})}|ConvertTo-Json -Compress
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        Assert.Equal(original, RunModuleProof(built.ArtifactPath!, probe, host));
        Assert.Contains("\"same\":true,\"bound\":true", original);
        Assert.Contains("\"pipeline\":[11,12]", original);
        Assert.Contains("offline-proxy-failure", original);
        Assert.Contains("\"result\":[\"LOCAL\"]", original);
        Assert.Contains("\"case\":\"missing\",\"count\":0", original);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void CommandProxy_RequiresExactSdkTypeAndNativeHostBinding(string framework)
    {
        var source = PowerShellSourceParser.Parse("""
            function Read-Metadata {param([Management.Automation.CommandMetadata]$Value);$Value}
            function Read-MetadataArray {param([Management.Automation.CommandMetadata[]]$Value);$Value}
            function Read-MetadataList {param([Collections.Generic.List[Management.Automation.CommandMetadata]]$Value);$Value}
            function Get-ProxyParameters {param($Value);[Management.Automation.ProxyCommand]::GetParamBlock($Value)}
            function Read-Coordinates {param([Management.Automation.Host.Coordinates]$Value);$Value}
            """, Path.Combine(Path.GetTempPath(), "command-proxy-boundaries.ps1"));
        Assert.Equal(4, new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework,
            PowerShellCompilationCapabilities.HybridModule).Emitted.Methods.Length);
        foreach(var capabilities in new[] { PowerShellCompilationCapabilities.TypedLibrary,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.PowerShellHostTypes })
            Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, capabilities).Emitted.Methods);
    }
}
