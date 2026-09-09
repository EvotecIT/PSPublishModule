namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> NativeRegistrationCases()
        => StatementErrorHosts().SelectMany(host => new[] { " ", "; ", "\n" }
            .Select(separator => new object[] { host[0], host[1], separator }));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(NativeRegistrationCases))]
    public void NativeRegistration_PreservesAdjacentDeclarationsAliasesAndCommands(string framework, string host, string separator)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-First { [CmdletBinding()][Alias('first')] param([ValidateNotNull()][string]$Value) 'first:'+ $Value }" + separator +
            "function Get-Second { [CmdletBinding()] param([ValidateNotNull()][string]$Value) Get-First -Value $Value; 'second' }" + separator +
            "Export-ModuleMember -Function Get-First,Get-Second -Alias first", ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeRegistration", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        const string probe = "@(Get-Second 'ready'; first 'alias') | ConvertTo-Json -Compress";
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "native-registration");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "native-registration");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("first:alias", original.StandardOutput);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
