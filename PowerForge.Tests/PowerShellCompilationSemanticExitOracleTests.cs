using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void NativeExitPreservesConversionPartialOutputFinallyScriptTerminationAndHostRecoveryOnBothHosts()
    {
        const string source = """
            function Request-NativeExit {
                param([object] $Code, [object] $Trace, [bool] $Default, [bool] $Computed)
                try {
                    'partial'
                    if ($Computed) { exit (& { param($Item) $Item } $Code) }
                    if ($Default) { exit }
                    exit $Code
                } catch { [void] $Trace.Add('caught') }
                finally { [void] $Trace.Add('finally') }
                'unreachable'
            }
            function Read-TypedExit {
                [OutputType([int])]
                param([int] $Code)
                if ($Code -eq 0) { return 42 }
                exit $Code
            }
            Export-ModuleMember -Function Request-NativeExit, Read-TypedExit
            """;
        foreach (var (framework, profile, host) in new[]
                 {
                     ("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                         Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH")),
                     ("net472", PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, (string?)null)
                 })
        {
            using var fixture = OracleFixture.Create(string.Empty);
            var module = Path.Combine(fixture.RootPath, "NativeExit.psm1");
            File.WriteAllText(module, source);
            var input = new PowerShellCompilationInputResolver().Resolve(module,
                PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict);
            Assert.False(new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Strict, framework).CanProceed);
            var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(module,
                Path.Combine(fixture.RootPath, "built"), "NativeExit", PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
            { TargetFramework = framework, SemanticProfileId = profile });
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, item => item.Name == "Request-NativeExit");
            Assert.True(unit.EmittedClrMethod);
            Assert.False(unit.RetainedHostedSource);
            var typed = Assert.Single(built.Manifest.UnitDispositionLedger.Entries, item => item.Name == "Read-TypedExit");
            Assert.True(typed.EmittedClrMethod);
            Assert.False(typed.RetainedHostedSource);
            var child = Path.Combine(fixture.RootPath, "exit-child.ps1");
            File.WriteAllText(child, "param($Code, $Trace, [bool] $Default, [bool] $Computed, [bool] $Typed)\nif ($Typed) { Read-TypedExit -Code $Code; return }\nRequest-NativeExit -Code $Code -Trace $Trace -Default $Default -Computed $Computed\n'unexpected-child'\n");
            PowerShellCompilationSemanticOracleEnvelope Observe(string path)
            {
                File.WriteAllText(fixture.ScriptPath, "$global:VerbosePreference='SilentlyContinue'\nImport-Module '" +
                    path.Replace("'", "''", StringComparison.Ordinal) + "' -Force -ErrorAction Stop\n$child='" +
                    child.Replace("'", "''", StringComparison.Ordinal) + "'\n" + """
                    $cases = @(
                        @{Code=19;Default=$false}, @{Code=-3;Default=$false}, @{Code='23';Default=$false},
                        @{Code='bad';Default=$false}, @{Code=$null;Default=$false}, @{Code=1.5;Default=$false},
                        @{Code=$true;Default=$false}, @{Code=@(1,2);Default=$false}, @{Code=99;Default=$true}
                        @{Code=29;Default=$false;Computed=$true}
                    )
                    $rows = @(foreach ($case in $cases) {
                        $trace = [Collections.Generic.List[string]]::new()
                        $output = @(& $child -Code $case.Code -Default $case.Default -Trace $trace -Computed ([bool]$case.Computed))
                        [ordered]@{code=$LASTEXITCODE;output=$output;trace=@($trace.ToArray());recovered=$true}
                    })
                    $typedSuccess = @(& $child -Code 0 -Typed $true)
                    $typedExit = @(& $child -Code 37 -Typed $true)
                    $typedCode = $LASTEXITCODE
                    [ordered]@{rows=$rows;typedSuccess=$typedSuccess;typedExit=$typedExit;typedCode=$typedCode;errors=@($Error | ForEach-Object FullyQualifiedErrorId)} | ConvertTo-Json -Depth 7 -Compress
                    """);
                return new PowerShellCompilationSemanticOracleRunner().Observe(new PowerShellCompilationSemanticOracleRequest(profile, fixture.ScriptPath)
                {
                    HostExecutablePath = host,
                    ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(
                        PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profile))
                });
            }
            var original = Observe(module);
            var generated = Observe(built.ArtifactPath!);
            Assert.Empty(original.ErrorRecords);
            Assert.Empty(generated.ErrorRecords);
            var value = Assert.Single(original.Success).Value;
            Assert.Contains("\"code\":19,\"output\":[\"partial\"],\"trace\":[\"finally\"],\"recovered\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"code\":29,\"output\":[\"partial\"],\"trace\":[\"finally\"],\"recovered\":true", value, StringComparison.Ordinal);
            Assert.DoesNotContain("unreachable", value, StringComparison.Ordinal);
            Assert.DoesNotContain("unexpected-child", value, StringComparison.Ordinal);
            Assert.DoesNotContain("caught", value, StringComparison.Ordinal);
            Assert.Contains("\"typedSuccess\":[42],\"typedExit\":[],\"typedCode\":37", value, StringComparison.Ordinal);
            var differences = PowerShellCompilationSemanticOracleComparer.Compare(original, generated);
            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Select(static difference =>
                difference.Path + ": original=" + difference.Expected + "; generated=" + difference.Actual)));
        }
    }
}
