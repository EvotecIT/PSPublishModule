param(
    [ValidateSet('Smoke', 'Standard')]
    [string] $Suite = 'Smoke',

    [string] $ModuleName = 'ThreadJob',

    [string] $Version = '',

    [string] $UpdateBaselineVersion = '',

    [string] $Repository = 'PSGallery',

    [string] $RepositoryName = 'PSGallery',

    [string] $ModuleFastSource = 'https://pwsh.gallery/index.json',

    [string[]] $Engine = @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet'),

    [string[]] $Operation,

    [string[]] $RepairScenario = @('StaleVersion'),

    [ValidateSet('Default', 'Cold', 'Warm')]
    [string] $CacheMode = 'Default',

    [int] $RepeatCount = 1,

    [int] $SetupRetryCount = 2,

    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\Ignore\Benchmarks\ManagedModules'),

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipBuild,

    [switch] $AcceptLicense,

    [switch] $AuthenticodeCheck,

    [switch] $ValidateImport,

    [int] $ImportTimeoutSeconds = 120,

    [switch] $RotateEngineOrder,

    [int] $ManagedMaxRank = 0,

    [double] $ManagedMaxVsFastest = 0,

    [switch] $ListScenarios,

    [switch] $RemoveOutputRoots
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
[Threading.Thread]::CurrentThread.CurrentCulture = $invariantCulture
[Threading.Thread]::CurrentThread.CurrentUICulture = $invariantCulture

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$runStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$workRoot = Join-Path $OutputDirectory ('Run-{0}-{1}' -f $runStamp, $PID)
$tempWorkRoot = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    Join-Path ([IO.Path]::GetPathRoot($repoRoot)) 'Temp\PFMM'
} else {
    Join-Path ([IO.Path]::GetTempPath()) 'pfmm'
}
$installWorkRoot = Join-Path $tempWorkRoot ('InstallRoots\Run-{0}-{1}' -f $runStamp, $PID)
$saveWorkRoot = Join-Path $tempWorkRoot ('SaveRoots\Run-{0}-{1}' -f $runStamp, $PID)
$managedPackageCacheRoot = Join-Path $tempWorkRoot ('PackageCaches\Run-{0}-{1}' -f $runStamp, $PID)
$publishWorkRoot = Join-Path $tempWorkRoot ('PublishRoots\Run-{0}-{1}' -f $runStamp, $PID)
$validEngines = @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')
$validOperations = @('Find', 'Save', 'SaveNoOp', 'SaveForce', 'Install', 'InstallManaged', 'InstallNoOp', 'InstallForce', 'Update', 'UpdateNoOp', 'UpdateForce', 'RepairPlan', 'Publish')
$validRepairScenarios = @('StaleVersion', 'SourceDrift', 'ScopeDrift', 'FamilyCoherence', 'LoadedModuleSafety', 'CleanupPlanning')

. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.Artifacts.ps1')

function Resolve-TokenList {
    param(
        [string[]] $Value,
        [string[]] $Allowed,
        [string] $Label
    )

    $resolved = [Collections.Generic.List[string]]::new()
    foreach ($item in @($Value)) {
        foreach ($token in ($item -split ',')) {
            $name = $token.Trim()
            if ([string]::IsNullOrWhiteSpace($name)) {
                continue
            }

            $match = @($Allowed | Where-Object { $_ -eq $name })
            if ($match.Count -eq 0) {
                throw "Unknown $Label '$name'. Valid values: $($Allowed -join ', ')."
            }

            if (-not $resolved.Contains($match[0])) {
                $resolved.Add($match[0])
            }
        }
    }

    , $resolved.ToArray()
}

function Resolve-OperationList {
    param([string[]] $Value)

    if ($Value -and $Value.Count -gt 0) {
        return Resolve-TokenList -Value $Value -Allowed $validOperations -Label 'operation'
    }

    if ($Suite -eq 'Smoke') {
        return @('Find', 'Save', 'Install')
    }

    @('Find', 'Save', 'Install')
}

function Resolve-RepairScenarioList {
    param([string[]] $Value)

    if (-not $Value -or $Value.Count -eq 0) {
        return @('StaleVersion')
    }

    $expanded = foreach ($item in @($Value)) {
        foreach ($token in ($item -split ',')) {
            $name = $token.Trim()
            if ([string]::IsNullOrWhiteSpace($name)) {
                continue
            }

            if ($name -eq 'All') {
                $validRepairScenarios
                continue
            }

            $name
        }
    }

    Resolve-TokenList -Value @($expanded) -Allowed $validRepairScenarios -Label 'repair scenario'
}

function Test-BenchmarkOperationUsesForce {
    param([string] $OperationName)

    $OperationName -in @('Save', 'SaveForce', 'Install', 'InstallManaged', 'InstallForce', 'UpdateForce')
}

