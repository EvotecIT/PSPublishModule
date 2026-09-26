using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void NativeCatchState_RemainsRejectedWithoutNativeHost(string framework)
    {
        using var fixture = ArtifactFixture.Create("function Read-Error { param([string]$Text) try { [int]::Parse($Text) } catch { $PSItem.Exception.Message } }");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Strict, targetFramework: framework,
            capabilities: PowerShellCompilationCapabilities.TypedExecutable));
        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeCatchState_PreservesNestedErrorsAliasesAndRestoration(string framework, string host)
    {
        var workflow = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Convert-IPToBinary.ps1");
        Assert.Equal("cd183e6262bd24eeb7adc8ce471cf60de63a214d918c0a490336e1e77f58e9e3", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(workflow))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(File.ReadAllText(workflow) + Environment.NewLine + """
            function Read-CatchState {
                param([string]$Text, $_='previous')
                try { [int]::Parse($Text) }
                catch {
                    'outer:' + $_.FullyQualifiedErrorId
                    'same:' + [object]::ReferenceEquals($_,$PSItem)
                    try { [int]::Parse('nested bad') }
                    catch { 'inner:' + $PSItem.Exception.GetType().FullName }
                    'restored:' + $_.FullyQualifiedErrorId
                }
                finally { 'finally:' + $_ }
                'after:' + $_
            }
            function Read-Rethrow {
                param([string]$Text)
                try { try { [int]::Parse($Text) } catch { $_.FullyQualifiedErrorId; throw } }
                catch { 'outer:' + $PSItem.FullyQualifiedErrorId; $PSItem.Exception.GetType().FullName }
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeCatchState",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "Read-CatchState", "Read-Rethrow", "Convert-IPToBinary" })
        {
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.True(unit.UsesNativeFunctionBinding);
        }
        const string probe = """
            foreach($text in '12','bad','34','bad again') {
                [pscustomobject]@{text=$text;records=@(Read-CatchState -Text $text)} | ConvertTo-Json -Compress
                [pscustomobject]@{text=$text;records=@(Read-Rethrow -Text $text)} | ConvertTo-Json -Compress
            }
            foreach($ip in '192.168.1.1','0.0.0.0','255.255.255.255','bad','256.1.1.1') {
                [pscustomobject]@{ip=$ip;records=@(Convert-IPToBinary -IP $ip 3>&1 | ForEach-Object { if($null -eq $_){'<null>'}else{$_.ToString()} })} | ConvertTo-Json -Compress
            }
            & (Get-Command Convert-IPToBinary).Module {
                function ForEach-Object { throw 'offline rebound provider' }
                [pscustomobject]@{rebound=(@(Convert-IPToBinary -IP '192.168.1.1' 3>&1) -join '|')} | ConvertTo-Json -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("same:True", generated);
        Assert.Contains("after:previous", generated);
    }
}
