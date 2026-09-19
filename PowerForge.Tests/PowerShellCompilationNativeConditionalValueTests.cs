using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
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
}
