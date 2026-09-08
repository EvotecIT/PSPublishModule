using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeFunctionStorage_PreservesWritesValidationAndContinuation(string framework, string host)
    {
        const string source = """
            function Read-NativeStorage {
                [CmdletBinding()]
                param([ValidateRange(1,9)][int]$Value = 1)
                $before = "value=$Value"
                $Value = 42
                $after = "value=$Value"
                return "before=$before;after=$after"
            }
            function Read-NativeWrite {
                [CmdletBinding()]
                param([ValidateRange(1,9)][int]$Value = 1)
                $copy = $Value
                $Value = 7
                $null = 123
                return "copy=$copy;value=$Value"
            }
            function Read-NativeCatch {
                [CmdletBinding()]
                param([ValidateRange(1,9)][int]$Value = 1)
                $_ = 'before'
                try { $Value = 42 }
                catch { "caught=$_;alias=$PSItem" }
                return "after=$_"
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var semantic = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.Parse(source, fixture.ScriptPath) }, framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.True(semantic.Emitted.Methods.Length == 3,
            string.Join(Environment.NewLine, semantic.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        Assert.All(semantic.Emitted.Methods, method => Assert.True(
            !method.RequiresPowerShellCommandRegions && !method.RequiresPowerShellRuntimeState,
            method.Source));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeStorage", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 3,
            string.Join(Environment.NewLine, result.Manifest.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        const string probe = """
            'command:'+(Get-Command Read-NativeStorage).CommandType
            Read-NativeWrite -Value 3
            foreach($preference in 'Continue','SilentlyContinue') {
                $errors=@()
                $records=@(Read-NativeStorage -Value 2 -ErrorAction $preference -ErrorVariable errors 2>&1)
                foreach($record in $records) {
                    if($record -is [System.Management.Automation.ErrorRecord]) {
                        'resumed-error:'+ $record.FullyQualifiedErrorId + ';line=' + $record.InvocationInfo.ScriptLineNumber
                    } else { 'resumed-output:'+ $record }
                }
                'resumed-errors:'+$errors.Count
            }
            foreach($preference in 'Continue','SilentlyContinue','Stop') {
                $errors=@()
                try {
                    $records=@(Read-NativeStorage -Value 2 -ErrorAction $preference -ErrorVariable errors 2>&1)
                    foreach($record in $records) {
                        if($record -is [System.Management.Automation.ErrorRecord]) {
                            'error:'+ $record.FullyQualifiedErrorId + ';line=' + $record.InvocationInfo.ScriptLineNumber
                        } else { 'output:'+ $record }
                    }
                } catch { 'caught:'+$_.FullyQualifiedErrorId }
                'saved-errors:'+$errors.Count
            }
            Read-NativeWrite
            Read-NativeCatch
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-storage");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-storage");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original:" + Environment.NewLine + original.StandardOutput + "Generated:" + Environment.NewLine + compiled.StandardOutput);
    }
}
