using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ReturnedAndCollectedCallbacksPreserveNativeScopeClosureAndFailureOnBothHosts()
    {
        const string source = """
            $script:Marker = 'module'
            $script:Trace = [Collections.Generic.List[string]]::new()
            function New-ReturnedCallback {
                [CmdletBinding()] param()
                return { param([int] $Increment) $Value + $Increment }
            }
            function New-CallbackBundle {
                [CmdletBinding()] param([int] $Value)
                [ordered]@{
                    direct = { param([string] $Suffix) $script:Marker + ':' + $Suffix }
                    closure = { param([int] $Increment) $Value + $Increment }.GetNewClosure()
                    pair = @({ 'left' }, { 'right' })
                    failure = {
                        try { 'partial'; throw 'controlled callback failure' }
                        finally { [void] $script:Trace.Add('finally') }
                    }
                }
            }
            Export-ModuleMember -Function New-ReturnedCallback, New-CallbackBundle
            """;
        const string probe = """
            $bundle = New-CallbackBundle -Value 5
            $returned = New-ReturnedCallback
            $module = (Get-Command New-CallbackBundle).Module
            $dynamic = & $module { param($Callback) $Value = 9; & $Callback 2 } $returned
            $partial = [Collections.Generic.List[string]]::new()
            $failure = try { & $bundle.failure | ForEach-Object { $partial.Add($_) }; 'unexpected' }
                catch { $_.Exception.Message }
            [ordered]@{
                direct = (& $bundle.direct 'one')
                captured = (& $bundle.closure 2)
                dynamic = $dynamic
                pair = @(& $bundle.pair[0]; & $bundle.pair[1])
                partial = @($partial.ToArray())
                failure = $failure
                trace = @(& $module { $script:Trace.ToArray() })
                recovered = (& $bundle.direct 'two')
            } | ConvertTo-Json -Depth 7 -Compress
            """;
        CompareScopedArtifacts(source, probe,
            new[] { "New-ReturnedCallback", "New-CallbackBundle" },
            value =>
            {
                Assert.Contains("\"captured\":7", value, StringComparison.Ordinal);
                Assert.Contains("\"dynamic\":11", value, StringComparison.Ordinal);
                Assert.Contains("\"pair\":[\"left\",\"right\"]", value, StringComparison.Ordinal);
                Assert.Contains("\"partial\":[\"partial\"]", value, StringComparison.Ordinal);
                Assert.Contains("\"trace\":[\"finally\"]", value, StringComparison.Ordinal);
                Assert.Contains("\"recovered\":\"module:two\"", value, StringComparison.Ordinal);
            });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void BorrowedInvocationObjectsPreserveIdentityMutationAndDynamicBindingOnBothHosts()
    {
        const string source = """
            function Return-SessionOwner { param([Management.Automation.SessionState] $Value) $Value }
            function Return-VariableCell { param([Management.Automation.PSVariable] $Value) $Value }
            function Return-ParameterMap { param([Management.Automation.RuntimeDefinedParameterDictionary] $Value) $Value }
            function Return-InitialState { param([Management.Automation.Runspaces.InitialSessionState] $Value) $Value }
            function Return-CommandNode { param([Management.Automation.Language.CommandAst] $Value) $Value }
            function Return-ScriptPosition { param([Management.Automation.Language.IScriptPosition] $Value) $Value }
            function Return-CallerCmdlet { param([Management.Automation.PSCmdlet] $Value) $Value }
            function Return-ParameterDefinition { param([Management.Automation.RuntimeDefinedParameter] $Value) $Value }
            function Return-GroupedData { param([Microsoft.PowerShell.Commands.GroupInfo] $Value) $Value }
            function Set-DelegateValue {
                [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
                42
            }
            function New-ParameterMetadata {
                [CmdletBinding()] param()
                $parameter = [Management.Automation.ParameterAttribute]::new()
                $parameter.Mandatory = $true
                $alias = [Management.Automation.AliasAttribute]::new('Text')
                $attributes = [Collections.ObjectModel.Collection[Attribute]]::new()
                $attributes.Add($parameter)
                $attributes.Add($alias)
                [Management.Automation.RuntimeDefinedParameter]::new('Value', [string], $attributes)
            }
            function Read-SyntaxHelpers {
                [CmdletBinding()] param()
                [ordered]@{
                    kind = [Management.Automation.Language.TokenKind]::Identifier.ToString()
                    escaped = [Management.Automation.Language.CodeGeneration]::EscapeSingleQuotedStringContent("can't")
                    converted = [Management.Automation.LanguagePrimitives]::ConvertTo('7', [int])
                }
            }
            function Read-PatternKind {
                param([Management.Automation.WildcardPattern] $Pattern, [Management.Automation.CommandTypes] $Kind)
                [ordered]@{match=$Pattern.IsMatch('alpha');kind=$Kind.ToString()}
            }
            function Read-DynamicValue {
                [CmdletBinding()] param([Management.Automation.RuntimeDefinedParameterDictionary] $Definition)
                dynamicparam { $Definition }
                process { $PSBoundParameters.Extra }
            }
            Export-ModuleMember -Function Return-*, Read-PatternKind, Read-DynamicValue, New-ParameterMetadata, Read-SyntaxHelpers, Set-DelegateValue
            """;
        const string probe = """
            function Invoke-InvocationProbe {
                [CmdletBinding()] param()
                $state = $ExecutionContext.SessionState
                $cell = [Management.Automation.PSVariable]::new('sample', 4)
                $returnedCell = Return-VariableCell $cell
                $returnedCell.Value = 8
                $definition = [Management.Automation.RuntimeDefinedParameterDictionary]::new()
                $attributes = [Collections.ObjectModel.Collection[Attribute]]::new()
                $attributes.Add([Management.Automation.ParameterAttribute]::new())
                $definition.Add('Extra', [Management.Automation.RuntimeDefinedParameter]::new('Extra', [string], $attributes))
                $initial = [Management.Automation.Runspaces.InitialSessionState]::CreateDefault()
                $tokens = $null; $errors = $null
                $tree = [Management.Automation.Language.Parser]::ParseInput('Get-Item .', [ref]$tokens, [ref]$errors)
                $command = $tree.Find({param($node) $node -is [Management.Automation.Language.CommandAst]}, $true)
                $position = $command.Extent.StartScriptPosition
                $group = @('a', 'a' | Group-Object)[0]
                $metadata = New-ParameterMetadata
                $previousDelegate = [Net.ServicePointManager]::ServerCertificateValidationCallback
                try {
                    $delegateResult = Set-DelegateValue
                    $delegateAccepted = [Net.ServicePointManager]::ServerCertificateValidationCallback.Invoke(
                        $null, $null, $null, [Net.Security.SslPolicyErrors]::None)
                } finally { [Net.ServicePointManager]::ServerCertificateValidationCallback = $previousDelegate }
                $invalid = try { Return-CallerCmdlet 'invalid' -ErrorAction Stop; 'unexpected' } catch { $_.FullyQualifiedErrorId }
                [ordered]@{
                    session = [object]::ReferenceEquals($state, (Return-SessionOwner $state))
                    cell = [object]::ReferenceEquals($cell, $returnedCell)
                    mutation = $cell.Value
                    dictionary = [object]::ReferenceEquals($definition, (Return-ParameterMap $definition))
                    initial = [object]::ReferenceEquals($initial, (Return-InitialState $initial))
                    command = [object]::ReferenceEquals($command, (Return-CommandNode $command))
                    position = [object]::ReferenceEquals($position, (Return-ScriptPosition $position))
                    cmdlet = [object]::ReferenceEquals($PSCmdlet, (Return-CallerCmdlet $PSCmdlet))
                    parameter = [object]::ReferenceEquals($metadata, (Return-ParameterDefinition $metadata))
                    grouped = [object]::ReferenceEquals($group, (Return-GroupedData $group))
                    metadata = @($metadata.Attributes[0].Mandatory, $metadata.Attributes[1].AliasNames[0])
                    delegate = @($delegateResult, $delegateAccepted)
                    helpers = Read-SyntaxHelpers
                    pattern = Read-PatternKind ([Management.Automation.WildcardPattern]::new('a*')) 'Function'
                    dynamic = Read-DynamicValue -Definition $definition -Extra 'bound'
                    invalid = $invalid
                    recovered = (Return-VariableCell $cell).Value
                }
            }
            Invoke-InvocationProbe | ConvertTo-Json -Depth 7 -Compress
            """;
        CompareScopedArtifacts(source, probe,
            new[] { "Return-SessionOwner", "Return-VariableCell", "Return-ParameterMap", "Return-InitialState",
                "Return-CommandNode", "Return-ScriptPosition", "Return-CallerCmdlet", "Read-PatternKind", "Read-DynamicValue",
                "Return-ParameterDefinition", "Return-GroupedData", "New-ParameterMetadata", "Read-SyntaxHelpers", "Set-DelegateValue" },
            value =>
            {
                foreach (var name in new[] { "session", "cell", "dictionary", "initial", "command", "position", "cmdlet", "parameter", "grouped" })
                    Assert.Contains("\"" + name + "\":true", value, StringComparison.Ordinal);
                Assert.Contains("\"mutation\":8", value, StringComparison.Ordinal);
                Assert.Contains("\"dynamic\":\"bound\"", value, StringComparison.Ordinal);
                Assert.Contains("\"metadata\":[true,\"Text\"]", value, StringComparison.Ordinal);
                Assert.Contains("\"delegate\":[42,true]", value, StringComparison.Ordinal);
                Assert.Contains("\"converted\":7", value, StringComparison.Ordinal);
                Assert.Contains("ParameterArgumentTransformationError", value, StringComparison.Ordinal);
                Assert.Contains("\"recovered\":8", value, StringComparison.Ordinal);
            });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void NativeCalleesPreserveBasicExtraArgumentsAndAdvancedCallerBindingOnBothHosts()
    {
        const string source = """
            function Read-NativeValue { param([Management.Automation.PSVariable] $Cell) $Cell.Value }
            function Read-ExtraArguments { $args }
            function Invoke-Caller {
                [CmdletBinding()] param([Management.Automation.PSVariable] $Cell)
                Read-ExtraArguments -Unknown 'extra'
                Read-NativeValue $Cell
            }
            function Invoke-Outer { [CmdletBinding()] param($Cell) Invoke-Caller -Cell $Cell }
            Export-ModuleMember -Function Invoke-Outer
            """;
        CompareScopedArtifacts(source,
            "Invoke-Outer ([Management.Automation.PSVariable]::new('owned', 7)) | ConvertTo-Json -Compress",
            new[] { "Read-NativeValue", "Read-ExtraArguments", "Invoke-Caller", "Invoke-Outer" },
            value => Assert.Equal("[\"-Unknown\",\"extra\",7]", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void LegacyModuleToProcessManifestPreservesCompiledImportOnBothHosts()
    {
        foreach (var (framework, profile, host) in new[]
                 {
                     ("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH")),
                     ("net472", PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, (string?)null)
                 })
        {
            using var fixture = OracleFixture.Create(string.Empty);
            var source = Path.Combine(fixture.RootPath, "Legacy.psm1");
            var manifest = Path.Combine(fixture.RootPath, "Legacy.psd1");
            File.WriteAllText(source, "function Read-LegacyValue { [CmdletBinding()] param([int] $Value) $Value + 1 }; Export-ModuleMember -Function Read-LegacyValue");
            File.WriteAllText(manifest, "@{ModuleToProcess='Legacy.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Read-LegacyValue')}");
            var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(source,
                Path.Combine(fixture.RootPath, "built"), "Legacy", PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
            { TargetFramework = framework, SemanticProfileId = profile, ModuleManifestPath = manifest });
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == "Read-LegacyValue");
            Assert.True(unit.EmittedClrMethod);
            Assert.DoesNotContain("RootModule", File.ReadAllText(built.ArtifactPath!), StringComparison.OrdinalIgnoreCase);
            PowerShellCompilationSemanticOracleEnvelope Observe(string path)
            {
                File.WriteAllText(fixture.ScriptPath, "$global:VerbosePreference='SilentlyContinue'; Import-Module '" + path.Replace("'", "''", StringComparison.Ordinal) + "' -Force -ErrorAction Stop; Read-LegacyValue 4");
                return new PowerShellCompilationSemanticOracleRunner().Observe(new PowerShellCompilationSemanticOracleRequest(profile, fixture.ScriptPath)
                { HostExecutablePath = host, ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profile)) });
            }
            var original = Observe(manifest);
            var generated = Observe(built.ArtifactPath!);
            Assert.Empty(original.ErrorRecords);
            Assert.Empty(generated.ErrorRecords);
            Assert.Equal("5", Assert.Single(original.Success).Value);
            Assert.Empty(PowerShellCompilationSemanticOracleComparer.Compare(original, generated));
        }
    }

    private static void CompareScopedArtifacts(string source, string probe, string[] compiledNames, Action<string> assertValue)
    {
        var modernHost = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(modernHost), "The exact PowerShell 7.6.6 host is required.");
        foreach (var (framework, profile, host) in new[]
                 {
                     ("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, modernHost),
                     ("net472", PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, (string?)null)
                 })
        {
            using var fixture = OracleFixture.Create(string.Empty);
            var module = Path.Combine(fixture.RootPath, "ScopedValues.psm1");
            File.WriteAllText(module, source);
            var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                module, Path.Combine(fixture.RootPath, "built"), "ScopedValues", PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
            { TargetFramework = framework, SemanticProfileId = profile });
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
            foreach (var name in compiledNames)
            {
                var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == name);
                Assert.True(unit.EmittedClrMethod, name + " requires a complete emitted body: " + System.Text.Json.JsonSerializer.Serialize(unit));
                Assert.False(unit.RetainedHostedSource);
            }
            PowerShellCompilationSemanticOracleEnvelope Observe(string path)
            {
                File.WriteAllText(fixture.ScriptPath, "$global:VerbosePreference='SilentlyContinue'\nImport-Module '" +
                    path.Replace("'", "''", StringComparison.Ordinal) + "' -Force -ErrorAction Stop\n" + probe);
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
            assertValue(Assert.Single(original.Success).Value);
            var differences = PowerShellCompilationSemanticOracleComparer.Compare(original, generated);
            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Select(static difference =>
                difference.Path + ": original=" + difference.Expected + "; generated=" + difference.Actual)));
        }
    }
}
