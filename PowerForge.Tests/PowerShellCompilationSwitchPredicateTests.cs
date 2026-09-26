namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void SwitchPredicates_PreserveLocalScopeRecordsIdentityAndRestoration(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-PredicateSwitch {
                [CmdletBinding()] param([object]$Values,[string]$Mode)
                $trace=[Collections.Generic.List[string]]::new()
                $Value='outside'
                $_='before'; $switch='iterator-before'
                try {
                    switch ($Values) {
                        'one' { 'literal:'+ $_; $_='two' }
                        {
                            $script:blocks.Add($MyInvocation.MyCommand.ScriptBlock)
                            $trace.Add('item:'+ $_)
                            $local:Value='inside'
                            $inner='inner'
                            switch($inner) { 'inner' { $trace.Add('nested:'+ $_) } }
                            try {
                                if($Mode -eq 'failure' -and $_ -eq 'failure') { throw 'predicate failure' }
                                $_ -in @('one','two')
                            } finally { $trace.Add('finally') }
                        } { 'predicate:'+ $_ }
                        { if($Mode -eq 'multi') { $false; $false } else { $false } } { 'multi-match' }
                        default { 'default:'+ $_ }
                    }
                } catch { 'error:'+ $_.FullyQualifiedErrorId; $_.Exception.Message }
                finally { $trace.Add('outer-finally') }
                [pscustomobject]@{trace=@($trace);value=$Value;item=$_;iterator=$switch} | ConvertTo-Json -Compress
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.SwitchPredicates", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == "Read-PredicateSwitch");
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.Equal(2, unit.RegionGraph!.ScriptBlocks.Count);
        const string probe = """
            & (Get-Command Read-PredicateSwitch).Module {
                $script:blocks=[Collections.Generic.List[object]]::new()
                foreach($mode in 'normal','multi','failure','normal') {
                    foreach($values in @(@(),@('one'),@('one','other','two'),@('failure','one'),@($null))) {
                        [pscustomobject]@{mode=$mode;values=$values;records=@(Read-PredicateSwitch -Values $values -Mode $mode)} | ConvertTo-Json -Depth 8 -Compress
                    }
                }
                $same=$true
                foreach($block in $script:blocks) { $same=$same -and [object]::ReferenceEquals($script:blocks[0],$block) }
                [pscustomobject]@{constantIdentity=$same;count=$script:blocks.Count} | ConvertTo-Json -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("predicate failure", generated);
        Assert.Contains("\"constantIdentity\":true", generated);
        Assert.Contains("\\\"value\\\":\\\"outside\\\"", generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void SwitchPredicates_QualifyUnchangedOfflineCloudDeviceSelection(string framework, string host)
    {
        var sources = new[] { "Get-CloudDeviceRecordKeys", "Find-ProcessedCloudDeviceRecord",
            "Get-CloudDeviceSelectionReason", "Get-CloudDevicesToProcess" }.Select(name =>
            FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", name + ".ps1")).ToArray();
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, sources.Select(File.ReadAllText)), ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CloudDeviceSelection", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == "Get-CloudDevicesToProcess");
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        const string probe = """
            & (Get-Command Get-CloudDevicesToProcess).Module {
                function script:Write-Color { param($Text,$Color) }
                function script:Get-Date { [datetime]'2020-01-02' }
            }
            $devices=@()
            $states=@('compliant','noncompliant','inGracePeriod','configManager','unknown',$null,$null,$null)
            for($i=0;$i -lt $states.Count;$i++) {
                $devices += [pscustomobject][ordered]@{Name="device-$i";ManagedDeviceId="intune-$i";
                    EntraDeviceObjectId="entra-$i";DeviceId="device-$i";RecordState='Matched';
                    HasIntuneRecord=$true;HasEntraRecord=$true;Enabled=($i % 2 -eq 0);
                    ComplianceState=$states[$i];IsCompliant=if($i -eq 5){$true}elseif($i -eq 6){$false}else{$null};
                    AutopilotOnboarded=$true;AutopilotInventoryLoaded=$true;AutopilotDeviceId="auto-$i";
                    ManagementAgent='mdm';IsManaged=$true;OperatingSystem='Windows';OwnerDisplayName='owner';
                    EntraLastSeenDays=100;IntuneLastSeenDays=100;RegisteredDays=100;
                    PreserveDuplicateNameGroup=($i -eq 3);ManagedDeviceOwnerType=if($i -eq 4){'company'}else{'personal'}}
            }
            foreach($type in 'Retire','Disable','Delete','RemoveAutopilotIdentity','Retire') {
                foreach($state in 'Compliant','NonCompliant','Unknown','Any') {
                    foreach($protect in $false,$true) {
                        $rules=[ordered]@{ComplianceState=$state;IncludeIntuneOnly=$true;IncludeEntraOnly=$true;
                            PreserveDuplicateDeviceNames=$protect;ExcludeCompanyOwned=$protect;ManagementState='Mdm';
                            IncludeManagementAgent=@('m*');ExcludeManagementAgent=@('none');LastSeenIntuneMoreThan=30}
                        $processed=@{'intune:intune-1'=[pscustomobject]@{Action='Retire';ActionStatus=$true}}
                        $records=@(Get-CloudDevicesToProcess -Type $type -Devices $devices -ActionIf $rules -ProcessedDevices $processed)
                        [pscustomobject]@{type=$type;state=$state;protect=$protect;count=$records.Count;records=$records} | ConvertTo-Json -Depth 8 -Compress
                    }
                }
            }
            [pscustomobject]@{inputs=$devices;helperVisible=[bool](Get-Command Get-CloudDeviceComplianceState -ErrorAction SilentlyContinue)} | ConvertTo-Json -Depth 8 -Compress
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("\"count\":2", generated);
        Assert.Contains("\"Action\":\"Retire\"", generated);
        Assert.Contains("\"helperVisible\":false", generated);
    }
}

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Theory]
    [InlineData("trap { continue }; $true")]
    [InlineData("dynamicparam { } end { $true }")]
    [InlineData("param($__writeOutput) $__writeOutput")]
    [InlineData("break")]
    [InlineData("continue")]
    public void SwitchPredicates_RetainOwnerWhenChildCannotCompile(string body)
    {
        var document = PowerShellSourceParser.Parse("function Read-Owner { param([object]$Value) switch($Value) { { " +
            body + " } { 'selected' } } }", TestPath("switch-predicate-fallback.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(result.Emitted.Methods);
        Assert.NotEmpty(result.Emitted.Diagnostics);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void SwitchPredicates_RejectRuntimeFreeDynamicScope(string framework)
    {
        var document = PowerShellSourceParser.Parse("function Read-Owner { param([string]$Value) switch($Value) { " +
            "{ $_ -eq 'one' } { return 'selected' } } }", TestPath("switch-predicate-strict.psm1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, framework,
            PowerShellCompilationCapabilities.TypedExecutable);
        Assert.Empty(result.Emitted.Methods);
        Assert.NotEmpty(result.Emitted.Diagnostics);
    }
}
