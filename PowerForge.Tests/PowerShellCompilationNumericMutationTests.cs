using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    public static IEnumerable<object[]> NumericArtifactTargets()
    {
        yield return new object[] { "net8.0", "pwsh" };
        yield return new object[] { "net10.0", "pwsh" };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            yield return new object[] { "net472", "powershell.exe" };
    }
    [Trait("Category", "PowerShellCompilerGate")]
    [Theory]
    [InlineData("")]
    [InlineData("# Empty entrypoint\nfunction Get-Answer { return 42 }")]
    public void Build_EmptyExecutableEntryPointExitsWithoutOutput(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.EmptyEntryPoint", PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = "net10.0" });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!);
        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.StandardOutput);
        Assert.Empty(run.StandardError);
    }

    [Trait("Category", "PowerShellCompilerGate")]
    [Theory]
    [MemberData(nameof(NumericArtifactTargets))]
    public void Build_NumericLibraryMatchesPowerShellWithoutRuntimeDependency(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-SingleSum { param([single] $Left, [single] $Right) return $Left + $Right }
            function Get-LongProduct { param([long] $Left, [long] $Right) $Left *= $Right; return $Left }
            function Get-Loop { param([int] $Limit) [int] $total = 0; for ([int] $index = 0; $index -lt $Limit; $index++) { $total += $index }; return $total }
            function Get-Overflow { param([int] $Value) try { $Value++; return 'success' } catch [OverflowException] { return 'overflow' } catch [InvalidCastException] { return 'invalid-cast' } }
            """);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.NumericLibrary",
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $". '{fixture.ScriptPath.Replace("'", "''")}'; " +
            "$value = Get-SingleSum -Left 16777216 -Right 1; $value.GetType().FullName; $value; " +
            "Get-LongProduct -Left ([long]::Parse('-3074457345618259115')) -Right 3; Get-Loop -Limit 10; Get-Overflow -Value ([int]::MaxValue)");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"$assembly = [Reflection.Assembly]::LoadFrom('{result.ArtifactPath!.Replace("'", "''")}'); " +
            "$type = $assembly.GetTypes() | Where-Object { $null -ne $_.GetMethod('Get_SingleSum') }; " +
            "$value = $type.GetMethod('Get_SingleSum').Invoke($null, [object[]]@([single]16777216, [single]1)); $value.GetType().FullName; $value; " +
            "$type.GetMethod('Get_LongProduct').Invoke($null, [object[]]@([long]::Parse('-3074457345618259115'), [long]3)); " +
            "$type.GetMethod('Get_Loop').Invoke($null, [object[]]@([int]10)); " +
            "$type.GetMethod('Get_Overflow').Invoke($null, [object[]]@([int]::MaxValue))");
        Assert.True(original.ExitCode == 0, original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
    }

    [Trait("Category", "PowerShellCompilerGate")]
    [Theory]
    [MemberData(nameof(NumericArtifactTargets))]
    public void Build_NumericArithmeticMatchesPowerShellValuesTypesAndCatchSelection(string framework, string host)
    {
        var functions = new List<string>();
        foreach (var (name, expression) in new[]
        {
            ("Add", "$Left + $Right"), ("Subtract", "$Left - $Right"),
            ("Multiply", "$Left * $Right"), ("Divide", "$Left / $Right"),
            ("Remainder", "$Left % $Right"), ("Positive", "+$Left"), ("Negative", "-$Left")
        })
            functions.Add($"function Get-Single{name} {{ [CmdletBinding()] param([single] $Left, [single] $Right) return {expression} }}");
        foreach (var type in new[] { "byte", "sbyte", "int16", "uint16", "int", "uint32", "long", "uint64" })
        foreach (var (name, expression) in new[]
        {
            ("Add", "$Value += $Right"), ("Subtract", "$Value -= $Right"),
            ("Multiply", "$Value *= $Right"), ("Remainder", "$Value %= $Right"),
            ("Increment", "$Value++"), ("Decrement", "--$Value")
        })
            functions.Add($"function Get-{type}{name} {{ [CmdletBinding()] param([{type}] $Value, [{type}] $Right) " +
                $"try {{ {expression}; return 'success|' + [string] $Value }} " +
                "catch [OverflowException] { return 'overflow|' + [string] $Value } " +
                "catch [InvalidCastException] { return 'invalid-cast|' + [string] $Value } " +
                "catch [DivideByZeroException] { return 'divide-zero|' + [string] $Value } " +
                "catch { return 'other|' + [string] $Value } }");
        functions.Add("function Get-SingleUpdate { [CmdletBinding()] param([single] $Value, [single] $Right) $Value *= $Right; return $Value }");
        foreach (var type in new[] { "byte", "sbyte", "int16", "uint16", "int", "uint32", "long", "uint64" })
            functions.Add($"function Get-{type}BinaryRemainder {{ [CmdletBinding()] param([{type}] $Left, [{type}] $Right) return $Left % $Right }}");
        functions.Add("function Get-RightOverflow { [CmdletBinding()] param([int] $Value, [long] $Right) " +
            "try { $Value += [Convert]::ToInt32($Right); return 'success' } " +
            "catch [OverflowException] { return 'overflow|' + [string] $Value } " +
            "catch [InvalidCastException] { return 'invalid-cast|' + [string] $Value } }");
        functions.Add("function Get-NumericNameCollision { [CmdletBinding()] param([int] $__typedNumericUpdate_0, [int] $__numericLeft_1) " +
            "$__typedNumericUpdate_0 += $__numericLeft_1; return $__typedNumericUpdate_0 }");
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, functions), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.NumericContracts",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);

        const string invocation = """
            $ErrorActionPreference = 'Stop'
            foreach ($name in 'Add','Subtract','Multiply','Divide','Remainder','Positive','Negative') {
                foreach ($pair in @(@(16777216,1), @(16777215,3), @(0,0), @(-3,2))) {
                    $value = & ('Get-Single' + $name) -Left $pair[0] -Right $pair[1]
                    $value.GetType().FullName + '|' + $value.ToString('R', [cultureinfo]::InvariantCulture)
                }
            }
            foreach ($type in 'byte','sbyte','int16','uint16','int','uint32','long','uint64') {
                $max = ([type]$type)::MaxValue
                $min = ([type]$type)::MinValue
                foreach ($name in 'Add','Subtract','Multiply','Remainder','Increment','Decrement') {
                    foreach ($pair in @(@($max,1), @($min,1), @($max,3), @(5,2), @(1,0))) {
                        & ('Get-' + $type + $name) -Value $pair[0] -Right $pair[1]
                    }
                }
            }
            Get-intRemainder -Value ([int]::MinValue) -Right -1
            Get-longRemainder -Value ([long]::MinValue) -Right -1
            Get-longSubtract -Value ([long]::MinValue) -Right 511
            Get-longSubtract -Value ([long]::MinValue) -Right 2048
            $single = Get-SingleUpdate -Value 16777215 -Right 3
            $single.GetType().FullName + '|' + $single.ToString('R', [cultureinfo]::InvariantCulture)
            foreach ($type in 'byte','sbyte','int16','uint16','int','uint32','long','uint64') {
                $min = ([type]$type)::MinValue
                $divisor = if ($min -lt 0) { -1 } else { 1 }
                foreach ($pair in @(@($min,$divisor), @((([type]$type)::MaxValue),3), @(5,2), @(1,0))) {
                    try {
                        $value = & ('Get-' + $type + 'BinaryRemainder') -Left $pair[0] -Right $pair[1]
                        $value.GetType().FullName + '|' + [string]$value
                    } catch [DivideByZeroException] { 'divide-zero' }
                }
            }
            Get-RightOverflow -Value 12 -Right ([long]::MaxValue)
            foreach ($text in '-3074457345618258603','-3074457345618258944','-3074457345618259115') {
                Get-longMultiply -Value ([long]::Parse($text)) -Right 3
            }
            Get-NumericNameCollision -__typedNumericUpdate_0 2 -__numericLeft_1 3
            """;
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module '{fixture.ScriptPath.Replace("'", "''")}' -Force; {invocation}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module '{result.ArtifactPath!.Replace("'", "''")}' -Force; {invocation}");
        Assert.True(original.ExitCode == 0, original.StandardError + original.StandardOutput);
        Assert.True(compiled.ExitCode == 0, compiled.StandardError + compiled.StandardOutput);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
    }

    [Trait("Category", "PowerShellCompilerGate")]
    [Theory]
    [InlineData("[int]", "$Value += 1", "RuntimeException")]
    [InlineData("[int]", "$Value++", "PSInvalidCastException")]
    [InlineData("[int]", "$Value -= 1", "PSInvalidCastException")]
    [InlineData("[decimal]", "$Value += 1d", "RuntimeException")]
    [InlineData("[decimal]", "$Value = $Value * $Value", "RuntimeException")]
    public void Analyze_RejectsNumericOperationsRequiringExactPowerShellErrorWrapping(string type, string operation, string exception)
    {
        using var fixture = ArtifactFixture.Create(
            $"function Test-NumericWrapping {{ param({type} $Value) try {{ {operation}; return 1 }} " +
            $"catch [System.Management.Automation.{exception}] {{ return 2 }} }}", ".psm1");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Strict, targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));
        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
    }
}
