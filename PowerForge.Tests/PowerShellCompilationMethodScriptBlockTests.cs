namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void MethodScriptBlocks_QualifyUnchangedSmbProjectionWithOfflineProvider(string framework, string host)
    {
        var source=FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Computers", "Get-ComputerSMB.ps1");
        using var fixture=ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var built=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.SmbSplitProjection",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true) { TargetFramework=framework });
        Assert.True(built.Succeeded,built.Error+Environment.NewLine+built.BuildOutput);
        var unit=Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,unit=>unit.Name=="Get-ComputerSMB");
        Assert.True(unit.EmittedClrMethod,System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.Single(unit.RegionGraph!.ScriptBlocks);
        const string probe="""
            & (Get-Command Get-ComputerSMB).Module {
                function Get-SmbServerConfiguration {
                    [CmdletBinding()] param([object[]]$CimSession)
                    $script:trace.Add(($CimSession -join ','))
                    if($CimSession -contains 'failure') { throw 'offline provider failure' }
                    $names=if($CimSession) { $CimSession } else { @($env:COMPUTERNAME) }
                    foreach($name in $names) { [pscustomobject]@{PSComputerName=$name;AnnounceComment='offline';EnableSMB1Protocol=$false;EnableSMB2Protocol=$true;MaxThreadsPerQueue=3} }
                }
                foreach($case in @(@(),@($env:COMPUTERNAME),@('remote-one','remote-two'),@($env:COMPUTERNAME,'remote-one'),@('failure'),@($env:COMPUTERNAME))) {
                    $script:trace=[Collections.Generic.List[string]]::new()
                    try {
                        $records=@(Get-ComputerSMB -ComputerName $case -ErrorAction Stop)
                        [pscustomobject]@{input=$case;trace=@($script:trace);records=$records} | ConvertTo-Json -Depth 7 -Compress
                    } catch { [pscustomobject]@{input=$case;trace=@($script:trace);error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message} | ConvertTo-Json -Depth 7 -Compress }
                }
            }
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        var generated=RunModuleProof(built.ArtifactPath!,probe,host);
        Assert.True(original==generated,"Original: "+original+Environment.NewLine+"Generated: "+generated);
        Assert.Contains("offline provider failure",generated);
        Assert.Contains("\"EnableSMB2Protocol\":true",generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void MethodScriptBlocks_QualifyUnchangedOfflineFileSelection(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSScriptTools", "FileSelection", "get-lastModified.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var folder = Path.Combine(fixture.RootPath, "files");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        foreach(var item in new[] { ("old.txt", "2020-01-01"), ("recent.txt", "2020-01-03"), ("other.bin", "2020-01-03"), ("nested/東京.txt", "2020-01-03") })
        {
            var path=Path.Combine(folder,item.Item1);
            File.WriteAllText(path, "owned file");
            File.SetLastWriteTime(path, DateTime.Parse(item.Item2, System.Globalization.CultureInfo.InvariantCulture));
        }
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.RecentFileSelection", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == "Get-LastModifiedFile");
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.Single(unit.RegionGraph!.ScriptBlocks);
        const string probe = """
            [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
            & (Get-Command Get-LastModifiedFile).Module {
                param($path)
                function Get-Date { [datetime]'2020-01-04' }
                foreach($case in @(
                    [ordered]@{Filter='*.txt';Interval='Days';IntervalCount=2},
                    [ordered]@{Filter='*.txt';Interval='Days';IntervalCount=2;Recurse=$true},
                    [ordered]@{Filter='*.txt';Interval='Hours';IntervalCount=1},
                    [ordered]@{Filter='*';Interval='Years';IntervalCount=1},
                    [ordered]@{Filter='*.txt';Interval='Days';IntervalCount=2})) {
                    $records=@(Get-LastModifiedFile -Path $path @case | Sort-Object FullName | ForEach-Object {
                        [pscustomobject]@{name=$_.Name;type=$_.GetType().FullName;relative=$_.FullName.Substring($path.Length);when=$_.LastWriteTime.ToString('o')}
                    })
                    [pscustomobject]@{case=$case;records=$records} | ConvertTo-Json -Depth 6 -Compress
                }
                try { Get-LastModifiedFile -Path (Join-Path $path 'missing') -ErrorAction Stop }
                catch { [pscustomobject]@{error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName} | ConvertTo-Json -Compress }
                try { Get-LastModifiedFile -Path $path -IntervalCount 0 -ErrorAction Stop }
                catch { [pscustomobject]@{error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName} | ConvertTo-Json -Compress }
            } '__PATH__'
            """;
        var command=probe.Replace("__PATH__", EscapeStatementErrorPath(folder), StringComparison.Ordinal);
        var transport = "$proof=@(& { " + command + " }); [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($proof -join \"`n\")))";
        var original=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(RunModuleProof(fixture.ScriptPath,transport,host)));
        var generated=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(RunModuleProof(built.ArtifactPath!,transport,host)));
        Assert.True(original==generated, "Original: "+original+Environment.NewLine+"Generated: "+generated);
        Assert.Contains("recent.txt",generated);
        Assert.Contains("東京.txt",generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void MethodScriptBlocks_PreserveDynamicScopeArgumentsSplitAndFailure(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Filtered {
                [CmdletBinding()] param([object[]]$Values,[int]$Threshold,[string]$Mode)
                $trace=[Collections.Generic.List[string]]::new()
                $selected=$Values.Where({
                    $trace.Add([string]$_)
                    try { if($Mode -eq 'failure' -and $_ -eq 2) { throw 'predicate failed' }; return $_ -gt $Threshold }
                    finally { $trace.Add('finally') }
                },'Split')
                [pscustomobject]@{matched=@($selected[0]);other=@($selected[1]);trace=@($trace);threshold=$Threshold}
            }
            function Find-Index {
                [CmdletBinding()] param([string]$Target)
                $items=[Collections.Generic.List[string]]::new()
                $items.Add('one');$items.Add('two');$items.Add('three')
                $items.FindIndex({ $args[0] -eq $Target })
                $Target='three'
                $items.FindIndex({ $args[0] -eq $Target })
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.MethodScriptBlocks", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach(var name in new[] { "Read-Filtered", "Find-Index" })
        {
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.NotEmpty(unit.RegionGraph!.ScriptBlocks);
        }
        const string probe = """
            foreach($values in @(@(),@(1),@(1,2,3,4))) {
                foreach($threshold in 0,2,5) {
                    Read-Filtered -Values $values -Threshold $threshold -Mode normal | ConvertTo-Json -Depth 6 -Compress
                }
            }
            try { Read-Filtered -Values @(1,2,3) -Threshold 1 -Mode failure -ErrorAction Stop }
            catch { [pscustomobject]@{error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message} | ConvertTo-Json -Compress }
            Read-Filtered -Values @(1,2,3) -Threshold 1 -Mode normal | ConvertTo-Json -Depth 6 -Compress
            foreach($target in 'one','missing','two') { @(Find-Index -Target $target) | ConvertTo-Json -Compress }
            """;
        Assert.Equal(RunModuleProof(fixture.ScriptPath, probe, host), RunModuleProof(built.ArtifactPath!, probe, host));
    }
}
