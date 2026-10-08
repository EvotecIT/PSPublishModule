using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NumericStatementErrors_PreserveDecimalFailuresCatchAndAssignmentContinuation(string framework, string host)
    {
        var declarations = new[] { ("Add", "+"), ("Subtract", "-"), ("Multiply", "*"), ("Divide", "/"), ("Remainder", "%") }
            .Select(pair => $"function Get-Decimal{pair.Item1} {{ [CmdletBinding()] param([decimal]$Left,[decimal]$Right) return $Left {pair.Item2} $Right }}");
        var source = string.Join(Environment.NewLine, declarations) + Environment.NewLine + """
            function Get-ArithmeticContinuation {
                [CmdletBinding()] param([decimal]$Left,[decimal]$Right)
                [decimal]$value=7
                $value=$Left*$Right
                return $value
            }
            function Get-ArithmeticCaught {
                [CmdletBinding()] param([int]$Left,[int]$Right)
                try { return $Left % $Right }
                catch [System.Management.Automation.RuntimeException] { return 37 }
            }
            function Get-ArithmeticOperandError {
                [CmdletBinding()] param([string]$Left,[string]$Right)
                return [decimal]::Parse($Left) / [decimal]::Parse($Right)
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.DecimalStatementIdentity", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(8, result.Manifest!.CompiledMethods);
        const string probe = """
            foreach($name in 'Add','Subtract','Multiply','Divide','Remainder') {
                foreach($pair in @(@([decimal]10,[decimal]3),@([decimal]10,[decimal]0),@([decimal]::MaxValue,[decimal]2),@([decimal]::MinValue,[decimal]2))) {
                    try {
                        $value=& ('Get-Decimal'+$name) -Left $pair[0] -Right $pair[1] -ErrorAction Stop
                        $value.GetType().FullName
                        [decimal]::GetBits($value) -join ','
                    }
                    catch {
                        $_.FullyQualifiedErrorId
                        $_.Exception.GetType().FullName
                        $_.Exception.Message
                        [string]$_.CategoryInfo.Category
                        $observedException=$_.Exception.InnerException
                        while($null -ne $observedException) { $observedException.GetType().FullName; $observedException.Message; $observedException=$observedException.InnerException }
                    }
                }
            }
            Get-ArithmeticContinuation -Left ([decimal]::MaxValue) -Right 2 -ErrorAction SilentlyContinue
            Get-ArithmeticContinuation -Left 3 -Right 2
            Get-ArithmeticCaught -Left 10 -Right 0
            Get-ArithmeticCaught -Left 10 -Right 3
            foreach($pair in @(@('invalid-left','0'),@('1','invalid-right'),@('invalid-left','invalid-right'),@('1','0'))) {
                try { Get-ArithmeticOperandError -Left $pair[0] -Right $pair[1] -ErrorAction Stop }
                catch {
                    $_.FullyQualifiedErrorId
                    $observedException=$_.Exception
                    while($null -ne $observedException) { $observedException.GetType().FullName; $observedException.Message; $observedException=$observedException.InnerException }
                }
            }
            try { Get-DecimalDivide -Left 1 -Right 0 -ErrorAction Stop }
            catch [System.DivideByZeroException] { 'typed-zero-catch' }
            catch { 'unexpected-catch:'+$_.Exception.GetType().FullName }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-decimal-identity");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-decimal-identity");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original:" + Environment.NewLine + original.StandardOutput + "Generated:" + Environment.NewLine + compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NumericStatementErrors_PreserveNativeRemainderIdentityAtExternalCallers(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-RemainderValue {
                [CmdletBinding()] param([int]$Left,[int]$Right)
                return $Left % $Right
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NumericStatementIdentity", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        const string probe = """
            foreach($pair in @(@(10,3),@(10,0),@([int]::MinValue,-1))) {
                try {
                    $value=Get-RemainderValue -Left $pair[0] -Right $pair[1] -ErrorAction Stop
                    $value.GetType().FullName
                    $value
                }
                catch {
                    $_.FullyQualifiedErrorId
                    $_.Exception.GetType().FullName
                    $_.Exception.Message
                    [string]$_.CategoryInfo.Category
                    $observedException=$_.Exception.InnerException
                    while($null -ne $observedException) { $observedException.GetType().FullName; $observedException.Message; $observedException=$observedException.InnerException }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-numeric-statement-identity");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-numeric-statement-identity");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original:" + Environment.NewLine + original.StandardOutput + "Generated:" + Environment.NewLine + compiled.StandardOutput);
    }
}