function Test-BenchmarkOperationRequiresExistingTarget {
    param([string] $OperationName)

    $OperationName -in @('SaveNoOp', 'SaveForce', 'InstallNoOp', 'InstallForce', 'UpdateNoOp', 'UpdateForce')
}

function Get-ManagedBenchmarkPackageCacheDirectory {
    param([string] $EngineName)

    if ($CacheMode -eq 'Warm' -and $EngineName -eq 'Managed') {
        return $managedPackageCacheRoot
    }

    ''
}

function Invoke-BenchmarkSetupOperation {
    param(
        [string] $Label,
        [scriptblock] $ScriptBlock
    )

    $attemptCount = 1 + $SetupRetryCount
    $errors = [Collections.Generic.List[string]]::new()
    for ($attempt = 1; $attempt -le $attemptCount; $attempt++) {
        try {
            & $ScriptBlock
            return
        } catch {
            $errors.Add(("Attempt {0}: {1}" -f $attempt, $_.Exception.Message))
            if ($attempt -ge $attemptCount) {
                throw ("{0} failed after {1} attempt(s). {2}" -f $Label, $attemptCount, ($errors -join "`n"))
            }

            Start-Sleep -Seconds ([Math]::Min(5, $attempt))
        }
    }
}

$script:ResolvedUpdateBaselineVersion = $UpdateBaselineVersion
$script:ResolvedUpdateTargetVersion = $Version
$script:UpdateBaselineResolutionError = ''

function Resolve-ModuleBinary {
    $frameworks = if ($PSVersionTable.PSEdition -eq 'Desktop') {
        @('net472')
    } else {
        @('net8.0', 'net10.0')
    }

    foreach ($framework in $frameworks) {
        $path = Join-Path $repoRoot ("PSPublishModule\bin\{0}\{1}\PSPublishModule.dll" -f $Configuration, $framework)
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    throw "PSPublishModule binary was not found for $($frameworks -join ', ') under configuration '$Configuration'."
}

function Invoke-LocalBuild {
    if ($SkipBuild.IsPresent) {
        return
    }

    $projectPath = Join-Path $repoRoot 'PSPublishModule\PSPublishModule.csproj'
    Write-Host "Building PSPublishModule ($Configuration) before benchmark import..."
    & dotnet build $projectPath -c $Configuration --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for PSPublishModule ($Configuration)."
    }
}

function Import-LocalModule {
    $binary = Resolve-ModuleBinary
    Remove-Module PSPublishModule -Force -ErrorAction SilentlyContinue
    Import-Module $binary -Force
    $binary
}

function Test-CommandAvailable {
    param([string] $Name)
    [bool](Get-Command -Name $Name -ErrorAction SilentlyContinue)
}

function Get-VersionParameter {
    param(
        [string] $CommandName,
        [string] $ExactVersion
    )

    if ([string]::IsNullOrWhiteSpace($ExactVersion)) {
        return @{}
    }

    $command = Get-Command -Name $CommandName -ErrorAction Stop
    if ($command.Parameters.ContainsKey('RequiredVersion')) {
        return @{ RequiredVersion = $ExactVersion }
    }

    if ($command.Parameters.ContainsKey('Version')) {
        return @{ Version = $ExactVersion }
    }

    @{}
}

function Add-SwitchParameterIfSupported {
    param(
        [hashtable] $Parameters,
        [string] $CommandName,
        [string] $ParameterName,
        [bool] $Enabled
    )

    if (-not $Enabled) {
        return
    }

    $command = Get-Command -Name $CommandName -ErrorAction Stop
    if ($command.Parameters.ContainsKey($ParameterName)) {
        $Parameters[$ParameterName] = $true
    }
}

. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.IsolatedHost.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.ImportValidation.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.VersionDiscovery.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.ResultRows.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.Summary.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.RepairPlan.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.ManagedDetails.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.PerformanceGate.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.OutputCleanup.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.Publish.ps1')
$repositorySource = Resolve-ManagedModuleBenchmarkRepositorySource -Repository $Repository -RepositoryName $RepositoryName

function Get-InstalledModuleVersion {
    param(
        [string] $Root,
        [string] $Name
    )

    $searchRoots = @(
        (Join-Path $Root $Name)
        $Root
    ) | Select-Object -Unique

    $versions = [Collections.Generic.List[object]]::new()
    foreach ($searchRoot in $searchRoots) {
        if (-not (Test-Path -LiteralPath $searchRoot)) {
            continue
        }

        foreach ($manifest in @(Get-ChildItem -LiteralPath $searchRoot -Filter "$Name.psd1" -Recurse -File -ErrorAction SilentlyContinue)) {
            $text = Get-Content -LiteralPath $manifest.FullName -Raw
            if ($text -notmatch "ModuleVersion\s*=\s*['""]([^'""]+)['""]") {
                continue
            }

            $versionText = $Matches[1]
            $parsedVersion = $null
            if (-not [version]::TryParse($versionText, [ref] $parsedVersion)) {
                continue
            }

            $versions.Add([pscustomobject]@{
                Text = $versionText
                Parsed = $parsedVersion
            })
        }
    }

    if ($versions.Count -eq 0) {
        return $null
    }

    ($versions | Sort-Object Parsed -Descending | Select-Object -First 1).Text
}

