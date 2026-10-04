using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> NativeFunctionStrictHosts()
        => StatementErrorHosts().SelectMany(configuration => new[] { 1, 2 }
            .SelectMany(version => new[] { false, true }
                .Select(allScope => configuration.Concat(new object[] { version, allScope }).ToArray())));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(NativeFunctionStrictHosts))]
    public void NativeFunctionReads_PreserveStrictModeAndInterpolationException(string framework, string host, int strictVersion, bool allScope)
    {
        var source = "Set-StrictMode -Version " + strictVersion + Environment.NewLine + """
            $copy='module'
            function Read-NativeInterpolated {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value = 1)
                return "missing=$undefinedVariable"
            }
            function Read-NativeDirect {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value = 1, [string]$Observed = $copy)
                "before=$copy;default=$Observed"
                $copy = $undefinedVariable
                return "copy=$copy;local=$local:copy"
            }
            function Read-NativePrior {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value = 1)
                $copy = 'kept'
                $copy = $undefinedVariable
                return "copy=$copy"
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeStrictReads", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        const string probe = """
            foreach($command in 'Read-NativeInterpolated','Read-NativeDirect','Read-NativePrior') {
                'command:'+$command
                $records=@(& $command -ErrorAction Continue 2>&1)
                foreach($record in $records) {
                    if($record -is [System.Management.Automation.ErrorRecord]) {
                        'error:'+$record.FullyQualifiedErrorId+';line='+$record.InvocationInfo.ScriptLineNumber
                    } else { 'value:'+$record }
                }
            }
            """;
        var setup = allScope ? "New-Variable -Name copy -Scope Global -Option AllScope -Value inherited; " : string.Empty;
        var original = RunStatementErrorProbe(host, setup + "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-strict-read");
        var compiled = RunStatementErrorProbe(host, setup + "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-strict-read");
        Assert.True(original.ExitCode == 0 && compiled.ExitCode == 0, original.StandardError + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError + compiled.StandardError), original.StandardError + compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original:" + Environment.NewLine + original.StandardOutput + "Generated:" + Environment.NewLine + compiled.StandardOutput);
    }
}
