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
                public sealed class Box<T> { }
                public static class Slots<T> {
                    public static T[] Values = new T[2];
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
            $classFiles = @(Get-ChildItem -Path $PSScriptRoot/OwnedClasses/*.ps1)
            foreach ($classFile in $classFiles) { . $classFile.FullName }
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
            function Set-TypeSlot {
                [CmdletBinding()]
                param([int] $Index, $Value)
                [CompilerProof.Types.Slots[int]]::Values[$Index] = $Value
                [CompilerProof.Types.Slots[int]]::Values[$Index]
            }
            function Read-TypeBox([CompilerProof.Types.Box[int]] $Value) { $Value }
            function Read-ObservedClass {
                param([int] $Count, [switch] $TypeValue)
                $argument = if ($TypeValue) { [int] } else { 'text' }
                try {
                    $record = [CompilerProofObservedRecord]::new($argument, $Count)
                    [ordered]@{value=[string]$record.Value;count=$record.Count}
                } catch {
                    [ordered]@{
                        id=$_.FullyQualifiedErrorId
                        type=$_.Exception.GetType().Name
                        column=$_.InvocationInfo.OffsetInLine
                    }
                }
            }
            Export-ModuleMember -Function New-TypeRecord, Use-TypeRecord, Convert-TypeRecord, Read-TypeState, Set-TypeSlot, Read-TypeBox, Read-ObservedClass
            """;
        var module = Path.Combine(moduleRoot, "Types.psm1");
        var manifest = Path.Combine(moduleRoot, "Types.psd1");
        var classes = Directory.CreateDirectory(Path.Combine(moduleRoot, "OwnedClasses"));
        File.WriteAllText(Path.Combine(classes.FullName, "ObservedRecord.ps1"), """
            class CompilerProofObservedRecord {
                [object] $Value
                [int] $Count
                CompilerProofObservedRecord([object] $Value, [int] $Count) {
                    if ($Count -lt 0) { throw 'Negative count' }
                    $this.Value = $Value
                    $this.Count = $Count
                }
            }
            """);
        File.WriteAllText(manifest, "@{RootModule='Types.psm1';ModuleVersion='1.0.0';ScriptsToProcess=@('OwnedClasses/ObservedRecord.ps1');FunctionsToExport=@('New-TypeRecord','Use-TypeRecord','Convert-TypeRecord','Read-TypeState','Set-TypeSlot','Read-TypeBox','Read-ObservedClass')}");
        const string probe = """
            $savedVerbosePreference = $global:VerbosePreference
            $global:VerbosePreference = 'SilentlyContinue'
            try {
                $moduleName = [IO.Path]::GetFileNameWithoutExtension($modulePath)
                $loaded = Import-Module $modulePath -PassThru -Force -ErrorAction Stop |
                    Where-Object { $_ -is [System.Management.Automation.PSModuleInfo] -and $_.Name -eq $moduleName } |
                    Select-Object -Last 1
            } finally { $global:VerbosePreference = $savedVerbosePreference }
            $atImport = $loaded.SessionState.PSVariable.GetValue('script:AtImport')
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
            $slot = Set-TypeSlot -Index 1 -Value '7'
            $box = [CompilerProof.Types.Box[int]]::new()
            $boxIdentity = [object]::ReferenceEquals($box,(Read-TypeBox $box))
            $slotError = try { Set-TypeSlot -Index 1 -Value 'invalid' -ErrorAction Stop } catch { $_.FullyQualifiedErrorId }
            $slotAfterError = [CompilerProof.Types.Slots[int]]::Values[1]
            $slotRecovered = Set-TypeSlot -Index 0 -Value 8
            [pscustomobject]@{
                atImport=$atImport;rows=$rows;third=$third;failure=$failure;fourth=$fourth
                state=(Read-TypeState);same=[object]::ReferenceEquals($record,$converted)
                converted=$dictionary.Text;convertedType=$dictionary.GetType().FullName
                parameterType=$metadata.Parameters['Value'].ParameterType.FullName
                enumType=$metadata.Parameters['Severity'].ParameterType.FullName
                outputTypes=@($metadata.OutputType.Name)
                attributes=@($metadata.Parameters['Value'].Attributes | ForEach-Object { $_.GetType().FullName })
                slots=@($slot,$slotAfterError,$slotRecovered)
                slotError=$slotError;boxIdentity=$boxIdentity
                observed=@((Read-ObservedClass -Count 4 -TypeValue),(Read-ObservedClass -Count -1 -TypeValue),(Read-ObservedClass -Count 9))
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
            foreach (var name in new[] { "New-TypeRecord", "Use-TypeRecord", "Convert-TypeRecord", "Read-TypeState", "Set-TypeSlot", "Read-TypeBox", "Read-ObservedClass" })
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
            Assert.True(original.ErrorRecords.Length == 0, profile + " original: " +
                string.Join(Environment.NewLine, original.ErrorRecords.Select(static error => error.FullyQualifiedErrorId + " " + error.Message)));
            Assert.True(generated.ErrorRecords.Length == 0, profile + " generated: " +
                string.Join(Environment.NewLine, generated.ErrorRecords.Select(static error => error.FullyQualifiedErrorId + " " + error.Message)));
            var value = Assert.Single(original.Success).Value;
            Assert.Contains("\"atImport\":0", value, StringComparison.Ordinal);
            Assert.Contains("\"state\":10", value, StringComparison.Ordinal);
            Assert.Contains("\"same\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"converted\":\"hash\"", value, StringComparison.Ordinal);
            Assert.Contains("\"parameterType\":\"CompilerProof.Types.Token\"", value, StringComparison.Ordinal);
            Assert.Contains("ParameterArgumentValidationError", value, StringComparison.Ordinal);
            Assert.Contains("\"slots\":[7,7,8]", value, StringComparison.Ordinal);
            Assert.Contains("\"boxIdentity\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"observed\":[{\"value\":\"int\",\"count\":4}", value, StringComparison.Ordinal);
            Assert.Contains("Negative count", value, StringComparison.Ordinal);
            Assert.Contains("{\"value\":\"text\",\"count\":9}", value, StringComparison.Ordinal);
            Assert.Contains("\"slotError\":\"", value, StringComparison.Ordinal);
            var differences = PowerShellCompilationSemanticOracleComparer.Compare(original, generated);
            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Select(static difference =>
                difference.Path + ": original=" + difference.Expected + "; generated=" + difference.Actual)));
        }
    }
}
