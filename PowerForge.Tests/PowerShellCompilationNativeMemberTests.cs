using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> NativeMemberHosts()
        => StatementErrorHosts().SelectMany(configuration => new[] { 0, 2 }
            .Select(version => configuration.Concat(new object[] { version }).ToArray()));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(NativeMemberHosts))]
    public void NativeMembers_PreserveAdaptersEnumerationAndErrorContinuation(string framework, string host, int strictVersion)
    {
        using var fixture = ArtifactFixture.Create((strictVersion == 0 ? "Set-StrictMode -Off" : "Set-StrictMode -Version 2") + Environment.NewLine + """
            function Read-NativeName { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); 'before'; $Value.Name; 'after' }
            function Read-NativeLength { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); return $Value.Length }
            function Read-NativeCount { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); return $Value.Count }
            function Read-NativeNested { [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value); return $Value.Name.Length }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeMembers", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.CompiledMethods == 4, string.Join(Environment.NewLine,
            result.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        const string probe = """
            $thrower=[pscustomobject]@{}
            $thrower | Add-Member -MemberType ScriptProperty -Name Name -Value { throw 'getter failed' }
            $values=@(@{value=$null},@{value='text'},@{value=1},@{value=@()},@{value=@(1,2)},
                @{value=@{Name='key';Count='shadow'}},@{value=[ordered]@{Name='ordered'}},
                @{value=[pscustomobject]@{Name='note'}},@{value=@([pscustomobject]@{Name='one'},[pscustomobject]@{Name='two'})},
                @{value=$thrower})
                foreach ($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
                    foreach ($name in 'Read-NativeName','Read-NativeLength','Read-NativeCount','Read-NativeNested') {
                        for ($index=0;$index -lt $values.Count;$index++) {
                            $Error.Clear(); $caught=$null; $records=@(); $emitted=@()
                            try { $records=@(& $name -Value $values[$index].value -ErrorAction $preference -OutVariable emitted 2>$null) }
                            catch { $caught=$_.FullyQualifiedErrorId }
                            [pscustomobject]@{name=$name;case=$index;preference=$preference;records=$records;emitted=@($emitted);caught=$caught;
                                errors=@($Error | ForEach-Object { [pscustomobject]@{id=$_.FullyQualifiedErrorId;category=[string]$_.CategoryInfo.Category;message=$_.Exception.Message} })} | ConvertTo-Json -Compress -Depth 12
                        }
                    }
                }
            """;
        var original = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; Import-Module $modulePath; " + probe,
            fixture.RootPath, "original-native-members");
        var compiled = RunStatementErrorProbe(host, "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; Import-Module $modulePath; " + probe,
            fixture.RootPath, "compiled-native-members");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(160, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        if (strictVersion == 2) Assert.Contains("PropertyNotFoundStrict", original.StandardOutput);
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.True(original.StandardError == compiled.StandardError,
            "Original errors: " + original.StandardError + Environment.NewLine + "Generated errors: " + compiled.StandardError);
    }
}
