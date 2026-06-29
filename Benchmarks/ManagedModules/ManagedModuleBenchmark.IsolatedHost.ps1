function Get-IsolatedInstallEnvironment {
    param(
        [string] $SandboxRoot,
        [string[]] $ProviderModuleSearchPaths
    )

    $homeRoot = Join-Path $SandboxRoot 'Home'
    $appDataRoot = Join-Path $SandboxRoot 'AppData\Roaming'
    $localAppDataRoot = Join-Path $SandboxRoot 'AppData\Local'
    $tempRoot = Join-Path $tempWorkRoot ('T\{0}' -f ([Guid]::NewGuid().ToString("N").Substring(0, 16)))
    $modulesRoot = Join-Path $SandboxRoot 'Modules'
    $documentsPowerShellRoot = Join-Path $homeRoot 'Documents\PowerShell\Modules'
    $documentsWindowsPowerShellRoot = Join-Path $homeRoot 'Documents\WindowsPowerShell\Modules'

    foreach ($path in @($homeRoot, $appDataRoot, $localAppDataRoot, $tempRoot, $modulesRoot, $documentsPowerShellRoot, $documentsWindowsPowerShellRoot)) {
        New-Item -Path $path -ItemType Directory -Force | Out-Null
    }

    $separator = [IO.Path]::PathSeparator
    $modulePathEntries = @(
            $modulesRoot
            $documentsPowerShellRoot
            $documentsWindowsPowerShellRoot
        ) + @($ProviderModuleSearchPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    @{
        USERPROFILE = $homeRoot
        HOME = $homeRoot
        APPDATA = $appDataRoot
        LOCALAPPDATA = $localAppDataRoot
        TEMP = $tempRoot
        TMP = $tempRoot
        PSModulePath = $modulePathEntries -join $separator
    }
}

function Clear-IsolatedPackageCaches {
    param([string] $Destination)

    foreach ($relativePath in @('AppData\Local', 'PSResourceGet', 'PackageManagement', 'ManagedPackageCache')) {
        $path = Join-Path $Destination $relativePath
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-BenchmarkHostPath {
    $process = Get-Process -Id $PID
    if (-not [string]::IsNullOrWhiteSpace($process.Path)) {
        return $process.Path
    }

    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        return (Get-Command powershell.exe -ErrorAction Stop).Source
    }

    (Get-Command pwsh -ErrorAction Stop).Source
}

function Get-ProviderModulePath {
    param([string] $EngineName)

    $moduleName = switch ($EngineName) {
        'ModuleFast' { 'ModuleFast' }
        'PSResourceGet' { 'Microsoft.PowerShell.PSResourceGet' }
        'PowerShellGet' { 'PowerShellGet' }
        default { $null }
    }

    if ([string]::IsNullOrWhiteSpace($moduleName)) {
        return @()
    }

    $module = Get-Module -ListAvailable -Name $moduleName | Sort-Object Version -Descending | Select-Object -First 1
    if (-not $module) {
        return @()
    }

    $module.Path
}

function Get-ProviderDependencyModulePath {
    param([string] $EngineName)

    $moduleNames = switch ($EngineName) {
        'PowerShellGet' { @('PackageManagement') }
        default { @() }
    }

    foreach ($moduleName in $moduleNames) {
        $module = Get-Module -ListAvailable -Name $moduleName | Sort-Object Version -Descending | Select-Object -First 1
        if ($module) {
            $module.Path
        }
    }
}

function Get-ProviderModuleSearchPath {
    $profileRoots = @($env:USERPROFILE, $env:HOME) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $userModuleRoots = foreach ($profileRoot in $profileRoots) {
        Join-Path $profileRoot 'Documents\PowerShell\Modules'
        Join-Path $profileRoot 'Documents\WindowsPowerShell\Modules'
    }
    $userModuleRoots = @($userModuleRoots | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\', '/') } | Select-Object -Unique)

    foreach ($entry in ($env:PSModulePath -split [Regex]::Escape([IO.Path]::PathSeparator))) {
        if ([string]::IsNullOrWhiteSpace($entry)) {
            continue
        }

        $fullEntry = [IO.Path]::GetFullPath($entry).TrimEnd('\', '/')
        $isUserRoot = $false
        foreach ($root in $userModuleRoots) {
            if ($fullEntry.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
                $isUserRoot = $true
                break
            }
        }

        if (-not $isUserRoot) {
            $fullEntry
        }
    }
}

function Invoke-IsolatedInstallHost {
    param(
        [string] $EngineName,
        [string] $Destination,
        [string] $DetailPath,
        [string] $OperationName = 'Install',
        [string] $VersionOverride = $Version,
        [string] $PackageCacheDirectory = '',
        [int] $ManagedDependencyConcurrency = 0,
        [bool] $Force = $false
    )

    $childScript = Join-Path $PSScriptRoot 'Invoke-ManagedModuleInstallChild.ps1'
    $hostPath = Get-BenchmarkHostPath
    $providerModulePath = Get-ProviderModulePath -EngineName $EngineName
    $providerDependencyModulePath = @(Get-ProviderDependencyModulePath -EngineName $EngineName)
    $environment = Get-IsolatedInstallEnvironment -SandboxRoot $Destination -ProviderModuleSearchPaths (Get-ProviderModuleSearchPath)
    $arguments = @(
        '-NoLogo'
        '-NoProfile'
        '-ExecutionPolicy'
        'Bypass'
        '-File'
        $childScript
        '-EngineName'
        $EngineName
        '-Operation'
        $OperationName
        '-ModuleName'
        $ModuleName
        '-Repository'
        $repositorySource
        '-RepositoryName'
        $RepositoryName
        '-Destination'
        $Destination
        '-ModuleBinary'
        (Resolve-ModuleBinary)
    )
    if (-not [string]::IsNullOrWhiteSpace($ModuleFastSource)) {
        $arguments += @(
            '-ModuleFastSource'
            $ModuleFastSource
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($PackageCacheDirectory)) {
        $arguments += @(
            '-PackageCacheDirectory'
            $PackageCacheDirectory
        )
    }
    if ($ManagedDependencyConcurrency -gt 0) {
        $arguments += @(
            '-ManagedDependencyConcurrency'
            ([string]$ManagedDependencyConcurrency)
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($VersionOverride)) {
        $arguments += @(
            '-Version'
            $VersionOverride
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($providerModulePath)) {
        $arguments += @(
            '-ProviderModulePath'
            $providerModulePath
        )
    }
    if ($providerDependencyModulePath.Count -gt 0) {
        $arguments += '-ProviderDependencyModulePath'
        $arguments += $providerDependencyModulePath
    }
    if ($AcceptLicense.IsPresent) {
        $arguments += '-AcceptLicense'
    }
    if ($AuthenticodeCheck.IsPresent) {
        $arguments += '-AuthenticodeCheck'
    }
    if ($Force) {
        $arguments += '-Force'
    }
    if (-not [string]::IsNullOrWhiteSpace($DetailPath)) {
        $arguments += @(
            '-ResultPath'
            $DetailPath
        )
    }

    try {
        $processResult = Invoke-ManagedBenchmarkProcess `
            -FileName $hostPath `
            -Arguments $arguments `
            -WorkingDirectory $Destination `
            -Environment $environment `
            -TimeoutSeconds $ChildTimeoutSeconds `
            -TimeoutMessage "$OperationName benchmark child host exceeded $ChildTimeoutSeconds seconds."
        if ($processResult.TimedOut) {
            throw "$($processResult.TimeoutMessage)`n$($processResult.StandardOutput)`n$($processResult.StandardError)"
        }

        if ($processResult.ExitCode -ne 0) {
            throw "$OperationName benchmark child host failed with exit code $($processResult.ExitCode).`n$($processResult.StandardOutput)`n$($processResult.StandardError)"
        }
    } finally {
        $tempPath = $environment['TEMP']
        if (-not [string]::IsNullOrWhiteSpace($tempPath) -and (Test-Path -LiteralPath $tempPath)) {
            Remove-Item -LiteralPath $tempPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
