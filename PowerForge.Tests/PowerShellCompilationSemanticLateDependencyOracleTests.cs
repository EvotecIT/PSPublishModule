using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void DeliveredUnloadedTypesPreserveBindingFailureAndRecoverFromRelocatedCompiledInitializationOnBothHosts()
    {
        using var fixture = OracleFixture.Create(string.Empty);
        var root = Path.Combine(fixture.RootPath, "module");
        Directory.CreateDirectory(Path.Combine(root, "Lib"));
        var library = Path.Combine(root, "Lib", "Late.dll");
        File.WriteAllText(fixture.ScriptPath, "Add-Type -OutputAssembly '" + library.Replace("'", "''", StringComparison.Ordinal) + "' -TypeDefinition @'\n" +
            "namespace CompilerProof.Late { public sealed class Token { public string Value { get; set; } public Token(string value) { Value = value; } } }\n'@");
        var runner = new PowerShellCompilationSemanticOracleRunner();
        var desktopProfile = PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId;
        var compiler = runner.Observe(new PowerShellCompilationSemanticOracleRequest(desktopProfile, fixture.ScriptPath)
        {
            ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(
                PowerShellCompilationSemanticHostArtifactPinCatalog.Get(desktopProfile))
        });
        Assert.Empty(compiler.ErrorRecords);
        Assert.True(File.Exists(library));
        var module = Path.Combine(root, "Late.psm1");
        var manifest = Path.Combine(root, "Late.psd1");
        File.WriteAllText(module, """
            function Initialize-LateLibrary { Add-Type -Path "$PSScriptRoot/Lib/Late.dll" }
            function Read-LateValue {
                [CmdletBinding()]
                param([CompilerProof.Late.Token] $Value)
                $Value
            }
            Export-ModuleMember -Function Initialize-LateLibrary, Read-LateValue
            """);
        File.WriteAllText(manifest, "@{RootModule='Late.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Initialize-LateLibrary','Read-LateValue')}");
        const string probe = """
            $global:VerbosePreference = 'SilentlyContinue'
            Import-Module $modulePath -Force -ErrorAction Stop
            $global:VerbosePreference = 'Continue'
            $early = try { Read-LateValue -Value early -ErrorAction Stop; 'unexpected' } catch { $_.FullyQualifiedErrorId }
            Initialize-LateLibrary
            $token = [CompilerProof.Late.Token]::new('late')
            $returned = Read-LateValue -Value $token
            [ordered]@{
                early=$early
                value=$returned.Value
                same=[object]::ReferenceEquals($token, $returned)
                type=(Get-Command Read-LateValue).Parameters['Value'].ParameterType.FullName
            } | ConvertTo-Json -Compress
            """;
        var modernHost = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(modernHost), "The exact PowerShell 7.6.6 host is required.");
        foreach (var (framework, profile, host) in new[]
                 {
                     ("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, modernHost),
                     ("net472", desktopProfile, (string?)null)
                 })
        {
            var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                module, Path.Combine(fixture.RootPath, "built-" + framework), "LateDependency",
                PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
                allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = framework,
                SemanticProfileId = profile,
                ModuleManifestPath = manifest,
                ResourceMode = PowerShellCompilationResourceMode.CompleteModule
            });
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
            foreach (var name in new[] { "Initialize-LateLibrary", "Read-LateValue" })
            {
                var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == name);
                Assert.True(unit.EmittedClrMethod, name + " must have a complete CLR body.");
                Assert.False(unit.RetainedHostedSource);
            }
            PowerShellCompilationSemanticOracleEnvelope Observe(string path)
            {
                File.WriteAllText(fixture.ScriptPath, "$modulePath='" + path.Replace("'", "''", StringComparison.Ordinal) + "'\n" + probe);
                return runner.Observe(new PowerShellCompilationSemanticOracleRequest(profile, fixture.ScriptPath)
                {
                    HostExecutablePath = host,
                    ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(
                        PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profile))
                });
            }
            var original = Observe(manifest);
            var withdrawn = Path.Combine(fixture.RootPath, "withdrawn-" + framework);
            PowerShellCompilationPathSafety.EnsureContained(fixture.RootPath, root, "The source fixture must remain contained.");
            PowerShellCompilationPathSafety.EnsureContained(fixture.RootPath, withdrawn, "Withdrawn input must remain contained.");
            Directory.Move(root, withdrawn);
            PowerShellCompilationSemanticOracleEnvelope generated;
            try { generated = Observe(built.ArtifactPath!); }
            finally { Directory.Move(withdrawn, root); }
            Assert.Empty(original.ErrorRecords);
            Assert.Empty(generated.ErrorRecords);
            var value = Assert.Single(original.Success).Value;
            Assert.Contains("TypeNotFound", value, StringComparison.Ordinal);
            Assert.Contains("\"same\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"value\":\"late\"", value, StringComparison.Ordinal);
            Assert.Contains("CompilerProof.Late.Token", value, StringComparison.Ordinal);
            var differences = PowerShellCompilationSemanticOracleComparer.Compare(original, generated);
            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Select(static difference =>
                difference.Path + ": original=" + difference.Expected + "; generated=" + difference.Actual)));
        }
    }
}
