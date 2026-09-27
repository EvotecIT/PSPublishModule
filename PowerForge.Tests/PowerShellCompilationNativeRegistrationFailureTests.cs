namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeRegistration_PreservesProtectedAuthoredBodyDeclarations(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Original { [CmdletBinding()] param([int]$Value) data { 'retained' } }
            Set-Item -LiteralPath Function:\Read-Locked -Value (Get-Item Function:\Read-Original).ScriptBlock -Options ReadOnly
            Set-Item -LiteralPath Function:\Read-Constant -Value (Get-Item Function:\Read-Original).ScriptBlock -Options Constant
            function Read-Locked { [CmdletBinding()] param([ValidateRange(0,5)][int]$Value) $Value + 1 }
            function Read-Constant { [CmdletBinding()] param([ValidateRange(0,5)][int]$Value) $Value + 2 }
            Export-ModuleMember -Function Read-Locked,Read-Constant
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeRegistrationFailure", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "Read-Locked", "Read-Constant" })
            Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name && unit.EmittedClrMethod);
        const string probe = """
            $failures=@($Error | ForEach-Object {$_.FullyQualifiedErrorId+'|'+$_.Exception.GetType().FullName})
            $values=@(Read-Locked;Read-Constant)
            [pscustomobject]@{failures=$failures;values=$values} | ConvertTo-Json -Compress
            """;
        var original = RunStatementErrorProbe(host, "$Error.Clear(); Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "' -ErrorAction SilentlyContinue; " + probe, fixture.RootPath, "original-protected-registration");
        var generated = RunStatementErrorProbe(host, "$Error.Clear(); Import-Module '" + EscapeStatementErrorPath(built.ArtifactPath!) +
            "' -ErrorAction SilentlyContinue; " + probe, fixture.RootPath, "generated-protected-registration");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, generated.ExitCode);
        Assert.True(original.StandardOutput == generated.StandardOutput,
            "Original:" + original.StandardOutput + Environment.NewLine + "Generated:" + generated.StandardOutput);
        Assert.Contains("FunctionNotWritable", generated.StandardOutput);
        Assert.Contains("retained", generated.StandardOutput);
    }
}
