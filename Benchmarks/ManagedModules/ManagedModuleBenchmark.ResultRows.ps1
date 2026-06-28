function Test-BenchmarkOperationUsesUpdateBaseline {
    param([string] $OperationName)

    $OperationName -eq 'Update' -or $OperationName -eq 'RepairPlan'
}

function New-SkippedRow {
    param(
        [string] $OperationName,
        [string] $EngineName,
        [int] $Iteration,
        [string] $Reason
    )

    [pscustomobject]@{
        Operation = $OperationName
        Engine = $EngineName
        Iteration = $Iteration
        Status = 'Skipped'
        ModuleName = $ModuleName
        Version = $null
        UpdateBaselineVersion = if (Test-BenchmarkOperationUsesUpdateBaseline -OperationName $OperationName) { $script:ResolvedUpdateBaselineVersion } else { '' }
        UpdateTargetVersion = if (Test-BenchmarkOperationUsesUpdateBaseline -OperationName $OperationName) { $script:ResolvedUpdateTargetVersion } else { '' }
        ElapsedMilliseconds = 0
        OutputCount = 0
        OutputDirectoryCount = 0
        OutputFileCount = 0
        OutputBytes = 0
        OutputRoot = ''
        DetailPath = ''
        ManagedPackageCount = 0
        ManagedDependencyCount = 0
        ManagedRootDependencyMilliseconds = 0
        ManagedTotalDownloadMilliseconds = 0
        ManagedTotalExtractionMilliseconds = 0
        ManagedTotalPromotionMilliseconds = 0
        ManagedRepositoryRequestCount = 0
        ManagedDownloadBytes = 0
        ManagedCacheHitCount = 0
        ImportStatus = ''
        ImportVersion = ''
        ImportMilliseconds = 0
        ImportManifestPath = ''
        ImportError = ''
        Error = $Reason
    }
}

function New-FailedRow {
    param(
        [string] $OperationName,
        [string] $EngineName,
        [int] $Iteration,
        [string] $Reason,
        [string] $OutputRoot = ''
    )

    $row = New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason $Reason
    $row.Status = 'Failed'
    $row.OutputRoot = $OutputRoot
    $row
}
