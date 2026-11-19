using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void DeliveredCompatibilityVariantsPreserveNativeSelectionAndConflictingOwnersStayHostedOnBothHosts()
    {
        using var fixture = OracleFixture.Create(string.Empty);
        var root = Path.Combine(fixture.RootPath, "module");
        var modern = Path.Combine(root, "Core", "Types.dll");
        var legacy = Path.Combine(root, "Desktop", "Types.dll");
        var conflict = Path.Combine(fixture.RootPath, "Other.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(modern)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        File.WriteAllText(fixture.ScriptPath, """
            $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
            try {
                foreach ($item in @(
            """ + "\n@(" + Quote(modern) + ",'modern'),@(" + Quote(legacy) + ",'legacy'),@(" + Quote(conflict) + ",'conflict')\n" + """
                )) {
                    $parameters = New-Object System.CodeDom.Compiler.CompilerParameters
                    $parameters.GenerateExecutable = $false
                    $parameters.OutputAssembly = $item[0]
                    [void]$parameters.ReferencedAssemblies.Add('System.dll')
                    $code = 'namespace CompilerProof.Variants { public sealed class Token { public string Value { get { return "' + $item[1] + '"; } } } }'
                    $result = $provider.CompileAssemblyFromSource($parameters, $code)
                    if ($result.Errors.HasErrors) { throw ($result.Errors | Out-String) }
                }
            } finally { $provider.Dispose() }
            """);
        var runner = new PowerShellCompilationSemanticOracleRunner();
        var desktop = PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId;
        var compilation = runner.Observe(new PowerShellCompilationSemanticOracleRequest(desktop, fixture.ScriptPath)
        {
            ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(
                PowerShellCompilationSemanticHostArtifactPinCatalog.Get(desktop))
        });
        Assert.Empty(compilation.ErrorRecords);
        Assert.Equal(System.Reflection.AssemblyName.GetAssemblyName(modern).FullName,
            System.Reflection.AssemblyName.GetAssemblyName(legacy).FullName);
        Assert.NotEqual(File.ReadAllBytes(modern), File.ReadAllBytes(legacy));
        var module = Path.Combine(root, "Variants.psm1");
        var manifest = Path.Combine(root, "Variants.psd1");
        File.WriteAllText(manifest, "@{RootModule='Variants.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Read-Variant')}");
        File.WriteAllText(module, """
            if ($PSVersionTable.PSVersion.Major -ge 7) { Add-Type -Path "$PSScriptRoot/Core/Types.dll" }
            else { Add-Type -Path "$PSScriptRoot/Desktop/Types.dll" }
            function Read-Variant { [CmdletBinding()] param([CompilerProof.Variants.Token] $Value) $Value.Value }
            Export-ModuleMember -Function Read-Variant
            """);
        foreach (var ambiguous in new[] { false, true })
        {
            if (ambiguous) File.Copy(conflict, Path.Combine(root, "Other.dll"));
            foreach (var (framework, profile, host, expected) in new[]
                     {
                         ("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                             Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH"), "modern"),
                         ("net472", desktop, (string?)null, "legacy")
                     })
            {
                var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(module,
                    Path.Combine(fixture.RootPath, "built-" + framework + "-" + ambiguous), "Variants",
                    PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
                    allowUnreviewedDependencyResolution: true)
                {
                    TargetFramework = framework, SemanticProfileId = profile, ModuleManifestPath = manifest,
                    ResourceMode = PowerShellCompilationResourceMode.CompleteModule
                });
                Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
                var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, item => item.Name == "Read-Variant");
                Assert.Equal(!ambiguous, unit.EmittedClrMethod);
                Assert.Equal(ambiguous, unit.RetainedHostedSource);
                PowerShellCompilationSemanticOracleEnvelope Observe(string path)
                {
                    File.WriteAllText(fixture.ScriptPath, "$global:VerbosePreference='SilentlyContinue'\nImport-Module " + Quote(path) +
                        " -Force -ErrorAction Stop\n$token=[CompilerProof.Variants.Token]::new()\n" +
                        "[ordered]@{value=(Read-Variant $token);same=[object]::ReferenceEquals($token.GetType(),(Get-Command Read-Variant).Parameters['Value'].ParameterType)} | ConvertTo-Json -Compress");
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
                Assert.Empty(generated.ErrorRecords);
                Assert.Equal("{\"value\":\"" + expected + "\",\"same\":true}", Assert.Single(original.Success).Value);
                var differences = PowerShellCompilationSemanticOracleComparer.Compare(original, generated);
                Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Select(static difference => difference.Path)));
            }
        }
    }
}
