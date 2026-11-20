namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void TransportedScriptBlocksCaptureCallerValuesOnBothHosts()
    {
        const string source = """
            function Read-TransportedValue {
                [CmdletBinding()] param([string] $Text)
                $label = "result=$Text"
                $block = { $using:label }
                if ($PSVersionTable.PSVersion.Major -ge 7) {
                    1 | ForEach-Object -Parallel $block
                } else {
                    $job = Start-Job -ScriptBlock $block
                    Receive-Job -Job $job -Wait -AutoRemoveJob
                }
            }
            Export-ModuleMember -Function Read-TransportedValue
            """;
        CompareScopedArtifacts(source, "Read-TransportedValue 'first'",
            new[] { "Read-TransportedValue" }, value => Assert.Equal("result=first", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void DynamicCallerLoopLabelsPreserveTransfersAndFinallyOnBothHosts()
    {
        const string source = """
            function Move-CallerLoop {
                [CmdletBinding()] param([string] $Label, [switch] $Stop)
                try {
                    if ($Stop) { break $Label }
                    continue $Label
                } finally { 'callee-finally' }
            }
            Export-ModuleMember -Function Move-CallerLoop
            """;
        const string probe = """
            $records = @(
                :outer foreach ($index in 1..3) {
                    "outer-$index"
                    foreach ($child in 1..2) {
                        "inner-$child"
                        Move-CallerLoop -Label 'OUTER' -Stop:($index -eq 2)
                        'unreachable-inner'
                    }
                    'unreachable-outer'
                }
                'after-loop'
            )
            ConvertTo-Json -InputObject $records -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Move-CallerLoop" }, value => Assert.Equal(
            "[\"outer-1\",\"inner-1\",\"callee-finally\",\"outer-2\",\"inner-1\",\"callee-finally\",\"after-loop\"]", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void NestedIndexKeysAndPrivateMutationsUseNativeStorageOnBothHosts()
    {
        const string source = """
            function Format-StorageKey { param([string] $Name) 'key=' + $Name }
            function Read-NestedStorage {
                [CmdletBinding()] param([object[]] $Items)
                $private:state = [pscustomobject]@{ Count = 1 }
                $values = @{}
                for ($index = 0; $index -lt $Items.Count; $index++) {
                    $values[$Items[$index].Item.FullName] = ++$private:state.Count
                    $values["$(Format-StorageKey -Name ($Items[$index].Item.FullName))"] = $index
                }
                [ordered]@{ first = $values['a']; second = $values['b']; count = $private:state.Count; formatted = $values['key=b'] }
            }
            Export-ModuleMember -Function Read-NestedStorage
            """;
        const string probe = """
            $items = 'a','b' | ForEach-Object { [pscustomobject]@{Item=[pscustomobject]@{FullName=$_}} }
            Read-NestedStorage $items | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-NestedStorage" },
            value => Assert.Equal("{\"first\":2,\"second\":3,\"count\":3,\"formatted\":1}", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ObservedClrInvocationsKeepErrorIdentityAndScopeOnBothHosts()
    {
        const string source = """
            function Read-ObservedFormat {
                [CmdletBinding()] param([string] $Pattern, [int] $Number)
                $kind = [int]
                try {
                    [string]::Format($Pattern, $kind, $Number)
                } catch {
                    [ordered]@{ id = $_.FullyQualifiedErrorId; type = $_.Exception.GetType().Name
                        line = $_.InvocationInfo.ScriptLineNumber; column = $_.InvocationInfo.OffsetInLine }
                }
            }
            function Read-ObservedScript {
                [CmdletBinding()] param([string] $Body)
                $kind = [int]
                $marker = 'caller'
                try {
                    $ExecutionContext.InvokeCommand.InvokeScript($false, ([scriptblock]::Create($Body)), $null, $kind)
                } catch {
                    [ordered]@{ id = $_.FullyQualifiedErrorId; type = $_.Exception.GetType().Name
                        line = $_.InvocationInfo.ScriptLineNumber; column = $_.InvocationInfo.OffsetInLine }
                }
            }
            function Read-EventTypeMetadata {
                [CmdletBinding()] param([System.Diagnostics.EventLogEntryType] $Kind)
                [ordered]@{ kind=$Kind.ToString(); logType=[System.Diagnostics.EventLog].FullName }
            }
            Export-ModuleMember -Function Read-ObservedFormat, Read-ObservedScript, Read-EventTypeMetadata
            """;
        const string probe = """
            [ordered]@{
                format = Read-ObservedFormat -Pattern '{0}:{1}' -Number 7
                formatError = Read-ObservedFormat -Pattern '{' -Number 7
                scope = Read-ObservedScript 'param($value) $marker + '':'' + $value.Name'
                scriptError = Read-ObservedScript 'throw ''from callback'''
                platform = Read-EventTypeMetadata -Kind Warning
            } | ConvertTo-Json -Depth 4 -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-ObservedFormat", "Read-ObservedScript", "Read-EventTypeMetadata" }, value =>
        {
            Assert.Contains("\"format\":\"System.Int32:7\"", value, StringComparison.Ordinal);
            Assert.Contains("\"formatError\":{\"id\":\"FormatException\"", value, StringComparison.Ordinal);
            Assert.Contains("\"scope\":\"caller:Int32\"", value, StringComparison.Ordinal);
            Assert.Contains("\"scriptError\":{\"id\":\"RuntimeException\"", value, StringComparison.Ordinal);
            Assert.Contains("\"platform\":{\"kind\":\"Warning\",\"logType\":\"System.Diagnostics.EventLog\"}", value, StringComparison.Ordinal);
        });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void PostfixMemberMutationReadsTheCallerObjectOnBothHosts()
    {
        const string source = """
            function Read-NextFixtureId {
                if ($null -eq $fixtureCounter) { return '' }
                '{0}:{1}' -f $fixtureCounter.Prefix, $fixtureCounter.Node++
            }
            function Read-InheritedCounter {
                $fixtureCounter = [pscustomobject]@{ Prefix = 'item'; Node = 3 }
                $first = Read-NextFixtureId
                $second = Read-NextFixtureId
                [ordered]@{ first = $first; second = $second; final = $fixtureCounter.Node }
            }
            Export-ModuleMember -Function Read-NextFixtureId, Read-InheritedCounter
            """;
        const string probe = """
            $evidence = Read-InheritedCounter
            $evidence.missing = Read-NextFixtureId
            $evidence | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-NextFixtureId" },
            value => Assert.Equal("{\"first\":\"item:3\",\"second\":\"item:4\",\"final\":5,\"missing\":\"\"}", value));
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void TerminalBooleanSubexpressionPreservesReturnAndOutputOnBothHosts()
    {
        const string source = """
            function Test-FixtureValues ($Values) {
                $(if (-not ($current = $Values)) { return $false } else { @($current).Length -gt 0 })
            }
            Export-ModuleMember -Function Test-FixtureValues
            """;
        const string probe = """
            [ordered]@{
                missing = @(Test-FixtureValues $null)
                zero = @(Test-FixtureValues 0)
                single = @(Test-FixtureValues 'value')
                multiple = @(Test-FixtureValues @(1, 2))
            } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Test-FixtureValues" },
            value => Assert.Equal("{\"missing\":[false],\"zero\":[false],\"single\":[true],\"multiple\":[true]}", value));
    }
}
