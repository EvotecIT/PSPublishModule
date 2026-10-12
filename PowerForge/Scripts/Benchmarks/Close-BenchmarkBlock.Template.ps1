param([scriptblock] $ScriptBlock)

$scriptRoot = __POWERFORGE_SCRIPT_ROOT__
$operationTimerAccessor = $__PowerForgeOperationTimerAccessor
$captured = @{}
$capturedFunctions = @{}
$authoredScopeFound = $false
$authoredModule = $null
$preserveCallbackModule = $false
$skipNames = @(
    'args', 'input', 'this', 'PSItem', '_', 'null', 'Error',
    'PWD', 'captured', 'capturedFunctions', 'scriptText', 'scriptRoot', 'operationTimerAccessor', '__PowerForgeOperationTimerAccessor',
    'BenchmarkCallerFunctions', 'PowerForgeBenchmarkDslCommandAliases', 'PowerForgeBenchmarkDslRuntimeType',
    'ConfirmPreference', 'DebugPreference', 'ErrorActionPreference', 'ErrorView',
    'InformationPreference', 'ProgressPreference', 'PSNativeCommandUseErrorActionPreference',
    'PSDefaultParameterValues', 'VerbosePreference', 'WarningPreference', 'WhatIfPreference'
)
$skipFunctions = @(
    '__PowerForgeCloseBenchmarkBlock', '__PowerForgeBenchmarkDslInvoke',
    'New-BenchmarkSuite', 'Add-BenchmarkCases', 'Add-BenchmarkCase', 'Add-BenchmarkCaseSource', 'Add-BenchmarkAxis',
    'Set-BenchmarkSetup', 'Set-BenchmarkDataFactory', 'Set-BenchmarkPolicy', 'Set-BenchmarkProfile', 'Set-BenchmarkCleanup', 'Add-BenchmarkEngine', 'Add-BenchmarkOperation',
    'Add-BenchmarkSkipRule', 'Add-BenchmarkValidation', 'Add-BenchmarkMetric', 'Add-BenchmarkMetadata', 'Add-BenchmarkComparison',
    'Add-BenchmarkReadmeBlock', 'Set-BenchmarkArtifacts', 'Get-BenchmarkInput'
)

function __PowerForgeBenchmarkDslInvoke {
    param([Parameter(Mandatory = $true)] [string] $Name, [object[]] $Arguments = @())

    $runtimeType = $PowerForgeBenchmarkDslRuntimeType
    if ($null -eq $runtimeType) { throw 'Benchmark DSL runtime type was not provided.' }
    $flags = [System.Reflection.BindingFlags]'Public, Static'
    $methods = @($runtimeType.GetMethods($flags) | Where-Object { $_.Name -eq $Name -and $_.GetParameters().Count -eq $Arguments.Count })
    if ($methods.Count -ne 1) { throw "Benchmark DSL runtime method '$Name' with $($Arguments.Count) argument(s) was not found." }
    try {
        $methods[0].Invoke($null, $Arguments)
    } catch [System.Reflection.TargetInvocationException] {
        if ($_.Exception.InnerException) { throw $_.Exception.InnerException }
        throw
    }
}

if ($null -ne $BenchmarkCallerFunctions) {
    foreach ($entry in $BenchmarkCallerFunctions.GetEnumerator()) {
        if ($skipFunctions -contains $entry.Key) { continue }
        if (-not $capturedFunctions.ContainsKey($entry.Key)) {
            $capturedFunctions[$entry.Key] = [string] $entry.Value
        }
    }
}

if ($null -ne $PowerForgeBenchmarkDslCommandAliases) {
    foreach ($entry in $PowerForgeBenchmarkDslCommandAliases.GetEnumerator()) {
        if ($skipFunctions -contains $entry.Key) { continue }
        $dslFunction = Get-Item -LiteralPath "Function:\$($entry.Value)" -ErrorAction SilentlyContinue
        if ($null -ne $dslFunction -and -not [string]::IsNullOrWhiteSpace($dslFunction.Definition)) {
            $capturedFunctions[[string] $entry.Key] = [string] $dslFunction.Definition
        }
    }
}

