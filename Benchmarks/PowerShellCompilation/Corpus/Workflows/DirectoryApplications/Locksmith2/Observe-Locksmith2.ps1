[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ImportPath,
    [Parameter(Mandatory)] [ValidateSet('Original', 'Built', 'Installed')] [string] $Surface,
    [Parameter(Mandatory)] [string] $ScenarioRoot,
    [Parameter(Mandatory)] [string] $ResultPath,
    [ValidateSet('audit', 'console', 'environment', 'reporting', 'issue-store')] [string] $Case,
    [string] $OwnershipPath
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.ToString() -cne '7.6.6' -or -not $IsWindows) {
    throw 'Locksmith2 qualification requires PowerShell 7.6.6 on Windows.'
}
$target = $env:POWERFORGE_EXTERNAL_AD_AUDIT_TARGET
if ([Uri]::CheckHostName($target) -ne [UriHostNameType]::Dns -or $target -notmatch '\.') {
    throw 'Set POWERFORGE_EXTERNAL_AD_AUDIT_TARGET to the DNS name of an approved read-only AD and AD CS lab.'
}

$cases = [ordered]@{
    audit = 'Observe-ReadOnlyAudit.ps1'
    console = 'Observe-Console.ps1'
    environment = 'Observe-Environment.ps1'
    reporting = 'Observe-Reporting.ps1'
    'issue-store' = 'Observe-IssueStore.ps1'
}
$moduleBase = Split-Path -LiteralPath $ImportPath

if ($Case) {
    $observer = Join-Path $PSScriptRoot $cases[$Case]
    $arguments = @{ Manifest = $ImportPath; Evidence = $ResultPath }
    if ($Case -eq 'console') { $arguments.ScratchRoot = Join-Path $ScenarioRoot 'profile' }
    $ownership = Get-Content -LiteralPath $OwnershipPath -Raw | ConvertFrom-Json -AsHashtable
    $owners = $ownership.frames
    $assemblyPath = $ownership.assembly
    $entered = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $debugger = [Management.Automation.Runspaces.Runspace]::DefaultRunspace.Debugger
    $previousLookup = $ExecutionContext.InvokeCommand.PostCommandLookupAction
    $observeScript = {
        param($name, $lookup)
        foreach ($frame in $debugger.GetCallStack()) {
            $command = $frame.InvocationInfo.MyCommand
            if ($command -is [Management.Automation.FunctionInfo] -and $command.Module.ModuleBase -ieq $moduleBase -and
                $owners.Contains($command.Name) -and $command.ScriptBlock.File -ieq $owners[$command.Name] -and
                $frame.FunctionName -ne '<ScriptBlock>') {
                [void] $entered.Add($command.Name)
            }
        }
    }
    $observeClr = {
        param($name, $lookup)
        foreach ($frame in [Diagnostics.StackTrace]::new().GetFrames()) {
            $method = $frame.GetMethod()
            $type = $method.DeclaringType
            if ($type -and $type.Assembly.Location -ieq $assemblyPath) {
                $identity = $type.FullName + '::' + $method.Name
                if ($owners.Contains($identity)) { [void] $entered.Add($owners[$identity]) }
            }
        }
    }
    $action = if ($Surface -eq 'Original') { $observeScript } else { $observeClr }
    $ExecutionContext.InvokeCommand.PostCommandLookupAction = $action.GetNewClosure()
    try {
        $Error.Clear()
        & $observer @arguments
        if ($LASTEXITCODE -or @($Error.Where({ $_.Exception -is [Management.Automation.ParameterBindingException] })).Count) {
            throw 'The observer encountered a script exit or a command binding error.'
        }
    } finally {
        $ExecutionContext.InvokeCommand.PostCommandLookupAction = $previousLookup
    }
    ConvertTo-Json -InputObject @($entered | Sort-Object) -Compress |
        Set-Content -LiteralPath ($ResultPath + '.entered.json') -Encoding utf8NoBOM
    return
}

function Get-FrameOwnership([object[]] $OwnedFunctions) {
    $frames = [ordered]@{}
    if ($Surface -eq 'Original') {
        foreach ($function in $OwnedFunctions) {
            $frames[$function.name] = [IO.Path]::GetFullPath((Join-Path $moduleBase $function.relativePath))
        }
        return @{ frames = $frames; assembly = $null }
    }

    $receipt = Join-Path (Split-Path -LiteralPath $ResultPath) 'build-receipt.json'
    $manifest = (Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json).manifest
    $assemblyName = [IO.Path]::GetFileNameWithoutExtension($ImportPath) + '.dll'
    $assembly = Join-Path $moduleBase $assemblyName
    $files = @($manifest.files | Where-Object { [IO.Path]::GetFileName($_.path) -ceq $assemblyName })
    if ($files.Count -ne 1 -or (Get-FileHash -LiteralPath $assembly).Hash -ine $files[0].sha256) {
        throw 'The observed CLR assembly did not match the canonical build receipt.'
    }

    $ledger = @{}
    foreach ($entry in $manifest.unitDispositionLedger.entries) {
        if ($entry.kind -eq 'Function' -and $entry.emittedClrMethod -and !$entry.retainedHostedSource) {
            $ledger[$entry.unitId] = $entry
        }
    }
    foreach ($location in $manifest.failureMap.entries) {
        $unit = $ledger[$location.unitId]
        if ($unit -and $unit.generatedMemberName -ceq $location.generatedMemberName -and
            $unit.name -ceq $location.unitName -and $unit.relativePath -ceq $location.relativePath) {
            $identity = $location.generatedDeclaringTypeName + '::' + $location.generatedMemberName.TrimStart('@')
            if ($frames.Contains($identity) -and $frames[$identity] -cne $unit.name) {
                throw 'The build receipt identifies multiple authored functions for one CLR frame.'
            }
            $frames[$identity] = $unit.name
        }
    }
    if (@($frames.Values | Sort-Object -Unique).Count -ne $OwnedFunctions.Count) {
        throw 'The build receipt does not bind every owned function to an emitted CLR method.'
    }
    return @{ frames = $frames; assembly = $assembly }
}

