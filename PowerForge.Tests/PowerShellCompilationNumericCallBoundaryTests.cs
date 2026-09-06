using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Trait("Category", "PowerShellCompilerGate")]
    [Theory]
    [MemberData(nameof(NumericArtifactTargets))]
    public void Build_HybridPreservesNumericWrappersThroughUnresolvedCallTargets(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-Number {
                [CmdletBinding()] [Alias('Read-Number')]
                param([int] $Left, [int] $Right)
                $Left += $Right; return $Left
            }
            function Get-Bridge {
                [CmdletBinding()] param()
                $Name = 'Get-Number'
                return & $Name -Left ([int]::MaxValue) -Right 1
            }
            function Test-Direct {
                [CmdletBinding()] param()
                $Name = 'Get-Number'
                try { & $Name -Left ([int]::MaxValue) -Right 1; return 'missed' }
                catch [System.Management.Automation.PSInvalidCastException] { return 'caught' }
            }
            function Test-Bridge {
                [CmdletBinding()] param()
                try { Get-Bridge; return 'missed' }
                catch [System.Management.Automation.PSInvalidCastException] { return 'caught' }
            }
            function Test-Alias {
                [CmdletBinding()] param()
                try { Read-Number -Left ([int]::MaxValue) -Right 1; return 'missed' }
                catch [System.Management.Automation.PSInvalidCastException] { return 'caught' }
            }
            function Get-Safe { [CmdletBinding()] param() return 7 }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.NumericDynamicCall", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.EmittedUnits >= 1);
        const string invocation = "Test-Direct; Test-Bridge; Test-Alias; Get-Safe";
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module '{fixture.ScriptPath.Replace("'", "''")}'; {invocation}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module '{result.ArtifactPath!.Replace("'", "''")}'; {invocation}");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, compiled.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(string.Join(Environment.NewLine, "caught", "caught", "caught", "7"), original.StandardOutput.Trim());
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
    }

    [Trait("Category", "PowerShellCompilerGate")]
    [Theory]
    [InlineData("System.Management.Automation.PSInvalidCastException", false)]
    [InlineData("System.InvalidCastException", true)]
    public void Bind_NumericErrorObservationCrossesDocumentsWithoutRejectingClrCatchContracts(string exception, bool typed)
    {
        var directory = Path.Combine(Path.GetTempPath(), "NumericCallBoundary");
        var helper = PowerShellSourceParser.Parse(
            "function Get-Number { param([int] $Value) $Value += 1; return $Value }",
            Path.Combine(directory, "Helper.ps1"));
        var caller = PowerShellSourceParser.Parse(
            $"function Test-Number {{ begin {{}} end {{ try {{ Get-Number -Value ([int]::MaxValue) }} catch [{exception}] {{ return 42 }} }} }}",
            Path.Combine(directory, "Caller.ps1"));
        var program = new PowerShellSemanticBinder().Bind(new[] { helper, caller }, "net10.0", PowerShellCompilationCapabilities.BinaryModule);
        Assert.Equal(typed, program.Functions.Any(function => function.Symbol.Name == "Get-Number"));
    }

    [Trait("Category", "PowerShellCompilerGate")]
    [Theory]
    [MemberData(nameof(NumericArtifactTargets))]
    public void Build_HybridPreservesNumericErrorWrappersAcrossTypedAndRetainedCallers(string framework, string host)
    {
        var helpers = new List<string> { "function Get-Safe { [CmdletBinding()] param() return 7 }" };
        var functions = new List<string>();
        var calls = new List<string>();
        foreach (var (name, type, operation, arguments, exception) in new[]
        {
            ("Remainder", "int", "return $Left % $Right", "1 -Right 0", "RuntimeException"),
            ("RemainderUpdate", "int", "$Left %= $Right; return $Left", "1 -Right 0", "RuntimeException"),
            ("Overflow", "int", "$Left += $Right; return $Left", "([int]::MaxValue) -Right 1", "PSInvalidCastException"),
            ("Increment", "int", "$Left++; return $Left", "([int]::MaxValue) -Right 1", "RuntimeException"),
            ("Decimal", "decimal", "return $Left * $Right", "([decimal]::MaxValue) -Right 2", "RuntimeException"),
            ("DecimalUpdate", "decimal", "$Left *= $Right; return $Left", "([decimal]::MaxValue) -Right 2", "RuntimeException")
        })
        {
            helpers.Add($"function Get-{name} {{ [CmdletBinding()] param([{type}] $Left, [{type}] $Right) {operation} }}");
            helpers.Add($"function Get-{name}Bridge {{ [CmdletBinding()] param() return Get-{name} -Left {arguments} }}");
            helpers.Add($"function Get-{name}Outer {{ [CmdletBinding()] param() return Get-{name}Bridge }}");
            foreach (var retained in new[] { false, true })
            {
                var caller = "Test-" + name + (retained ? "Retained" : "Typed");
                var body = $"try {{ Get-{name}Outer; return 'missed' }} catch [System.Management.Automation.{exception}] {{ return 'caught' }}";
                if (retained) body = "begin {} end { " + body + " }";
                functions.Add($"function {caller} {{ [CmdletBinding()] param() {body} }}");
                calls.Add(caller);
            }
        }
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, helpers.Concat(functions)), ".psm1");
        var strict = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, Path.Combine(fixture.OutputPath, "strict"), "PowerForge.NumericCall.Strict",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.False(strict.Succeeded);
        var hybrid = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, Path.Combine(fixture.OutputPath, "hybrid"), "PowerForge.NumericCall.Hybrid",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(hybrid.Succeeded, hybrid.Error + Environment.NewLine + hybrid.BuildOutput);
        Assert.True(hybrid.Manifest!.EmittedUnits >= 1);
        var invocation = string.Join("; ", calls) + "; Get-Safe";
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module '{fixture.ScriptPath.Replace("'", "''")}'; {invocation}");
        var compiled = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module '{hybrid.ArtifactPath!.Replace("'", "''")}'; {invocation}");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, compiled.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(string.Join(Environment.NewLine, Enumerable.Repeat("caught", calls.Count).Append("7")), original.StandardOutput.Trim());
        Assert.Equal(original.StandardOutput.Trim(), compiled.StandardOutput.Trim());
    }
}
