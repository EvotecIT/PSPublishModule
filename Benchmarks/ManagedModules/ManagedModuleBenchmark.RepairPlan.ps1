function Invoke-IsolatedRepairPlanHost {
    param(
        [string] $Destination,
        [string] $DetailPath
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

function Invoke-RepairPlanScenario {
    param([string] $EngineName, [int] $Iteration)

    if ([string]::IsNullOrWhiteSpace($script:ResolvedUpdateBaselineVersion)) {
        $reason = if ([string]::IsNullOrWhiteSpace($script:UpdateBaselineResolutionError)) {
            'UpdateBaselineVersion could not be resolved for repair-plan benchmarks.'
        } else {
            $script:UpdateBaselineResolutionError
        }

        return New-SkippedRow -OperationName 'RepairPlan' -EngineName $EngineName -Iteration $Iteration -Reason $reason
    }

    if ($EngineName -ne 'Managed') {
        return New-SkippedRow -OperationName 'RepairPlan' -EngineName $EngineName -Iteration $Iteration -Reason "$EngineName does not expose an equivalent module-estate repair planning command."
    }

    $destination = Join-Path $installWorkRoot ("repairplan-{0}-{1}" -f $EngineName, $Iteration)
    New-Item -Path $destination -ItemType Directory -Force | Out-Null
    $packageCacheDirectory = if ($CacheMode -eq 'Warm') {
        Join-Path $destination 'ManagedPackageCache'
    } else {
        ''
    }

    try {
        Invoke-IsolatedInstallHost -EngineName $EngineName -Destination $destination -DetailPath '' -OperationName 'Install' -VersionOverride $script:ResolvedUpdateBaselineVersion -PackageCacheDirectory $packageCacheDirectory
    } catch {
        return New-FailedRow -OperationName 'RepairPlan' -EngineName $EngineName -Iteration $Iteration -Reason "Baseline install failed: $($_.Exception.Message)" -OutputRoot $destination
    }
    if ($CacheMode -eq 'Cold') {
        Clear-IsolatedPackageCaches -Destination $destination
    }

    $detailPath = Join-Path $workRoot ("managed-repairplan-details-{0}.json" -f $Iteration)
    Invoke-TimedOperation -OperationName 'RepairPlan' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath $detailPath -ScriptBlock {
        Invoke-IsolatedRepairPlanHost -Destination $destination -DetailPath $detailPath
    }
}