$owned = @((Get-Content -LiteralPath (Join-Path $ScenarioRoot 'owned-functions.json') -Raw | ConvertFrom-Json))
if (@($owned.name | Sort-Object -Unique).Count -ne $owned.Count) {
    throw 'The Locksmith2 observer requires a single owned declaration per command name.'
}
$ownership = Get-FrameOwnership $owned
$module = Import-Module $ImportPath -PassThru 6>$null |
    Where-Object { $_ -is [Management.Automation.PSModuleInfo] -and $_.Name -eq 'Locksmith2' } |
    Select-Object -Last 1
if (!$module -or $module.ModuleBase -ine $moduleBase) {
    throw 'The expected Locksmith2 module did not load from the requested surface.'
}
$exported = @($module.ExportedCommands.Values |
    Where-Object { $_.CommandType -in 'Function', 'Cmdlet' } |
    ForEach-Object Name | Sort-Object)
$aliases = @($module.ExportedAliases.Values | Sort-Object Name | ForEach-Object {
    [ordered]@{ name = $_.Name; definition = $_.Definition }
})
$surfaceRoot = (New-Item -ItemType Directory -Path (Join-Path $ScenarioRoot $Surface)).FullName
$ownershipPath = Join-Path $surfaceRoot 'frame-ownership.json'
$ownership | ConvertTo-Json -Depth 5 -Compress |
    Set-Content -LiteralPath $ownershipPath -Encoding utf8NoBOM

# These commands run directly through the module's CommandInfo objects. Each case checks
# the resulting value or module-owned state before it writes its observation.
$direct = @{
    console = @('ConvertTo-ConsoleColor', 'Get-TrueColorFg', 'Get-TrueColorBg')
    environment = @('Expand-IssueTemplate', 'Get-ForestNameFromDN', 'Test-IsPowerShellCore')
    'issue-store' = @('Add-ToIssueStore', 'Convert-IdentityReferenceToNTAccount', 'Get-AdcsObjectName',
        'Get-IssueCount', 'Get-IssueTextOverride', 'Get-LS2Stores', 'Get-PrincipalRiskBonus', 'Set-LS2RiskRating')
}

$hostExecutable = Join-Path $PSHOME 'pwsh.exe'
$scenarios = foreach ($id in $cases.Keys) {
    $caseRoot = (New-Item -ItemType Directory -Path (Join-Path $surfaceRoot $id)).FullName
    $casePath = Join-Path $caseRoot 'observation.json'
    $enteredPath = $casePath + '.entered.json'
    $processLog = Join-Path $caseRoot 'process.log'
    try {
        $arguments = @('-ImportPath', $ImportPath, '-Surface', $Surface, '-ScenarioRoot', $caseRoot,
            '-ResultPath', $casePath, '-Case', $id, '-OwnershipPath', $ownershipPath)
        & $hostExecutable -NoLogo -NoProfile -NonInteractive -File $PSCommandPath @arguments *> $processLog
        if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $casePath -PathType Leaf)) {
            throw "The $id observation failed with exit $LASTEXITCODE."
        }
        $enteredNames = @(Get-Content -LiteralPath $enteredPath -Raw | ConvertFrom-Json)
        if ($id -eq 'audit' -and ('Invoke-Locksmith2' -notin $enteredNames -or 'Get-FlattenedIssues' -notin $enteredNames)) {
            throw 'The observer did not reach the audit and its issue collection.'
        }
        $directNames = @($direct[$id] | Where-Object { $_ })
        $executed = @($owned | Where-Object { $_.name -in $enteredNames -or $_.name -in $directNames })
        [ordered]@{
            scenarioId = $id
            succeeded = $true
            executedExports = @($executed.name | Where-Object { $_ -in $exported })
            executedOwnedFunctions = $executed
            activeOwnedBodies = @($executed.name | Where-Object { $_ -in $enteredNames })
            directOwnedCalls = @($executed.name | Where-Object { $_ -in $directNames })
            observation = Get-Content -LiteralPath $casePath -Raw | ConvertFrom-Json
        }
    } finally {
        if (Test-Path -LiteralPath $processLog -PathType Leaf) { Remove-Item -LiteralPath $processLog -Force }
    }
}

[ordered]@{
    schemaVersion = 1
    surface = $Surface
    importPath = [IO.Path]::GetFullPath($ImportPath)
    actualExports = $exported
    actualAliases = $aliases
    scenarios = @($scenarios)
} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $ResultPath -Encoding utf8NoBOM
