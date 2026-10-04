using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("IEnumerable")]
    [InlineData("IEnumerator")]
    public void EnumerableLoops_RequireAStatementErrorHost(string interfaceName)
    {
        var source = "function Measure-Records { param([Collections." + interfaceName +
            "]$Items) [int]$Count=0; foreach($Item in $Items) { $Count++ }; return $Count }";
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var hosted = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, targetFramework: "net10.0", capabilities: PowerShellCompilationCapabilities.BinaryModule));
        Assert.True(Assert.Single(Assert.Single(hosted.Files).Units).IsCompilable);
        foreach(var capabilities in new[] {
            PowerShellCompilationCapabilities.TypedLibrary,
            PowerShellCompilationCapabilities.TypedExecutable,
            PowerShellCompilationCapabilities.BinaryModule & ~PowerShellCompilationCapability.PowerShellStatementErrors })
        {
            var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
                fixture.ScriptPath, PowerShellCompilationMode.Strict, targetFramework: "net10.0", capabilities: capabilities));
            var unit = Assert.Single(Assert.Single(plan.Files).Units);
            Assert.False(unit.IsCompilable);
            Assert.NotEmpty(unit.Diagnostics);
        }
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void EnumerableLoops_StopOutputFreeEnumerationAndRunAuthoredFinally(string framework, string host)
    {
        var source = string.Join(Environment.NewLine, new[] { "IEnumerable", "IEnumerator" }.Select(type =>
            "function Invoke-" + type + " { [CmdletBinding()] param([Collections." + type +
            "]$Items,[IDisposable]$Cleanup) [int]$Count=0; try { foreach($Item in $Items) { $Count++ } } finally { $Cleanup.Dispose() }; return $Count }"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.EnumerableStopping", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            using System.Threading;
            public sealed class StoppingEnumeration : IEnumerable, IEnumerator, IDisposable {
                public readonly ManualResetEvent Started = new ManualResetEvent(false);
                public bool Cleaned;
                public IEnumerator GetEnumerator() { return this; }
                public bool MoveNext() { Started.Set(); return true; }
                public object Current { get { return 1; } }
                public void Reset() { throw new NotSupportedException(); }
                public void Dispose() { Cleaned=true; }
            }
            '@
            foreach($type in 'IEnumerable','IEnumerator') {
                $inputValue=[StoppingEnumeration]::new()
                $ps=[powershell]::Create()
                try {
                    [void]$ps.AddCommand('Import-Module').AddParameter('Name',$modulePath)
                    [void]$ps.Invoke(); $ps.Commands.Clear()
                    [void]$ps.AddCommand('Invoke-'+$type).AddParameter('Items',$inputValue).AddParameter('Cleanup',$inputValue)
                    $running=$ps.BeginInvoke()
                    if(-not $inputValue.Started.WaitOne(5000)) { throw 'Enumeration did not start.' }
                    $stop=$ps.BeginStop($null,$null)
                    if(-not $stop.AsyncWaitHandle.WaitOne(5000)) { throw 'Enumeration did not stop.' }
                    $ps.EndStop($stop)
                    try { [void]$ps.EndInvoke($running) } catch [Management.Automation.PipelineStoppedException] { }
                    [pscustomobject]@{type=$type;state=[string]$ps.InvocationStateInfo.State;cleaned=$inputValue.Cleaned} | ConvertTo-Json -Compress
                } finally { $ps.Dispose(); $inputValue.Started.Dispose() }
            }
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-enumerable-stop");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-enumerable-stop");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Contains("\"cleaned\":true", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void EnumerableLoops_PreserveAuthoredDictionaryIdentity(string framework, string host)
    {
        const string source = """
            function Invoke-LiteralRecords {
                [CmdletBinding()] param([Collections.Generic.List[object]]$Observed)
                $Items=@{Name='sample'}
                foreach($Item in $Items) { $Observed.Add($Item) }
            }
            function Invoke-AliasRecords {
                [CmdletBinding()] param([Collections.Generic.List[object]]$Observed)
                $Source=@{Name='sample'}
                $Items=$Source
                foreach($Item in $Items) { $Observed.Add($Item) }
            }
            function Invoke-ConvertedRecords {
                [CmdletBinding()] param([Collections.Generic.List[object]]$Observed)
                $Source=@{Name='sample'}
                [Collections.IEnumerable]$Items=$Source
                foreach($Item in $Items) { $Observed.Add($Item) }
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.EnumerableDictionaryIdentity", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        const string probe = """
            foreach($name in 'LiteralRecords','AliasRecords','ConvertedRecords') {
                $observed=[Collections.Generic.List[object]]::new()
                & ('Invoke-'+$name) -Observed $observed -ErrorAction Stop
                [pscustomobject]@{name=$name;count=$observed.Count;types=@($observed | ForEach-Object {$_.GetType().FullName})} | ConvertTo-Json -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-enumerable-dictionary");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-enumerable-dictionary");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void EnumerableLoops_PreservePartialOutputContinuationAndAuthoredCleanup(string framework, string host)
    {
        const string loop = """
            foreach($Item in $Items) {
                $Observed.Add($Item)
                $Count++
                $Count
                if($Flow -eq 'break') { break }
                if($Flow -eq 'return') { return 'returned' }
                if($Flow -eq 'throw') { throw [System.InvalidOperationException]::new('body failed') }
                if($Flow -eq 'continue') { continue }
                'body-tail'
            }
            """;
        var source = string.Join(Environment.NewLine, new[] { "EnumerableRecords", "EnumerableCleanup", "CursorRecords", "EnumerableCapture" }.Select(name =>
            "function Invoke-" + name + " { [CmdletBinding()] param([Collections." +
            (name == "CursorRecords" ? "IEnumerator" : "IEnumerable") + "]$Items," +
            "[Collections.Generic.List[object]]$Observed,[string]$Flow,[System.IDisposable]$Cleanup,[object]$Prior) [int]$Count=0; [object]$Item=$null; 'before'; " +
            (name == "EnumerableCleanup" ? "try { " + loop + " } finally { $Cleanup.Dispose(); 'cleanup-tail' }" :
                name == "EnumerableCapture" ? "$Values=$Prior; $Values=" + loop.Replace("if($Flow -eq 'return') { return 'returned' }", "") + "; $Observed.Add($Values)" : loop) +
            "; $Observed.Add($Item); 'after'; return $Count }"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.EnumerableLoops", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(4, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Management.Automation;
            using System.Management.Automation.Internal;
            public sealed class EnumerationInput : IEnumerable, IDisposable {
                public string Failure;
                public int Count;
                public static string Trace="";
                private Cursor last;
                public IEnumerator GetEnumerator() {
                    Trace+="get;";
                    if(Failure=="get" || Failure=="runtime-get") throw FailureFor("get",Failure);
                    return Failure=="null-cursor" ? null : CreateCursor();
                }
                public IEnumerator CreateCursor() { last=new Cursor(Failure,Count); return last; }
                public void Dispose() { Trace+="owner.dispose;"; if(last!=null) last.Dispose(); Trace+="owner.after;"; }
                private static Exception FailureFor(string stage,string failure) {
                    return failure.StartsWith("runtime-") ? (Exception)new RuntimeException(stage+" failed") : new InvalidOperationException(stage+" failed");
                }
                private sealed class Cursor : IEnumerator, IDisposable {
                    private readonly string failure;
                    private readonly int count;
                    private int index=-1;
                    public Cursor(string failure,int count) { this.failure=failure; this.count=count; }
                    public bool MoveNext() {
                        index++; Trace+="move"+index+";";
                        if((failure=="move" || failure=="runtime-move") && index==1) throw FailureFor("move",failure);
                        return index<count;
                    }
                    public object Current { get {
                        Trace+="current"+index+";";
                        if((failure=="current" || failure=="runtime-current") && index==1) throw FailureFor("current",failure);
                        switch(index) {
                            case 0: return null;
                            case 1: return AutomationNull.Value;
                            case 2: return 7;
                            case 3: return new PSObject((object)7);
                            case 4: return new int[] {1,2};
                            default: return "text";
                        }
                    } }
                    public void Reset() { Trace+="reset;"; throw new NotSupportedException(); }
                    public void Dispose() { Trace+="cursor.dispose;"; if(failure=="dispose") throw new InvalidOperationException("dispose failed"); }
                }
                public static string Describe(object[] values) {
                    var shapes=new List<string>();
                    foreach(var value in values) shapes.Add(Shape(value));
                    return String.Join("|",shapes.ToArray());
                }
                private static string Shape(object value) {
                    if(value==null) return "null";
                    if(Object.ReferenceEquals(value,AutomationNull.Value)) return "AutomationNull";
                    var array=value as Array;
                    if(array!=null) {
                        var shapes=new List<string>();
                        foreach(var item in array) shapes.Add(Shape(item));
                        return value.GetType().FullName+"["+String.Join("|",shapes.ToArray())+"]";
                    }
                    return value.GetType().FullName+":"+value.ToString();
                }
            }
            '@
            function Describe-Record($item) {
                if($item -is [Management.Automation.ErrorRecord]) {
                    [pscustomobject]@{kind='error';id=$item.FullyQualifiedErrorId;type=$item.Exception.GetType().FullName;
                        inner=$(if($null -ne $item.Exception.InnerException){$item.Exception.InnerException.GetType().FullName}else{$null});
                        message=$item.Exception.Message;category=[string]$item.CategoryInfo.Category}
                } elseif($item -is [Management.Automation.IContainsErrorRecord]) {
                    [pscustomobject]@{kind='container';type=$item.GetType().FullName;record=(Describe-Record $item.ErrorRecord)}
                } elseif($item -is [Exception]) {
                    [pscustomobject]@{kind='exception';type=$item.GetType().FullName;message=$item.Message}
                } elseif($null -eq $item) { [pscustomobject]@{kind='null'} }
                else { [pscustomobject]@{kind='value';type=$item.GetType().FullName;value=$item} }
            }
            foreach($name in 'EnumerableRecords','EnumerableCleanup','CursorRecords','EnumerableCapture') {
                foreach($count in 0,1,6) {
                    foreach($failure in 'none','get','move','current','dispose','null-cursor','runtime-get','runtime-move','runtime-current') {
                        foreach($flow in 'normal','break','return','continue','throw','stop') {
                            if($name -eq 'EnumerableCapture' -and $flow -eq 'return') { continue }
                            foreach($action in 'Continue','SilentlyContinue','Stop') {
                                foreach($outerTry in $false,$true) {
                                    if(-not $outerTry -and ($action -eq 'Stop' -or $flow -eq 'throw')) { continue }
                                    $source=[EnumerationInput]::new(); $source.Failure=$failure; $source.Count=$count
                                    $inputValue=$source
                                    if($name -eq 'CursorRecords') { $inputValue=$source.CreateCursor() }
                                    [EnumerationInput]::Trace=''; $Error.Clear(); $faults=@(); $caught=$null
                                    $observed=[Collections.Generic.List[object]]::new()
                                    $records=[Collections.Generic.List[object]]::new()
                                    $invoke={
                                        if($flow -eq 'stop') {
                                            & ('Invoke-'+$name) -Items $inputValue -Observed $observed -Flow normal -Cleanup $source -Prior prior -ErrorAction $action -ErrorVariable faults 2>&1 |
                                                Select-Object -First 2 | ForEach-Object { $records.Add((Describe-Record $_)) }
                                        } else {
                                            & ('Invoke-'+$name) -Items $inputValue -Observed $observed -Flow $flow -Cleanup $source -Prior prior -ErrorAction $action -ErrorVariable faults 2>&1 |
                                                ForEach-Object { $records.Add((Describe-Record $_)) }
                                        }
                                    }
                                    if($outerTry) { try { . $invoke } catch { $caught=Describe-Record $_ } } else { . $invoke }
                                    $errors=@($Error | ForEach-Object { Describe-Record $_ })
                                    $variable=@($faults | ForEach-Object { Describe-Record $_ })
                                    [pscustomobject]@{name=$name;count=$count;failure=$failure;flow=$flow;action=$action;outerTry=$outerTry;
                                        records=$records.ToArray();observed=[EnumerationInput]::Describe($observed.ToArray());caught=$caught;
                                        trace=[EnumerationInput]::Trace;errors=$errors;variable=$variable} | ConvertTo-Json -Depth 12 -Compress
                                }
                            }
                        }
                    }
                }
            }
            foreach($value in @($null,'',[string]'text',@{a=1},[object[]]@(1,2))) {
                $observed=[Collections.Generic.List[object]]::new()
                $records=@(Invoke-EnumerableRecords -Items $value -Observed $observed -Flow normal -ErrorAction Stop)
                [pscustomobject]@{special=$true;records=$records;observed=[EnumerationInput]::Describe($observed.ToArray())} | ConvertTo-Json -Depth 6 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-enumerable-loops");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-enumerable-loops");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(2894, original.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        var expectedRows = original.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var actualRows = compiled.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expectedRows.Length, actualRows.Length);
        for(var index = 0; index < expectedRows.Length; index++)
            Assert.True(expectedRows[index] == actualRows[index], "Row " + index + ":\nOriginal: " + expectedRows[index] + "\nCompiled: " + actualRows[index]);
    }
}
