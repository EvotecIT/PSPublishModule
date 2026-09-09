namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void TypedLocalCapture_PreservesFailureContinuationAndPreviousAssignment(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Failing { [CmdletBinding()] param([bool]$Fail) 1; if($Fail) { [int]::Parse('bad') }; 2 }
            function Read-Resuming { [CmdletBinding()] param([bool]$Fail) [object]$value='before'; $value=(Read-Failing -Fail $Fail); 'after'; $value }
            function Read-Handled { [CmdletBinding()] param([bool]$Fail) [object]$value='before'; try { $value=(Read-Failing -Fail $Fail) } catch { 'caught' }; 'after'; $value }
            function Read-Nested { [CmdletBinding()] param([bool]$Fail) return Read-Failing -Fail $Fail }
            function Read-DeepCaptured { [CmdletBinding()] param([bool]$Fail) return ,(Read-Nested -Fail $Fail) }
            """, ".psm1");
        const string probe = """
            foreach($name in 'Read-Resuming','Read-Handled','Read-DeepCaptured') {
                foreach($preference in 'Continue','SilentlyContinue','Ignore','Stop') {
                    foreach($fail in $false,$true) {
                        $records=@(try { & $name -Fail $fail -ErrorAction $preference 2>&1 } catch { 'outer-caught' })
                        $normalized=@(foreach($record in $records) {
                            if($record -is [System.Management.Automation.ErrorRecord]) {
                                [pscustomobject]@{error=$record.FullyQualifiedErrorId;category=$record.CategoryInfo.Category.ToString()}
                            } else { $record }
                        })
                        [pscustomobject]@{name=$name;preference=$preference;fail=$fail;records=$normalized} | ConvertTo-Json -Depth 10 -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "typed-local-capture-continuation-original");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(24, original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TypedLocalCaptureContinuation", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(5, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.EmittedClrMethod),
            unit => Assert.False(unit.UsesNativeFunctionBinding));
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "typed-local-capture-continuation-compiled");
        Assert.True(original == compiled, "Original: " + original.StandardOutput + original.StandardError + Environment.NewLine +
            "Compiled: " + compiled.StandardOutput + compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void TypedLocalCapture_PreservesRecordShapeAcrossCallPositions(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Values {
                [CmdletBinding()] param([int]$Count)
                if($Count -eq 0) { return }
                if($Count -eq 1) { return $null }
                if($Count -eq 2) { return 7 }
                if($Count -eq 3) { return @(7,8) }
                return ,@(7,8)
            }
            function Receive-Value { [CmdletBinding()] param([object]$Value) return ,$Value }
            function Read-Captured { [CmdletBinding()] param([int]$Count) return ,(Read-Values -Count $Count) }
            function Read-Assigned { [CmdletBinding()] param([int]$Count) $value=(Read-Values -Count $Count); return ,$value }
            function Read-BareAssigned { [CmdletBinding()] param([int]$Count) $value=Read-Values -Count $Count; return ,$value }
            function Read-Argument { [CmdletBinding()] param([int]$Count) return Receive-Value -Value (Read-Values -Count $Count) }
            function Read-VariableArgument { [CmdletBinding()] param([int]$Count) $value=(Read-Values -Count $Count); return Receive-Value -Value $value }
            function Read-Empty { [CmdletBinding()] param([int]$Count) }
            function Read-EmptyCaptured { [CmdletBinding()] param([int]$Count) return ,(Read-Empty -Count $Count) }
            function Read-Suppressed { [CmdletBinding()] param([int]$Count) $null=Read-Values -Count $Count; 'after' }
            function Read-Stream { [CmdletBinding()] param([int]$Count) Read-Values -Count $Count }
            function Read-Paren { [CmdletBinding()] param([int]$Count) (Read-Values -Count $Count) }
            function Read-Return { [CmdletBinding()] param([int]$Count) return Read-Values -Count $Count }
            """, ".psm1");
        const string probe = """
            foreach($name in 'Read-Captured','Read-Assigned','Read-BareAssigned','Read-Argument','Read-VariableArgument','Read-EmptyCaptured','Read-Suppressed','Read-Stream','Read-Paren','Read-Return') {
                foreach($count in 0..4) {
                    $records=@(& $name -Count $count)
                    [pscustomobject]@{name=$name;count=$count;records=$records} | ConvertTo-Json -Depth 10 -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "typed-local-capture-shape-original");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(50, original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TypedLocalCaptureShape", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(13, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.EmittedClrMethod),
            unit => Assert.False(unit.UsesNativeFunctionBinding));
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "typed-local-capture-shape-compiled");
        Assert.True(original == compiled, "Original: " + original.StandardOutput + original.StandardError + Environment.NewLine +
            "Compiled: " + compiled.StandardOutput + compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void TypedLocalCapture_OuterErrorMergeBypassesConsumedSuccessOutput(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Inner { [CmdletBinding()] param() [int]::Parse('bad'); 7 }
            function Read-Outer { [CmdletBinding()] param() $value=(Read-Inner); 'after'; $value }
            """, ".psm1");
        const string probe = """
            $records=@(Read-Outer 2>&1)
            foreach($record in $records) {
                if($record -is [System.Management.Automation.ErrorRecord]) { 'error' }
                else { '{0}:{1}' -f $record.GetType().FullName,$record }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "typed-local-capture-error-original");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(new[] { "error", "System.String:after", "System.Int32:7" },
            original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TypedLocalCaptureErrors", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.EmittedClrMethod),
            unit => Assert.False(unit.UsesNativeFunctionBinding));
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "typed-local-capture-error-compiled");
        Assert.True(original == compiled, "Original: " + original.StandardOutput + original.StandardError + Environment.NewLine +
            "Compiled: " + compiled.StandardOutput + compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void TypedLocalCapture_PreservesConsumedOutputAndOperandOrder(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Candidate { [CmdletBinding()] param([Text.StringBuilder]$Trace) $null=$Trace.Append('candidate;'); return 2 }
            function Read-Collection { [CmdletBinding()] param([Text.StringBuilder]$Trace) $null=$Trace.Append('collection;'); return @(1,2) }
            function Test-InOrder { [CmdletBinding()] param([Text.StringBuilder]$Trace) return (Read-Candidate -Trace $Trace) -in (Read-Collection -Trace $Trace) }
            function Test-ContainsOrder { [CmdletBinding()] param([Text.StringBuilder]$Trace) return (Read-Collection -Trace $Trace) -contains (Read-Candidate -Trace $Trace) }
            """, ".psm1");
        const string probe = """
            foreach($name in 'Test-InOrder','Test-ContainsOrder') {
                $trace=[Text.StringBuilder]::new()
                $records=@(& $name -Trace $trace)
                [pscustomobject]@{name=$name;records=$records;trace=$trace.ToString()} | ConvertTo-Json -Depth 8 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "typed-local-capture-original");
        Assert.Equal(0, original.ExitCode);
        Assert.Contains("\"records\":[true]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("collection;candidate;", original.StandardOutput, StringComparison.Ordinal);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.TypedLocalCapture", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(4, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.EmittedClrMethod),
            unit => Assert.False(unit.UsesNativeFunctionBinding));
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "typed-local-capture-compiled");
        Assert.True(original == compiled, "Original: " + original.StandardOutput + original.StandardError + Environment.NewLine +
            "Compiled: " + compiled.StandardOutput + compiled.StandardError);
    }
}