function Get-OutputRootMetrics {
    param([string] $Root)

    if ([string]::IsNullOrWhiteSpace($Root) -or -not (Test-Path -LiteralPath $Root)) {
        return [pscustomobject]@{
            DirectoryCount = 0
            FileCount = 0
            TotalBytes = 0L
        }
    }

    $directories = @(Get-ChildItem -LiteralPath $Root -Directory -Recurse -ErrorAction SilentlyContinue)
    $files = @(Get-ChildItem -LiteralPath $Root -File -Recurse -ErrorAction SilentlyContinue)
    $bytes = 0L
    foreach ($file in $files) {
        $bytes += [long]$file.Length
    }

    [pscustomobject]@{
        DirectoryCount = $directories.Count
        FileCount = $files.Count
        TotalBytes = $bytes
    }
}

function Invoke-TimedOperation {
    param(
        [string] $OperationName,
        [string] $ScenarioName = '',
        [string] $EngineName,
        [int] $Iteration,
        [scriptblock] $ScriptBlock,
        [string] $OutputRoot,
        [string] $DetailPath
    )

    $timer = [Diagnostics.Stopwatch]::StartNew()
    $status = 'Succeeded'
    $errorText = ''
    $versionText = $null
    $outputCount = 0
    $metrics = $null
    $detail = $null
    $importValidation = $null

    try {
        $output = @(& $ScriptBlock)
        $outputCount = $output.Count
        if ($OutputRoot) {
            $versionText = Get-InstalledModuleVersion -Root $OutputRoot -Name $ModuleName
        } elseif ($output.Count -gt 0 -and $output[0].PSObject.Properties['Version']) {
            $versionText = [string]$output[0].Version
        }
    } catch {
        $status = 'Failed'
        $errorText = $_.Exception.Message
    } finally {
        if ($OutputRoot) {
            $metrics = Get-OutputRootMetrics -Root $OutputRoot
            if (-not $versionText) {
                $versionText = Get-InstalledModuleVersion -Root $OutputRoot -Name $ModuleName
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($DetailPath) -and (Test-Path -LiteralPath $DetailPath)) {
            $detail = Get-Content -LiteralPath $DetailPath -Raw | ConvertFrom-Json
        }

        $timer.Stop()
    }

    if ($status -eq 'Succeeded') {
        $importValidation = Invoke-ImportValidation -OutputRoot $OutputRoot
    }

    if (-not $metrics) {
        $metrics = [pscustomobject]@{
            DirectoryCount = 0
            FileCount = 0
            TotalBytes = 0L
        }
    }

    $detailSummary = if ($detail) { $detail.Summary } else { $null }
    function Get-DetailNumber {
        param(
            [object] $InputObject,
            [string] $Name
        )

        if ($null -eq $InputObject -or -not $InputObject.PSObject.Properties[$Name]) {
            return 0
        }

        $value = $InputObject.PSObject.Properties[$Name].Value
        if ($null -eq $value) {
            return 0
        }

        return $value
    }

    $elapsedMilliseconds = [math]::Round($timer.Elapsed.TotalMilliseconds, 2)
    $managedRootElapsedMilliseconds = [double] (Get-DetailNumber -InputObject $detailSummary -Name 'RootElapsedMilliseconds')
    $managedHarnessOverheadMilliseconds = if ($detail) {
        [math]::Round([math]::Max(0, $elapsedMilliseconds - $managedRootElapsedMilliseconds), 2)
    } else {
        0
    }

    [pscustomobject]@{
        Operation = $OperationName
        Scenario = $ScenarioName
        Engine = $EngineName
        Iteration = $Iteration
        Status = $status
        ModuleName = $ModuleName
        Version = $versionText
        UpdateBaselineVersion = if (Test-BenchmarkOperationUsesUpdateBaseline -OperationName $OperationName) { $script:ResolvedUpdateBaselineVersion } else { '' }
        UpdateTargetVersion = if (Test-BenchmarkOperationUsesUpdateBaseline -OperationName $OperationName) { $script:ResolvedUpdateTargetVersion } else { '' }
        ElapsedMilliseconds = $elapsedMilliseconds
        OutputCount = $outputCount
        OutputDirectoryCount = $metrics.DirectoryCount
        OutputFileCount = $metrics.FileCount
        OutputBytes = $metrics.TotalBytes
        OutputRoot = $OutputRoot
        DetailPath = if ($detail) { $DetailPath } else { '' }
        ManagedPackageCount = [int] (Get-DetailNumber -InputObject $detailSummary -Name 'PackageCount')
        ManagedDependencyCount = [int] (Get-DetailNumber -InputObject $detailSummary -Name 'DependencyCount')
        ManagedUniquePackageCount = [int] (Get-DetailNumber -InputObject $detailSummary -Name 'UniquePackageCount')
        ManagedUniqueDependencyCount = [int] (Get-DetailNumber -InputObject $detailSummary -Name 'UniqueDependencyCount')
        ManagedInstalledPackageCount = [int] (Get-DetailNumber -InputObject $detailSummary -Name 'InstalledPackageCount')
        ManagedAlreadyInstalledPackageCount = [int] (Get-DetailNumber -InputObject $detailSummary -Name 'AlreadyInstalledPackageCount')
        ManagedRootElapsedMilliseconds = $managedRootElapsedMilliseconds
        ManagedHarnessOverheadMilliseconds = $managedHarnessOverheadMilliseconds
        ManagedRootDependencyMilliseconds = [double] (Get-DetailNumber -InputObject $detailSummary -Name 'RootDependencyMilliseconds')
        ManagedTotalDownloadMilliseconds = [double] (Get-DetailNumber -InputObject $detailSummary -Name 'TotalDownloadMilliseconds')
        ManagedTotalExtractionMilliseconds = [double] (Get-DetailNumber -InputObject $detailSummary -Name 'TotalExtractionMilliseconds')
        ManagedTotalPromotionMilliseconds = [double] (Get-DetailNumber -InputObject $detailSummary -Name 'TotalPromotionMilliseconds')
        ManagedRepositoryRequestCount = [long] (Get-DetailNumber -InputObject $detailSummary -Name 'TotalRepositoryRequestCount')
        ManagedPackageRepositoryRequestCount = [long] (Get-DetailNumber -InputObject $detailSummary -Name 'TotalPackageRepositoryRequestCount')
        ManagedPackageRepositoryRedirectCount = [long] (Get-DetailNumber -InputObject $detailSummary -Name 'TotalPackageRepositoryRedirectCount')
        ManagedDownloadBytes = [long] (Get-DetailNumber -InputObject $detailSummary -Name 'TotalDownloadBytes')
        ManagedCacheHitCount = [int] (Get-DetailNumber -InputObject $detailSummary -Name 'CacheHitCount')
        ManagedExtractionCacheHitCount = [int] (Get-DetailNumber -InputObject $detailSummary -Name 'ExtractionCacheHitCount')
        ManagedAuthenticodeCheckedFileCount = [int] (Get-DetailNumber -InputObject $detailSummary -Name 'TotalAuthenticodeCheckedFiles')
        ManagedAuthenticodeCatalogFileCount = [int] (Get-DetailNumber -InputObject $detailSummary -Name 'TotalAuthenticodeCatalogFiles')
        ManagedMaintenanceActionCount = [int] (Get-DetailNumber -InputObject $detailSummary -Name 'MaintenanceActionCount')
        ManagedMaintenanceFindingCount = [int] (Get-DetailNumber -InputObject $detailSummary -Name 'MaintenanceFindingCount')
        ImportStatus = if ($importValidation) { [string] $importValidation.Status } else { '' }
        ImportVersion = if ($importValidation) { [string] $importValidation.Version } else { '' }
        ImportMilliseconds = if ($importValidation) { [double] $importValidation.ElapsedMilliseconds } else { 0 }
        ImportManifestPath = if ($importValidation) { [string] $importValidation.ManifestPath } else { '' }
        ImportError = if ($importValidation) { [string] $importValidation.Error } else { '' }
        Error = $errorText
    }
}

function Invoke-FindScenario {
    param([string] $EngineName, [int] $Iteration)

    switch ($EngineName) {
        'ModuleFast' {
            return New-SkippedRow -OperationName 'Find' -EngineName $EngineName -Iteration $Iteration -Reason 'ModuleFast does not expose an equivalent find command.'
        }
        'Managed' {
            Invoke-TimedOperation -OperationName 'Find' -EngineName $EngineName -Iteration $Iteration -OutputRoot '' -DetailPath '' -ScriptBlock {
                Find-ManagedModule -Name $ModuleName -Repository $repositorySource -RepositoryName $RepositoryName
            }
        }
        'PSResourceGet' {
            if (-not (Test-CommandAvailable 'Find-PSResource')) {
                return New-SkippedRow -OperationName 'Find' -EngineName $EngineName -Iteration $Iteration -Reason 'Find-PSResource is not available.'
            }

            Invoke-TimedOperation -OperationName 'Find' -EngineName $EngineName -Iteration $Iteration -OutputRoot '' -DetailPath '' -ScriptBlock {
                Find-PSResource -Name $ModuleName -Repository $RepositoryName
            }
        }
        'PowerShellGet' {
            if (-not (Test-CommandAvailable 'Find-Module')) {
                return New-SkippedRow -OperationName 'Find' -EngineName $EngineName -Iteration $Iteration -Reason 'Find-Module is not available.'
            }

            Invoke-TimedOperation -OperationName 'Find' -EngineName $EngineName -Iteration $Iteration -OutputRoot '' -DetailPath '' -ScriptBlock {
                Find-Module -Name $ModuleName -Repository $RepositoryName
            }
        }
    }
}

function Invoke-SaveEngineCommand {
    param(
        [string] $EngineName,
        [string] $Destination,
        [bool] $Force,
        [string] $PackageCacheDirectory = '',
        [string] $DetailPath = ''
    )

    switch ($EngineName) {
        'Managed' {
            $parameters = @{
                Name = $ModuleName
                Path = $Destination
                Repository = $repositorySource
                RepositoryName = $RepositoryName
                AllowClobber = $true
            }
            if ($Force) {
                $parameters.Force = $true
            }
            if (-not [string]::IsNullOrWhiteSpace($Version)) {
                $parameters.Version = $Version
            }
            if (-not [string]::IsNullOrWhiteSpace($PackageCacheDirectory)) {
                $parameters.PackageCacheDirectory = $PackageCacheDirectory
            }
            Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Save-ManagedModule' -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
            Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Save-ManagedModule' -ParameterName 'AuthenticodeCheck' -Enabled $AuthenticodeCheck.IsPresent
            $result = Save-ManagedModule @parameters
            Write-ManagedInstallDetail -Result $result -Path $DetailPath
            $result
        }
        'PSResourceGet' {
            $parameters = @{
                Name = $ModuleName
                Path = $Destination
                Repository = $RepositoryName
                TrustRepository = $true
            }
            foreach ($entry in (Get-VersionParameter -CommandName 'Save-PSResource' -ExactVersion $Version).GetEnumerator()) {
                $parameters[$entry.Key] = $entry.Value
            }
            Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Save-PSResource' -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
            Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Save-PSResource' -ParameterName 'AuthenticodeCheck' -Enabled $AuthenticodeCheck.IsPresent
            Save-PSResource @parameters
        }
        'PowerShellGet' {
            $parameters = @{
                Name = $ModuleName
                Path = $Destination
                Repository = $RepositoryName
            }
            if ($Force) {
                $parameters.Force = $true
            }
            foreach ($entry in (Get-VersionParameter -CommandName 'Save-Module' -ExactVersion $Version).GetEnumerator()) {
                $parameters[$entry.Key] = $entry.Value
            }
            Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Save-Module' -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
            Save-Module @parameters
        }
    }
}

function Invoke-SaveScenario {
    param([string] $EngineName, [int] $Iteration, [string] $OperationName = 'Save')

    $destination = Join-Path $saveWorkRoot ("save-{0}-{1}-{2}" -f $OperationName, $EngineName, $Iteration)
    New-Item -Path $destination -ItemType Directory -Force | Out-Null
    $force = Test-BenchmarkOperationUsesForce -OperationName $OperationName
    $packageCacheDirectory = Get-ManagedBenchmarkPackageCacheDirectory -EngineName $EngineName

    switch ($EngineName) {
        'ModuleFast' {
            return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'ModuleFast does not expose an equivalent save command.'
        }
        'PSResourceGet' {
            if (-not (Test-CommandAvailable 'Save-PSResource')) {
                return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'Save-PSResource is not available.'
            }
        }
        'PowerShellGet' {
            if (-not (Test-CommandAvailable 'Save-Module')) {
                return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'Save-Module is not available.'
            }
        }
    }

    if ($EngineName -eq 'PSResourceGet' -and $OperationName -eq 'SaveForce') {
        return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'Save-PSResource does not expose an equivalent force/reinstall save parameter.'
    }

    if (Test-BenchmarkOperationRequiresExistingTarget -OperationName $OperationName) {
        try {
            Invoke-BenchmarkSetupOperation -Label 'Preseed save' -ScriptBlock {
                Invoke-SaveEngineCommand -EngineName $EngineName -Destination $destination -Force $true -PackageCacheDirectory $packageCacheDirectory | Out-Null
            }
        } catch {
            return New-FailedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason $_.Exception.Message -OutputRoot $destination
        }
        if ($CacheMode -eq 'Cold') {
            Clear-IsolatedPackageCaches -Destination $destination
        }
    }

    $detailPath = if ($EngineName -eq 'Managed') {
        Join-Path $workRoot ("managed-{0}-details-{1}.json" -f $OperationName, $Iteration)
    } else {
        ''
    }

    Invoke-TimedOperation -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath $detailPath -ScriptBlock {
        Invoke-SaveEngineCommand -EngineName $EngineName -Destination $destination -Force $force -PackageCacheDirectory $packageCacheDirectory -DetailPath $detailPath
    }
}

function Invoke-InstallScenario {
    param([string] $EngineName, [int] $Iteration, [string] $OperationName = 'Install')

    $destination = Join-Path $installWorkRoot ("install-{0}-{1}-{2}" -f $OperationName, $EngineName, $Iteration)
    New-Item -Path $destination -ItemType Directory -Force | Out-Null
    $force = Test-BenchmarkOperationUsesForce -OperationName $OperationName
    $packageCacheDirectory = Get-ManagedBenchmarkPackageCacheDirectory -EngineName $EngineName

    switch ($EngineName) {
        'ModuleFast' {
            if ($PSVersionTable.PSEdition -eq 'Desktop' -or $PSVersionTable.PSVersion -lt [version]'7.2') {
                return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'ModuleFast requires PowerShell 7.2 or newer.'
            }
            if (-not (Get-ProviderModulePath -EngineName $EngineName)) {
                return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'ModuleFast is not installed for this benchmark host.'
            }
        }
        'PSResourceGet' {
            if (-not (Test-CommandAvailable 'Install-PSResource')) {
                return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'Install-PSResource is not available.'
            }
        }
        'PowerShellGet' {
            if (-not (Test-CommandAvailable 'Install-Module')) {
                return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'Install-Module is not available.'
            }
        }
    }

    if (Test-BenchmarkOperationRequiresExistingTarget -OperationName $OperationName) {
        try {
            Invoke-BenchmarkSetupOperation -Label 'Preseed install' -ScriptBlock {
                Invoke-IsolatedInstallHost -EngineName $EngineName -Destination $destination -DetailPath '' -OperationName 'Install' -PackageCacheDirectory $packageCacheDirectory -Force $true
            }
        } catch {
            return New-FailedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason $_.Exception.Message -OutputRoot $destination
        }
        if ($CacheMode -eq 'Cold') {
            Clear-IsolatedPackageCaches -Destination $destination
        }
    }

    $detailPath = if ($EngineName -eq 'Managed') {
        Join-Path $workRoot ("managed-{0}-details-{1}.json" -f $OperationName, $Iteration)
    } else {
        ''
    }

    Invoke-TimedOperation -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath $detailPath -ScriptBlock {
        Invoke-IsolatedInstallHost -EngineName $EngineName -Destination $destination -DetailPath $detailPath -OperationName 'Install' -PackageCacheDirectory $packageCacheDirectory -Force $force
    }
}

function Invoke-UpdateScenario {
    param([string] $EngineName, [int] $Iteration, [string] $OperationName = 'Update')

    if ($OperationName -eq 'Update' -and [string]::IsNullOrWhiteSpace($script:ResolvedUpdateBaselineVersion)) {
        $reason = if ([string]::IsNullOrWhiteSpace($script:UpdateBaselineResolutionError)) {
            'UpdateBaselineVersion could not be resolved for update benchmarks.'
        } else {
            $script:UpdateBaselineResolutionError
        }

        return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason $reason
    }

    if ($EngineName -eq 'ModuleFast') {
        return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'ModuleFast does not expose an equivalent update command.'
    }

    switch ($EngineName) {
        'PSResourceGet' {
            if (-not (Test-CommandAvailable 'Update-PSResource')) {
                return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'Update-PSResource is not available.'
            }
        }
        'PowerShellGet' {
            if (-not (Test-CommandAvailable 'Update-Module')) {
                return New-SkippedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason 'Update-Module is not available.'
            }
        }
    }

    $destination = Join-Path $installWorkRoot ("update-{0}-{1}-{2}" -f $OperationName, $EngineName, $Iteration)
    New-Item -Path $destination -ItemType Directory -Force | Out-Null
    $packageCacheDirectory = Get-ManagedBenchmarkPackageCacheDirectory -EngineName $EngineName
    $force = Test-BenchmarkOperationUsesForce -OperationName $OperationName
    $preseedVersion = if ($OperationName -eq 'Update') {
        $script:ResolvedUpdateBaselineVersion
    } else {
        $Version
    }

    try {
        Invoke-BenchmarkSetupOperation -Label 'Preseed install' -ScriptBlock {
            Invoke-IsolatedInstallHost -EngineName $EngineName -Destination $destination -DetailPath '' -OperationName 'Install' -VersionOverride $preseedVersion -PackageCacheDirectory $packageCacheDirectory -Force $true
        }
    } catch {
        return New-FailedRow -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -Reason $_.Exception.Message -OutputRoot $destination
    }
    if ($CacheMode -eq 'Cold') {
        Clear-IsolatedPackageCaches -Destination $destination
    }

    $detailPath = if ($EngineName -eq 'Managed') {
        Join-Path $workRoot ("managed-{0}-details-{1}.json" -f $OperationName, $Iteration)
    } else {
        ''
    }

    Invoke-TimedOperation -OperationName $OperationName -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath $detailPath -ScriptBlock {
        Invoke-IsolatedInstallHost -EngineName $EngineName -Destination $destination -DetailPath $detailPath -OperationName 'Update' -PackageCacheDirectory $packageCacheDirectory -Force $force
    }
}

function Get-IterationEngineOrder {
    param([int] $Iteration)

    if (-not $RotateEngineOrder.IsPresent -or $Engine.Count -lt 2) {
        return $Engine
    }

    $offset = ($Iteration - 1) % $Engine.Count
    if ($offset -eq 0) {
        return $Engine
    }

    @($Engine[$offset..($Engine.Count - 1)] + $Engine[0..($offset - 1)])
}

$Operation = Resolve-OperationList -Value $Operation
$Engine = Resolve-TokenList -Value $Engine -Allowed $validEngines -Label 'engine'
$RepairScenario = Resolve-RepairScenarioList -Value $RepairScenario

if ($RepeatCount -lt 1) {
    throw 'RepeatCount must be greater than zero.'
}

if ($SetupRetryCount -lt 0) {
    throw 'SetupRetryCount cannot be negative.'
}

if ($ListScenarios.IsPresent) {
    foreach ($operationName in $Operation) {
        $scenarioNames = if ($operationName -eq 'RepairPlan') { $RepairScenario } else { @('') }
        foreach ($scenarioName in $scenarioNames) {
            foreach ($engineName in $Engine) {
                [pscustomobject]@{
                    Operation = $operationName
                    Scenario = $scenarioName
                    Engine = $engineName
                }
            }
        }
    }
    return
}

New-Item -Path $workRoot -ItemType Directory -Force | Out-Null
Invoke-LocalBuild
$moduleBinary = Import-LocalModule
$updateBaselineResolution = Initialize-ManagedModuleBenchmarkUpdateBaseline -Operations $Operation -RepairScenarios $RepairScenario -CurrentBaselineVersion $UpdateBaselineVersion -ModuleName $ModuleName -RequestedVersion $Version -RepositorySource $repositorySource
$script:ResolvedUpdateBaselineVersion = [string]$updateBaselineResolution.BaselineVersion
$script:ResolvedUpdateTargetVersion = [string]$updateBaselineResolution.TargetVersion
$script:UpdateBaselineResolutionError = [string]$updateBaselineResolution.Error
if (-not [string]::IsNullOrWhiteSpace([string]$updateBaselineResolution.Message)) {
    Write-Host ([string]$updateBaselineResolution.Message)
} elseif (-not [string]::IsNullOrWhiteSpace($script:UpdateBaselineResolutionError)) {
    Write-Warning $script:UpdateBaselineResolutionError
}

$results = [Collections.Generic.List[object]]::new()
$removedOutputRootCount = 0
foreach ($iteration in 1..$RepeatCount) {
    $engineOrder = Get-IterationEngineOrder -Iteration $iteration
    foreach ($operationName in $Operation) {
        $scenarioNames = if ($operationName -eq 'RepairPlan') { $RepairScenario } else { @('') }
        foreach ($scenarioName in $scenarioNames) {
            foreach ($engineName in $engineOrder) {
                $row = switch ($operationName) {
                    'Find' { Invoke-FindScenario -EngineName $engineName -Iteration $iteration }
                    'Save' { Invoke-SaveScenario -EngineName $engineName -Iteration $iteration -OperationName $operationName }
                    'SaveNoOp' { Invoke-SaveScenario -EngineName $engineName -Iteration $iteration -OperationName $operationName }
                    'SaveForce' { Invoke-SaveScenario -EngineName $engineName -Iteration $iteration -OperationName $operationName }
                    'Install' { Invoke-InstallScenario -EngineName $engineName -Iteration $iteration -OperationName $operationName }
                    'InstallManaged' { Invoke-InstallScenario -EngineName $engineName -Iteration $iteration -OperationName $operationName }
                    'InstallNoOp' { Invoke-InstallScenario -EngineName $engineName -Iteration $iteration -OperationName $operationName }
                    'InstallForce' { Invoke-InstallScenario -EngineName $engineName -Iteration $iteration -OperationName $operationName }
                    'Update' { Invoke-UpdateScenario -EngineName $engineName -Iteration $iteration -OperationName $operationName }
                    'UpdateNoOp' { Invoke-UpdateScenario -EngineName $engineName -Iteration $iteration -OperationName $operationName }
                    'UpdateForce' { Invoke-UpdateScenario -EngineName $engineName -Iteration $iteration -OperationName $operationName }
                    'RepairPlan' { Invoke-RepairPlanScenario -EngineName $engineName -Iteration $iteration -ScenarioName $scenarioName }
                    'Publish' { Invoke-PublishScenario -EngineName $engineName -Iteration $iteration -OperationName $operationName }
                }
                foreach ($item in @($row)) {
                    $results.Add($item)
                    if ($RemoveOutputRoots.IsPresent) {
                        $removedOutputRootCount += Remove-ManagedModuleBenchmarkOutputRoots -Rows @($item) -AllowedRoots @($workRoot, $installWorkRoot, $saveWorkRoot, $publishWorkRoot)
                    }
                }
            }
        }
    }
}

$summary = @(New-Summary -Rows $results)
$comparison = @(New-Comparison -SummaryRows $summary)
$gateViolations = @(Get-ManagedPerformanceGateViolation -Rows $comparison -MaxRank $ManagedMaxRank -MaxVsFastest $ManagedMaxVsFastest)
$metadata = [ordered]@{
    ModuleName = $ModuleName
    Version = $Version
    UpdateBaselineVersion = $script:ResolvedUpdateBaselineVersion
    RequestedUpdateBaselineVersion = $UpdateBaselineVersion
    ResolvedUpdateBaselineVersion = $script:ResolvedUpdateBaselineVersion
    ResolvedUpdateTargetVersion = $script:ResolvedUpdateTargetVersion
    UpdateBaselineResolutionError = $script:UpdateBaselineResolutionError
    Repository = $Repository
    RepositoryName = $RepositoryName
    ModuleFastSource = $ModuleFastSource
    AcceptLicense = $AcceptLicense.IsPresent
    AuthenticodeCheck = $AuthenticodeCheck.IsPresent
    CacheMode = $CacheMode
    ValidateImport = $ValidateImport.IsPresent
    ImportTimeoutSeconds = $ImportTimeoutSeconds
    RotateEngineOrder = $RotateEngineOrder.IsPresent
    ManagedMaxRank = $ManagedMaxRank
    ManagedMaxVsFastest = $ManagedMaxVsFastest
    ManagedPerformanceGatePassed = $gateViolations.Count -eq 0
    Suite = $Suite
    Engines = $Engine
    Operations = $Operation
    RepairScenarios = $RepairScenario
    RepeatCount = $RepeatCount
    SetupRetryCount = $SetupRetryCount
    ModuleBinary = $moduleBinary
    OutputDirectory = $workRoot
    SaveOutputDirectory = $saveWorkRoot
    ManagedPackageCacheDirectory = if ($CacheMode -eq 'Warm') { $managedPackageCacheRoot } else { '' }
    RemoveOutputRoots = $RemoveOutputRoots.IsPresent
    OutputRootsRemoved = 0
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    PSEdition = $PSVersionTable.PSEdition
    OS = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    ProcessArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
}

$resultsPath = Join-Path $workRoot 'managed-module-results.csv'
$resultsJsonPath = Join-Path $workRoot 'managed-module-results.json'
$summaryPath = Join-Path $workRoot 'managed-module-summary.csv'
$comparisonPath = Join-Path $workRoot 'managed-module-comparison.csv'
$gatePath = Join-Path $workRoot 'managed-module-gate.csv'
$metadataPath = Join-Path $workRoot 'metadata.json'

Write-ManagedBenchmarkCsv -InputObject @($results) -Path $resultsPath
Write-ManagedBenchmarkJson -InputObject @($results) -Path $resultsJsonPath -Depth 8
Write-ManagedBenchmarkCsv -InputObject @($summary) -Path $summaryPath
Write-ManagedBenchmarkCsv -InputObject @($comparison) -Path $comparisonPath
if ($ManagedMaxRank -gt 0 -or $ManagedMaxVsFastest -gt 0) {
    Write-ManagedBenchmarkCsv -InputObject @($gateViolations) -Path $gatePath
}
if ($RemoveOutputRoots.IsPresent) {
    $removedOutputRootCount += Remove-ManagedModuleBenchmarkOutputRoots -Rows $results -AllowedRoots @($workRoot, $installWorkRoot, $saveWorkRoot)
    if ($CacheMode -eq 'Warm') {
        $removedOutputRootCount += Remove-ManagedModuleBenchmarkOutputRoots -Rows @([pscustomobject]@{ OutputRoot = $managedPackageCacheRoot }) -AllowedRoots @($tempWorkRoot)
    }
    $metadata.OutputRootsRemoved = $removedOutputRootCount
}
Write-ManagedBenchmarkJson -InputObject $metadata -Path $metadataPath -Depth 8

$comparison
Write-Host "Benchmark output: $workRoot"
if ($RemoveOutputRoots.IsPresent) {
    Write-Host "Removed benchmark output roots: $($metadata.OutputRootsRemoved)"
}
if ($gateViolations.Count -gt 0) {
    throw "Managed performance gate failed for $($gateViolations.Count) row(s). See '$gatePath'."
}
