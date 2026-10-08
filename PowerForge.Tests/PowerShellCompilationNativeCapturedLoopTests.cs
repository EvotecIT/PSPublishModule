using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCapturedLoops_PreserveRecordsDestinationsAndFinally(string framework, string host)
    {
        const string body = """
            try {
                'pending'
                if($i -eq 1) {
                    if($Mode -like 'break*') { break outer }
                    if($Mode -like 'continue*') { continue outer }
                    if($Mode -eq 'throw') { throw 'failure' }
                }
                'value'
            } finally { 'capture-finally'; if($i -eq 1 -and $Mode -like '*finally') { throw 'finally failure' } }
            """;
        var source = string.Join(Environment.NewLine, new[] {
            ("Direct", "try { BODY } finally { 'direct-finally' }", "$result"),
            ("Array", "@(try { BODY } finally { 'array-finally' })", "$result"),
            ("Nested", "try { $inner = @(try { BODY } finally { 'inner-array-finally' }); 'after-inner'; ,$inner } finally { 'nested-finally' }", "$result"),
            ("Switch", "switch(1){ 1 { BODY } default { 'value' } }", "$result"),
            ("Member", "try { BODY } finally { 'member-finally' }", "$box.Value")
        }.Select(shape => "function Read-Captured" + shape.Item1 + " { [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1,[string]$Mode); " +
            "$result='old'; $box=@{Value='old'}; :outer foreach($i in 1,2) { try { " + shape.Item3 + " = " +
            shape.Item2.Replace("BODY", body, StringComparison.Ordinal) +
            " } catch { 'caught:'+ $_.FullyQualifiedErrorId } finally { 'loop-finally' }; 'after'; ," + shape.Item3 +
            " }; 'done'; ," + shape.Item3 + " }"));
        var forest = File.ReadAllText(FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "ActiveDirectory", "Get-WinADForestDetails.ps1"));
        var authored = System.Management.Automation.Language.Parser.ParseInput(forest, out _, out _);
        var recovery = authored.FindAll(node => node is System.Management.Automation.Language.CatchClauseAst clause &&
            clause.Body.Extent.Text.Contains("Error listing DCs for domain $Domain - $($_.Exception.Message)", StringComparison.Ordinal), true)
            .Cast<System.Management.Automation.Language.CatchClauseAst>().Single().Body.Extent.Text;
        // Keep the real catch body unchanged. Only its outer driver and failing
        // provider are offline; this is excerpt proof, not a full AD workflow.
        source += Environment.NewLine + "function Read-ForestRecovery { [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1); " +
            "foreach($Domain in 'failed.invalid','owned.invalid') { [Array]$AllDC=try { " +
            "if($Domain -eq 'failed.invalid') { throw 'offline provider failure' }; 'owned-dc' } catch " + recovery +
            "; 'after:'+ $Domain; ,$AllDC }; 'done' }";
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CapturedLoops", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(6, built.Manifest!.CompiledMethods);
        const string probe = """
            foreach($name in 'Read-CapturedDirect','Read-CapturedArray','Read-CapturedNested','Read-CapturedSwitch','Read-CapturedMember') {
                foreach($mode in 'none','break','continue','throw','break-finally','continue-finally') {
                    $errors=@()
                    $records=@(& $name -Mode $mode -ErrorVariable errors 2>$null)
                    [pscustomobject]@{name=$name;mode=$mode;records=$records;errors=@($errors | ForEach-Object {$_.FullyQualifiedErrorId})} | ConvertTo-Json -Depth 9 -Compress
                }
            }
            $warnings=@()
            $records=@(Read-ForestRecovery -WarningAction SilentlyContinue -WarningVariable warnings)
            [pscustomobject]@{name='Read-ForestRecovery';records=$records;warnings=@($warnings | ForEach-Object {$_.Message})} | ConvertTo-Json -Depth 9 -Compress
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Equal(31, original.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCapturedLoops_CancelledTransferPreservesLaterEnumerationFailure(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-CancelledTransfer {
                [CmdletBinding()]param([Collections.IEnumerable]$Items,[ValidateRange(1,9)][int]$Seed=1)
                $value='old'
                :outer foreach($i in 1,2) {
                    try {
                        $value=@(try {
                            try { 'before'; continue outer } finally { throw 'cancel transfer' }
                        } catch { 'caught-cancellation'; $Items })
                    } catch { 'caught-enumeration' }
                    'after'; ,$value
                    break
                }
                'done'; ,$value
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CancelledTransfer", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(1, built.Manifest!.CompiledMethods);
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            public sealed class TransferEnumerable : IEnumerable {
                public IEnumerator GetEnumerator() { return new Cursor(); }
                private sealed class Cursor : IEnumerator {
                    private int index=-1;
                    public bool MoveNext() { index++; if(index==1) throw new InvalidOperationException("move failed"); return true; }
                    public object Current { get { return "enumerated"; } }
                    public void Reset() { throw new NotSupportedException(); }
                }
            }
            '@
            $records=@(Read-CancelledTransfer -Items ([TransferEnumerable]::new()))
            ConvertTo-Json -InputObject $records -Depth 9 -Compress
            """;
        Assert.Equal(RunModuleProof(fixture.ScriptPath, probe, host), RunModuleProof(built.ArtifactPath!, probe, host));
    }

    [Theory]
    [InlineData("$result+=if($i -eq 1){continue}")]
    [InlineData("$result=@(try { continue $Label } finally { 'cleanup' })")]
    [InlineData("$result=@(try { continue 2 } finally { 'cleanup' })")]
    [InlineData("switch($i){ 1 {$result=if($i -eq 1){continue}} }")]
    public void NativeCapturedLoops_UnresolvedOrCompoundTransfersRemainHosted(string body)
    {
        using var fixture = ArtifactFixture.Create("function Read-Transfer { param([ValidateRange(1,9)][int]$Seed=1,$Label) foreach($i in 1,2){" + body + "} }", ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(new[] { fixture.ScriptPath },
            "Generated.CaptureGuards", "CompiledPowerShell", "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(result.Methods);
    }
}
