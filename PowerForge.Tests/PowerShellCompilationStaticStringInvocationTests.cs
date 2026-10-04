using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void StaticStringInvocation_PreservesUntypedConversionAndOfflineDistinguishedNames(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        var sourcePath = Path.Combine(FindStaticNumericRepositoryRoot(), "Benchmarks", "PowerShellCompilation",
            "Corpus", "ExternalWorkflows", "PSSharedGoods", "FullModule", "Private", "Deprecated", "ActiveDirectory",
            "Get-WinADOrganizationalUnitFromDN.ps1");
        var source = File.ReadAllText(sourcePath) + """

            function Get-StaticString { [CmdletBinding()]param($Text,$Pattern,$Trace)
                try { [Regex]::Match($Text,$Pattern).Value; [Regex]::Escape($Text); [string]::IsNullOrEmpty($Text) }
                finally { $Trace.Add('finally') }
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.StaticStringArguments",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.True(built.Manifest!.CompiledMethods == 2, string.Join(Environment.NewLine,
            built.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit => unit.DiagnosticChain.Select(cause => unit.Name + ": " + cause.Message))));
        Assert.All(built.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.Name is "Get-StaticString" or "Get-WinADOrganizationalUnitFromDN"),
            unit => { Assert.True(unit.EmittedClrMethod); Assert.True(unit.UsesNativeFunctionBinding); Assert.False(unit.RetainedHostedSource); });
        const string probe = """
            Add-Type -TypeDefinition @'
            public sealed class StaticStringCallback {
                public static System.Collections.Generic.List<string> Trace;
                public bool Fail;
                public override string ToString() {
                    Trace.Add("stringify");
                    if (Fail) throw new System.InvalidOperationException("stringify failed");
                    return "OU=Callback";
                }
            }
            '@
            foreach($sample in @('CN=User,OU=People,OU=Office,DC=offline,DC=invalid','OU=People,DC=offline,DC=invalid','CN=User,DC=offline,DC=invalid',"OU=One`nOU=Two",'',42,$null,@('OU=One','OU=Two'))) {
                try {
                    $result=@(Get-WinADOrganizationalUnitFromDN -DistinguishedName $sample -ErrorAction Stop)
                    [pscustomobject]@{kind='dn';input=$sample;result=$result}|ConvertTo-Json -Depth 8 -Compress
                } catch { [pscustomobject]@{kind='dn';input=$sample;id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}|ConvertTo-Json -Depth 8 -Compress }
            }
            foreach($mode in 'string','null','number','array','callback','throw') {
                foreach($pattern in '^OU=','[') {
                    $global:StaticStringTrace=[Collections.Generic.List[string]]::new()
                    $global:StaticStringMode=$mode
                    [StaticStringCallback]::Trace=$global:StaticStringTrace
                    $value=switch($mode) {
                        'string' {'OU=One'}
                        'null' {$null}
                        'number' {42}
                        'array' {,@('OU=One','OU=Two')}
                        default {
                            $item=[StaticStringCallback]::new()
                            $item.Fail=$mode -eq 'throw'
                            $item
                        }
                    }
                    $records=[Collections.Generic.List[object]]::new()
                    $caught=$null
                    try { Get-StaticString -Text $value -Pattern $pattern -Trace $global:StaticStringTrace -ErrorAction Stop | ForEach-Object { $records.Add($_) } }
                    catch { $caught=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine} }
                    [pscustomobject]@{kind='conversion';mode=$mode;pattern=$pattern;result=@($records.ToArray());caught=$caught;trace=@($global:StaticStringTrace.ToArray())}|ConvertTo-Json -Depth 8 -Compress
                }
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Equal(20, generated.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("stringify", generated);
        Assert.Contains("finally", generated);
    }

    [Fact]
    public void StaticStringInvocation_RequiresClosedOverloadAndPreservesStrictBoundary()
    {
        const string source = """
            function Get-Closed { [CmdletBinding()]param($Text) return [Regex]::Escape($Text) }
            function Get-Overloaded { [CmdletBinding()]param($Text) return [string]::Equals($Text,'x') }
            function Get-ObjectArgument { [CmdletBinding()]param($Text) return [string]::Format('{0}',$Text) }
            """;
        var document = PowerShellSourceParser.Parse(source, Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "static-string.psm1"));
        var hybrid = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.Contains(hybrid.Emitted.Methods, method => method.GeneratedName == "Get_Closed" && method.NativeFunctionBinding is not null);
        Assert.DoesNotContain(hybrid.Emitted.Methods, method => method.GeneratedName == "Get_Overloaded" && method.NativeFunctionBinding is not null);
        Assert.DoesNotContain(hybrid.Emitted.Methods, method => method.GeneratedName == "Get_ObjectArgument" && method.NativeFunctionBinding is not null);
        var strict = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.TypedLibrary);
        Assert.DoesNotContain(strict.Emitted.Methods, method => method.GeneratedName == "Get_Closed");
    }
}
