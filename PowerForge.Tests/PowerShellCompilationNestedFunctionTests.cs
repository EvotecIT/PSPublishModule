namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NestedFunctions_PreserveFilterRecordsAndHeaderParameterMetadata(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-NestedFilter {
                [CmdletBinding()] param([object[]]$Values, [int]$Threshold)
                $trace = [Collections.Generic.List[string]]::new()
                filter Read-Selected([Alias('minimum')][ValidateRange(0,9)][int]$Min = $Threshold) {
                    try {
                        $trace.Add('item:'+ $_)
                        if ($_ -eq 'failure') { throw 'filter failed' }
                        return $_ -is [int] -and $_ -gt $Min
                    } finally { $trace.Add('finally') }
                }
                function Read-Header([Alias('n')][ValidateRange(0,9)][int]$Count = $Threshold, [string]$Label = 'default') {
                    [pscustomobject]@{count=$Count;label=$Label;extra=@($args);bound=@($PSBoundParameters.Keys | Sort-Object)}
                }
                $results = @($Values | Read-Selected)
                $Threshold = 5
                [pscustomobject]@{records=$results;changed=@($Values | Read-Selected);explicit=@($Values | Read-Selected -minimum 1);
                    trace=@($trace);header=@(Read-Header);named=@(Read-Header -n 2 -Label 'named');
                    positional=@(Read-Header 3 'position' 'extra');isFilter=(Get-Command Read-Selected).CommandType.ToString();
                    parameters=@((Get-Command Read-Header).Parameters.Keys | Sort-Object)}
                try { Read-Header -n 10 -ErrorAction Stop }
                catch { $_.FullyQualifiedErrorId; $_.Exception.GetType().FullName }
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NestedFilterHeader", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == "Invoke-NestedFilter");
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.Equal(2, unit.RegionGraph!.ScriptBlocks.Count);
        const string probe = """
            foreach ($values in @(@(),@(1),@(1,2,7),@('text',0,9),@('failure'),@(1,2,7))) {
                try { Invoke-NestedFilter -Values $values -Threshold 2 -ErrorAction Stop | ConvertTo-Json -Depth 8 -Compress }
                catch { [pscustomobject]@{error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message} | ConvertTo-Json -Compress }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("filter failed", generated);
        Assert.Contains("ParameterArgumentValidationError", generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NestedFunctions_QualifyUnchangedOfflineObjectFormattingFilters(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Converts", "ConvertTo-PrettyObject.ps1");
        var helper = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "ConvertTo-InvariantJoinedString.ps1");
        var jsonHelper = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "ConvertTo-StringByType.ps1");
        var jsonSource = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Converts", "ConvertTo-JsonLiteral.ps1");
        var hostSource = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Converts", "ConvertFrom-ObjectToString.ps1");
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine,
            new[] { helper, jsonHelper, source, jsonSource, hostSource }.Select(File.ReadAllText)), ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.PrettyObjectFilters", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "ConvertTo-PrettyObject", "ConvertTo-JsonLiteral", "ConvertFrom-ObjectToString" })
        {
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.Equal(2, unit.RegionGraph!.ScriptBlocks.Count);
        }
        const string probe = """
            $value=[pscustomobject][ordered]@{name='text';number=12;fraction=[decimal]1.5;flag=$true;
                date=[datetime]'2020-01-02';empty=$null;array=@(1,'two');nested=@{key='value'}}
            foreach($case in @([ordered]@{},[ordered]@{NumberAsString=$true;BoolAsString=$true},
                [ordered]@{ArrayJoin=$true;ArrayJoinString='|'},[ordered]@{DateTimeFormat='yyyy'},[ordered]@{})) {
                $records=@($value | ConvertTo-PrettyObject @case)
                [pscustomobject]@{case=$case;records=$records;types=@($records | ForEach-Object {
                    foreach($property in $_.PSObject.Properties) {
                        [pscustomobject]@{name=$property.Name;type=if($null -eq $property.Value){'null'}else{$property.Value.GetType().FullName}}
                    }
                })} | ConvertTo-Json -Depth 9 -Compress
            }
            foreach($values in @(@(),@($null),@([ordered]@{}),@([ordered]@{n=3;flag=$false}),@(3,'text'),@($value,$value))) {
                @(ConvertTo-PrettyObject -Object $values) | ConvertTo-Json -Depth 9 -Compress
            }
            foreach($case in @([ordered]@{Depth=2},[ordered]@{Depth=2;AsArray=$true},
                [ordered]@{Depth=2;NumberAsString=$true;BoolAsString=$true},
                [ordered]@{Depth=2;ArrayJoin=$true;ArrayJoinString='|'},[ordered]@{Depth=2})) {
                [pscustomobject]@{case=$case;json=@($value | ConvertTo-JsonLiteral @case)} | ConvertTo-Json -Depth 9 -Compress
            }
            foreach($case in @([ordered]@{},[ordered]@{NumbersAsString=$true;QuotePropertyNames=$true},
                [ordered]@{OutputType='Ordered';IncludeProperties=@('name','number','date')},[ordered]@{})) {
                $records=@(ConvertFrom-ObjectToString -Objects @($value) @case -InformationAction Continue 6>&1)
                [pscustomobject]@{case=$case;records=@($records | ForEach-Object {
                    [pscustomobject]@{type=$_.GetType().FullName;text=$_.ToString()}
                })} | ConvertTo-Json -Depth 9 -Compress
            }
            & (Get-Command ConvertTo-PrettyObject).Module {
                [pscustomobject]@{numericHelper=[bool](Get-Command IsNumeric -ErrorAction SilentlyContinue);
                    typeHelper=[bool](Get-Command IsOfType -ErrorAction SilentlyContinue)} | ConvertTo-Json -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("\"numericHelper\":false", generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NestedFunctions_PreserveDeclarationScopeMetadataRecursionAndEscape(string framework, string host)
    {
        using var fixture=ArtifactFixture.Create("""
            function Invoke-Nested {
                [CmdletBinding()] param([string]$Outer,[switch]$Declare,[switch]$Protect)
                if($Protect) { Set-Item function:Read-Nested -Value { 'protected' } -Options ReadOnly }
                if($Declare) {
                    function Read-Nested {
                        [CmdletBinding()] [Alias('Read-NestedAlias')] param([string]$Suffix='default')
                        if($Suffix -eq 'failure') { throw 'nested failure' }
                        $Outer+':'+$Suffix
                    }
                }
                Read-Nested -Suffix 'first'
                $Outer='changed'
                Read-NestedAlias -Suffix 'second'
                (Get-Command Read-Nested).ScriptBlock
            }
            function Invoke-NestedRecursive {
                [CmdletBinding()] param([int]$Value)
                function Read-Sum { param([int]$N) if($N -le 0) { return 0 }; return $N+(Read-Sum -N ($N-1)) }
                Read-Sum -N $Value
            }
            function Invoke-NestedFailure {
                [CmdletBinding()] param([string]$Value)
                function Read-Failing { param([string]$Text) try { if($Text -eq 'bad') { throw 'nested failure' }; $Text } finally { 'finally' } }
                try { Read-Failing -Text $Value } catch { $_.FullyQualifiedErrorId; $_.Exception.Message }
                'after'
            }
            function Invoke-NestedLifecycle {
                [CmdletBinding()] param([int[]]$Values)
                function local:Read-Child {
                    [CmdletBinding()] param([Parameter(ValueFromPipeline)][int]$Value)
                    begin { 'begin' }
                    process { 'process:'+ $Value }
                    end { 'end' }
                }
                $Values | Read-Child
            }
            """, ".psm1");
        var built=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.NestedFunctions",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true) { TargetFramework=framework });
        Assert.True(built.Succeeded,built.Error+Environment.NewLine+built.BuildOutput);
        foreach(var name in new[] { "Invoke-Nested", "Invoke-NestedRecursive", "Invoke-NestedFailure", "Invoke-NestedLifecycle" })
        {
            var unit=Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,unit=>unit.Name==name);
            Assert.True(unit.EmittedClrMethod,System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.NotEmpty(unit.RegionGraph!.ScriptBlocks);
        }
        const string probe="""
            & (Get-Command Invoke-Nested).Module {
                function Read-Nested { param($Suffix) 'outer:'+ $Suffix }
                function Read-NestedAlias { param($Suffix) 'outer-alias:'+ $Suffix }
                foreach($declare in $false,$true,$false,$true) {
                    $records=@(Invoke-Nested -Outer 'original' -Declare:$declare)
                    [pscustomobject]@{declare=$declare;records=@($records | Where-Object { $_ -isnot [scriptblock] });
                        outside=(Read-Nested -Suffix 'outside');aliasOutside=(Read-NestedAlias -Suffix 'outside')} | ConvertTo-Json -Compress
                    $escaped=$records[-1]
                    $Outer='caller'
                    [pscustomobject]@{escaped=@(& $escaped -Suffix 'escaped');parameters=@($escaped.Ast.ParamBlock.Parameters | ForEach-Object {$_.Name.VariablePath.UserPath})} | ConvertTo-Json -Compress
                }
                foreach($n in 0,1,5,2) { [pscustomobject]@{n=$n;sum=@(Invoke-NestedRecursive -Value $n)} | ConvertTo-Json -Compress }
                foreach($value in 'ok','bad','ok') { [pscustomobject]@{value=$value;records=@(Invoke-NestedFailure -Value $value)} | ConvertTo-Json -Compress }
                foreach($values in @(@(),@(1),@(1,2,3))) { [pscustomobject]@{values=$values;records=@(Invoke-NestedLifecycle -Values $values)} | ConvertTo-Json -Compress }
                try { Invoke-Nested -Outer 'original' -Declare -Protect -ErrorAction Stop }
                catch { [pscustomobject]@{protectedError=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName} | ConvertTo-Json -Compress }
            }
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        var generated=RunModuleProof(built.ArtifactPath!,probe,host);
        Assert.True(original==generated,"Original: "+original+Environment.NewLine+"Generated: "+generated);
        Assert.Contains("nested failure",generated);
        Assert.Contains("protectedError",generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NestedFunctions_QualifyUnchangedOfflineCloudReportMerge(string framework,string host)
    {
        var source=FindCompleteConversionWorkflow("CleanupMonster","FullModule","Private","Merge-CloudDeviceReportInventory.ps1");
        using var fixture=ArtifactFixture.Create(File.ReadAllText(source),".psm1");
        var built=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.CloudReportMerge",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true) { TargetFramework=framework });
        Assert.True(built.Succeeded,built.Error+Environment.NewLine+built.BuildOutput);
        var unit=Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,unit=>unit.Name=="Merge-CloudDeviceReportInventory");
        Assert.True(unit.EmittedClrMethod,System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.Single(unit.RegionGraph!.ScriptBlocks);
        const string probe="""
            $primary=@([pscustomobject]@{Name='primary';ManagedDeviceId='INTUNE-1';DeviceId='device-1'})
            $additional=@(
                [pscustomobject]@{Name='duplicate';ManagedDeviceId='intune-1'},
                [pscustomobject]@{Name='new';EntraDeviceObjectId='entra-2';AutopilotDeviceId='auto-2'},
                [pscustomobject]@{Name='duplicate-new';AutopilotDeviceId='AUTO-2'},
                [pscustomobject]@{Name='keyless'},
                [pscustomobject]@{Name='keyless-two'})
            foreach($case in @(
                [ordered]@{PrimaryDevices=@();AdditionalDevices=@()},
                [ordered]@{PrimaryDevices=$primary;AdditionalDevices=@()},
                [ordered]@{PrimaryDevices=@();AdditionalDevices=$additional},
                [ordered]@{PrimaryDevices=$primary;AdditionalDevices=$additional},
                [ordered]@{PrimaryDevices=$primary;AdditionalDevices=$additional})) {
                $records=@(Merge-CloudDeviceReportInventory @case)
                [pscustomobject]@{records=$records;primaryUnchanged=$primary;additionalUnchanged=$additional;
                    helperVisible=[bool](Get-Command Get-CloudDeviceReportInventoryKey -ErrorAction SilentlyContinue)} | ConvertTo-Json -Depth 6 -Compress
            }
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        var generated=RunModuleProof(built.ArtifactPath!,probe,host);
        Assert.True(original==generated,"Original: "+original+Environment.NewLine+"Generated: "+generated);
        Assert.Contains("\"helperVisible\":false",generated);
    }
}
