param([Parameter(Mandatory)][string] $ScratchRoot)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $ScratchRoot -PathType Container)) { throw 'Scratch root is unavailable.' }
$fixtureRoot = Join-Path $ScratchRoot ('catalog-workflow-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$savedEnvironment = @{}
foreach ($name in @('GITHUB_REPOSITORY', 'GITHUB_OUTPUT', 'CATALOG_EXECUTE', 'CATALOG_RESERVATION_KEY')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    $env:GITHUB_REPOSITORY = 'fixture/app'
    $env:GITHUB_OUTPUT = Join-Path $fixtureRoot 'outputs.txt'
    $env:CATALOG_EXECUTE = 'false'
    $profilePath = Join-Path $fixtureRoot 'catalog.json'
    Set-Content -LiteralPath $profilePath -Value '{"ReleaseConfigPath":"release.json"}'
    $identity = "$env:GITHUB_REPOSITORY`n$profilePath`nApp-v1.2.3"
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identity))).ToLowerInvariant()
    $prefix = "catalog-update-$($digest.Substring(0, 24))-"
    $global:CatalogWorkflowTestState = @{Expired = $false; Prefix = $prefix;
        Calls = [Collections.Generic.List[object]]::new(); Downloads = [Collections.Generic.List[object]]::new()}
    function gh {
        $global:LASTEXITCODE = 0
        if ($args[0] -eq 'api') {
            @(
                @{ artifacts = @(@{ id = 10; name = $global:CatalogWorkflowTestState.Prefix + 'older-result'; expired = $false; workflow_run = @{id = 100} }) },
                @{ artifacts = @(@{ id = 20; name = $global:CatalogWorkflowTestState.Prefix + 'newer-intent'; expired = $global:CatalogWorkflowTestState.Expired; workflow_run = @{id = 200} }) }
            ) | ConvertTo-Json -Depth 6 -Compress
        } elseif ($args[0] -eq 'run') {
            $global:CatalogWorkflowTestState.Downloads.Add(@($args))
            $destination = $args[[Array]::IndexOf($args, '--dir') + 1]
            New-Item -ItemType Directory -Path $destination | Out-Null
            Set-Content -LiteralPath (Join-Path $destination 'catalog-update.json') -Value '{"DeliveryReleaseId":"release-123"}'
        } else { throw 'Unexpected artifact-service operation.' }
    }
    function dotnet { $global:CatalogWorkflowTestState.Calls.Add(@($args)); $global:LASTEXITCODE = 0 }
    $parameters = @{ToolPath = 'owner.dll'; ProfilePath = $profilePath; ReleaseTag = 'App-v1.2.3';
        DeliveryReleaseId = 'release-123'; OutputPath = (Join-Path $fixtureRoot 'prepared'); Channel = 'winget'}
    & "$PSScriptRoot/catalog-update-workflow.ps1" -Action Prepare @parameters
    if ($global:CatalogWorkflowTestState.Downloads.Count -ne 1 -or $global:CatalogWorkflowTestState.Downloads[0][2] -ne 200) { throw 'Did not restore the newest receipt across artifact pages.' }
    if ($global:CatalogWorkflowTestState.Calls.Count -ne 1 -or $global:CatalogWorkflowTestState.Calls[0][3] -ne 'submit' -or $global:CatalogWorkflowTestState.Calls[0] -contains '--execute') { throw 'Restored preparation did not remain a preflight.' }
    & "$PSScriptRoot/catalog-update-workflow.ps1" -Action Reserve @parameters
    $key = (Get-Content -LiteralPath $env:GITHUB_OUTPUT | Where-Object { $_ -like 'reservation-key=*' }).Substring(16)
    if ($key -notmatch '^[a-f0-9]{32}$' -or $global:CatalogWorkflowTestState.Calls[1][3] -ne 'reserve') { throw 'Did not create a fresh submission reservation.' }
    $env:CATALOG_RESERVATION_KEY = $key
    & "$PSScriptRoot/catalog-update-workflow.ps1" -Action Submit @parameters
    if ($global:CatalogWorkflowTestState.Calls[2] -notcontains '--execute' -or $global:CatalogWorkflowTestState.Calls[2] -notcontains $key) { throw 'Execution did not consume the archived reservation.' }
    $global:CatalogWorkflowTestState.Expired = $true
    $expiredRejected = $false
    try { & "$PSScriptRoot/catalog-update-workflow.ps1" -Action Prepare @parameters } catch { $expiredRejected = $_.Exception.Message -like '*receipt expired*' }
    if (!$expiredRejected -or $global:CatalogWorkflowTestState.Calls.Count -ne 3) { throw 'An expired receipt permitted another attempt.' }
    Write-Host 'Catalog workflow recovery, preflight, reservation and expired-receipt contracts passed.'
} finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Remove-Variable CatalogWorkflowTestState -Scope Global
    Remove-Item -LiteralPath $fixtureRoot -Recurse
}
