param([scriptblock] $ScriptBlock)

$scriptRoot = __POWERFORGE_SCRIPT_ROOT__
$captured = @{}
$capturedFunctions = @{}
$skipNames = @(
    'args', 'input', 'this', 'PSItem', '_', 'Error',
    'PWD', 'captured', 'capturedFunctions', 'scriptText', 'scriptRoot',
    'BenchmarkCallerFunctions',
    'ConfirmPreference', 'DebugPreference', 'ErrorActionPreference', 'ErrorView',
    'InformationPreference', 'ProgressPreference', 'PSNativeCommandUseErrorActionPreference',
    'PSDefaultParameterValues', 'VerbosePreference', 'WarningPreference', 'WhatIfPreference'
)
$skipFunctions = @(
    '__PowerForgeCloseBenchmarkBlock',
    'benchmark', 'cases', 'case', 'caseSource', 'from', 'axis', 'setup', 'data', 'skip', 'validate', 'profile', 'cleanup', 'engine', 'operation', 'metric', 'compare', 'comparison', 'readme', 'artifacts',
    'New-BenchmarkSuite', 'Add-BenchmarkCases', 'Add-BenchmarkCase', 'Add-BenchmarkCaseSource', 'Add-BenchmarkAxis',
    'Set-BenchmarkSetup', 'Set-BenchmarkDataFactory', 'Set-BenchmarkProfile', 'Set-BenchmarkCleanup', 'Add-BenchmarkEngine', 'Add-BenchmarkOperation',
    'Add-BenchmarkSkipRule', 'Add-BenchmarkValidation', 'Add-BenchmarkMetric', 'Add-BenchmarkComparison',
    'Add-BenchmarkReadmeBlock', 'Set-BenchmarkArtifacts'
)
if ($null -ne $BenchmarkCallerFunctions) {
    foreach ($entry in $BenchmarkCallerFunctions.GetEnumerator()) {
        if ($skipFunctions -contains $entry.Key) { continue }
        if (-not $capturedFunctions.ContainsKey($entry.Key)) {
            $capturedFunctions[$entry.Key] = [string] $entry.Value
        }
    }
}

for ($scope = 2; $scope -lt 20; $scope++) {
    try {
        $variables = Get-Variable -Scope $scope -ErrorAction Stop
    } catch {
        break
    }

    foreach ($variable in $variables) {
        if ($skipNames -contains $variable.Name) { continue }
        if ($captured.ContainsKey($variable.Name)) { continue }
        if (($variable.Options -band [System.Management.Automation.ScopedItemOptions]::Constant) -or
            ($variable.Options -band [System.Management.Automation.ScopedItemOptions]::ReadOnly)) { continue }
        $captured[$variable.Name] = $variable.Value
    }
}

foreach ($function in Get-Command -CommandType Function -ErrorAction SilentlyContinue) {
    if ($skipFunctions -contains $function.Name) { continue }
    if ($function.Name -like '*:*') { continue }
    if (-not [string]::IsNullOrWhiteSpace($function.Source)) { continue }
    if (-not [string]::IsNullOrWhiteSpace($function.ModuleName)) { continue }
    if (($function.Options -band [System.Management.Automation.ScopedItemOptions]::Constant) -or
        ($function.Options -band [System.Management.Automation.ScopedItemOptions]::ReadOnly)) { continue }
    if ([string]::IsNullOrWhiteSpace($function.Definition)) { continue }
    if (-not $capturedFunctions.ContainsKey($function.Name)) {
        $capturedFunctions[$function.Name] = [PowerForge.PowerShellBenchmarkDslRuntime]::CaptureScriptText([scriptblock]::Create([string] $function.Definition), $scriptRoot)
    }
}

$scriptText = [PowerForge.PowerShellBenchmarkDslRuntime]::CaptureScriptText($ScriptBlock, $scriptRoot)
{
    $previousFunctions = @{}
    $missingFunctions = @{}
    try {
        foreach ($entry in $capturedFunctions.GetEnumerator()) {
            $functionPath = "Function:\$($entry.Key)"
            $existingFunction = Get-Item -Path $functionPath -ErrorAction SilentlyContinue
            if ($null -eq $existingFunction) {
                $missingFunctions[$entry.Key] = $true
            } else {
                $previousFunctions[$entry.Key] = $existingFunction.ScriptBlock
            }
            Set-Item -Path $functionPath -Value ([scriptblock]::Create([string] $entry.Value)) -ErrorAction Stop
        }
        foreach ($entry in $captured.GetEnumerator()) {
            Set-Variable -Name $entry.Key -Value $entry.Value -Scope Local
        }
        $ErrorActionPreference = 'Stop'
        $PSNativeCommandUseErrorActionPreference = $true
        & ([scriptblock]::Create($scriptText)) @args
    }
    finally {
        foreach ($entry in $capturedFunctions.GetEnumerator()) {
            $functionPath = "Function:\$($entry.Key)"
            if ($previousFunctions.ContainsKey($entry.Key)) {
                Set-Item -Path $functionPath -Value $previousFunctions[$entry.Key] -ErrorAction SilentlyContinue
            } elseif ($missingFunctions.ContainsKey($entry.Key)) {
                Remove-Item -Path $functionPath -ErrorAction SilentlyContinue
            }
        }
    }
}.GetNewClosure()
