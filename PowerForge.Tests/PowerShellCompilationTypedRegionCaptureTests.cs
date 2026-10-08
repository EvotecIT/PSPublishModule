namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void TypedRegionCapture_PreservesAuthoredErrorLocations(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-RegionLocation {
                [CmdletBinding()] param([int]$Count, [switch]$Flag)
                Get-RegionLocationRecord -Count $Count
                'after'
            }
            function Read-RegionGroupedLocation {
                [CmdletBinding()] param([int]$Count, [switch]$Flag)
                if($Flag) { Get-RegionLocationRecord -Count $Count }
                Get-RegionLocationRecord -Count $Count
                return $Count
            }
            function Read-RegionAssignedLocation {
                [CmdletBinding()] param([int]$Count, [switch]$Flag)
                [object]$value = Get-RegionLocationRecord -Count $Count
                return $value
            }
            """, ".psm1");
        const string probe = """
            function global:Get-RegionLocationRecord { [CmdletBinding()] param([int]$Count) Write-Error 'region-location' }
            foreach($name in 'Read-RegionLocation','Read-RegionGroupedLocation','Read-RegionAssignedLocation') {
                $records=@(& $name -Count 2 -Flag 2>&1)
                foreach($record in $records) {
                    if($record -is [System.Management.Automation.ErrorRecord]) {
                        [pscustomobject]@{error=$record.FullyQualifiedErrorId;file=[IO.Path]::GetFileName($record.InvocationInfo.ScriptName);line=$record.InvocationInfo.ScriptLineNumber;column=$record.InvocationInfo.OffsetInLine;text=$record.InvocationInfo.Line.Trim()} | ConvertTo-Json -Compress
                    } else { $record }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "typed-region-location-original");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(6, original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TypedRegionLocation", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.EmittedClrMethod),
            unit => Assert.False(unit.UsesNativeFunctionBinding));
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "typed-region-location-compiled");
        Assert.True(original == compiled, "Original: " + original.StandardOutput + original.StandardError + Environment.NewLine +
            "Compiled: " + compiled.StandardOutput + compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void TypedRegionCapture_PreservesErrorsAndSynchronousOutput(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Region { [CmdletBinding()] param([int]$Mode) Get-RegionCaptureRecords -Mode $Mode; 'tail' }
            function Read-RegionCaptured { [CmdletBinding()] param([int]$Mode) return ,(Read-Region -Mode $Mode) }
            """, ".psm1");
        const string probe = """
            function global:Get-RegionCaptureRecords {
                [CmdletBinding()] param([int]$Mode)
                1
                if($Mode -eq 1) { Write-Error 'region-error' }
                if($Mode -eq 2) { throw 'region-throw' }
                $global:RegionProducerContinued=$true
                2
            }
            foreach($name in 'Read-Region','Read-RegionCaptured') {
                foreach($mode in 0..2) {
                    foreach($preference in 'Continue','SilentlyContinue','Ignore','Stop') {
                        $global:RegionProducerContinued=$false
                        $records=@(try { & $name -Mode $mode -ErrorAction $preference 2>&1 } catch { 'outer-caught' })
                        $normalized=@(foreach($record in $records) {
                            if($record -is [System.Management.Automation.ErrorRecord]) {
                                [pscustomobject]@{error=$record.FullyQualifiedErrorId;category=$record.CategoryInfo.Category.ToString()}
                            } else { $record }
                        })
                        [pscustomobject]@{name=$name;mode=$mode;preference=$preference;records=$normalized;continued=$global:RegionProducerContinued} | ConvertTo-Json -Depth 10 -Compress
                    }
                }
            }
            $global:RegionProducerContinued=$false
            $first=@(Read-Region -Mode 0 | Select-Object -First 1)
            [pscustomobject]@{first=$first;continued=$global:RegionProducerContinued} | ConvertTo-Json -Compress
            foreach($name in 'Read-Region','Read-RegionCaptured') {
                $savedOutput=$null; $savedErrors=$null
                $records=@(& $name -Mode 1 -ErrorAction SilentlyContinue -OutVariable savedOutput -ErrorVariable savedErrors 2>$null)
                [pscustomobject]@{name=$name;records=$records;saved=@($savedOutput);errors=@($savedErrors | ForEach-Object FullyQualifiedErrorId)} | ConvertTo-Json -Depth 10 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "typed-region-errors-original");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(27, original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TypedRegionErrors", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.EmittedClrMethod),
            unit => Assert.False(unit.UsesNativeFunctionBinding));
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "typed-region-errors-compiled");
        Assert.True(original == compiled, "Original: " + original.StandardOutput + original.StandardError + Environment.NewLine +
            "Compiled: " + compiled.StandardOutput + compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void TypedRegionCapture_PreservesHostedAndTypedOutputRecords(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Region { [CmdletBinding()] param([int]$Count) 10; Get-RegionCaptureRecords -Count $Count; 20 }
            function Read-RegionCaptured { [CmdletBinding()] param([int]$Count) return ,(Read-Region -Count $Count) }
            function Read-RegionDirect { [CmdletBinding()] param([int]$Count) Read-Region -Count $Count; 'after' }
            """, ".psm1");
        const string probe = """
            function global:Get-RegionCaptureRecords {
                [CmdletBinding()] param([int]$Count)
                if($Count -eq 0) { return }
                if($Count -eq 1) { return $null }
                if($Count -eq 2) { return 7 }
                if($Count -eq 3) { return @(7,8) }
                return ,@(7,8)
            }
            foreach($name in 'Read-Region','Read-RegionCaptured','Read-RegionDirect') {
                foreach($count in 0..4) {
                    $records=@(& $name -Count $count)
                    [pscustomobject]@{name=$name;count=$count;records=$records} | ConvertTo-Json -Depth 10 -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "typed-region-capture-original");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(15, original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TypedRegionCapture", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.EmittedClrMethod),
            unit => Assert.False(unit.UsesNativeFunctionBinding));
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "typed-region-capture-compiled");
        Assert.True(original == compiled, "Original: " + original.StandardOutput + original.StandardError + Environment.NewLine +
            "Compiled: " + compiled.StandardOutput + compiled.StandardError);
    }
}
