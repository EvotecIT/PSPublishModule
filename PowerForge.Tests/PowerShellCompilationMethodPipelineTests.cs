using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void MethodPipelines_PreserveProducerEnumerationStreamsAndCleanup(string framework, string host)
    {
        var source = string.Join(Environment.NewLine, new[] {
            ("Direct", "$Value.GetItems() | ForEach-Object { $_; Write-Warning 'item' }"),
            ("Parenthesized", "($Value.GetItems()) | ForEach-Object { $_; Write-Warning 'item' }"),
            ("Literal", "$result=[pscustomobject]@{Items=$Value.GetItems() | ForEach-Object { $_; Write-Warning 'item' }}; ,$result.Items"),
            ("Return", "return $Value.GetItems() | ForEach-Object { $_; Write-Warning 'item' }")
        }.Select(shape => "function Read-Method" + shape.Item1 + " { [CmdletBinding()]param([object]$Value); try { " +
            shape.Item2 + "; 'after' } catch { 'caught:'+ $_.FullyQualifiedErrorId } finally { 'finally' }; 'done' }"));
        source += Environment.NewLine + """
            function Read-MethodUncaught { [CmdletBinding()]param([object]$Value); return $Value.GetItems() | ForEach-Object { $_; Write-Warning 'item' } }
            function Read-MethodFinally { [CmdletBinding()]param([object]$Value); try { return $Value.GetItems() | ForEach-Object { $_; Write-Warning 'item' } } finally { 'finally' } }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.MethodPipelines", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(6, built.Manifest!.CompiledMethods);
        Assert.All(built.Manifest.UnitDispositionLedger!.Entries, entry => {
            Assert.True(entry.EmittedClrMethod);
            Assert.True(entry.UsesNativeFunctionBinding);
            Assert.False(entry.RetainedHostedSource);
        });
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            public sealed class MethodPipelineValue {
                public static string Trace="";
                public int Count;
                public string Failure;
                public IEnumerable GetItems() {
                    Trace+="method;";
                    if(Failure=="method") throw new InvalidOperationException("method failed");
                    return new Items(this);
                }
                private sealed class Items : IEnumerable {
                    private readonly MethodPipelineValue owner;
                    public Items(MethodPipelineValue value) { owner=value; }
                    public IEnumerator GetEnumerator() {
                        Trace+="get;";
                        if(owner.Failure=="get") throw new InvalidOperationException("get failed");
                        return new Cursor(owner);
                    }
                }
                private sealed class Cursor : IEnumerator, IDisposable {
                    private readonly MethodPipelineValue owner;
                    private int index=-1;
                    public Cursor(MethodPipelineValue value) { owner=value; }
                    public bool MoveNext() {
                        index++; Trace+="move"+index+";";
                        if(owner.Failure=="move" && index==1) throw new InvalidOperationException("move failed");
                        return index<owner.Count;
                    }
                    public object Current { get {
                        Trace+="current"+index+";";
                        if(owner.Failure=="current" && index==1) throw new InvalidOperationException("current failed");
                        return index==0 ? (object)"first" : new object[] {null,"nested"};
                    } }
                    public void Reset() { throw new NotSupportedException(); }
                    public void Dispose() { Trace+="dispose;"; if(owner.Failure=="dispose") throw new InvalidOperationException("dispose failed"); }
                }
            }
            '@
            foreach($name in 'Read-MethodDirect','Read-MethodParenthesized','Read-MethodLiteral','Read-MethodReturn','Read-MethodUncaught','Read-MethodFinally') {
                foreach($case in 'empty','one','two','null','method','get','move','current','dispose') {
                    $value=[MethodPipelineValue]::new(); $value.Count=2; $value.Failure=$case
                    if($case -eq 'empty') {$value.Count=0}
                    if($case -eq 'one') {$value.Count=1}
                    if($case -eq 'null') {$value=$null}
                    foreach($action in 'Continue','Stop') {
                        [MethodPipelineValue]::Trace=''; $errors=@(); $warnings=@()
                        $outer=$null; $records=@()
                        try { $records=@(& $name -Value $value -ErrorAction $action -ErrorVariable errors -WarningAction SilentlyContinue -WarningVariable warnings 2>$null) }
                        catch { $outer=$_.FullyQualifiedErrorId }
                        [pscustomobject]@{name=$name;case=$case;action=$action;records=$records;trace=[MethodPipelineValue]::Trace;
                            outer=$outer;errors=@($errors | ForEach-Object {$_.FullyQualifiedErrorId});warnings=@($warnings | ForEach-Object {$_.Message})} | ConvertTo-Json -Depth 9 -Compress
                    }
                }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        Assert.Equal(original, RunModuleProof(built.ArtifactPath!, probe, host));
        Assert.Equal(108, original.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
