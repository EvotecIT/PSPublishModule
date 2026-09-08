param([Parameter(Mandatory)][string]$Assembly)
$ErrorActionPreference = 'Stop'
Add-Type -Path $Assembly
function Read-NativeBinding {
    [CmdletBinding()] param([ValidatePattern('.*')][string]$First,[string]$Value)
    $First.GetType().FullName+'|'+$First+'|'+$Value
}
function Read-NativePipelineBinding {
    [CmdletBinding()] param([ValidatePattern('.*')][string]$First,[Parameter(ValueFromPipeline)][string]$Value)
    process { $First.GetType().FullName+'|'+$First+'|'+$Value }
}
function New-ObservedValue([string]$Mode) {
    $item = [pscustomobject]@{Mode=$Mode}
    Add-Member -InputObject $item -MemberType ScriptMethod -Name ToString -Force -Value {
        if ($this.Mode -eq 'ClearAttributes') {
            $variable = Get-Variable -Scope 1 -Name First
            $variable.Attributes.Clear()
            $variable.Value = 42
        }
        'seen='+$First
    }
    $item
}
Set-Item -LiteralPath Function:\Read-CompiledBinding -Value ([Generic.Compiler.StatementErrors.NativeFunctionBindingFixture]::Create($false))
Set-Item -LiteralPath Function:\Read-CompiledPipelineBinding -Value ([Generic.Compiler.StatementErrors.NativeFunctionBindingFixture]::Create($true))
$results = @{}
foreach ($command in 'Read-NativeBinding','Read-CompiledBinding') {
    $results[$command] = @(foreach ($mode in 'Plain','ClearAttributes') {
        & $command -First before -Value (New-ObservedValue $mode)
        & $command -First fresh -Value plain
    }) -join "`n"
}
foreach ($command in 'Read-NativePipelineBinding','Read-CompiledPipelineBinding') {
    $results[$command] = @(
        $items = foreach ($mode in 'Plain','ClearAttributes','Plain') { New-ObservedValue $mode }
        $items | & $command -First before
        'plain' | & $command -First fresh
    ) -join "`n"
}
foreach ($pair in @(@('Read-NativeBinding','Read-CompiledBinding'), @('Read-NativePipelineBinding','Read-CompiledPipelineBinding'))) {
    if ($results[$pair[0]] -cne $results[$pair[1]]) {
        throw "Mismatch: $($pair[0])`n$($results[$pair[0]])`n$($results[$pair[1]])"
    }
    $results[$pair[1]]
}
Set-Item -LiteralPath Function:\Read-CompiledLifecycle -Value ([Generic.Compiler.StatementErrors.NativeFunctionBindingFixture]::CreateLifecycle())
$lifecycle = @(1,2,3 | Read-CompiledLifecycle) -join '|'
if ($lifecycle -cne 'begin|1|2|3|end:3') { throw "Lifecycle mismatch: $lifecycle" }
$lifecycle
$fresh = @(4 | Read-CompiledLifecycle) -join '|'
if ($fresh -cne 'begin|4|end:1') { throw "Fresh invocation mismatch: $fresh" }
$fresh
[Generic.Compiler.StatementErrors.NativeFunctionBindingFixture]::VerifyRetiredContext()
[Generic.Compiler.StatementErrors.NativeFunctionBindingFixture]::VerifyMetadataBoundary()
function Read-NativeObservedBody {
    [CmdletBinding()] param([object]$Value)
    $Observed = 'before'
    $OFS = '::'
    "value=$Value"
    "after=$Observed"
}
Set-Item -LiteralPath Function:\Read-CompiledObservedBody -Value ([Generic.Compiler.StatementErrors.NativeFunctionBindingFixture]::CreateObservedBody())
foreach ($shape in 'callback','array','null') {
    $observedResults = foreach ($command in 'Read-NativeObservedBody','Read-CompiledObservedBody') {
        $item = switch ($shape) {
            callback {
                $value = [pscustomobject]@{}
                Add-Member -InputObject $value -MemberType ScriptMethod -Name ToString -Force -Value {
                    $observed = Get-Variable -Scope 1 -Name Observed
                    $previous = $observed.Value
                    $observed.Value = 42
                    'saw:'+ $previous
                }
                $value
            }
            array { ,@('one','two') }
            null { $null }
        }
        @(& $command -Value $item) -join '|'
    }
    if ($observedResults[0] -cne $observedResults[1]) { throw "Observed body mismatch: $observedResults" }
    $shape+':'+$observedResults[1]
}
function Read-NativeParameterWrite {
    [CmdletBinding()] param([ValidateRange(0,10)][int]$Value,[object]$Replacement)
    try { $Value = $Replacement }
    catch { 'error:'+$_.Exception.GetType().FullName+'|'+$_.FullyQualifiedErrorId }
    $Value.GetType().FullName+'|'+$Value
}
Set-Item -LiteralPath Function:\Read-CompiledParameterWrite -Value ([Generic.Compiler.StatementErrors.NativeFunctionBindingFixture]::CreateParameterWrite())
foreach ($replacement in 4.5,11,'invalid') {
    $expected = @(Read-NativeParameterWrite -Value 1 -Replacement $replacement) -join '|'
    $actual = @(Read-CompiledParameterWrite -Value 1 -Replacement $replacement) -join '|'
    if ($expected -cne $actual) { throw "Parameter write mismatch: $expected / $actual" }
    $actual
}
function New-NestedObservedValue([string]$Command,[int]$Depth) {
    $value = [pscustomobject]@{Command=$Command;Depth=$Depth}
    Add-Member -InputObject $value -MemberType ScriptMethod -Name ToString -Force -Value {
        $observed = Get-Variable -Scope 1 -Name Observed
        $before = $observed.Value
        $inner = if ($this.Depth -gt 0) {
            $child = New-NestedObservedValue -Command $this.Command -Depth ($this.Depth-1)
            @(& $this.Command -Value $child) -join '/'
        } else { 'leaf' }
        $after = (Get-Variable -Scope 1 -Name Observed).Value
        $observed.Value = 42
        $this.Depth.ToString()+':'+$before+':'+$after+':'+$inner
    }
    $value
}
$nested = foreach ($command in 'Read-NativeObservedBody','Read-CompiledObservedBody') {
    @(& $command -Value (New-NestedObservedValue -Command $command -Depth 3)) -join '|'
}
if ($nested[0] -cne $nested[1]) { throw "Nested body mismatch: $nested" }
$nested[1]
$modules = foreach ($marker in 'first','second') {
    New-Module -Name ('NativeHostProbe_'+$marker) -ArgumentList $marker -ScriptBlock {
        param($Marker)
        $script:Marker = $Marker
        function Get-LocalValue { $script:Marker+'-default' }
        function Test-LocalValue($Value) { $Value -like ($script:Marker+'-*') }
        function Invoke-NativeModule {
            [CmdletBinding()] param([ValidateScript({ Test-LocalValue $_ })][string]$Value = (Get-LocalValue))
            $script:Marker+'|'+$Value
        }
        $compiled = [Generic.Compiler.StatementErrors.NativeFunctionBindingFixture]::CreateModuleOwned($ExecutionContext.SessionState.Module)
        Set-Item -LiteralPath Function:\local:Invoke-CompiledModule -Value $compiled
        Export-ModuleMember -Function Invoke-NativeModule,Invoke-CompiledModule
    }
}
foreach ($module in $modules) {
    $native = $module.ExportedFunctions['Invoke-NativeModule']
    $compiled = $module.ExportedFunctions['Invoke-CompiledModule']
    $expected = & $native
    $actual = & $compiled
    if ($expected -cne $actual) { throw "Module default mismatch: $expected / $actual" }
    $actual
    foreach ($command in $native,$compiled) {
        $rejected = $false
        try { & $command -Value 'wrong-module' }
        catch { $rejected = $_.FullyQualifiedErrorId -like 'ParameterArgumentValidationError*' }
        if (!$rejected) { throw 'Module validation did not reject a foreign value.' }
    }
}
if ($PSVersionTable.PSVersion.Major -ge 7) {
    Set-Item -LiteralPath Function:\Read-CompiledCleanup -Value ([Generic.Compiler.StatementErrors.NativeFunctionBindingFixture]::CreateCleanup())
    foreach ($mode in 'complete','stop','fail') {
        [Generic.Compiler.StatementErrors.NativeFunctionBindingFixture]::CleanupTrace.Clear()
        $output = switch ($mode) {
            complete { @(1,2 | Read-CompiledCleanup) -join '|' }
            stop { @(1,2 | Read-CompiledCleanup | Select-Object -First 1) -join '|' }
            fail { try { 1,2 | Read-CompiledCleanup -Fail } catch { 'caught' } }
        }
        $trace = [Generic.Compiler.StatementErrors.NativeFunctionBindingFixture]::CleanupTrace -join '|'
        $expectedTrace = switch ($mode) {
            complete { 'begin|process:1|process:2|end|clean' }
            stop { 'begin|process:1|clean' }
            fail { 'begin|process:1|clean' }
        }
        $expectedOutput = switch ($mode) { complete { '1|2' } stop { '1' } fail { 'caught' } }
        if ($trace -cne $expectedTrace -or $output -cne $expectedOutput) { throw "Cleanup mismatch ($mode): $trace / $output" }
        $mode+':'+$trace
    }
}
'Native function host qualification passed.'
