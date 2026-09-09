using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeOutput_PreservesEnumerationFailuresDisposalAndPartialRecords(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-NativeEnumeration { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); 'before'; $Value; 'after' }
            function Read-NativeCollectedEnumeration { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); $result=@($Value; 'tail'); return ,$result }
            function Read-NativeCapturedEnumeration { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); $result='prior'; $result=for ($i=0;$i -lt 1;$i++) { $Value; 'tail' }; return ,$result }
            function Read-NativeForeachEnumeration { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); 'before'; foreach ($item in $Value) { $item }; 'after' }
            function Read-NativeCapturedForeachEnumeration { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); $result='prior'; $result=foreach ($item in $Value) { $item; 'tail' }; return ,$result }
            function Read-NativeCatchEnumeration { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); try { $Value; 'try-tail' } catch { 'caught' } finally { 'finally' }; 'after' }
            function Read-NativeSingleEnumeration { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); $result='prior'; $result=@($Value); return ,$result }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeEnumeration", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 7, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        VerifyEnumerationFaults(fixture, result.ArtifactPath!, host, new[] {
            "Read-NativeEnumeration", "Read-NativeCollectedEnumeration", "Read-NativeCapturedEnumeration", "Read-NativeCatchEnumeration",
            "Read-NativeSingleEnumeration", "Read-NativeForeachEnumeration", "Read-NativeCapturedForeachEnumeration" });
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CommandOutput_PreservesEnumerationFailuresDisposalAndPartialRecords(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-CommandEnumeration { [CmdletBinding()] param([object]$Value); 'before'; $Value; 'after' }
            function Read-CommandReturnEnumeration { [CmdletBinding()] param([object]$Value); 'before'; return $Value; 'after' }
            function Read-CommandCatchEnumeration { [CmdletBinding()] param([object]$Value); try { $Value; 'try-tail' } catch { 'caught' } finally { 'finally' }; 'after' }
            function Read-CommandCapturedEnumeration { [CmdletBinding()] param([object]$Value); $result=$null; $result=for ($i=0;$i -lt 1;$i++) { $Value; 'tail' }; return ,$result }
            function Read-CommandInnerEnumeration { [CmdletBinding()] param([object]$Value); $Value; 'inner-tail' }
            function Read-CommandCallEnumeration { [CmdletBinding()] param([object]$Value); 'before'; Read-CommandInnerEnumeration -Value $Value; 'after' }
            function Read-CommandNestedEnumeration { [CmdletBinding()] param([object]$Value); 'outer-before'; Read-CommandCallEnumeration -Value $Value; 'outer-after' }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CommandEnumeration", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(7, result.Manifest!.CompiledMethods);
        VerifyEnumerationFaults(fixture, result.ArtifactPath!, host, new[] {
            "Read-CommandEnumeration", "Read-CommandReturnEnumeration", "Read-CommandCatchEnumeration", "Read-CommandCapturedEnumeration",
            "Read-CommandInnerEnumeration", "Read-CommandCallEnumeration", "Read-CommandNestedEnumeration" });
    }

    private static void VerifyEnumerationFaults(ArtifactFixture fixture, string artifactPath, string host, string[] names)
    {
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            using System.Management.Automation;
            public sealed class NativeOutputEnumerable : IEnumerable {
                public static string Trace="";
                public string Failure;
                public int Count;
                public IEnumerator GetEnumerator() {
                    Trace+="get;";
                    Fail("get");
                    return Failure=="null-cursor" ? null : GetCursor();
                }
                public IEnumerator GetCursor() { return new Cursor(this); }
                private void Fail(string stage) {
                    if(Failure==stage) throw new InvalidOperationException(stage+" failed");
                    if(Failure=="runtime-"+stage) throw new RuntimeException(stage+" failed");
                }
                private sealed class Cursor : IEnumerator, IDisposable {
                    private readonly NativeOutputEnumerable owner;
                    private int index=-1;
                    public Cursor(NativeOutputEnumerable owner) { this.owner=owner; }
                    public bool MoveNext() { index++; Trace+="move"+index+";"; if(index==1) owner.Fail("move"); return index<owner.Count; }
                    public object Current { get { Trace+="current"+index+";"; if(index==1) owner.Fail("current"); return index==0 ? (object)7 : new object[] {null,index}; } }
                    public void Reset() { throw new NotSupportedException(); }
                    public void Dispose() { Trace+="dispose;"; owner.Fail("dispose"); }
                }
            }
            '@
            function Describe-Fault($fault) {
                $record=if($fault -is [Management.Automation.ErrorRecord]) { $fault } else { $fault.ErrorRecord }
                $fault.GetType().FullName+':'+$record.FullyQualifiedErrorId+':'+$record.Exception.GetType().FullName+':'+$record.InvocationInfo.ScriptLineNumber+':'+$record.InvocationInfo.OffsetInLine
            }
            foreach ($name in $names) {
                foreach ($kind in 'enumerable','cursor') {
                    foreach ($failure in 'none','get','runtime-get','move','runtime-move','current','runtime-current','dispose','runtime-dispose') {
                        foreach ($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                            $owner=[NativeOutputEnumerable]::new(); $owner.Count=3; $owner.Failure=$failure
                            $value=$owner
                            if ($kind -eq 'cursor') { $value=$owner.GetCursor() }
                            [NativeOutputEnumerable]::Trace=''; $Error.Clear(); $faults=@(); $emitted=@(); $records=@(); $caught=$null
                            try { $records=@(& $name -Value $value -ErrorAction $action -ErrorVariable faults -OutVariable emitted 2>$null) }
                            catch { $caught=Describe-Fault $_ }
                            [pscustomobject]@{name=$name;kind=$kind;failure=$failure;action=$action;records=$records;emitted=@($emitted);
                                trace=[NativeOutputEnumerable]::Trace;caught=$caught;faults=@($faults | ForEach-Object { Describe-Fault $_ });
                                errors=@($Error | ForEach-Object { Describe-Fault $_ })} | ConvertTo-Json -Compress -Depth 10
                        }
                    }
                }
            }
            """;
        var selectNames = "$names=@(" + string.Join(",", names.Select(name => "'" + name + "'")) + "); ";
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + selectNames + probe,
            fixture.RootPath, "original-native-enumeration");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(artifactPath) + "'; " + selectNames + probe,
            fixture.RootPath, "compiled-native-enumeration");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(names.Length * 2 * 9 * 4, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}
