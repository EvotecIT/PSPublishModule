<#
.SYNOPSIS
Clears the private temporary directory belonging to this runner between jobs.
.DESCRIPTION
Installed outside the runner checkout and used by both GitHub job hooks. Only
the configured directory is eligible; shared caches and other runners are outside
its scope. A failed or locked entry is retained and reported without stopping
processes. The owning directory and its permissions survive cleanup.
.PARAMETER ConfigurationPath
Machine-local configuration written by Install-RunnerJobTempCleanup.ps1.
.EXAMPLE
& .\Clear-RunnerJobTemp.ps1 -ConfigurationPath 'C:\Program Files\PowerForge.RunnerMaintenance\runner\configuration.json'
#>
[CmdletBinding()]
param([string] $ConfigurationPath = (Join-Path $PSScriptRoot 'configuration.json'))

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-PrivateDirectory {
    param([string] $Path)
    $entry = Get-Item -LiteralPath $Path -Force
    if (-not $entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Not a physical directory: $Path"
    }
    $acl = Get-Acl -LiteralPath $Path
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin @('S-1-5-18', 'S-1-5-32-544')) {
            throw "Temporary directory permits a non-administrator identity: $Path"
        }
    }
}

$failureStage = 'configuration'
try {
    $configuration = Get-Content -LiteralPath $ConfigurationPath -Raw | ConvertFrom-Json
    if ($configuration.schemaVersion -ne 1) { throw 'Unsupported runner temporary-directory configuration.' }
    $runnerRoot = [IO.Path]::GetFullPath($configuration.runnerRoot)
    $identity = Get-Content -LiteralPath (Join-Path $runnerRoot '.runner') -Raw | ConvertFrom-Json
    if ($identity.agentName -cne $configuration.runnerName -or $identity.gitHubUrl -cne $configuration.gitHubUrl) {
        throw 'Runner registration changed; temporary cleanup is held.'
    }
    if ($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_RUN_ID -notmatch '^\d+$') {
        throw 'Cleanup requires an assigned GitHub Actions job.'
    }
    $failureStage = 'worker'
    $ancestorId = $PID
    $workerFound = $false
    for ($depth = 0; $depth -lt 8 -and $ancestorId; $depth++) {
        $ancestor = Get-CimInstance Win32_Process -Filter "ProcessId=$ancestorId" -ErrorAction Stop
        if (-not $ancestor) { break }
        if ($ancestor.Name -ceq 'Runner.Worker.exe' -and
            $ancestor.ExecutablePath.StartsWith($runnerRoot + '\bin\', [StringComparison]::OrdinalIgnoreCase)) {
            $workerFound = $true
            break
        }
        $ancestorId = $ancestor.ParentProcessId
    }
    if (-not $workerFound) { throw 'Cleanup is not a child of this runner worker.' }
    $failureStage = 'private-directory'
    $parent = Join-Path $env:SystemRoot 'SystemTemp\PowerForge.RunnerJobs'
    $expectedRoot = Join-Path $parent $configuration.runnerName
    if ($configuration.runnerName -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]*$' -or
        -not [string]::Equals([IO.Path]::GetFullPath($configuration.tempRoot), $expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Temporary directory does not match its runner identity.'
    }
    foreach ($path in @((Join-Path $env:SystemRoot 'SystemTemp'), $parent, $expectedRoot)) {
        Assert-PrivateDirectory $path
    }
    $marker = Join-Path $PSScriptRoot 'installation.json'
    $installation = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    if ($installation.tempRoot -cne $configuration.tempRoot -or $installation.runnerRoot -cne $runnerRoot -or
        $installation.installationId -cne $configuration.installationId) {
        throw 'Installation ownership does not match the configuration.'
    }
    $failureStage = 'temporary-path'
    if (-not [string]::Equals([IO.Path]::GetTempPath().TrimEnd('\'), $expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The runner private temporary-directory configuration is not active.'
    }

    [long] $removedBytes = 0
    $removedEntries = 0
    $retained = [Collections.Generic.List[string]]::new()
    $clock = [Diagnostics.Stopwatch]::StartNew()
    foreach ($target in Get-ChildItem -LiteralPath $expectedRoot -Force) {
        if ($clock.Elapsed.TotalSeconds -gt 30) {
            $retained.Add('Cleanup time budget reached; remaining entries retained.')
            break
        }
        try {
            $hasProtectedAttributes = $false
            $pending = [Collections.Generic.Stack[object]]::new()
            $pending.Push($target)
            [long] $bytes = 0
            while ($pending.Count) {
                if ($clock.Elapsed.TotalSeconds -gt 30) { throw 'Cleanup time budget reached.' }
                $entry = $pending.Pop()
                if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Redirected entry retained.' }
                if ($entry.Attributes -band ([IO.FileAttributes]::ReadOnly -bor [IO.FileAttributes]::Hidden)) {
                    $hasProtectedAttributes = $true
                }
                if ($entry.PSIsContainer) {
                    foreach ($child in Get-ChildItem -LiteralPath $entry.FullName -Force -ErrorAction Stop) {
                        $pending.Push($child)
                    }
                } else {
                    $handle = [IO.File]::Open($entry.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
                    $handle.Dispose()
                    $bytes += $entry.Length
                }
            }
            if (-not $target.FullName.StartsWith($expectedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Cleanup target escaped its private root.'
            }
            try {
                Remove-Item -LiteralPath $target.FullName -Recurse -ErrorAction Stop
            } catch {
                # Retry only after an actual filesystem failure on qualified private job output.
                if (-not $hasProtectedAttributes -or $clock.Elapsed.TotalSeconds -gt 30) { throw }
                Remove-Item -LiteralPath $target.FullName -Recurse -Force -ErrorAction Stop
            }
            $removedBytes += $bytes
            $removedEntries++
        } catch {
            # A later public job can recover a previous private job's temporary files.
            # Keep consumer names and paths out of the shared job log.
            $retained.Add($_.Exception.GetType().Name)
        }
    }
    [pscustomobject]@{
        runnerName = $configuration.runnerName
        removedEntries = $removedEntries
        removedBytes = $removedBytes
        retainedCount = $retained.Count
        errorCategories = @($retained | Select-Object -Unique)
        temporaryDirectoryVerified = $true
        systemIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().IsSystem
        completedUtc = [datetime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 4 -Compress
    if ($retained.Count) { Write-Warning "Private runner temporary cleanup retained $($retained.Count) entry or budget warning(s)." }
} catch {
    Write-Warning "Private runner temporary cleanup held ($failureStage; $($_.Exception.GetType().Name))."
}
