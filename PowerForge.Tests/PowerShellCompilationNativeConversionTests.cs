using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeConversions_PreserveCastsCallbacksAndFailedAssignment(string framework, string host)
    {
        var casts = new[] { "int", "string", "object", "bool", "double", "int[]", "datetime", "Nullable[int]" };
        var functions = string.Join(Environment.NewLine, casts.Select((type, index) =>
            "function Read-NativeCast" + index + " { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); " +
            "$seen='before'; $OFS=':'; $copy='prior'; $copy=[" + type + "]$Value; \"state=$seen;ofs=$OFS;status=$?\"; return ,$copy }"));
        const string support = """
            function New-NativeCastToken {
                $token=[pscustomobject]@{}
                Add-Member -InputObject $token -MemberType ScriptMethod -Name ToString -Force -Value {
                    $before=(Get-Variable -Scope 1 -Name seen).Value
                    Set-Variable -Scope 1 -Name seen -Value 'changed'
                    Set-Variable -Scope 1 -Name OFS -Value '/'
                    return "seen=$before"
                }
                $token
            }
            """;
        using var fixture = ArtifactFixture.Create(functions + Environment.NewLine + support, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeConversions", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == casts.Length, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            Add-Type -TypeDefinition 'public sealed class NativeCastFailure { public override string ToString() { throw new System.InvalidOperationException("cast failed"); } }'
            $values=@(@{value=$null},@{value=''},@{value='12'},@{value='bad'},@{value=12.5},@{value=[psobject]12.5},
                @{value=@()},@{value=@(1,2)},@{value=@('1','bad')},@{value=(New-NativeCastToken)},@{value=(New-Object NativeCastFailure)})
            foreach ($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
                for ($cast=0;$cast -lt 8;$cast++) {
                    for ($index=0;$index -lt $values.Count;$index++) {
                        $name='Read-NativeCast'+$cast
                        $Error.Clear(); $caught=$null; $records=@(); $emitted=@()
                        try { $records=@(& $name -Value $values[$index].value -ErrorAction $preference -OutVariable emitted 2>$null) }
                        catch { $caught=$_.FullyQualifiedErrorId }
                        [pscustomobject]@{cast=$cast;value=$index;preference=$preference;records=$records;emitted=@($emitted);caught=$caught;
                            errors=@($Error | ForEach-Object { [pscustomobject]@{id=$_.FullyQualifiedErrorId;category=[string]$_.CategoryInfo.Category;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine} })} | ConvertTo-Json -Compress -Depth 12
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-conversions");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-conversions");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(352, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Contains("state=changed;ofs=/", original.StandardOutput);
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}