for ($scope = 2; $scope -lt 20; $scope++) {
    try {
        $variables = Get-Variable -Scope $scope -ErrorAction Stop
    } catch {
        break
    }

    # DSL dispatch and generated command parameters are implementation scopes,
    # not authored values, even when their names match a callback's variables.
    $scopeInvocation = $null
    foreach ($variable in $variables) {
        if ($variable.Name -eq 'MyInvocation') { $scopeInvocation = $variable.Value; break }
    }
    if ($null -ne $scopeInvocation -and $null -ne $scopeInvocation.MyCommand) {
        $scopeCommand = $scopeInvocation.MyCommand
        if ($scopeCommand.Name -eq '__PowerForgeBenchmarkDslInvoke' -or
            $scopeCommand.Name -like '__PowerForgeBenchmarkDslCommand_*') { continue }
        $scopeBlockProperty = $scopeCommand.PSObject.Properties['ScriptBlock']
        if (-not $authoredScopeFound -and $null -ne $scopeBlockProperty -and
            $scopeBlockProperty.Value -is [scriptblock]) {
            $authoredScopeFound = $true
            $authoredModule = $scopeBlockProperty.Value.Module
            # An explicit GetNewClosure has its own module. Nested blocks that
            # merely inherit the author's module still need local scope capture.
            $preserveCallbackModule = $null -ne $ScriptBlock.Module -and
                -not [object]::ReferenceEquals($ScriptBlock.Module, $scopeBlockProperty.Value.Module)
        }
    }

    foreach ($variable in $variables) {
        if ($skipNames -contains $variable.Name) { continue }
        if ($captured.ContainsKey($variable.Name)) { continue }
        if (($variable.Options -band [System.Management.Automation.ScopedItemOptions]::Constant) -or
            ($variable.Options -band [System.Management.Automation.ScopedItemOptions]::ReadOnly)) { continue }
        $captured[$variable.Name] = $variable.Value
    }
}

foreach ($function in Get-ChildItem -Path Function: -ErrorAction SilentlyContinue) {
    if ($skipFunctions -contains $function.Name) { continue }
    if ($function.Name -like '__PowerForgeBenchmarkDslCommand_*') { continue }
    if ($function.Name -like '*:*') { continue }
    # Helpers declared in closed settings belong to that authored module.
    # Keep excluding functions supplied by unrelated imported modules.
    $isAuthoredModuleFunction = $null -ne $authoredModule -and
        [object]::ReferenceEquals($function.Module, $authoredModule)
    if (-not $isAuthoredModuleFunction -and
        (-not [string]::IsNullOrWhiteSpace($function.Source) -or
         -not [string]::IsNullOrWhiteSpace($function.ModuleName))) { continue }
    if (($function.Options -band [System.Management.Automation.ScopedItemOptions]::Constant) -or
        ($function.Options -band [System.Management.Automation.ScopedItemOptions]::ReadOnly)) { continue }
    if ([string]::IsNullOrWhiteSpace($function.Definition)) { continue }
    $capturedFunctions[$function.Name] = __PowerForgeBenchmarkDslInvoke -Name 'CaptureScriptText' -Arguments ([object[]] @([scriptblock]::Create([string] $function.Definition), $scriptRoot))
}

