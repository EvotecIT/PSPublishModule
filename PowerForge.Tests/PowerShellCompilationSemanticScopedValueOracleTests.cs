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
    public void NativeMultipleAssignmentPreservesStorageAndCapturedOutputOnBothHosts()
    {
        const string source = """
            function Read-AssignedValues {
                [CmdletBinding()]
                param([string[]] $Values, [switch] $Captured)
                [int] $first = 9
                $rest = 'sentinel'
                if ($Captured) {
                    $first, $rest = if ($Values.Count -gt 0) { $Values }
                } else {
                    $first, $rest = $Values | Write-Output
                }
                [ordered]@{first=$first;rest=$rest}
            }
            function Read-WholeArrayRecord {
                [CmdletBinding()]
                param([string[]] $Values, [switch] $Captured, [switch] $Direct)
                if ($Direct) {
                    $first, $rest = $Values
                } elseif ($Captured) {
                    $first, $rest = if ($true) { ,$Values }
                } else {
                    $first, $rest = Write-Output -NoEnumerate $Values
                }
                [ordered]@{first=$first;rest=$rest}
            }
            function Read-UnwoundAssignment {
                [CmdletBinding()]
                param([switch] $ReturnEarly)
                if ($ReturnEarly) {
                    $first, $rest = if ($true) { 'discard'; return 'also discarded' }
                } else {
                    $list = [Collections.ArrayList]::new()
                    [void]$list.Add(1)
                    [void]$list.Add(2)
                    $first, $rest = foreach ($item in $list) { $item; [void]$list.Add(3) }
                }
            }
            Export-ModuleMember -Function Read-AssignedValues, Read-WholeArrayRecord, Read-UnwoundAssignment
            """;
        const string probe = """
            $unwind = @(Read-UnwoundAssignment -ErrorAction Continue 2>&1 | ForEach-Object {
                if ($_ -is [Management.Automation.ErrorRecord]) { 'error=' + $_.FullyQualifiedErrorId }
                else { $_ }
            })
            $failure = try {
                Read-AssignedValues -Values 'invalid','discard' -ErrorAction Stop
            } catch { $_.FullyQualifiedErrorId }
            [ordered]@{
                failure=$failure
                empty=(Read-AssignedValues -Values @() -Captured)
                singleton=(Read-AssignedValues -Values '4' -Captured)
                captured=(Read-AssignedValues -Values '7','keep','tail' -Captured)
                streamed=(Read-AssignedValues -Values '8','one','two')
                oneRecord=(Read-WholeArrayRecord -Values 'a','b')
                statementRecord=(Read-WholeArrayRecord -Values 'a','b' -Captured)
                direct=(Read-WholeArrayRecord -Values 'a','b' -Direct)
                early=@(Read-UnwoundAssignment -ReturnEarly)
                unwind=$unwind
            } | ConvertTo-Json -Depth 6 -Compress
            """;
        CompareScopedArtifacts(source, probe,
            new[] { "Read-AssignedValues", "Read-WholeArrayRecord", "Read-UnwoundAssignment" }, value =>
        {
            Assert.Contains("\"empty\":{\"first\":0,\"rest\":null}", value, StringComparison.Ordinal);
            Assert.Contains("\"singleton\":{\"first\":4,\"rest\":null}", value, StringComparison.Ordinal);
            Assert.Contains("\"captured\":{\"first\":7,\"rest\":[\"keep\",\"tail\"]}", value, StringComparison.Ordinal);
            Assert.Contains("\"streamed\":{\"first\":8,\"rest\":[\"one\",\"two\"]}", value, StringComparison.Ordinal);
            Assert.True(
                value.Contains("\"oneRecord\":{\"first\":[\"a\",\"b\"],\"rest\":null}", StringComparison.Ordinal) ||
                value.Contains("\"oneRecord\":{\"first\":{\"value\":[\"a\",\"b\"],\"Count\":2},\"rest\":null}", StringComparison.Ordinal),
                "The command produced one array record in the first target: " + value);
            Assert.Contains("\"statementRecord\":{\"first\":[\"a\",\"b\"],\"rest\":null}", value, StringComparison.Ordinal);
            Assert.Contains("\"direct\":{\"first\":\"a\",\"rest\":\"b\"}", value, StringComparison.Ordinal);
            Assert.Contains("\"early\":[]", value, StringComparison.Ordinal);
            Assert.Contains("\"unwind\":[\"error=", value, StringComparison.Ordinal);
            Assert.Contains("\"failure\":\"", value, StringComparison.Ordinal);
        });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void NativeWildcardSwitchAndCapturedStorageMatchBothHosts()
    {
        const string source = """
            function Read-WildcardRecords {
                [CmdletBinding()]
                param([string[]] $Values)
                switch -Wildcard ($Values) {
                    'a*' { "prefix=$PSItem" }
                    '*Z' { "suffix=$PSItem" }
                    default { "other=$PSItem" }
                }
                switch -Wildcard -CaseSensitive ($Values) {
                    'a*' { "case=$PSItem" }
                }
            }
            function Set-CapturedParameter {
                [CmdletBinding()]
                param([string] $Text)
                $buffer = [Text.StringBuilder]::new()
                $PSBoundParameters.Text = if ($Text) {
                    [void]$buffer.Append($Text)
                    $buffer.ToString()
                } else { 'default' }
                $PSBoundParameters.Text
            }
            Export-ModuleMember -Function Read-WildcardRecords, Set-CapturedParameter
            """;
        const string probe = """
            [ordered]@{
                matches=@(Read-WildcardRecords -Values 'AZ','apple','pear')
                empty=@(Read-WildcardRecords -Values @())
                captured=@((Set-CapturedParameter -Text 'value'),(Set-CapturedParameter -Text ''))
            } | ConvertTo-Json -Depth 5 -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-WildcardRecords", "Set-CapturedParameter" }, value =>
        {
            Assert.Contains("\"matches\":[\"prefix=AZ\",\"suffix=AZ\",\"prefix=apple\",\"other=pear\",\"case=apple\"]", value, StringComparison.Ordinal);
            Assert.Contains("\"empty\":[]", value, StringComparison.Ordinal);
            Assert.Contains("\"captured\":[\"value\",\"default\"]", value, StringComparison.Ordinal);
        });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void BorrowedDirectoryAndCompletionTypesKeepNativeIdentityOnBothHosts()
    {
        const string source = """
            function Set-BorrowedSearchScope {
                [CmdletBinding()]
                param(
                    [System.DirectoryServices.DirectoryEntry] $Entry,
                    [System.DirectoryServices.DirectorySearcher] $Searcher,
                    [System.DirectoryServices.SearchScope] $Scope
                )
                $Searcher.SearchScope = $Scope
                [ordered]@{entry=$Entry;searcher=$Searcher;scope=$Searcher.SearchScope.ToString()}
            }
            function Read-CompletionMetadata {
                param([Management.Automation.PSModuleInfo] $Module,
                      [Management.Automation.Language.Token[]] $Tokens,
                      [Management.Automation.CommandCompletion] $Completion,
                      [Management.Automation.PSLanguageMode] $Mode)
                [ordered]@{module=$Module;token=$Tokens[0];completion=$Completion;mode=$Mode.ToString()}
            }
            function Read-CommonParameterType {
                [Management.Automation.Internal.CommonParameters].GetProperty('ErrorAction').PropertyType.FullName
                [Management.Automation.Internal.ShouldProcessParameters].GetProperty('WhatIf').PropertyType.FullName
                [Management.Automation.PagingParameters].GetProperty('IncludeTotalCount').PropertyType.FullName
                [Management.Automation.Internal.TransactionParameters].GetProperty('UseTransaction').PropertyType.FullName
                [DirectoryServices.ActiveDirectory.Domain].GetProperty('Name').PropertyType.FullName
            }
            function Read-InlineModule([Management.Automation.PSModuleInfo] $Module) { $Module }
            Export-ModuleMember -Function Set-BorrowedSearchScope, Read-CompletionMetadata, Read-CommonParameterType, Read-InlineModule
            """;
        const string probe = """
            Add-Type -AssemblyName System.DirectoryServices
            $entry = [System.DirectoryServices.DirectoryEntry]::new('oracle-nonbinding-path')
            $searcher = [System.DirectoryServices.DirectorySearcher]::new($entry)
            try {
                $updated = Set-BorrowedSearchScope -Entry $entry -Searcher $searcher -Scope OneLevel
                $invalid = try { Set-BorrowedSearchScope -Entry $entry -Searcher $searcher -Scope invalid -ErrorAction Stop } catch { $_.FullyQualifiedErrorId }
                $module = (Get-Command Read-CompletionMetadata).Module
                $tokens = $null; $errors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput('Get-ChildItem', [ref]$tokens, [ref]$errors)
                $completion = TabExpansion2 'Get-ChildIte' 12
                $returned = Read-CompletionMetadata $module $tokens $completion FullLanguage
                [ordered]@{
                    entry=[object]::ReferenceEquals($entry,$updated.entry)
                    searcher=[object]::ReferenceEquals($searcher,$updated.searcher)
                    scope=$updated.scope
                    invalid=$invalid
                    module=[object]::ReferenceEquals($module,$returned.module)
                    inline=[object]::ReferenceEquals($module,(Read-InlineModule $module))
                    token=[object]::ReferenceEquals($tokens[0],$returned.token)
                    completion=[object]::ReferenceEquals($completion,$returned.completion)
                    mode=$returned.mode
                    common=(Read-CommonParameterType)
                } | ConvertTo-Json -Compress
            } finally {
                $searcher.Dispose()
                # PowerShell's ADSI adapter can bind to a directory when resolving an instance method.
                [void][IDisposable].GetMethod('Dispose').Invoke($entry, @())
            }
            """;
        CompareScopedArtifacts(source, probe,
            new[] { "Set-BorrowedSearchScope", "Read-CompletionMetadata", "Read-CommonParameterType", "Read-InlineModule" }, value =>
            {
                foreach (var name in new[] { "entry", "searcher", "module", "inline", "token", "completion" })
                    Assert.Contains("\"" + name + "\":true", value, StringComparison.Ordinal);
                Assert.Contains("\"scope\":\"OneLevel\"", value, StringComparison.Ordinal);
                Assert.Contains("\"mode\":\"FullLanguage\"", value, StringComparison.Ordinal);
                Assert.Contains("ParameterArgumentTransformationError", value, StringComparison.Ordinal);
                Assert.Contains("\"common\":[\"System.Management.Automation.ActionPreference\",\"System.Management.Automation.SwitchParameter\",\"System.Management.Automation.SwitchParameter\",\"System.Management.Automation.SwitchParameter\",\"System.String\"]", value, StringComparison.Ordinal);
            });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void NativeReferencesArrayCastsAndChainedAssignmentsMatchBothHosts()
    {
        const string source = """
            function Read-ParsedText {
                [CmdletBinding()]
                param([string] $Text)
                $tokens = $null
                $tree = [Management.Automation.Language.Parser]::ParseInput($Text, [ref]$tokens, [ref]$null)
                [ordered]@{text=$tree.Extent.Text;kind=$tokens[0].Kind.ToString()}
            }
            function Convert-InputVector {
                [CmdletBinding()]
                param($Value)
                $result = [string[]]($Value, 'tail')
                [ordered]@{type=$result.GetType().FullName;values=$result}
            }
            function Set-ChainedValues {
                [CmdletBinding()]
                param($Value, $Outer, $Inner)
                $Outer.Value = $Inner.Value = $Value
                [ordered]@{outer=$Outer.Value;inner=$Inner.Value}
            }
            function Set-FailingChain {
                [CmdletBinding()]
                param($Inner, $Key)
                $Outer = 'text'
                $Outer.Length = $Inner[$($Key + 0)] = 'new'
            }
            Export-ModuleMember -Function Read-ParsedText, Convert-InputVector, Set-ChainedValues, Set-FailingChain
            """;
        const string probe = """
            $outer = @{Value='outer'}
            $inner = @{Value='inner'}
            $indexed = @{1='old'}
            $failure = try { Set-FailingChain $indexed 1 -ErrorAction Stop } catch {
                [ordered]@{id=$_.FullyQualifiedErrorId;column=$_.InvocationInfo.OffsetInLine;line=$_.InvocationInfo.Line.Trim()}
            }
            [ordered]@{
                failure=$failure
                indexed=$indexed[1]
                valid=(Read-ParsedText -Text 'Get-Item')
                incomplete=(Read-ParsedText -Text 'if (')
                vector=(Convert-InputVector -Value 17)
                chained=(Set-ChainedValues -Value 'updated' -Outer $outer -Inner $inner)
                callers=@($outer.Value,$inner.Value)
            } | ConvertTo-Json -Depth 5 -Compress
            """;
        CompareScopedArtifacts(source, probe,
            new[] { "Read-ParsedText", "Convert-InputVector", "Set-ChainedValues", "Set-FailingChain" }, value =>
            {
                Assert.Contains("PropertyAssignmentException", value, StringComparison.Ordinal);
                Assert.Contains("\"indexed\":\"new\"", value, StringComparison.Ordinal);
                Assert.Contains("\"valid\":{\"text\":\"Get-Item\",\"kind\":\"Generic\"}", value, StringComparison.Ordinal);
                Assert.Contains("\"incomplete\":{\"text\":\"if (\",\"kind\":\"If\"}", value, StringComparison.Ordinal);
                Assert.Contains("\"vector\":{\"type\":\"System.String[]\",\"values\":[\"17\",\"tail\"]}", value, StringComparison.Ordinal);
                Assert.Contains("\"chained\":{\"outer\":\"updated\",\"inner\":\"updated\"}", value, StringComparison.Ordinal);
                Assert.Contains("\"callers\":[\"updated\",\"updated\"]", value, StringComparison.Ordinal);
            });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void NativeSwitchVariablesAndNestedStorageUseAuthoredSemanticsOnBothHosts()
    {
        const string source = """
            function Read-VariableSwitch {
                [CmdletBinding()]
                param($Value, $Label)
                process {
                    switch ($Value) {
                        $Label { 'match' }
                        7 { 'seven' }
                        default { 'default' }
                    }
                }
            }
            function Set-IndexedMember {
                param($Target, $Key, $Value)
                $Target.Map[$Key.Inner.Name] = $Value
                $Target.Map[$Key.Inner.Name]
            }
            function Set-LocalOutputEncoding {
                param([int] $CodePage)
                [Console]::OutputEncoding = [Text.Encoding]::GetEncoding($CodePage)
                [Console]::OutputEncoding.CodePage
            }
            Export-ModuleMember -Function Read-VariableSwitch, Set-IndexedMember, Set-LocalOutputEncoding
            """;
        const string probe = """
            $target = @{Map=@{}}
            $key = @{Inner=@{Name='chosen'}}
            $previous = [Console]::OutputEncoding
            try { $codePage = Set-LocalOutputEncoding 65001 }
            finally { [Console]::OutputEncoding = $previous }
            [ordered]@{
                nullMatch=(Read-VariableSwitch -Value $null -Label $null)
                noMatch=(Read-VariableSwitch -Value 'other' -Label 'prefix')
                numberMatch=@(Read-VariableSwitch -Value @('7','07',7) -Label 'never')
                wildcard=(Read-VariableSwitch -Value 'prefix' -Label ([WildcardPattern]::new('pre*')))
                indexed=(Set-IndexedMember -Target $target -Key $key -Value 'written')
                caller=$target.Map['chosen']
                codepage=$codePage
            } | ConvertTo-Json -Depth 5 -Compress
            """;
        CompareScopedArtifacts(source, probe,
            new[] { "Read-VariableSwitch", "Set-IndexedMember", "Set-LocalOutputEncoding" }, value =>
            {
                Assert.Contains("\"nullMatch\":\"match\"", value, StringComparison.Ordinal);
                Assert.Contains("\"noMatch\":\"default\"", value, StringComparison.Ordinal);
            Assert.Contains("\"numberMatch\":[\"seven\",\"default\",\"seven\"]", value, StringComparison.Ordinal);
                Assert.Contains("\"wildcard\":\"match\"", value, StringComparison.Ordinal);
                Assert.Contains("\"indexed\":\"written\"", value, StringComparison.Ordinal);
                Assert.Contains("\"caller\":\"written\"", value, StringComparison.Ordinal);
                Assert.Contains("\"codepage\":65001", value, StringComparison.Ordinal);
            });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void NativeScopedAssignmentsAndEnvironmentReadsPreserveOwnershipOnBothHosts()
    {
        const string source = """
            $script:Values = @{row=@{cell=@()}}
            $prefix = [char]35
            function Read-RootPrefix { param([string] $Value) "$prefix$Value" }
            function Read-RecordArray {
                $rows = @(
                    @{Name='one';Size=2}
                    @{Name='two';Size=5}
                )
                foreach ($row in $rows) {
                    if ($row.Size -gt 2) { $row.Name }
                }
            }
            function Add-ScopedValue {
                param($Key, $Value)
                $script:Values[$Key]['cell'] += $Value
                [ordered]@{values=$script:Values[$Key]['cell']}
            }
            function Read-EnvironmentFlag {
                [CmdletBinding()]
                param()
                $present = [bool]$env:PowerForgeScopedValueProof
                Write-Verbose "present=$present"
                $present
            }
            Export-ModuleMember -Function Add-ScopedValue, Read-EnvironmentFlag, Read-RootPrefix, Read-RecordArray
            """;
        const string probe = """
            $Values = @{row=@{cell=@('caller')}}
            $prefix = 'caller'
            $saved = $env:PowerForgeScopedValueProof
            try {
                $env:PowerForgeScopedValueProof = $null
                $missing = @(Read-EnvironmentFlag -Verbose 4>&1 | ForEach-Object ToString)
                $env:PowerForgeScopedValueProof = 'value'
                $present = @(Read-EnvironmentFlag -Verbose 4>&1 | ForEach-Object ToString)
                [ordered]@{
                    missing=$missing;present=$present
                    prefix=(Read-RootPrefix 'value')
                    row=(Read-RecordArray)
                    first=(Add-ScopedValue row 'one')
                    second=(Add-ScopedValue row 'two')
                    caller=$Values.row.cell
                } | ConvertTo-Json -Depth 6 -Compress
            } finally { $env:PowerForgeScopedValueProof = $saved }
            """;
        CompareScopedArtifacts(source, probe, new[] { "Add-ScopedValue", "Read-EnvironmentFlag", "Read-RootPrefix", "Read-RecordArray" }, value =>
        {
            Assert.Contains("\"missing\":[\"present=False\",\"False\"]", value, StringComparison.Ordinal);
            Assert.Contains("\"present\":[\"present=True\",\"True\"]", value, StringComparison.Ordinal);
            Assert.Contains("\"first\":{\"values\":[\"one\"]}", value, StringComparison.Ordinal);
            Assert.Contains("\"second\":{\"values\":[\"one\",\"two\"]}", value, StringComparison.Ordinal);
            Assert.Contains("\"caller\":[\"caller\"]", value, StringComparison.Ordinal);
            Assert.Contains("\"prefix\":\"#value\"", value, StringComparison.Ordinal);
            Assert.Contains("\"row\":\"two\"", value, StringComparison.Ordinal);
        });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void NativeModuleRequirementsPreserveLoadBeforeInvocationOnBothHosts()
    {
        const string source = """
            #requires -Modules Generic.Compiler.Requirement
            $script:RequiredModuleAtImport = [bool]$global:PowerForgeRequiredModuleFixtureLoaded
            function Read-RequiredModule {
                param([int]$Value)
                [ordered]@{loaded=$script:RequiredModuleAtImport;value=$Value}
            }
            Export-ModuleMember -Function Read-RequiredModule
            """;
        const string probe = """
            Read-RequiredModule -Value '7' | ConvertTo-Json -Compress
            """;
        using var dependency = OracleFixture.Create(string.Empty);
        var moduleDirectory = Directory.CreateDirectory(Path.Combine(dependency.RootPath, "Generic.Compiler.Requirement"));
        File.WriteAllText(Path.Combine(moduleDirectory.FullName, "Generic.Compiler.Requirement.psm1"),
            "$global:PowerForgeRequiredModuleFixtureLoaded = $true");
        var importSetup = "$env:PSModulePath = '" + dependency.RootPath.Replace("'", "''", StringComparison.Ordinal) +
            "' + [IO.Path]::PathSeparator + $env:PSModulePath";
        CompareScopedArtifacts(source, probe, new[] { "Read-RequiredModule" }, value =>
            Assert.Equal("{\"loaded\":true,\"value\":7}", value), importSetup);
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RepeatedRootDeclarationsPreserveImportOrderAndNativeBindingOnBothHosts()
    {
        const string source = """
            function Read-Revision { param([int] $Count = 1) $Count + 10 }
            $script:Before = Read-Revision
            function Read-Revision { param([int] $Count = 2) $Count + 20 }
            $script:After = Read-Revision
            function Read-RevisionOrder {
                [ordered]@{before=$script:Before;after=$script:After;caller=(Read-Revision 3)}
            }
            Export-ModuleMember -Function Read-Revision, Read-RevisionOrder
            """;
        const string probe = """
            [ordered]@{
                current=(Read-Revision)
                order=(Read-RevisionOrder)
                commandType=(Get-Command Read-Revision).CommandType.ToString()
            } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-Revision", "Read-Revision", "Read-RevisionOrder" }, value =>
        {
            Assert.Contains("\"current\":22", value, StringComparison.Ordinal);
            Assert.Contains("\"order\":{\"before\":11,\"after\":22,\"caller\":23}", value, StringComparison.Ordinal);
            Assert.Contains("\"commandType\":\"Function\"", value, StringComparison.Ordinal);
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

    private static void CompareScopedArtifacts(string source, string probe, string[] compiledNames, Action<string> assertValue,
        string? importSetup = null, string? requiredModule = null, Action<PowerShellCompilationBuildResult>? assertArtifact = null,
        IReadOnlyDictionary<string, string>? additionalSources = null, bool retainAdditionalSources = false,
        bool discoverAdditionalSources = false, string? manifestFunctionPattern = "*", bool resolveModuleSources = false,
        string? nestedModule = null)
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
            File.WriteAllText(module, source, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var additionalSourcePaths = new List<string>();
            if (additionalSources is not null)
                foreach (var (name, content) in additionalSources)
                {
                    var path = Path.Combine(fixture.RootPath, name);
                    File.WriteAllText(path, content);
                    additionalSourcePaths.Add(path);
                }
            string? manifest = null;
            if (requiredModule is not null || nestedModule is not null || retainAdditionalSources ||
                (discoverAdditionalSources || resolveModuleSources) && manifestFunctionPattern is not null)
            {
                manifest = Path.ChangeExtension(module, ".psd1");
                var required = requiredModule is null ? string.Empty : " RequiredModules=@('" + requiredModule + "');";
                var files = retainAdditionalSources ? " FileList=@(" + string.Join(", ", additionalSources!.Keys.Select(name => "'" + name + "'")) + ");" : string.Empty;
                var functions = manifestFunctionPattern is null ? string.Empty : " FunctionsToExport='" + manifestFunctionPattern + "';";
                var nested = nestedModule is null ? string.Empty : " NestedModules=@('" + nestedModule + "');";
                File.WriteAllText(manifest, "@{ RootModule='ScopedValues.psm1'; ModuleVersion='1.0.0';" + functions + required + files + nested + " }");
            }
            var input = resolveModuleSources
                ? new PowerShellCompilationInputResolver(profile).Resolve(manifest ?? module,
                    PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid)
                : null;
            var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                module, Path.Combine(fixture.RootPath, "built"), "ScopedValues", PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = framework,
                SemanticProfileId = profile,
                ModuleManifestPath = manifest,
                CompilationSourcePaths = input?.CompilationSourceFiles ??
                    (retainAdditionalSources || discoverAdditionalSources ? Array.Empty<string>() : additionalSourcePaths.ToArray()),
                RuntimeSourcePaths = input?.SourceFiles ?? Array.Empty<string>()
            });
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
            assertArtifact?.Invoke(built);
            foreach (var expected in compiledNames.GroupBy(static name => name, StringComparer.Ordinal))
            {
                var units = built.Manifest!.UnitDispositionLedger!.Entries.Where(entry => entry.Name == expected.Key).ToArray();
                Assert.Equal(expected.Count(), units.Length);
                Assert.Equal(units.Length, units.Select(static unit => unit.UnitId).Distinct(StringComparer.Ordinal).Count());
                foreach (var unit in units)
                {
                    Assert.True(unit.EmittedClrMethod, unit.Name + " requires a complete emitted body: " + System.Text.Json.JsonSerializer.Serialize(unit));
                    Assert.False(unit.RetainedHostedSource, unit.Name + " must execute its emitted body.");
                }
            }
            PowerShellCompilationSemanticOracleEnvelope Observe(string path)
            {
                File.WriteAllText(fixture.ScriptPath, "$global:VerbosePreference='SilentlyContinue'\n" +
                    (importSetup is null ? string.Empty : importSetup + "\n") + "Import-Module '" +
                    path.Replace("'", "''", StringComparison.Ordinal) + "' -Force -ErrorAction Stop\n" + probe);
                return new PowerShellCompilationSemanticOracleRunner().Observe(new PowerShellCompilationSemanticOracleRequest(profile, fixture.ScriptPath)
                {
                    HostExecutablePath = host,
                    ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(
                        PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profile))
                });
            }
            var original = Observe(manifest ?? module);
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
