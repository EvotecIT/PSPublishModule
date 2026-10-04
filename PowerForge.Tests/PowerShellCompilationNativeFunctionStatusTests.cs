using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeFunctionStatus_PreservesFailureAndSuccessfulStatementTransitions(string framework, string host)
    {
        const string source = """
            function Read-NativeFailedStatus {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $Value=42
                return "failed=$?"
            }
            function Read-NativeAssignedStatus {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $Value=42
                $copy='assigned'
                return "assigned=$?"
            }
            function Read-NativeOutputStatus {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $Value=42
                'output'
                return "output=$?"
            }
            function Read-NativeArrayStatus {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $Value=42
                $copy=@("first=$?"; "second=$?")
                return "$copy;array=$?"
            }
            function Read-NativeArrayFailure {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $copy=@('kept'; 'lost',[int]::Parse('broken'); "continued=$?")
                return "$copy;after=$?"
            }
            function Read-NativeNestedArray {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $Value=42
                $copy=@(@("inner=$?"; "next=$?"); "outer=$?"; $null; @())
                return "$copy;after=$?"
            }
            function Read-NativeArrayLastFailure {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $copy=@('kept'; [int]::Parse('broken'))
                return "$copy;after=$?"
            }
            function Read-NativeWrappedArray {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $Value=42
                $copy=@(@("first=$?"; "second=$?"))
                return "$copy;after=$?"
            }
            function Read-NativeEmptyArray {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $same=[object]::ReferenceEquals(@(@(); @()), @(@(); @()))
                return "empty-shared=$same"
            }
            function Read-NativeNestedFailure {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $copy=@(@('inner'; [int]::Parse('broken')); "outer=$?")
                return "$copy;after=$?"
            }
            function Read-NativeSingleFailure {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $copy='old'
                $copy=@([int]::Parse('broken'))
                return "copy=$copy;after=$?"
            }
            function Read-NativeNestedSingleFailure {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $copy=@(@([int]::Parse('broken')), 'kept'; 'tail')
                return "$copy;after=$?"
            }
            function Read-NativeCommaStatus {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Value=1)
                $copy=@(@('inner'; [int]::Parse('broken')), "next=$?"; 'tail')
                return "$copy;after=$?"
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeStatus", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 13,
            string.Join(Environment.NewLine, result.Manifest.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        const string probe = """
            Read-NativeFailedStatus -ErrorAction SilentlyContinue
            Read-NativeAssignedStatus -ErrorAction SilentlyContinue
            Read-NativeOutputStatus -ErrorAction SilentlyContinue
            Read-NativeArrayStatus -ErrorAction SilentlyContinue
            Read-NativeArrayFailure -ErrorAction SilentlyContinue
            Read-NativeNestedArray -ErrorAction SilentlyContinue
            Read-NativeArrayLastFailure -ErrorAction SilentlyContinue
            Read-NativeWrappedArray -ErrorAction SilentlyContinue
            Read-NativeEmptyArray
            Read-NativeSingleFailure -ErrorAction SilentlyContinue
            Read-NativeNestedSingleFailure -ErrorAction SilentlyContinue
            Read-NativeCommaStatus -ErrorAction SilentlyContinue
            foreach($command in 'Read-NativeArrayFailure','Read-NativeNestedFailure') {
            foreach($preference in 'Continue','SilentlyContinue','Stop') {
                $errors=@()
                try {
                    $records=@(& $command -ErrorAction $preference -ErrorVariable errors 2>&1)
                    foreach($record in $records) {
                        if($record -is [System.Management.Automation.ErrorRecord]) {
                            'error:'+$record.FullyQualifiedErrorId+';line='+$record.InvocationInfo.ScriptLineNumber+';column='+$record.InvocationInfo.OffsetInLine
                        } else { 'output:'+$record }
                    }
                } catch { 'caught:'+$_.FullyQualifiedErrorId+';line='+$_.InvocationInfo.ScriptLineNumber+';column='+$_.InvocationInfo.OffsetInLine }
                'errors:'+$errors.Count
            }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-status");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-status");
        Assert.True(original.ExitCode == 0 && compiled.ExitCode == 0 &&
            string.IsNullOrWhiteSpace(original.StandardError + compiled.StandardError), original.StandardError + compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original:" + Environment.NewLine + original.StandardOutput + "Generated:" + Environment.NewLine + compiled.StandardOutput);
    }
}