$scriptText = __PowerForgeBenchmarkDslInvoke -Name 'CaptureScriptText' -Arguments ([object[]] @($ScriptBlock, $scriptRoot))
$operationBody = [scriptblock]::Create($scriptText)
$callbackVariables = $captured
if ($preserveCallbackModule) {
    # Bound closure values, including null, win over the registering caller.
    # Retain authored outer variables that the closure did not itself capture.
    $moduleBindings = @{}
    foreach ($variable in $ScriptBlock.Module.Invoke({ Get-Variable -Scope Script })) {
        $moduleBindings[$variable.Name] = $true
    }
    $callbackVariables = @{}
    foreach ($entry in $captured.GetEnumerator()) {
        if (-not $moduleBindings.ContainsKey($entry.Key)) { $callbackVariables[$entry.Key] = $entry.Value }
    }
}
$operationInvoker = {
    param(
        [scriptblock] $__PowerForgeCallbackBlock,
        [object[]] $__PowerForgeCallbackArguments,
        [object] $__PowerForgeCallbackTimerAccessor,
        [object] $__PowerForgeCallbackFunctions,
        [object] $__PowerForgeCallbackVariables
    )
    $ErrorActionPreference = 'Stop'
    $PSNativeCommandUseErrorActionPreference = $true
    $PSDefaultParameterValues = @{}
    $__PowerForgePreviousFunctions = @{}
    $__PowerForgeMissingFunctions = @{}
    try {
        foreach ($__PowerForgeFunction in $__PowerForgeCallbackFunctions.GetEnumerator()) {
            $__PowerForgeFunctionPath = "Function:\$($__PowerForgeFunction.Key)"
            $__PowerForgeExistingFunction = Get-Item -Path $__PowerForgeFunctionPath -ErrorAction SilentlyContinue
            if ($null -eq $__PowerForgeExistingFunction) {
                $__PowerForgeMissingFunctions[$__PowerForgeFunction.Key] = $true
            } else {
                $__PowerForgePreviousFunctions[$__PowerForgeFunction.Key] = $__PowerForgeExistingFunction.ScriptBlock
            }
            Set-Item -Path $__PowerForgeFunctionPath -Value ([scriptblock]::Create([string] $__PowerForgeFunction.Value)) -ErrorAction Stop
        }
        foreach ($__PowerForgeVariable in $__PowerForgeCallbackVariables.GetEnumerator()) {
            Set-Variable -Name $__PowerForgeVariable.Key -Value $__PowerForgeVariable.Value -Scope Local
        }
        $__PowerForgeCallbackTimer = $__PowerForgeCallbackTimerAccessor.Invoke()
        if ($null -ne $__PowerForgeCallbackTimer) { $__PowerForgeCallbackTimer.Start() }
        try {
            & $__PowerForgeCallbackBlock @__PowerForgeCallbackArguments
        }
        finally {
            if ($null -ne $__PowerForgeCallbackTimer) { $__PowerForgeCallbackTimer.Stop() }
        }
    }
    finally {
        foreach ($__PowerForgeFunction in $__PowerForgeCallbackFunctions.GetEnumerator()) {
            $__PowerForgeFunctionPath = "Function:\$($__PowerForgeFunction.Key)"
            if ($__PowerForgePreviousFunctions.ContainsKey($__PowerForgeFunction.Key)) {
                Set-Item -Path $__PowerForgeFunctionPath -Value $__PowerForgePreviousFunctions[$__PowerForgeFunction.Key] -ErrorAction SilentlyContinue
            } elseif ($__PowerForgeMissingFunctions.ContainsKey($__PowerForgeFunction.Key)) {
                Remove-Item -Path $__PowerForgeFunctionPath -ErrorAction SilentlyContinue
            }
        }
    }
}
if ($preserveCallbackModule) {
    $operationBody = $ScriptBlock.Module.NewBoundScriptBlock($operationBody)
    $operationInvoker = $ScriptBlock.Module.NewBoundScriptBlock($operationInvoker)
}
$closed = {
    # Install context in the body's module and time only the authored body. The
    # native call operator also supports a closure after its evaluating runspace
    # has closed; it executes through the runner's active PowerShell context.
    & $operationInvoker $operationBody ([object[]] $args) $operationTimerAccessor $capturedFunctions $callbackVariables
}.GetNewClosure()
if (-not $preserveCallbackModule) {
    $closed.Module.SessionState.PSVariable.Set('operationInvoker', $closed.Module.NewBoundScriptBlock($operationInvoker))
}
$closed
