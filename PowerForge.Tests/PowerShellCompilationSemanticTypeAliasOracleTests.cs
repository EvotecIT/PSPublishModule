using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void DeliveredTypeAliasesPreserveNativeRegistrationIdentityAndRecoveryOnBothHosts()
    {
        using var fixture = OracleFixture.Create(string.Empty);
        var root = Path.Combine(fixture.RootPath, "module");
        Directory.CreateDirectory(root);
        var library = Path.Combine(root, "Parcels.dll");
        static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        File.WriteAllText(fixture.ScriptPath, "Add-Type -TypeDefinition 'namespace Anywhere.Types { public sealed class Parcel { public string Text; public Parcel(string text) { Text=text; } } }' -OutputAssembly " + Quote(library));
        var desktopProfile = PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId;
        var runner = new PowerShellCompilationSemanticOracleRunner();
        var dependency = runner.Observe(new PowerShellCompilationSemanticOracleRequest(desktopProfile, fixture.ScriptPath)
        {
            ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(
                PowerShellCompilationSemanticHostArtifactPinCatalog.Get(desktopProfile))
        });
        Assert.Empty(dependency.ErrorRecords);
        Assert.True(File.Exists(library));
        File.WriteAllText(Path.Combine(root, "Register.ps1"), """
            function Register-ImportedNames {
                param([hashtable] $Names)
                $registry = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')
                foreach ($name in $Names.Keys) { $registry::Add($name, $Names[$name]) }
            }
            """);
        File.WriteAllText(Path.Combine(root, "Names.ps1"), """
            $registered = @{ ParcelArgument = 'Anywhere.Types.Parcel' }
            Register-ImportedNames -Names $registered
            """);
        File.WriteAllText(Path.Combine(root, "AliasModule.psm1"), """
            Add-Type -Path "$PSScriptRoot/Parcels.dll"
            . "$PSScriptRoot/Register.ps1"
            . "$PSScriptRoot/Names.ps1"
            function Use-ImportedParcel {
                param([ParcelArgument] $Value, [switch] $Fail, [switch] $Replace)
                if ($Fail) { throw 'controlled alias failure' }
                if ($Replace) { $Value = 'replacement' }
                $Value
            }
            function New-ImportedParcel { param([string] $Text) [ParcelArgument]::new($Text) }
            function Read-ImportedPipeline {
                [CmdletBinding()] param([Parameter(ValueFromPipeline)][ParcelArgument] $Value, [switch] $Fail)
                begin { 'begin' }
                process { try { if ($Fail) { throw 'pipeline alias failure' }; $Value.Text } finally { $script:Finalized++ } }
                end { 'end' }
            }
            Export-ModuleMember -Function Use-ImportedParcel, New-ImportedParcel, Read-ImportedPipeline
            """);
        var manifest = Path.Combine(root, "AliasModule.psd1");
        File.WriteAllText(manifest, "@{RootModule='AliasModule.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Use-ImportedParcel','New-ImportedParcel','Read-ImportedPipeline')}");
        var modernHost = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(modernHost));
        foreach (var (framework, profile, host) in new[]
                 {
                     ("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, modernHost),
                     ("net472", desktopProfile, (string?)null)
                 })
        {
            var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                Path.Combine(root, "AliasModule.psm1"), Path.Combine(fixture.RootPath, "built-" + framework), "AliasModule", PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
            { TargetFramework = framework, SemanticProfileId = profile, ResourceMode = PowerShellCompilationResourceMode.CompleteModule,
                ModuleManifestPath = manifest });
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
            foreach (var name in new[] { "Use-ImportedParcel", "New-ImportedParcel", "Read-ImportedPipeline" })
            {
                var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == name);
                Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
                Assert.False(unit.RetainedHostedSource);
            }
            Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetType("Anywhere.Types.Parcel", false) is not null);
            PowerShellCompilationSemanticOracleEnvelope Observe(string path)
            {
                File.WriteAllText(fixture.ScriptPath, "$global:VerbosePreference='SilentlyContinue'\nImport-Module " + Quote(path) +
                    " -Force -ErrorAction Stop\n" + """
                    $registry = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')
                    $type = $registry::Get['ParcelArgument']
                    [void]$registry::Remove('ParcelArgument')
                    $missing = try { Use-ImportedParcel -Value 'before' -ErrorAction Stop; 'unexpected' } catch { $_.FullyQualifiedErrorId }
                    $registry::Add('ParcelArgument', $type)
                    $first = New-ImportedParcel -Text 'parcel'
                    $same = Use-ImportedParcel $first
                    $converted = Use-ImportedParcel 'converted'
                    $failure = try { Use-ImportedParcel $first -Fail; 'unexpected' } catch { $_.FullyQualifiedErrorId }
                    $replaced = Use-ImportedParcel $first -Replace
                    $pipeline = @('one','two' | Read-ImportedPipeline)
                    $pipelineFailure = try { 'three' | Read-ImportedPipeline -Fail -ErrorAction Stop; 'unexpected' } catch { $_.FullyQualifiedErrorId }
                    $stopped = @('four','five' | Read-ImportedPipeline | Select-Object -First 2)
                    $pipelineRecovery = @('six' | Read-ImportedPipeline)
                    $same.Text = 'changed'
                    [ordered]@{
                        missing = $missing
                        same = [object]::ReferenceEquals($first,$same)
                        mutation = $first.Text
                        converted = $converted.Text
                        failure = $failure
                        replacement = $replaced.Text
                        replacementType = $replaced.GetType().FullName
                        pipeline = $pipeline
                        pipelineFailure = $pipelineFailure
                        stopped = $stopped
                        pipelineRecovery = $pipelineRecovery
                        finalized = & (Get-Module AliasModule) { $script:Finalized }
                        identity = (Get-Command Use-ImportedParcel).Parameters.Value.ParameterType.FullName
                        recovered = (Use-ImportedParcel 'after').Text
                    } | ConvertTo-Json -Depth 5 -Compress
                    """);
                return runner.Observe(new PowerShellCompilationSemanticOracleRequest(profile, fixture.ScriptPath)
                {
                    HostExecutablePath = host,
                    ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(
                        PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profile))
                });
            }
            var original = Observe(manifest);
            var generated = Observe(built.ArtifactPath!);
            Assert.Empty(original.ErrorRecords);
            Assert.True(!generated.ErrorRecords.Any(), System.Text.Json.JsonSerializer.Serialize(generated.ErrorRecords));
            var value = Assert.Single(original.Success).Value;
            Assert.Contains("\"same\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"mutation\":\"changed\"", value, StringComparison.Ordinal);
            Assert.Contains("\"identity\":\"Anywhere.Types.Parcel\"", value, StringComparison.Ordinal);
            Assert.Contains("\"recovered\":\"after\"", value, StringComparison.Ordinal);
            Assert.Contains("\"replacement\":\"replacement\"", value, StringComparison.Ordinal);
            Assert.Contains("\"replacementType\":\"Anywhere.Types.Parcel\"", value, StringComparison.Ordinal);
            Assert.DoesNotContain("unexpected", value, StringComparison.Ordinal);
            Assert.Contains("\"pipeline\":[\"begin\",\"one\",\"two\",\"end\"]", value, StringComparison.Ordinal);
            Assert.Contains("\"pipelineRecovery\":[\"begin\",\"six\",\"end\"]", value, StringComparison.Ordinal);
            var differences = PowerShellCompilationSemanticOracleComparer.Compare(original, generated);
            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Select(static difference =>
                difference.Path + ": original=" + difference.Expected + "; generated=" + difference.Actual)));
        }
    }
}
