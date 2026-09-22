using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void LoopStopping_PreservesInterruptBoundariesAndFinally(string framework, string host)
    {
        var loops = new (string Name, string Loop)[] {
            ("While", "while($i -lt $Limit) { $i++; BODY }"),
            ("DoWhile", "do { $i++; BODY } while($i -lt $Limit)"),
            ("DoUntil", "do { $i++; BODY } until($i -ge $Limit)"),
            ("For", "for($i=0; $i -lt $Limit; $i++) { BODY }"),
            ("TypedArray", "foreach($item in $Items) { BODY }"),
            ("SystemArray", "foreach($item in $Array) { BODY }"),
            ("ScalarString", "foreach($item in $Text) { BODY }"),
            ("Enumerable", "foreach($item in $Enumerable) { BODY }"),
            ("Cursor", "foreach($item in $Cursor) { BODY }"),
        };
        const string pause = "$null=$Started.Set(); $null=$Release.WaitOne()";
        const string body = "$Observed.Add(1); if(-not $PauseBefore -and -not $paused) { $paused=$true; " + pause + " }; if($UseContinue) { continue }";
        var source = string.Join(Environment.NewLine, new[] { false, true }.SelectMany(capture => loops.Select(loop =>
            "function Invoke-" + (capture ? "Capture" : "") + loop.Name + " { [CmdletBinding()] param([Threading.ManualResetEvent]$Started," +
            "[Threading.ManualResetEvent]$Release,[Collections.Generic.List[object]]$Observed," +
            "[int[]]$Items,[Array]$Array,[string]$Text,[Collections.IEnumerable]$Enumerable,[Collections.IEnumerator]$Cursor," +
            "[bool]$PauseBefore,[bool]$UseContinue,[int]$Limit) " +
            "[int]$i=0; $paused=$false; $collected=[object]'prior'; if($PauseBefore) { " + pause + " }; try { " +
            (capture ? "$collected=" : "") + loop.Loop.Replace("BODY", (capture ? "7; " : "") + body) +
            "; $Observed.Add(200) } catch [InvalidOperationException] { $Observed.Add(300) } finally { " +
            (capture ? "$Observed.Add($collected); " : "") +
            "[int]$cleanup=0; while($cleanup -lt 2) { $Observed.Add(100); $cleanup++; if($cleanup -eq 1) { continue } }; " +
            "try { foreach($cleanupItem in $Items) { $Observed.Add(101); break } } finally { $Observed.Add(102) } } }")));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.LoopStopping", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(loops.Length * 2, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            public sealed class LoopStopEnumeration : IEnumerable, IEnumerator {
                public int Acquisitions;
                public int Moves;
                public int Reads;
                public int Limit;
                public bool FailMove;
                public IEnumerator GetEnumerator() { Acquisitions++; return this; }
                public bool MoveNext() { Moves++; if(FailMove) throw new InvalidOperationException("move failed"); return Moves <= Limit; }
                public object Current { get { Reads++; return Moves; } }
                public void Reset() { throw new NotSupportedException(); }
            }
            '@
            foreach($name in 'While','DoWhile','DoUntil','For','TypedArray','SystemArray','ScalarString','Enumerable','Cursor','CaptureWhile','CaptureDoWhile','CaptureDoUntil','CaptureFor','CaptureTypedArray','CaptureSystemArray','CaptureScalarString','CaptureEnumerable','CaptureCursor') {
                foreach($pauseBefore in $true,$false) {
                    foreach($useContinue in $true,$false) {
                      foreach($shape in 'NonEmpty','Empty','Null','MoveFailure') {
                        if($shape -ne 'NonEmpty' -and (-not $pauseBefore -or $name.StartsWith('Capture'))) { continue }
                        if($shape -eq 'MoveFailure' -and $name -notin 'Enumerable','Cursor') { continue }
                        $started=[Threading.ManualResetEvent]::new($false)
                        $release=[Threading.ManualResetEvent]::new($false)
                        $observed=[Collections.Generic.List[object]]::new()
                        $items=if($shape -eq 'Null') { $null } elseif($shape -eq 'Empty') { ,([int[]]@()) } else { ,([int[]]@(1,2)) }
                        $array=if($shape -eq 'Null') { $null } elseif($shape -eq 'Empty') { ,([object[]]@()) } else { ,([object[]]@(1,2)) }
                        $limit=if($shape -eq 'NonEmpty') { 2 } else { 0 }
                        $enumeration=[LoopStopEnumeration]::new()
                        $enumeration.Limit=$limit
                        $enumeration.FailMove=$shape -eq 'MoveFailure'
                        $enumerable=if($shape -eq 'Null') { $null } else { ,$enumeration }
                        $ps=[powershell]::Create()
                        try {
                            [void]$ps.AddCommand('Import-Module').AddParameter('Name',$modulePath)
                            [void]$ps.Invoke(); $ps.Commands.Clear()
                            [void]$ps.AddCommand('Invoke-'+$name).AddParameter('Started',$started).AddParameter('Release',$release).AddParameter('Observed',$observed).AddParameter('Items',$items).AddParameter('Array',$array).AddParameter('Text','').AddParameter('Enumerable',$enumerable).AddParameter('Cursor',$enumerable).AddParameter('PauseBefore',$pauseBefore).AddParameter('UseContinue',$useContinue).AddParameter('Limit',$limit)
                            $running=$ps.BeginInvoke()
                            if(-not $started.WaitOne(5000)) { throw "The loop did not reach its pause: $name/$shape. $($ps.Streams.Error)" }
                            # BeginStop sets the public state before the asynchronous stop worker marks
                            # the engine. Observe the flag used by native loop interrupt checks before
                            # releasing either implementation; Stopping alone races that worker.
                            $flags=[Reflection.BindingFlags]'Instance,Public,NonPublic'
                            $context=$ps.Runspace.GetType().GetProperty('ExecutionContext',$flags).GetValue($ps.Runspace,$null)
                            $stopping=$context.GetType().GetProperty('CurrentPipelineStopping',$flags)
                            if($null -eq $stopping) { throw 'The host does not expose the loop stopping contract.' }
                            $stop=$ps.BeginStop($null,$null)
                            $deadline=[DateTime]::UtcNow.AddSeconds(5)
                            while(-not $stopping.GetValue($context,$null) -and [DateTime]::UtcNow -lt $deadline) { [Threading.Thread]::Sleep(1) }
                            if(-not $stopping.GetValue($context,$null)) { throw 'The engine did not acknowledge the stop request.' }
                            [void]$release.Set()
                            if(-not $stop.AsyncWaitHandle.WaitOne(5000)) { throw 'The finite loop did not stop.' }
                            $ps.EndStop($stop)
                            try { [void]$ps.EndInvoke($running) } catch [Management.Automation.PipelineStoppedException] {}
                            [pscustomobject]@{name=$name;before=$pauseBefore;continued=$useContinue;shape=$shape;state=[string]$ps.InvocationStateInfo.State;observed=$observed.ToArray();acquires=$enumeration.Acquisitions;moves=$enumeration.Moves;reads=$enumeration.Reads} | ConvertTo-Json -Compress
                        } finally {
                            [void]$release.Set()
                            $ps.Dispose(); $started.Dispose(); $release.Dispose()
                        }
                      }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-loop-stopping");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-loop-stopping");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Contains("\"observed\":[100,100,101,102]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"observed\":[1,100,100,101,102]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"observed\":[200,100,100,102]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"observed\":[1,\"prior\",100,100,101,102]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"observed\":[300,100,100,101,102]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"moves\":1,\"reads\":0", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"moves\":2,\"reads\":1", original.StandardOutput, StringComparison.Ordinal);
        var originalRows = original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var compiledRows = compiled.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(originalRows.Length, compiledRows.Length);
        for (var index = 0; index < originalRows.Length; index++)
            Assert.True(originalRows[index] == compiledRows[index], "Original: " + originalRows[index] + Environment.NewLine + "Compiled: " + compiledRows[index]);
    }
}
