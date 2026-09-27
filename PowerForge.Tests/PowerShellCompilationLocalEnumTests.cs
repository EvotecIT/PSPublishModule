using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void LocalEnum_ExistingAuthoredDeclarationCallbacksKeepTheirBroaderTypeContract(string framework, string host)
    {
        var project = FindStatementErrorFixtureProject();
        var build = RunProcess("dotnet", "build", project, "-c", "Release", "-f", framework, "--nologo");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var assembly = Path.Combine(Path.GetDirectoryName(project)!, "bin", "Release", framework, "Generic.Compiler.StatementErrors.dll");
        using var fixture = ArtifactFixture.Create("""
            param([string]$Assembly)
            $ErrorActionPreference='Stop'
            Add-Type -Path $Assembly
            $module=New-Module -Name AuthoredTypeCallbacks -ScriptBlock {
                function Read-WideEnum {
                    enum WideEnum {
                        One = 1
                    }
                    'original'
                }
                function Read-LocalClass {
                    class LocalClass {
                        [int]$Value
                    }
                    'original'
                }
                [Generic.Compiler.StatementErrors.NativeAuthoredMetadataFixture]::InstallEndOnly($ExecutionContext.SessionState.Module,'Read-WideEnum')
                [Generic.Compiler.StatementErrors.NativeAuthoredMetadataFixture]::InstallEndOnly($ExecutionContext.SessionState.Module,'Read-LocalClass')
            }
            $rows=@($module.Invoke({Read-WideEnum;Read-LocalClass}))
            $enumBlock=if($PSVersionTable.PSVersion.Major -gt 5){'{enum BlockEnum : long { One = 1 }; "original"}'}else{'{enum BlockEnum { One = 1 }; "original"}'}
            foreach($literal in @($enumBlock,'{class BlockClass { [int]$Value }; "original"}')) {
                $block=[Generic.Compiler.StatementErrors.NativeAuthoredMetadataFixture]::CreateEndOnlyBlock($module,$literal)
                $rows+=@(& $block)
            }
            if(($rows -join ',') -ne 'compiled,compiled,block,block'){throw ('Callback output: '+($rows -join ','))}
            'Authored type callback compatibility passed: 4 callbacks'
            """, ".ps1");
        var result = RunProcess(host, "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, "-Assembly", assembly);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Contains("Authored type callback compatibility passed: 4 callbacks", result.StandardOutput);
    }

    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void LocalEnum_PreservesFunctionIdentityConversionAndOfflineTimeSettings(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Time", "Get-TimeSettings.ps1");
        Assert.Equal("B0E7F3F20BFF26E95E940861A0DC89E7E2F9980816FCD6ED086D51A181083E5A",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + """

            function Read-LocalEnum {
                [CmdletBinding()]param($Value)
                [Flags()] enum LocalChoice {
                    None = 0
                    One = 1
                    Two = 2
                }
                $converted=$Value -as [LocalChoice]
                [pscustomobject]@{value=$converted;inside=$null -ne ('LocalChoice' -as [type])}
            }
            Export-ModuleMember -Function Get-TimeSettings,Read-LocalEnum
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.LocalEnum", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(2, built.Manifest!.CompiledMethods);
        const string probe = """
            $module=Get-Module|Where-Object {$_.Path -eq $modulePath}
            $before=$null -ne ('LocalChoice' -as [type])
            $previous=$null
            foreach($inputValue in @(3,0,1,2,'One, Two','Missing',$null,4,3)) {
                $result=Read-LocalEnum $inputValue
                $type=if($null -ne $result.value){$result.value.GetType()}else{$null}
                [pscustomobject]@{input=[string]$inputValue;name=[string]$result.value;number=if($type){[int]$result.value}else{$null};inside=$result.inside;outside=$null -ne ('LocalChoice' -as [type]);before=$before;same=if($type -and $previous){[object]::ReferenceEquals($type,$previous)}else{$true}}|ConvertTo-Json -Compress
                if($type){$previous=$type}
            }
            $other=New-Module -Name OtherLocalChoice -ScriptBlock {
                function Read-OtherChoice {
                    enum LocalChoice {
                        Other = 1
                    }
                    1 -as [LocalChoice]
                }
            }
            $otherValue=@($other.Invoke({Read-OtherChoice}))[0]
            [pscustomobject]@{isolated=$previous -ne $otherValue.GetType();other=[string]$otherValue;outside=$null -ne ('LocalChoice' -as [type])}|ConvertTo-Json -Compress
            Import-Module $modulePath -Force
            $module=Get-Module|Where-Object {$_.Path -eq $modulePath}
            $reloaded=Read-LocalEnum 3
            [pscustomobject]@{reloaded=[string]$reloaded.value;inside=$reloaded.inside;outside=$null -ne ('LocalChoice' -as [type])}|ConvertTo-Json -Compress
            $module.Invoke({
                function script:Get-PSRegistry {
                    param($ComputerName,$RegistryPath,$Key)
                    $script:Calls.Add($ComputerName+'|'+$RegistryPath+'|'+$Key)
                    if($Key){return [pscustomobject]@{PSType='DWord';PSValue=$script:Secure}}
                    if($RegistryPath -like '*\Parameters') {
                        if($script:Fallback -and $RegistryPath -like '*Policies*'){return [pscustomobject]@{Type='NTP'}}
                        return [pscustomobject]@{NtpServer=$script:Peers;Type='NTP'}
                    }
                    if($RegistryPath -like '*\Config'){return [pscustomobject]@{AnnounceFlags=10;UtilizeSslTimeData=$script:Secure;MaxPollInterval=10;MinPollInterval=6}}
                    if($RegistryPath -like '*\NtpClient') {
                        if($script:Fallback -and $RegistryPath -like '*Policies*'){return $null}
                        return [pscustomobject]@{Enabled=1;CrossSiteSyncFlags=2;InputProvider=1;SpecialPollInterval=3600}
                    }
                    [pscustomobject]@{Enabled=1;InputProvider=1}
                }
            })
            $cases=@(
                @{peers='clock,0x9';fallback=$false;secure=0;split=''},
                @{peers='first,0x1 second,0x8';fallback=$true;secure=1;split=';'},
                @{peers='zero,0';fallback=$false;secure=$null;split=''},
                @{peers='bad,invalid';fallback=$false;secure=0;split=''},
                @{peers='unknown,0x10';fallback=$false;secure=0;split=''},
                @{peers='missing';fallback=$false;secure=0;split=''},
                @{peers='';fallback=$false;secure=0;split=''},
                @{peers='clock,0x9';fallback=$false;secure=0;split=''}
            )
            foreach($case in $cases) {
                $module.Invoke({param($case)
                    $script:Peers=$case.peers;$script:Fallback=$case.fallback;$script:Secure=$case.secure
                    $script:Calls=[Collections.Generic.List[string]]::new()
                },$case)
                $warnings=@();$errors=@()
                $rows=@(Get-TimeSettings -ComputerName @('offline-one','offline-two') -Splitter $case.split -WarningAction SilentlyContinue -WarningVariable warnings -ErrorAction SilentlyContinue -ErrorVariable errors)
                $calls=@($module.Invoke({$script:Calls.ToArray()}))
                [pscustomobject]@{rows=$rows;warnings=@($warnings|ForEach-Object Message);errors=@($errors|ForEach-Object {$_.FullyQualifiedErrorId});calls=$calls;enumOutside=$null -ne ('NtpServerFlags' -as [type])}|ConvertTo-Json -Depth 8 -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, "$modulePath='" + EscapeStatementErrorPath(built.ArtifactPath!) + "'; " + probe, host);
        Assert.True(original == generated, "Original:" + original + Environment.NewLine + "Generated:" + generated);
        var records = generated.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(19, records.Count(static record => record.StartsWith('{')));
        Assert.Equal(6, records.Count(static record => record.StartsWith("WARNING:")));
        Assert.Contains("\"inside\":true", generated);
        Assert.DoesNotContain("\"outside\":true", generated);
        Assert.DoesNotContain("\"same\":false", generated);
        Assert.Contains("\"isolated\":true", generated);
        Assert.Contains("SpecialInterval+Client", generated);
        Assert.Contains("Incorrect", generated);
    }
}

public sealed class PowerShellLocalEnumAdmissionTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void DirectLocalEnumUsesNativeFrameAndKeepsStrictClosed(string framework)
    {
        var document = Parse("function Read-Choice {param($Value) enum LocalChoice { One=1 }; $Value -as [LocalChoice]}");
        Assert.True(PowerShellHostedLocalEnumPolicy.IsQualified(document, framework, PowerShellCompilationCapabilities.HybridModule));
        var compiled = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.Single(compiled.Lowered.Functions);
        Assert.False(PowerShellHostedLocalEnumPolicy.IsQualified(document, framework, PowerShellCompilationCapability.None));
        Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, framework, PowerShellCompilationCapability.None).Lowered.Functions);
    }

    [Theory]
    [InlineData("function Read-Choice {if($true){enum LocalChoice {One=1}}; 1 -as [LocalChoice]}")]
    [InlineData("function Read-Choice {enum LocalChoice {One=1}; [LocalChoice]}")]
    [InlineData("function Read-Choice {enum LocalChoice {One=1}; [LocalChoice]::One}")]
    [InlineData("function Read-Choice {enum LocalChoice {One=1}; 1 -as [LocalChoice]}; function Other {1 -as [LocalChoice]}")]
    [InlineData("function Read-Choice {enum LocalChoice : long {One=1}; 1 -as [LocalChoice]}")]
    [InlineData("function Read-Choice {process{enum LocalChoice {One=1}; 1 -as [LocalChoice]}}")]
    public void UnsupportedDeclarationOrConsumerKeepsHostedBoundary(string source)
        => Assert.False(PowerShellHostedLocalEnumPolicy.IsQualified(Parse(source), "net10.0", PowerShellCompilationCapabilities.HybridModule));

    private static ParsedSourceDocument Parse(string source)
        => PowerShellSourceParser.Parse(source, Path.Combine(Path.GetTempPath(), "local-enum.psm1"));
}
