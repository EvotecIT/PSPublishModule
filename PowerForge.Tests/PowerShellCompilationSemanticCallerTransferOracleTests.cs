using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void CallerDirectedBreakAndContinuePreserveLoopsFinallyAndRecoveryOnBothHosts()
    {
        const string source = """
            function Request-CallerTransfer {
                param([bool] $Continue, [object] $Trace)
                try {
                    [void] $Trace.Add('request')
                    if ($Continue) { continue }
                    break
                } catch {
                    [void] $Trace.Add('caught')
                } finally {
                    [void] $Trace.Add('finally')
                }
                [void] $Trace.Add('after-request')
            }
            function Read-CallerTransfer {
                param([bool] $Continue)
                $trace = [Collections.Generic.List[string]]::new()
                foreach ($item in 1, 2, 3) {
                    [void] $trace.Add("item=$item")
                    Request-CallerTransfer -Continue $Continue -Trace $trace
                    [void] $trace.Add('after-call')
                }
                [void] $trace.Add('after-loop')
                $trace.ToArray()
            }
            function Request-LifecycleTransfer {
                [CmdletBinding()]
                param([Parameter(ValueFromPipeline)] $Value, [string] $Stage, [object] $Trace)
                begin { try { [void] $Trace.Add('begin'); if ($Stage -eq 'begin') { 'partial-begin'; break } } finally { [void] $Trace.Add('finally-begin') } }
                process { try { [void] $Trace.Add('process'); if ($Stage -eq 'process') { 'partial-process'; break }; $Value } finally { [void] $Trace.Add('finally-process') } }
                end { try { [void] $Trace.Add('end'); if ($Stage -eq 'end') { 'partial-end'; break } } finally { [void] $Trace.Add('finally-end') } }
            }
            Export-ModuleMember -Function Request-CallerTransfer, Read-CallerTransfer, Request-LifecycleTransfer
            """;
        const string probe = """
            $break = @(Read-CallerTransfer -Continue $false)
            $continue = @(Read-CallerTransfer -Continue $true)
            $native = [Collections.Generic.List[string]]::new()
            foreach ($item in 1, 2, 3) {
                [void] $native.Add("native=$item")
                Request-CallerTransfer -Continue $false -Trace $native
                [void] $native.Add('unexpected')
            }
            $lifecycle = @(foreach ($stage in 'begin', 'process', 'end') {
                $trace = [Collections.Generic.List[string]]::new()
                $output = @(foreach ($item in 1, 2) { 10, 20 | Request-LifecycleTransfer -Stage $stage -Trace $trace; 'unexpected' })
                [ordered]@{stage=$stage;output=$output;trace=@($trace.ToArray())}
            })
            [ordered]@{
                break = $break
                continue = $continue
                native = @($native.ToArray())
                recovered = @(Read-CallerTransfer -Continue $false)
                errors = @($Error | ForEach-Object { $_.FullyQualifiedErrorId })
                lifecycle = $lifecycle
            } | ConvertTo-Json -Depth 5 -Compress
            """;
        var modernHost = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(modernHost), "The exact PowerShell 7.6.6 host is required.");
        foreach (var (framework, profile, host) in new[]
                 {
                     ("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, modernHost),
                     ("net472", PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, (string?)null)
                 })
        {
            using var fixture = OracleFixture.Create(string.Empty);
            var module = Path.Combine(fixture.RootPath, "CallerTransfers.psm1");
            File.WriteAllText(module, source);
            var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                module, Path.Combine(fixture.RootPath, "built"), "CallerTransfers",
                PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
                allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = framework,
                SemanticProfileId = profile
            });
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
            foreach (var name in new[] { "Request-CallerTransfer", "Read-CallerTransfer", "Request-LifecycleTransfer" })
            {
                var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == name);
                Assert.True(unit.EmittedClrMethod, name + " must have a complete CLR body.");
                Assert.False(unit.RetainedHostedSource);
            }
            PowerShellCompilationSemanticOracleEnvelope Observe(string path, string script = probe)
            {
                File.WriteAllText(fixture.ScriptPath,
                    "$global:VerbosePreference='SilentlyContinue'\nImport-Module '" +
                    path.Replace("'", "''", StringComparison.Ordinal) + "' -Force -ErrorAction Stop\n" +
                    "$global:VerbosePreference='Continue'\n" + script);
                return new PowerShellCompilationSemanticOracleRunner().Observe(
                    new PowerShellCompilationSemanticOracleRequest(profile, fixture.ScriptPath)
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
            Assert.Contains("\"break\":[\"item=1\",\"request\",\"finally\",\"after-loop\"]", value, StringComparison.Ordinal);
            Assert.Contains("\"continue\":[\"item=1\",\"request\",\"finally\",\"item=2\",\"request\",\"finally\",\"item=3\",\"request\",\"finally\",\"after-loop\"]", value, StringComparison.Ordinal);
            Assert.Contains("\"native\":[\"native=1\",\"request\",\"finally\"]", value, StringComparison.Ordinal);
            Assert.Contains("\"errors\":[]", value, StringComparison.Ordinal);
            Assert.Contains("partial-begin", value, StringComparison.Ordinal);
            Assert.Contains("partial-process", value, StringComparison.Ordinal);
            Assert.Contains("partial-end", value, StringComparison.Ordinal);
            var differences = PowerShellCompilationSemanticOracleComparer.Compare(original, generated);
            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Select(static difference =>
                difference.Path + ": original=" + difference.Expected + "; generated=" + difference.Actual)));
            const string outsideLoop = "'before-transfer'; Request-CallerTransfer -Continue $false -Trace ([Collections.Generic.List[string]]::new()); 'unexpected-after-transfer'";
            var originalOutside = Observe(module, outsideLoop);
            var generatedOutside = Observe(built.ArtifactPath!, outsideLoop);
            Assert.Equal("before-transfer", Assert.Single(originalOutside.Success).Value);
            Assert.Empty(PowerShellCompilationSemanticOracleComparer.Compare(originalOutside, generatedOutside));
        }
    }
}
