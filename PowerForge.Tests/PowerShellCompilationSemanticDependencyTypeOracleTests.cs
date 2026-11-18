using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void BodyLoadedTypesPreserveNativeMetadataIdentityStateAndBindingOnBothHosts()
    {
        using var fixture = OracleFixture.Create(string.Empty);
        var moduleRoot = Path.Combine(fixture.RootPath, "module");
        var library = Path.Combine(moduleRoot, "Lib", "Types.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        const string dependency = """
            using System;
            using System.Management.Automation;
            namespace CompilerProof.Types {
                public enum Severity { Low, High }
                public sealed class Token {
                    public string Text { get; set; }
                    public Token() { Text = "default"; }
                    public Token(string text) { Text = text; }
                }
                public static class State {
                    private static int value;
                    public static void Add(int amount) { value += amount; }
                    public static int Read() { return value; }
                }
                public sealed class PrefixAttribute : ArgumentTransformationAttribute {
                    public static int Created;
                    private readonly string prefix;
                    public PrefixAttribute(string prefix) { this.prefix = prefix; Created++; }
                    public override object Transform(EngineIntrinsics engine, object input) {
                        if (input is Token) return input;
                        return new Token(prefix + LanguagePrimitives.ConvertTo<string>(input));
                    }
                }
                public sealed class RequiredTokenAttribute : ValidateArgumentsAttribute {
                    protected override void Validate(object input, EngineIntrinsics engine) {
                        Token token = input as Token;
                        if (token == null || token.Text.IndexOf("deny", StringComparison.Ordinal) >= 0)
                            throw new ValidationMetadataException("A permitted token is required.");
                    }
                }
            }
            """;
        File.WriteAllText(fixture.ScriptPath, "Add-Type -TypeDefinition @'\n" + dependency +
            "\n'@ -OutputAssembly '" + library.Replace("'", "''", StringComparison.Ordinal) + "'\n");
        var runner = new PowerShellCompilationSemanticOracleRunner();
        var desktopProfile = PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId;
        var compiler = runner.Observe(new PowerShellCompilationSemanticOracleRequest(desktopProfile, fixture.ScriptPath)
        {
            ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(
                PowerShellCompilationSemanticHostArtifactPinCatalog.Get(desktopProfile))
        });
        Assert.Empty(compiler.ErrorRecords);
        Assert.True(File.Exists(library), "The real Windows PowerShell dependency compiler must produce its assembly.");
        const string source = """
            $script:ModuleRoot = $PSScriptRoot
            Add-Type -Path "$script:ModuleRoot/Lib/Types.dll"
            $script:AtImport = [CompilerProof.Types.PrefixAttribute]::Created
            $script:Sequence = 0
            function New-TypeRecord {
                param([string] $Text)
                [CompilerProof.Types.Token]::new($Text)
            }
            function Use-TypeRecord {
                [CmdletBinding()]
                [OutputType([CompilerProof.Types.Token])]
                param(
                    [Parameter(ValueFromPipeline, ValueFromPipelineByPropertyName)]
                    [Alias('Record')]
                    [CompilerProof.Types.Prefix('p:')]
                    [CompilerProof.Types.RequiredToken()]
                    [CompilerProof.Types.Token] $Value,
                    [CompilerProof.Types.Severity] $Severity = [CompilerProof.Types.Severity]::Low
                )
                begin { $count = 0; 'begin' }
                process {
                    $count++
                    $script:Sequence++
                    [CompilerProof.Types.State]::Add($script:Sequence)
                    [pscustomobject]@{text=$Value.Text;type=$Value.GetType().FullName;severity=$Severity.ToString()}
                }
                end { "end=$count" }
            }
            function Convert-TypeRecord { param($Value) [CompilerProof.Types.Token] $Value }
            function Read-TypeState { [CompilerProof.Types.State]::Read() }
            Export-ModuleMember -Function New-TypeRecord, Use-TypeRecord, Convert-TypeRecord, Read-TypeState
            """;
        var module = Path.Combine(moduleRoot, "Types.psm1");
        var manifest = Path.Combine(moduleRoot, "Types.psd1");
        File.WriteAllText(manifest, "@{RootModule='Types.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('New-TypeRecord','Use-TypeRecord','Convert-TypeRecord','Read-TypeState')}");
        const string probe = """
            $savedVerbosePreference = $global:VerbosePreference
            $global:VerbosePreference = 'SilentlyContinue'
            try { $loaded = Import-Module $modulePath -PassThru -Force -ErrorAction Stop }
            finally { $global:VerbosePreference = $savedVerbosePreference }
            $atImport = @($loaded.Invoke({ $script:AtImport }))[0]
            $metadata = Get-Command Use-TypeRecord -ListImported
            $rows = @('one','deny','two' | Use-TypeRecord -Severity High -ErrorAction Continue 2>&1 | ForEach-Object {
                if ($_ -is [System.Management.Automation.ErrorRecord]) { 'error=' + $_.FullyQualifiedErrorId }
                else { $_ }
            })
            $third = @([pscustomobject]@{Record='three'} | Use-TypeRecord)
            $failure = try { Use-TypeRecord -Value 'deny' -ErrorAction Stop } catch { $_.FullyQualifiedErrorId }
            $fourth = @('four' | Use-TypeRecord)
            $record = New-TypeRecord -Text keep
            $converted = Convert-TypeRecord -Value $record
            $dictionary = Convert-TypeRecord -Value @{Text='hash'}
            [pscustomobject]@{
                atImport=$atImport;rows=$rows;third=$third;failure=$failure;fourth=$fourth
                state=(Read-TypeState);same=[object]::ReferenceEquals($record,$converted)
                converted=$dictionary.Text;convertedType=$dictionary.GetType().FullName
                parameterType=$metadata.Parameters['Value'].ParameterType.FullName
                enumType=$metadata.Parameters['Severity'].ParameterType.FullName
                outputTypes=@($metadata.OutputType.Name)
                attributes=@($metadata.Parameters['Value'].Attributes | ForEach-Object { $_.GetType().FullName })
            } | ConvertTo-Json -Depth 8 -Compress
            """;
        var modernHost = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(modernHost), "The exact PowerShell 7.6.6 host is required.");
        foreach (var (framework, profile, host, indirect) in new[]
                 {
                     ("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, modernHost, false),
                     ("net472", desktopProfile, (string?)null, false),
                     ("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, modernHost, true),
                     ("net472", desktopProfile, (string?)null, true)
                 })
        {
            File.WriteAllText(module, indirect
                ? source.Replace("Add-Type -Path \"$script:ModuleRoot/Lib/Types.dll\"",
                    "$loader = Get-Command Add-Type\n$library = \"$script:ModuleRoot/Lib/Types.dll\"\n& $loader -Path $library", StringComparison.Ordinal)
                : source);
            var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                module, Path.Combine(fixture.RootPath, "built-" + framework + "-" + indirect), "NativeDependencyTypes",
                PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
                allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = framework,
                SemanticProfileId = profile,
                ModuleManifestPath = manifest,
                ResourceMode = PowerShellCompilationResourceMode.CompleteModule
            });
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
            foreach (var name in new[] { "New-TypeRecord", "Use-TypeRecord", "Convert-TypeRecord", "Read-TypeState" })
            {
                var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == name);
                Assert.True(unit.EmittedClrMethod, name + " must have a complete CLR body.");
                Assert.False(unit.RetainedHostedSource, name + " must not fall back to its authored body.");
            }
            PowerShellCompilationSemanticOracleEnvelope Observe(string path)
            {
                File.WriteAllText(fixture.ScriptPath,
                    "$modulePath='" + path.Replace("'", "''", StringComparison.Ordinal) + "'\n" + probe);
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
            var value = Assert.Single(original.Success).Value;
            Assert.Contains("\"atImport\":0", value, StringComparison.Ordinal);
            Assert.Contains("\"state\":10", value, StringComparison.Ordinal);
            Assert.Contains("\"same\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"converted\":\"hash\"", value, StringComparison.Ordinal);
            Assert.Contains("\"parameterType\":\"CompilerProof.Types.Token\"", value, StringComparison.Ordinal);
            Assert.Contains("ParameterArgumentValidationError", value, StringComparison.Ordinal);
            var differences = PowerShellCompilationSemanticOracleComparer.Compare(original, generated);
            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Select(static difference =>
                difference.Path + ": original=" + difference.Expected + "; generated=" + difference.Actual)));
        }
    }
}
