using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NumericStatementErrors_PreserveCompoundUpdatesAtExternalCallers(string framework, string host)
    {
        var updates = new[] { ("Add", "$value += $Right"), ("Subtract", "$value -= $Right"),
            ("Multiply", "$value *= $Right"), ("Remainder", "$value %= $Right"),
            ("Increment", "$value++"), ("Decrement", "$value--") };
        var types = new[] { "byte", "sbyte", "int16", "uint16", "int", "uint32", "long", "uint64", "decimal" };
        var source = string.Join(Environment.NewLine, types.SelectMany(type => updates.Select(update =>
            $"function Get-Update{type}{update.Item1} {{ [CmdletBinding()] param([{type}]$Left,[{type}]$Right) [{type}]$value=$Left; {update.Item2}; return \"after=$value\" }}")));
        source += Environment.NewLine + """function Get-UpdatedecimalDivide { [CmdletBinding()] param([decimal]$Left,[decimal]$Right) [decimal]$value=$Left; $value /= $Right; return "after=$value" }""";
        source += Environment.NewLine + """
            function Get-UpdateIterator {
                [CmdletBinding()] param([int]$Right)
                [int]$value=7
                [int]$count=0
                for (; $count -lt 3; $value %= $Right) {
                    $count++
                    if ($count -lt 3) { continue }
                }
                return "after=$value;count=$count"
            }
            function Get-UpdateInitializer {
                [CmdletBinding()] param([int]$Right)
                [int]$value=7
                [int]$count=0
                for ($value=1 % $Right; $count -lt 3; $count++) { }
                return "after=$value;count=$count"
            }
            function Get-UpdateCaught {
                [CmdletBinding()] param([int]$Left,[int]$Right)
                [int]$value=$Left
                try { $value += $Right; return "after=$value" }
                catch [System.Management.Automation.PSInvalidCastException] { return "cast=$value" }
            }
            function Get-UpdateBridge {
                [CmdletBinding()] param([int]$Left,[int]$Right)
                try { return Get-UpdateintAdd -Left $Left -Right $Right }
                catch [System.Management.Automation.PSInvalidCastException] { return 'bridge-cast' }
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NumericMutationIdentity", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(59, result.Manifest!.CompiledMethods);
        const string probe = """
            foreach($type in 'byte','sbyte','int16','uint16','int','uint32','long','uint64','decimal') {
                $minimum=([type]$type)::MinValue
                $maximum=([type]$type)::MaxValue
                $operations=@('Add','Subtract','Multiply','Remainder','Increment','Decrement')
                if($type -eq 'decimal') { $operations += 'Divide' }
                foreach($name in $operations) {
                    foreach($pair in @(@(10,3),@(10,0),@($maximum,2),@($minimum,1))) {
                        foreach($action in 'Stop','SilentlyContinue') {
                            try { & ('Get-Update'+$type+$name) -Left $pair[0] -Right $pair[1] -ErrorAction $action }
                            catch {
                                $_.FullyQualifiedErrorId
                                $_.Exception.GetType().FullName
                                $_.Exception.Message
                                $observedException=$_.Exception.InnerException
                                while($null -ne $observedException) {
                                    $observedException.GetType().FullName
                                    $observedException.Message
                                    $observedException=$observedException.InnerException
                                }
                                [string]$_.CategoryInfo.Category
                                if($null -eq $_.TargetObject) { 'target:null' }
                                else { 'target:'+$_.TargetObject.GetType().FullName+';'+[string]$_.TargetObject }
                            }
                        }
                    }
                    & ('Get-Update'+$type+$name) -Left $maximum -Right 0 -ErrorAction SilentlyContinue
                    & ('Get-Update'+$type+$name) -Left $minimum -Right 1 -ErrorAction SilentlyContinue
                }
            }
            foreach($right in 0,2) {
                Get-UpdateIterator -Right $right -ErrorAction SilentlyContinue
                Get-UpdateInitializer -Right $right -ErrorAction SilentlyContinue
            }
            Get-UpdateCaught -Left ([int]::MaxValue) -Right 2
            Get-UpdateCaught -Left 10 -Right 2
            Get-UpdateBridge -Left ([int]::MaxValue) -Right 2
            Get-UpdateBridge -Left 10 -Right 2
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-numeric-mutation-identity");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-numeric-mutation-identity");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original:" + Environment.NewLine + original.StandardOutput + "Generated:" + Environment.NewLine + compiled.StandardOutput);
    }
}
