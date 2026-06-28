function Invoke-IsolatedRepairPlanHost {
    param(
        [string] $Destination,
        [string] $DetailPath,
        [string] $MaintenanceReceiptPath = '',
        [bool] $Latest
    )

    $childScript = Join-Path $PSScriptRoot 'Invoke-ManagedModuleMaintenanceChild.ps1'
    $hostPath = Get-BenchmarkHostPath
    $environment = Get-IsolatedInstallEnvironment -SandboxRoot $Destination -ProviderModuleSearchPaths (Get-ProviderModuleSearchPath)
    $arguments = @(
        '-NoLogo'
        '-NoProfile'
        '-ExecutionPolicy'
        'Bypass'
        '-File'
        $childScript
        '-ModuleName'
        $ModuleName
        '-Repository'
        $repositorySource
        '-Destination'
        $Destination
        '-ModuleBinary'
        (Resolve-ModuleBinary)
    )
    if ($Latest) {
        $arguments += '-Latest'
    }
    if (-not [string]::IsNullOrWhiteSpace($MaintenanceReceiptPath)) {
        $arguments += @(
            '-MaintenanceReceiptPath'
            $MaintenanceReceiptPath
        )
    }
    if ($AcceptLicense.IsPresent) {
        $arguments += '-AcceptLicense'
    }
    if (-not [string]::IsNullOrWhiteSpace($DetailPath)) {
        $arguments += @(
            '-ResultPath'
            $DetailPath
        )
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $hostPath
    $startInfo.Arguments = ($arguments | ForEach-Object { Join-CommandLineArgument $_ }) -join ' '
    $startInfo.WorkingDirectory = $Destination
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($entry in $environment.GetEnumerator()) {
        $startInfo.Environment[$entry.Key] = $entry.Value
    }

    try {
        $process = [Diagnostics.Process]::Start($startInfo)
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "RepairPlan benchmark child host failed with exit code $($process.ExitCode).`n$stdout`n$stderr"
        }
    } finally {
        $tempPath = $environment['TEMP']
        if (-not [string]::IsNullOrWhiteSpace($tempPath) -and (Test-Path -LiteralPath $tempPath)) {
            Remove-Item -LiteralPath $tempPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Find-RepairPlanModuleDirectory {
    param(
        [string] $Destination,
        [string] $ExpectedVersion
    )

    $candidateRoots = @(
        (Join-Path $Destination $ModuleName)
        $Destination
    ) | Select-Object -Unique

    foreach ($candidateRoot in $candidateRoots) {
        if (-not (Test-Path -LiteralPath $candidateRoot)) {
            continue
        }

        foreach ($manifest in @(Get-ChildItem -LiteralPath $candidateRoot -Filter "$ModuleName.psd1" -Recurse -File -ErrorAction SilentlyContinue)) {
            if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
                return $manifest.Directory.FullName
            }

            $text = Get-Content -LiteralPath $manifest.FullName -Raw
            if ($text -match "ModuleVersion\s*=\s*['""]([^'""]+)['""]" -and $Matches[1] -eq $ExpectedVersion) {
                return $manifest.Directory.FullName
            }
        }
    }

    throw "Installed module '$ModuleName' version '$ExpectedVersion' was not found under '$Destination'."
}

function Set-RepairPlanModuleSourceMetadata {
    param(
        [string] $ModuleDirectory,
        [string] $RepositoryName
    )

    $path = Join-Path $ModuleDirectory 'PSGetModuleInfo.xml'
    @"
<Objects>
  <Object>
    <Members>
      <String N="Repository">$RepositoryName</String>
      <String N="RepositoryName">$RepositoryName</String>
    </Members>
  </Object>
</Objects>
"@ | Set-Content -LiteralPath $path -Encoding UTF8
}

function New-RepairPlanMaintenanceReceipt {
    param(
        [string] $Destination,
        [string] $Version
    )

    $path = Join-Path $Destination 'module-state-maintenance-receipt.json'
    [pscustomobject]@{
        Source = 'Managed module benchmark source-drift seed'
        Modules = @(
            [pscustomobject]@{
                Name = $ModuleName
                Version = $Version
                SourceRepository = $RepositoryName
            }
        )
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding UTF8
    $path
}

function Invoke-RepairPlanScenario {
    param([string] $EngineName, [int] $Iteration, [string] $ScenarioName)

    if ($ScenarioName -eq 'StaleVersion' -and [string]::IsNullOrWhiteSpace($script:ResolvedUpdateBaselineVersion)) {
        $reason = if ([string]::IsNullOrWhiteSpace($script:UpdateBaselineResolutionError)) {
            'UpdateBaselineVersion could not be resolved for repair-plan benchmarks.'
        } else {
            $script:UpdateBaselineResolutionError
        }

        return New-SkippedRow -OperationName 'RepairPlan' -ScenarioName $ScenarioName -EngineName $EngineName -Iteration $Iteration -Reason $reason
    }

    if ($EngineName -ne 'Managed') {
        return New-SkippedRow -OperationName 'RepairPlan' -ScenarioName $ScenarioName -EngineName $EngineName -Iteration $Iteration -Reason "$EngineName does not expose an equivalent module-estate repair planning command."
    }

    $destination = Join-Path $installWorkRoot ("repairplan-{0}-{1}-{2}" -f $ScenarioName, $EngineName, $Iteration)
    New-Item -Path $destination -ItemType Directory -Force | Out-Null
    $packageCacheDirectory = if ($CacheMode -eq 'Warm') {
        Join-Path $destination 'ManagedPackageCache'
    } else {
        ''
    }
    $versionOverride = if ($ScenarioName -eq 'SourceDrift' -and -not [string]::IsNullOrWhiteSpace($script:ResolvedUpdateTargetVersion)) {
        $script:ResolvedUpdateTargetVersion
    } else {
        $script:ResolvedUpdateBaselineVersion
    }

    try {
        Invoke-IsolatedInstallHost -EngineName $EngineName -Destination $destination -DetailPath '' -OperationName 'Install' -VersionOverride $versionOverride -PackageCacheDirectory $packageCacheDirectory
    } catch {
        return New-FailedRow -OperationName 'RepairPlan' -ScenarioName $ScenarioName -EngineName $EngineName -Iteration $Iteration -Reason "Baseline install failed: $($_.Exception.Message)" -OutputRoot $destination
    }
    if ($CacheMode -eq 'Cold') {
        Clear-IsolatedPackageCaches -Destination $destination
    }

    $maintenanceReceiptPath = ''
    $latest = $ScenarioName -eq 'StaleVersion'
    if ($ScenarioName -eq 'SourceDrift') {
        $installedVersion = Get-InstalledModuleVersion -Root $destination -Name $ModuleName
        $moduleDirectory = Find-RepairPlanModuleDirectory -Destination $destination -ExpectedVersion $installedVersion
        Set-RepairPlanModuleSourceMetadata -ModuleDirectory $moduleDirectory -RepositoryName 'BenchmarkWrongSource'
        $maintenanceReceiptPath = New-RepairPlanMaintenanceReceipt -Destination $destination -Version $installedVersion
    }

    $detailPath = Join-Path $workRoot ("managed-repairplan-{0}-details-{1}.json" -f $ScenarioName, $Iteration)
    Invoke-TimedOperation -OperationName 'RepairPlan' -ScenarioName $ScenarioName -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath $detailPath -ScriptBlock {
        Invoke-IsolatedRepairPlanHost -Destination $destination -DetailPath $detailPath -MaintenanceReceiptPath $maintenanceReceiptPath -Latest $latest
    }
}
