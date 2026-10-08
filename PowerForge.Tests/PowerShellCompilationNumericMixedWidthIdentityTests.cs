using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NumericStatementErrors_PreserveMixedWidthUpdates(string framework, string host)
    {
        var pairs = new[] { ("int", "long"), ("uint32", "uint64"), ("sbyte", "int"), ("int16", "long"), ("uint32", "long"), ("int", "uint32"), ("uint32", "int"), ("byte", "uint32"), ("uint64", "byte"), ("byte", "uint64"), ("long", "int") };
        var updates = new[] { ("Add", "+="), ("Subtract", "-="), ("Multiply", "*="), ("Remainder", "%=") };
        var source = string.Join(Environment.NewLine, pairs.SelectMany(pair => updates.Select(update =>
            $"function Get-Mixed{pair.Item1}{pair.Item2}{update.Item1} {{ [CmdletBinding()] param([{pair.Item1}]$Left,[{pair.Item2}]$Right) [{pair.Item1}]$value=$Left; $value {update.Item2} $Right; return \"after=$value\" }}")));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NumericMixedWidthIdentity", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(44, result.Manifest!.CompiledMethods);
        const string probe = """
            foreach($types in @(@('int','long'),@('uint32','uint64'),@('sbyte','int'),@('int16','long'),@('uint32','long'),@('int','uint32'),@('uint32','int'),@('byte','uint32'),@('uint64','byte'),@('byte','uint64'),@('long','int'))) {
                $leftType=[type]$types[0]; $rightType=[type]$types[1]
                foreach($name in 'Add','Subtract','Multiply','Remainder') {
                    foreach($pair in @(@(0,$rightType::MaxValue),@($leftType::MaxValue,$rightType::MaxValue),@($leftType::MinValue,$rightType::MinValue),@(10,0),@($leftType::MaxValue,2))) {
                        $label=$types[0]+'/'+$types[1]+'/'+$name+'/'+$pair[0]+'/'+$pair[1]
                        $label
                        try { & ('Get-Mixed'+$types[0]+$types[1]+$name) -Left $pair[0] -Right $pair[1] -ErrorAction Stop }
                        catch {
                            $_.FullyQualifiedErrorId
                            [string]$_.CategoryInfo.Category
                            if($null -eq $_.TargetObject) { 'target:null' }
                            else { 'target:'+$_.TargetObject.GetType().FullName+';'+[string]$_.TargetObject }
                            $observedException=$_.Exception
                            while($null -ne $observedException) {
                                $observedException.GetType().FullName
                                $observedException.Message
                                $observedException=$observedException.InnerException
                            }
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-numeric-mixed-width");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-numeric-mixed-width");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, compiled.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        var expected = original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var actual = compiled.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(expected.SequenceEqual(actual), "Native versus generated mismatch:" + Environment.NewLine +
            string.Join(Environment.NewLine, expected.Zip(actual, (left, right) => left == right ? null : "native: " + left + " | generated: " + right).Where(line => line is not null)));
    }
}
