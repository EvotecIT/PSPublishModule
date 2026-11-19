[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ImportPath,
    [Parameter(Mandatory)] [ValidateSet('Original', 'Built', 'Installed')] [string] $Surface,
    [Parameter(Mandatory)] [string] $ScenarioRoot,
    [Parameter(Mandatory)] [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -LiteralPath $PSScriptRoot) 'DirectoryObserver.Common.ps1')
. (Join-Path $PSScriptRoot 'Observe-ReadOnlyApplication.ps1')
. (Join-Path $PSScriptRoot 'Observe-LocalContracts.ps1')

$moduleBase = Split-Path -LiteralPath $ImportPath
$inventory = @(Get-Content -LiteralPath (Join-Path $ScenarioRoot 'owned-functions.json') -Raw | ConvertFrom-Json)
$ownership = Get-DirectoryFunctionOwnership -Surface $Surface -ImportPath $ImportPath -ResultPath $ResultPath -OwnedFunctions $inventory
$entered = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$previous = $ExecutionContext.InvokeCommand.PostCommandLookupAction
$ExecutionContext.InvokeCommand.PostCommandLookupAction = New-DirectoryFunctionObserver -Surface $Surface -ModuleBase $moduleBase -Ownership $ownership -Entered $entered
try {
    $state = & (Join-Path $PSScriptRoot 'Test-Module.ps1') -ArtifactPath $ImportPath -PassThru
    $module = $state.Module
    $auditRoot = (New-Item -ItemType Directory -Path (Join-Path $ScenarioRoot ($Surface + '-read-only'))).FullName
    $audit = Invoke-ClassicReadOnlyApplication -Module $module -Directory $auditRoot -Scans Auditing
    $auditEntered = @($entered)
    $entered.Clear()
    $failureRoot = (New-Item -ItemType Directory -Path (Join-Path $ScenarioRoot ($Surface + '-incomplete-record'))).FullName
    $failure = Invoke-ClassicReadOnlyApplication -Module $module -Directory $failureRoot -Scans All
    $failureEntered = @($entered)
    $entered.Clear()
    $localRoot = (New-Item -ItemType Directory -Path (Join-Path $ScenarioRoot ($Surface + '-local-contracts'))).FullName
    $local = Invoke-ClassicLocalContracts -Module $module -Directory $localRoot
} finally {
    $ExecutionContext.InvokeCommand.PostCommandLookupAction = $previous
}
if ('Invoke-Locksmith' -notin $auditEntered -or 'Invoke-Scans' -notin $auditEntered -or 'Format-Result' -notin $failureEntered) {
    throw 'The observer did not reach the owned public audit and its scan dispatcher.'
}
$direct = @('New-Dictionary', 'Find-AuditingIssue')
$executed = @($inventory | Where-Object { $_.name -in $auditEntered -or $_.name -in $direct })
$failedRecord = @($inventory | Where-Object { $_.name -in $failureEntered })
$localFunctions = @($inventory | Where-Object { $entered.Contains($_.name) -or $_.name -in $local.direct })
[ordered]@{
    schemaVersion = 1
    surface = $Surface
    importPath = [IO.Path]::GetFullPath($ImportPath)
    actualExports = @($module.ExportedCommands.Values | Where-Object { $_.CommandType -in 'Function', 'Cmdlet' } | ForEach-Object Name)
    actualAliases = @($module.ExportedAliases.Values | ForEach-Object { [ordered]@{ name = $_.Name; definition = $_.Definition } })
    scenarios = @(
        [ordered]@{
            scenarioId = 'read-only-application'
            succeeded = $true
            executedExports = @('Invoke-Locksmith')
            executedOwnedFunctions = $executed
            activeOwnedBodies = @($executed.name | Where-Object { $_ -in $auditEntered })
            directOwnedCalls = $direct
            observation = [ordered]@{
                audit = $audit
                dictionaryCount = $state.DictionaryCount
                directoryParameterType = $state.DirectoryParameterType
                inertAuditMatches = $state.InertAuditMatches
            }
        }
        [ordered]@{
            scenarioId = 'incomplete-directory-record'
            succeeded = $true
            executedExports = @('Invoke-Locksmith')
            executedOwnedFunctions = $failedRecord
            activeOwnedBodies = @($failedRecord.name)
            directOwnedCalls = @()
            observation = $failure
        }
        [ordered]@{
            scenarioId = 'local-contracts'
            succeeded = $true
            executedExports = @()
            executedOwnedFunctions = $localFunctions
            activeOwnedBodies = @($localFunctions.name | Where-Object { $entered.Contains($_) })
            directOwnedCalls = $local.direct
            observation = $local.observation
        }
    )
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $ResultPath -Encoding utf8NoBOM
