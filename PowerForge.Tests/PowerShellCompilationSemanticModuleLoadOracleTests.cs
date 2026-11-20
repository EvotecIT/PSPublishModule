namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void DotSourcedClassKeepsItsIdentityAfterTheAuthoredLoadOnBothHosts()
    {
        const string source = """
            if ($env:POWERFORGE_ORACLE_DOT_LOAD -eq 'load') {
                . "$PSScriptRoot/RecordTypes.ps1"
                function New-LoadedRecord {
                    [CmdletBinding()] param([string] $Name)
                    [DotLoadedOracleRecord]::new($Name)
                }
            }
            Export-ModuleMember -Function New-LoadedRecord
            """;
        const string types = """
            class DotLoadedOracleRecord {
                [string] $Name
                DotLoadedOracleRecord([string] $name) { $this.Name = $name }
                [string] Describe() { return 'loaded:' + $this.Name }
            }
            """;
        const string probe = """
            & (Get-Module ScopedValues) {
                $record = New-LoadedRecord 'sample'
                [ordered]@{
                    value = $record.Describe()
                    sameType = [object]::ReferenceEquals($record.GetType(), [DotLoadedOracleRecord])
                } | ConvertTo-Json -Compress
            }
            """;
        CompareScopedArtifacts(source, probe, new[] { "New-LoadedRecord" },
            value => Assert.Equal("{\"value\":\"loaded:sample\",\"sameType\":true}", value),
            importSetup: "$env:POWERFORGE_ORACLE_DOT_LOAD = 'load'",
            additionalSources: new Dictionary<string, string> { ["RecordTypes.ps1"] = types }, retainAdditionalSources: true);
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ModuleClassRetainsItsMethodsAndOutputTypeIdentityOnBothHosts()
    {
        const string source = """
            class ModuleOutputOracleRecord {
                [string] $Label
                ModuleOutputOracleRecord([string] $label) { $this.Label = $label }
                [string] Display() { return 'record=' + $this.Label }
            }
            function New-OutputRecord {
                [CmdletBinding()]
                [OutputType([ModuleOutputOracleRecord])]
                param([string] $Label)
                [ModuleOutputOracleRecord] $record = [ModuleOutputOracleRecord]::new($Label)
                $record
            }
            Export-ModuleMember -Function New-OutputRecord
            """;
        const string probe = """
            $record = New-OutputRecord -Label 'one'
            $command = Get-Command New-OutputRecord
            [ordered]@{
                value = $record.Display()
                type = $record.GetType().Name
                same = [object]::ReferenceEquals($record.GetType(), $command.OutputType[0].Type)
            } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "<script>", "New-OutputRecord" }, value => Assert.Equal(
            "{\"value\":\"record=one\",\"type\":\"ModuleOutputOracleRecord\",\"same\":true}", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void NestedRuntimeTypeCastsPreserveOverloadAndErrorIdentityOnBothHosts()
    {
        const string source = """
            Add-Type @'
            public class ModuleCastOracleRecord {
                public string Describe(object value) { return "object"; }
                public string Describe(ModuleCastOracleRecord value) { return "record"; }
            }
            '@
            function Read-CastEvidence {
                [CmdletBinding()] param([object] $Value)
                function Read-NestedCast {
                    try {
                        ([ModuleCastOracleRecord] $Value).Describe([ModuleCastOracleRecord] $Value)
                    } catch {
                        [ordered]@{ id=$_.FullyQualifiedErrorId; type=$_.Exception.GetType().Name
                            line=$_.InvocationInfo.ScriptLineNumber; column=$_.InvocationInfo.OffsetInLine }
                    }
                }
                Read-NestedCast
            }
            Export-ModuleMember -Function Read-CastEvidence
            """;
        const string probe = """
            [ordered]@{ valid=Read-CastEvidence ([ModuleCastOracleRecord]::new()); invalid=Read-CastEvidence 'bad' } |
                ConvertTo-Json -Depth 4 -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-CastEvidence" }, value =>
        {
            Assert.Contains("\"valid\":\"record\"", value, StringComparison.Ordinal);
            Assert.Contains("\"invalid\":{\"id\":\"Convert", value, StringComparison.Ordinal);
        });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void CommandOnlyModuleInitializerKeepsItsModuleScopeOnBothHosts()
    {
        const string source = """
            Write-Warning 'startup message'
            Set-Alias -Name Get-StartupProbe -Value Get-Item
            Set-Alias -Name Get-StartupPath -Value (Join-Path $PSScriptRoot 'startup.ps1')
            Set-Alias -Name Get-StartupPrivate -Value Get-Location
            Export-ModuleMember -Alias Get-StartupProbe, Get-StartupPath
            """;
        const string probe = """
            $module = Get-Module ScopedValues
            $path = Get-Command Get-StartupPath
            [ordered]@{
                target = (Get-Command Get-StartupProbe).Definition
                owner = $path.ModuleName
                relativeToModule = $path.Definition -eq (Join-Path $module.ModuleBase 'startup.ps1')
                private = $null -ne (Get-Command Get-StartupPrivate -ErrorAction Ignore)
                exported = @($module.ExportedAliases.Keys | Sort-Object)
            } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "<script>" }, value => Assert.Equal(
            "{\"target\":\"Get-Item\",\"owner\":\"ScopedValues\",\"relativeToModule\":true,\"private\":false," +
            "\"exported\":[\"Get-StartupPath\",\"Get-StartupProbe\"]}", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void CompiledModuleInitializerRetainsInvocationAndStorageOnBothHosts()
    {
        const string source = """
            $path = $MyInvocation.MyCommand.Path
            $observed = 'before'
            if ($path) {
                try {
                    $observed = [int]::Parse('no-number')
                } catch {
                    $observed = $_.FullyQualifiedErrorId
                } finally {
                    $observed += ':finally'
                }
            }
            """;
        const string probe = """
            $module = Get-Module ScopedValues
            & $module {
                [ordered]@{
                    samePath = $path -eq $ExecutionContext.SessionState.Module.Path
                    observed = $observed
                } | ConvertTo-Json -Compress
            }
            """;
        CompareScopedArtifacts(source, probe, new[] { "<script>" }, value => Assert.Equal(
            "{\"samePath\":true,\"observed\":\"FormatException:finally\"}", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void CompiledInitializerDeclaresFunctionsAtTheirAuthoredPositionOnBothHosts()
    {
        const string source = """
            $before = $null -ne (Get-Command Read-InitializerValue -ErrorAction Ignore)
            if ($env:POWERFORGE_ORACLE_STARTUP_BRANCH -eq 'load') {
                function Read-InitializerValue {
                    [CmdletBinding()] param([Alias('n')][int] $Number = 4)
                    $Number + 1
                }
            }
            $after = Read-InitializerValue -n 8
            function Read-InitializerEvidence {
                [ordered]@{before = $before; after = $after; next = Read-InitializerValue}
            }
            Export-ModuleMember -Function Read-InitializerValue, Read-InitializerEvidence
            """;
        CompareScopedArtifacts(source, "Read-InitializerEvidence | ConvertTo-Json -Compress",
            new[] { "<script>", "Read-InitializerValue", "Read-InitializerEvidence" },
            value => Assert.Equal("{\"before\":false,\"after\":9,\"next\":5}", value),
            importSetup: "$env:POWERFORGE_ORACLE_STARTUP_BRANCH = 'load'");
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void CompiledInitializerBindsDataInItsModuleScopeOnBothHosts()
    {
        const string source = """
            data Messages {
                ConvertFrom-StringData @'
            greeting=hello
            farewell=goodbye
            '@
            }
            $script:captured = @{}
            $script:captured['farewell'] = if ($Messages.ContainsKey('greeting')) {
                data { @{ farewell = 'from capture' } }
            }
            $script:direct = data { @{ value = 'from data assignment' } }
            function Read-ModuleMessage {
                [CmdletBinding()] param([string] $Key)
                [ordered]@{ original = $Messages[$Key]; captured = $script:captured[$Key]; direct = $script:direct.value }
            }
            Export-ModuleMember -Function Read-ModuleMessage
            """;
        const string probe = """
            $first = Read-ModuleMessage 'greeting'
            $second = Read-ModuleMessage 'farewell'
            [ordered]@{ first = $first.original; second = $second.original; captured = $second.captured.farewell; direct = $first.direct
                private = $null -ne (Get-Variable Messages -ErrorAction Ignore) } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "<script>", "Read-ModuleMessage" },
            value => Assert.Equal("{\"first\":\"hello\",\"second\":\"goodbye\",\"captured\":\"from capture\"," +
                "\"direct\":\"from data assignment\",\"private\":false}", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void CompiledInitializerKeepsModuleArgumentsAndParameterMetadataOnBothHosts()
    {
        const string source = """
            param([ValidateSet('en', 'fr')][string] $Locale = 'en')
            $observed = [ordered]@{ locale = $Locale; supplied = $PSBoundParameters.ContainsKey('Locale'); rest = @($args) }
            function Read-ModuleArguments { $observed }
            Export-ModuleMember -Function Read-ModuleArguments
            """;
        const string probe = """
            $default = Read-ModuleArguments
            $module = Get-Module ScopedValues
            $path = $module.Path
            Remove-Module $module
            Import-Module $path -ArgumentList 'fr', 'extra' -ErrorAction Stop
            [ordered]@{ default = $default; supplied = Read-ModuleArguments } | ConvertTo-Json -Depth 4 -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "<script>", "Read-ModuleArguments" }, value => Assert.Equal(
            "{\"default\":{\"locale\":\"en\",\"supplied\":false,\"rest\":[]}," +
            "\"supplied\":{\"locale\":\"fr\",\"supplied\":true,\"rest\":[\"extra\"]}}", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ModuleStartupPreservesRegisteredCallbackHandlersOnBothHosts()
    {
        const string source = """
            Add-Type @'
            public class ModuleCallbackOracleHolder {
                public static System.Management.Automation.ScriptBlock Body;
            }
            '@
            [ModuleCallbackOracleHolder]::Body = {
                trap { 'caught=' + $_.FullyQualifiedErrorId; continue }
                throw 'record'
                'after'
            }
            function Register-StartupBlock {
                param([scriptblock] $ScriptBlock)
                $script:registered = $ScriptBlock
            }
            $block = {
                try { 'callback' } finally { 'final' }
            }
            Register-StartupBlock -ScriptBlock $block
            $block = { 'replacement' }
            $script:replacement = $block
            function Read-StartupBlock { & $script:registered }
            Export-ModuleMember -Function Read-StartupBlock
            """;
        const string probe = """
            [ordered]@{ direct=@(& ([ModuleCallbackOracleHolder]::Body)); registered=@(Read-StartupBlock) } |
                ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "<script>", "Register-StartupBlock", "Read-StartupBlock" },
            value => Assert.Equal("{\"direct\":[\"caught=record\",\"after\"],\"registered\":[\"callback\",\"final\"]}", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ModuleInitializedTypesBindAtInvocationOnBothHosts()
    {
        const string source = """
            function Read-ModuleLoadRecord {
                [CmdletBinding()]
                param([ModuleLoadOracleRecord[]] $Records)
                [ordered]@{ records = $Records; typed = $Records -is [ModuleLoadOracleRecord[]] }
            }
            $script:before = try {
                Read-ModuleLoadRecord -Records @() -ErrorAction Stop
            } catch {
                $_.FullyQualifiedErrorId
            }
            if ($env:POWERFORGE_ORACLE_LOAD_RECORD -eq 'yes') {
                Add-Type @'
            public class ModuleLoadOracleRecord {
                public string Name { get; set; }
            }
            '@
            }
            [ModuleLoadOracleRecord] $startupRecord = [ModuleLoadOracleRecord]::new()
            $startupRecord.Name = 'startup'
            function Read-TypeLoadEvidence { [ordered]@{ before=$script:before; startup=$script:startupRecord } }
            Export-ModuleMember -Function Read-ModuleLoadRecord, Read-TypeLoadEvidence
            """;
        const string probe = """
            $record = [ModuleLoadOracleRecord]::new()
            $record.Name = 'loaded'
            $result = Read-ModuleLoadRecord -Records $record
            $evidence = Read-TypeLoadEvidence
            [ordered]@{
                before = $evidence.before
                startupName = $evidence.startup.Name
                startupType = $evidence.startup -is [ModuleLoadOracleRecord]
                typed = $result.typed
                same = [object]::ReferenceEquals($record, $result.records[0])
                name = $result.records[0].Name
            } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "<script>", "Read-ModuleLoadRecord" }, value =>
        {
            Assert.Contains("TypeNotFound", value, StringComparison.Ordinal);
            Assert.Contains("\"startupName\":\"startup\",\"startupType\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"typed\":true,\"same\":true,\"name\":\"loaded\"", value, StringComparison.Ordinal);
        }, importSetup: "$env:POWERFORGE_ORACLE_LOAD_RECORD = 'yes'");
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void DeferredModuleDeclarationsPreservePrivateStorageAcrossClausesOnBothHosts()
    {
        const string source = """
            Add-Type @'
            public class DeferredStorageOracleRecord {
                public string Name;
                public DeferredStorageOracleRecord(string name) { Name = name; }
            }
            '@
            function Read-DeferredStorageRecord {
                [CmdletBinding()]
                param([Parameter(ValueFromPipeline)][DeferredStorageOracleRecord] $Record, [switch] $Fail)
                begin { $private:__PowerForgeDeferredNestedFailure = 'authored' }
                process {
                    if ($Fail) { throw 'deferred record' }
                    $private:__PowerForgeDeferredNestedFailure + ':' + $Record.Name
                }
                end { 'end:' + $private:__PowerForgeDeferredNestedFailure }
            }
            Export-ModuleMember -Function Read-DeferredStorageRecord
            """;
        const string probe = """
            $first = [DeferredStorageOracleRecord]::new('one')
            $second = [DeferredStorageOracleRecord]::new('two')
            $rows = @($first, $second | Read-DeferredStorageRecord)
            $failure = try { $first | Read-DeferredStorageRecord -Fail -ErrorAction Stop; 'unexpected' } catch { $_.FullyQualifiedErrorId }
            [ordered]@{ rows=$rows; failure=$failure } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "<script>", "Read-DeferredStorageRecord" },
            value => Assert.Equal("{\"rows\":[\"authored:one\",\"authored:two\",\"end:authored\"],\"failure\":\"deferred record\"}", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void StartupDeclarationsResolveLateParameterAttributesOnBothHosts()
    {
        const string source = """
            function Read-LateParameterAttribute {
                [CmdletBinding()] param([ModuleValidationOracleAttribute()][string] $Value)
                $Value
            }
            Add-Type @'
            public class ModuleValidationOracleAttribute : System.Management.Automation.ValidateArgumentsAttribute {
                protected override void Validate(object value, System.Management.Automation.EngineIntrinsics engine) {
                    if (!string.Equals((string)value, "accepted", System.StringComparison.Ordinal))
                        throw new System.Management.Automation.ValidationMetadataException("Wrong value");
                }
            }
            '@
            Export-ModuleMember -Function Read-LateParameterAttribute
            """;
        const string probe = """
            $invalid = try { Read-LateParameterAttribute 'bad' -ErrorAction Stop } catch { $_.FullyQualifiedErrorId }
            [ordered]@{ valid=Read-LateParameterAttribute 'accepted'; invalid=$invalid } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "<script>", "Read-LateParameterAttribute" }, value => Assert.Equal(
            "{\"valid\":\"accepted\",\"invalid\":\"ParameterArgumentValidationError,Read-LateParameterAttribute\"}", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void StartupDeclarationsPreserveRemovableParameterStorageOnBothHosts()
    {
        const string source = """
            $script:StartupMarker = 'ready'
            function Read-RecreatedParameter {
                [CmdletBinding()]
                param([string] $Value)
                $before = $Value
                Microsoft.PowerShell.Utility\Remove-Variable -Name Value
                $removed = $null -eq (Get-Variable -Name Value -Scope Local -ErrorAction Ignore)
                New-Variable -Name Value -Value 42
                [ordered]@{ startup=$script:StartupMarker; before=$before; removed=$removed; after=$Value; type=$Value.GetType().Name }
            }
            Export-ModuleMember -Function Read-RecreatedParameter
            """;
        const string probe = "Read-RecreatedParameter -Value bound | ConvertTo-Json -Compress";
        CompareScopedArtifacts(source, probe, new[] { "<script>", "Read-RecreatedParameter" },
            value => Assert.Equal("{\"startup\":\"ready\",\"before\":\"bound\",\"removed\":true,\"after\":42,\"type\":\"Int32\"}", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void SeparatelyLoadedFunctionsPreserveCommandAvailabilityAndBindingOnBothHosts()
    {
        const string source = """
            $script:before = $null -ne (Get-Command Read-DeferredFixtureValue -ErrorAction Ignore)
            . "$PSScriptRoot/Commands.ps1"
            $script:after = (Get-Command Read-DeferredFixtureValue).CommandType.ToString()
            $script:onImport = Read-DeferredFixtureValue -Value 20
            function Read-LoadEvidence {
                [CmdletBinding()]
                param()
                [ordered]@{ before = $script:before; after = $script:after; onImport = $script:onImport }
            }
            Export-ModuleMember -Function Read-DeferredFixtureValue, Read-LoadEvidence
            """;
        const string commands = """
            function Read-DeferredFixtureValue {
                [CmdletBinding()]
                param([Alias('n')][int] $Value = 4)
                $Value + 1
            }
            """;
        const string probe = """
            $evidence = Read-LoadEvidence
            $evidence.defaultValue = Read-DeferredFixtureValue
            $evidence.aliasValue = Read-DeferredFixtureValue -n 7
            $evidence | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-DeferredFixtureValue" },
            value => Assert.Equal("{\"before\":false,\"after\":\"Function\",\"onImport\":21,\"defaultValue\":5,\"aliasValue\":8}", value),
            additionalSources: new Dictionary<string, string> { ["Commands.ps1"] = commands });
    }
}
