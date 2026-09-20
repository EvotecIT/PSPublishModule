using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void NativeConditionalValues_DoNotBroadenRuntimeFreeAdmission()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-RuntimeFreeAlternative { param([bool]$Enabled) if ($Enabled) { return 1 }; return 2 }
            function Get-HostedConditionalValue { param([bool]$Enabled) $items = @(if ($Enabled) { 1 } else { 2 }); return ,$items }
            """, ".psm1");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(
            new[] { fixture.ScriptPath }, "Generated", "ConditionalAdmission", "net10.0");

        Assert.Contains(result.Methods, static method => method.SourceName == "Get-RuntimeFreeAlternative");
        Assert.DoesNotContain(result.Methods, static method => method.SourceName == "Get-HostedConditionalValue");
        Assert.NotEmpty(result.Diagnostics);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeConditionalValues_KeepEscapingReturnInTheAuthoredFunction(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Test-EscapingConditional {
                [CmdletBinding()] param([bool]$Exit)
                $value = [pscustomobject]@{ A = if ($Exit) { return 9 } else { 7 } }
                return $value
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.EscapingConditional", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var entry = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == "Test-EscapingConditional");
        Assert.False(entry.EmittedClrMethod);
        Assert.True(entry.RetainedHostedSource);

        const string probe = "foreach ($exit in $false, $true) { $result = Test-EscapingConditional -Exit:$exit; $exit.ToString() + '|' + [string]$result + '|' + [string]$result.A }";
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-escaping-conditional");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-escaping-conditional");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeConditionalValues_PreserveObjectMapAndArrayCardinality(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-ConditionalObject {
                [CmdletBinding()] param([int]$Mode)
                $seen = 'before'
                [pscustomobject]@{
                    A = if ($Mode -eq 0) { } elseif ($Mode -eq 1) { $null } elseif ($Mode -eq 2) { $seen = 'after'; 7 } elseif ($Mode -eq 3) { 7; 8 } else { ,@(7, 8) }
                    B = $seen
                }
            }
            function Get-ConditionalMap {
                [CmdletBinding()] param([int]$Mode)
                $map = [ordered]@{
                    A = if ($Mode -eq 0) { } elseif ($Mode -eq 1) { $null } elseif ($Mode -eq 2) { 7 } elseif ($Mode -eq 3) { 7; 8 } else { ,@(7, 8) }
                    B = 'after'
                }
                return $map
            }
            function Get-ConditionalHashtable {
                [CmdletBinding()] param([int]$Mode)
                $seen = 'before'
                $map = @{
                    A = if ($Mode -eq 0) { } elseif ($Mode -eq 1) { $null } elseif ($Mode -eq 2) { $seen = 'after'; 7 } elseif ($Mode -eq 3) { 7; 8 } else { ,@(7, 8) }
                    B = $seen
                }
                return $map
            }
            function Get-ConditionalArray {
                [CmdletBinding()] param([int]$Mode)
                $items = @(if ($Mode -eq 0) { } elseif ($Mode -eq 1) { $null } elseif ($Mode -eq 2) { 7 } elseif ($Mode -eq 3) { 7; 8 } else { ,@(7, 8) })
                return ,$items
            }
            function Get-ConditionalNoElse {
                [CmdletBinding()] param([bool]$Enabled)
                $items = @(if ($Enabled) { 7 })
                return ,$items
            }
            function Get-ConditionalError {
                [CmdletBinding()] param([bool]$Fail)
                [pscustomobject]@{
                    A = if ($Fail) { Write-Error -Message 'branch failure'; 7 } else { 8 }
                    B = 'after'
                }
            }
            function Get-ConditionalArrayError {
                [CmdletBinding()] param([bool]$Fail)
                $items = @(if ($Fail) { Write-Error -Message 'branch failure'; 7 } else { 8 })
                return ,$items
            }
            function Get-ConditionalNestedError {
                [CmdletBinding()] param([bool]$Fail)
                [pscustomobject]@{
                    A = if ($Fail) { (-join (1..2 | ForEach-Object { Write-Error -Message 'nested failure'; $_ })) } else { 'ok' }
                    B = 'after'
                }
            }
            function Get-ConditionalArrayNestedError {
                [CmdletBinding()] param([bool]$Fail)
                $items = @(if ($Fail) { (-join (1..2 | ForEach-Object { Write-Error -Message 'nested failure'; $_ })) } else { 'ok' })
                return ,$items
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeConditionalValues", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(9, result.Manifest!.CompiledMethods);
        foreach (var entry in result.Manifest.UnitDispositionLedger!.Entries.Where(entry => entry.Name.StartsWith("Get-Conditional", StringComparison.Ordinal)))
        {
            Assert.True(entry.EmittedClrMethod, entry.Name + ": " + string.Join(" | ", entry.DiagnosticChain.Select(cause => cause.Message)));
            Assert.True(entry.UsesNativeFunctionBinding, entry.Name);
            Assert.False(entry.RetainedHostedSource, entry.Name);
        }

        const string probe = """
            function Describe-Value($value) {
                $type = if ($null -eq $value) { 'null' } else { $value.GetType().FullName }
                $children = if ($value -is [array]) { @($value | ForEach-Object { if ($null -eq $_) { 'null' } else { $_.GetType().FullName + ':' + [string]$_ } }) -join '|' } else { '' }
                return $type + ';' + @($value).Count + ';' + $children
            }
            foreach ($mode in 0..4) {
                $object = Get-ConditionalObject -Mode $mode
                $map = Get-ConditionalMap -Mode $mode
                $hashtable = Get-ConditionalHashtable -Mode $mode
                $array = @(Get-ConditionalArray -Mode $mode)[0]
                $objectValue = $object.PSObject.Properties['A'].Value
                $mapValue = $map['A']
                $mode.ToString() + '|object|' + (Describe-Value $objectValue) + '|' + $object.B
                $mode.ToString() + '|map|' + (Describe-Value $mapValue) + '|' + $map['B']
                $mode.ToString() + '|hashtable|' + (Describe-Value $hashtable['A']) + '|' + $hashtable['B']
                $mode.ToString() + '|array|' + (Describe-Value $array)
            }
            foreach ($enabled in $false, $true) {
                $items = @(Get-ConditionalNoElse -Enabled $enabled)[0]
                'no-else|' + $enabled.ToString() + '|' + (Describe-Value $items)
            }
            foreach ($action in 'SilentlyContinue', 'Stop') {
                foreach ($fail in $false, $true) {
                    foreach ($name in 'Get-ConditionalError', 'Get-ConditionalArrayError', 'Get-ConditionalNestedError', 'Get-ConditionalArrayNestedError') {
                        $branchErrors = @()
                        $record = $null
                        $caught = $null
                        try { $record = & $name -Fail:$fail -ErrorAction $action -ErrorVariable +branchErrors }
                        catch { $caught = $_.FullyQualifiedErrorId }
                        $value = if ($name -in 'Get-ConditionalArrayError', 'Get-ConditionalArrayNestedError') { @(,$record)[0] } elseif ($null -ne $record) { $record.PSObject.Properties['A'].Value } else { $null }
                        'error|' + $name + '|' + $action + '|' + $fail.ToString() + '|' + (Describe-Value $value) + '|' +
                            [string]$record.B + '|' + @($branchErrors).Count + '|' +
                            (@($branchErrors | ForEach-Object { $_.FullyQualifiedErrorId }) -join ',') + '|' + [string]$caught
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-conditional-values");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-conditional-values");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var originalLines = original.StandardOutput.Split('\n');
        var compiledLines = compiled.StandardOutput.Split('\n');
        Assert.Equal(originalLines.Length, compiledLines.Length);
        for (var index = 0; index < originalLines.Length; index++)
            Assert.True(string.Equals(originalLines[index], compiledLines[index], StringComparison.Ordinal),
                $"Observation {index}: expected '{originalLines[index]}', compiled '{compiledLines[index]}'.");
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeConditionalValues_PreserveConditionStreamsAndPriorDestination(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-ConditionalConditionStreams {
                [CmdletBinding()] param([bool]$Fail)
                $value = 'prior'
                $caught = $null
                try {
                    $value = [pscustomobject]@{
                        A = if ($(Write-Warning 'condition-warning'; Write-Verbose 'condition-verbose' -Verbose; Write-Information 'condition-information'; if ($Fail) { Write-Error 'condition-error' } else { $true })) { 7 } else { 8 }
                        B = 'after'
                    }
                } catch { $caught = $_.FullyQualifiedErrorId }
                [pscustomobject]@{ Value = $value; Caught = $caught }
            }
            function Get-ConditionalPriorDestination {
                [CmdletBinding()] param([bool]$Fail)
                $value = 'prior'
                $caught = $null
                try {
                    $value = [pscustomobject]@{
                        A = if ($Fail) { [int]::Parse('bad') } else { 7 }
                        B = 'after'
                    }
                } catch { $caught = $_.FullyQualifiedErrorId }
                [pscustomobject]@{ Value = $value; Caught = $caught }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeConditionalConditionState", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        foreach (var entry in result.Manifest!.UnitDispositionLedger!.Entries)
        {
            Assert.True(entry.EmittedClrMethod, entry.Name + ": " + string.Join(" | ", entry.DiagnosticChain.Select(cause => cause.Message)));
            Assert.True(entry.UsesNativeFunctionBinding, entry.Name);
            Assert.False(entry.RetainedHostedSource, entry.Name);
        }

        const string probe = """
            function Describe-ConditionalResult($result) {
                $value = $result.Value
                $type = if ($null -eq $value) { 'null' } else { $value.GetType().FullName }
                $a = if ($value -is [string]) { '' } else { [string]$value.A }
                return $type + ':' + [string]$value + ':A=' + $a + ':caught=' + [string]$result.Caught
            }
            foreach ($action in 'SilentlyContinue', 'Continue', 'Stop') {
                foreach ($fail in $false, $true) {
                    foreach ($name in 'Get-ConditionalConditionStreams', 'Get-ConditionalPriorDestination') {
                        $errors = @(); $warnings = @(); $information = @(); $outer = $null; $records = @()
                        try {
                            $records = @(& $name -Fail:$fail -ErrorAction $action -ErrorVariable +errors `
                                -WarningVariable +warnings -InformationVariable +information 4>&1)
                        } catch { $outer = $_.FullyQualifiedErrorId }
                        $verbose = @($records | Where-Object { $_ -is [Management.Automation.VerboseRecord] })
                        $result = @($records | Where-Object { $_.PSObject.Properties['Value'] })[-1]
                        'case|' + $name + '|' + $action + '|' + $fail.ToString() + '|result=' +
                            $(if ($null -eq $result) { 'none' } else { Describe-ConditionalResult $result }) +
                            '|errors=' + @($errors).Count + ':' + (@($errors | ForEach-Object { $_.FullyQualifiedErrorId }) -join ',') +
                            '|warnings=' + (@($warnings | ForEach-Object { $_.Message }) -join ',') +
                            '|verbose=' + (@($verbose | ForEach-Object { $_.Message }) -join ',') +
                            '|information=' + (@($information | ForEach-Object { [string]$_.MessageData }) -join ',') + '|outer=' + [string]$outer
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-conditional-condition-state");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-conditional-condition-state");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("warnings=condition-warning", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("verbose=condition-verbose", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("information=condition-information", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("System.String:prior:A=:caught=", original.StandardOutput, StringComparison.Ordinal);
        var originalObservations = original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.StartsWith("case|", StringComparison.Ordinal)).ToArray();
        var compiledObservations = compiled.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.StartsWith("case|", StringComparison.Ordinal)).ToArray();
        Assert.Equal(12, originalObservations.Length);
        Assert.Equal(originalObservations, compiledObservations);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeConditionalValues_PreservePartialEnumerationFailureAndCleanup(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-ConditionalEnumerableValue {
                [CmdletBinding()] param([Collections.IEnumerable]$Items, [bool]$UseItems)
                $value = 'prior'
                $caught = $null
                try {
                    $value = [pscustomobject]@{
                        A = if ($UseItems) { $Items } else { 9 }
                        B = 'after'
                    }
                } catch { $caught = $_.FullyQualifiedErrorId }
                [pscustomobject]@{ Value = $value; Caught = $caught }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeConditionalEnumeration", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var entry = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(entry.EmittedClrMethod, string.Join(" | ", entry.DiagnosticChain.Select(cause => cause.Message)));
        Assert.True(entry.UsesNativeFunctionBinding);
        Assert.False(entry.RetainedHostedSource);

        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            public sealed class ConditionalValueEnumerable : IEnumerable {
                public static string Trace = "";
                public string Failure;
                public int Count;
                public IEnumerator GetEnumerator() {
                    Trace += "get;";
                    if (Failure == "get") throw new InvalidOperationException("get failed");
                    return new Cursor(Failure, Count);
                }
                private sealed class Cursor : IEnumerator, IDisposable {
                    private readonly string failure;
                    private readonly int count;
                    private int index = -1;
                    internal Cursor(string failure, int count) { this.failure = failure; this.count = count; }
                    public bool MoveNext() {
                        index++; Trace += "move" + index + ";";
                        if (failure == "move" && index == 1) throw new InvalidOperationException("move failed");
                        return index < count;
                    }
                    public object Current { get {
                        Trace += "current" + index + ";";
                        if (failure == "current" && index == 1) throw new InvalidOperationException("current failed");
                        return index == 0 ? null : index == 1 ? (object)new int[] { 7, 8 } : index;
                    } }
                    public void Reset() { throw new NotSupportedException(); }
                    public void Dispose() {
                        Trace += "dispose;";
                        if (failure == "dispose") throw new InvalidOperationException("dispose failed");
                    }
                }
            }
            '@
            function Describe-EnumerationResult($result) {
                if ($null -eq $result) { return 'none' }
                $value = $result.Value
                if ($value -is [string]) { return 'string:' + $value + ':caught=' + [string]$result.Caught }
                $items = @($value.A)
                $shapes = @($items | ForEach-Object {
                    if ($null -eq $_) { 'null' }
                    elseif ($_ -is [array]) { $_.GetType().FullName + '[' + (@($_) -join ',') + ']' }
                    else { $_.GetType().FullName + ':' + [string]$_ }
                })
                return $value.GetType().FullName + ':' + $items.Count + ':' + ($shapes -join '|') +
                    ':B=' + [string]$value.B + ':caught=' + [string]$result.Caught
            }
            foreach ($action in 'SilentlyContinue', 'Continue', 'Stop') {
                foreach ($failure in 'none', 'get', 'move', 'current', 'dispose') {
                    [ConditionalValueEnumerable]::Trace = ''
                    $items = [ConditionalValueEnumerable]::new()
                    $items.Failure = $failure
                    $items.Count = 3
                    $errors = @(); $outer = $null; $records = @()
                    try {
                        $records = @(Get-ConditionalEnumerableValue -Items $items -UseItems $true `
                            -ErrorAction $action -ErrorVariable +errors)
                    } catch { $outer = $_.FullyQualifiedErrorId }
                    $result = @($records | Where-Object { $_.PSObject.Properties['Value'] })[-1]
                    'case|' + $action + '|' + $failure + '|result=' + (Describe-EnumerationResult $result) +
                        '|errors=' + @($errors).Count + ':' + (@($errors | ForEach-Object { $_.FullyQualifiedErrorId }) -join ',') +
                        '|outer=' + [string]$outer + '|trace=' + [ConditionalValueEnumerable]::Trace
                }
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-conditional-enumeration");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-conditional-enumeration");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("case|SilentlyContinue|none|result=System.Management.Automation.PSCustomObject:3:", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("|trace=get;move0;current0;move1;dispose;", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("|trace=get;move0;current0;move1;current1;dispose;", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("|trace=get;move0;current0;move1;current1;move2;current2;move3;dispose;", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
