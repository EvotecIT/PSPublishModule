namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void TryArrayCapture_RemainsRejectedWithoutNativeHost(string framework)
    {
        using var fixture = ArtifactFixture.Create("function Read-Capture { param() $value=@(try {'value'} catch {'caught'}); $value }");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Strict, targetFramework: framework,
            capabilities: PowerShellCompilationCapabilities.TypedExecutable));
        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void TryArrayCapture_PreservesHandledRecordsRollbackAndReturn(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-TryRecords {
                [CmdletBinding()] param([int]$Case)
                $value=@(try { if($Case -gt 0) { 'before' }; if($Case -eq 2) { throw 'failure' }; if($Case -eq 3) { $null }; 'after' }
                    catch { 'caught'; $_.Exception.Message } finally { 'finally' })
                return ,$value
            }
            function Read-TryRollback {
                [CmdletBinding()] param()
                $value='previous'
                try { $value=@(try { 'pending'; throw 'failure' } finally { 'pending-finally' }) }
                catch { 'outer-caught' }
                $value
            }
            function Read-TryReturn {
                [CmdletBinding()] param()
                $value=@(try { 'pending'; return 'returned' } finally { 'finally' })
                'unreachable'; $value
            }
            function Read-TryConversion {
                [CmdletBinding()] param([object]$Value)
                [int[]]$items=7
                try { $items=@(try { $Value } finally { 8 }) }
                catch { $_.FullyQualifiedErrorId; $_.Exception.GetType().FullName }
                ,$items
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TryArrayCapture",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(4, built.Manifest!.CompiledMethods);
        Assert.All(built.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.EmittedClrMethod),
            unit => Assert.True(unit.UsesNativeFunctionBinding));
        const string probe = """
            foreach($case in 0,1,2,3,1) {
                [pscustomobject]@{case=$case;records=@(Read-TryRecords -Case $case)} | ConvertTo-Json -Depth 6 -Compress
            }
            foreach($name in 'Read-TryRollback','Read-TryReturn') {
                [pscustomobject]@{name=$name;records=@(& $name)} | ConvertTo-Json -Depth 6 -Compress
            }
            foreach($value in 1,'bad',2) {
                [pscustomobject]@{value=$value;records=@(Read-TryConversion -Value $value)} | ConvertTo-Json -Depth 6 -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("previous", generated);
        Assert.DoesNotContain("unreachable", generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void TryArrayCapture_PreservesHostedBoundaryForEscapingLoopTransfer(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-TryBreak { param() foreach($item in 1,2) { $value=@(try { 'pending'; break } finally { 'finally' }); 'unreachable' }; 'done' }
            function Read-TryContinue { param() foreach($item in 1,2) { $value=@(try { 'pending'; continue } finally { 'finally' }); 'unreachable' }; 'done' }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TryCaptureTransfer",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.All(built.Manifest!.UnitDispositionLedger!.Entries.Where(unit => unit.Kind == PowerShellCompilationUnitKind.Function), unit =>
        {
            Assert.False(unit.EmittedClrMethod);
            Assert.True(unit.RetainedHostedSource);
            Assert.Contains(unit.DiagnosticChain, cause => cause.Message.Contains("enclosing-control-flow", StringComparison.Ordinal));
        });
        const string probe = "foreach($name in 'Read-TryBreak','Read-TryContinue') { [pscustomobject]@{name=$name;records=@(& $name)} | ConvertTo-Json -Compress }";
        Assert.Equal(RunModuleProof(fixture.ScriptPath, probe, host), RunModuleProof(built.ArtifactPath!, probe, host));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void TryArrayCapture_QualifiesUnchangedSqlMetadataWithOfflineProvider(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Deprecated", "SQL", "Get-SqlQueryColumnInformation.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.SqlMetadataCapture",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == "Get-SqlQueryColumnInformation");
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.True(unit.UsesNativeFunctionBinding);
        const string probe = """
            & (Get-Command Get-SqlQueryColumnInformation).Module {
                function Invoke-DbaQuery {
                    [CmdletBinding()] param([string]$SqlInstance,[string]$Query)
                    $script:trace=@($SqlInstance,$Query)
                    switch($SqlInstance) {
                        'zero' { return }
                        'one' { [pscustomobject]@{COLUMN_NAME='name';ORDINAL_POSITION=1}; return }
                        'many' { [pscustomobject]@{COLUMN_NAME='name';ORDINAL_POSITION=1}; [pscustomobject]@{COLUMN_NAME='東京';ORDINAL_POSITION=2}; return }
                        'null' { $null; return }
                        'failure' { 'partial'; throw "offline`r`nprovider failure" }
                    }
                }
                foreach($case in 'zero','one','many','null','failure','one') {
                    $records=@(Get-SqlQueryColumnInformation -SqlServer $case -SqlDatabase '[offline]' -Table '[dbo.columns]')
                    [pscustomobject]@{case=$case;trace=$script:trace;records=$records} | ConvertTo-Json -Depth 6 -Compress
                }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("offline  provider failure", generated);
        Assert.Contains("INFORMATION_SCHEMA.COLUMNS", generated);
    }
}
