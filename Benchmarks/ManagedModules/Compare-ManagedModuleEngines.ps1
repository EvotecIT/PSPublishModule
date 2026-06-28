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

    [ValidateSet('Default', 'Cold', 'Warm')]
    [string] $CacheMode = 'Default',

    [int] $RepeatCount = 1,

    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\Ignore\Benchmarks\ManagedModules'),

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipBuild,

    [switch] $AcceptLicense,

    [switch] $ValidateImport,

    [int] $ImportTimeoutSeconds = 120,

    [switch] $RotateEngineOrder,

    [switch] $ListScenarios
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
$validEngines = @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')
$validOperations = @('Find', 'Save', 'Install', 'InstallManaged', 'Update')

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

. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.ImportValidation.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.VersionDiscovery.ps1')
. (Join-Path $PSScriptRoot 'ManagedModuleBenchmark.ResultRows.ps1')
$repositorySource = Resolve-ManagedModuleBenchmarkRepositorySource -Repository $Repository -RepositoryName $RepositoryName

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

function Join-CommandLineArgument {
    param([string] $Value)

    if ($null -eq $Value) {
        return '""'
    }

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    '"' + ($Value -replace '"', '\"') + '"'
}

function Invoke-IsolatedInstallHost {
    param(
        [string] $EngineName,
        [string] $Destination,
        [string] $DetailPath,
        [string] $OperationName = 'Install',
        [string] $VersionOverride = $Version,
        [string] $PackageCacheDirectory = ''
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
        '-ModuleFastSource'
        $ModuleFastSource
        '-Destination'
        $Destination
        '-ModuleBinary'
        (Resolve-ModuleBinary)
    )
    if (-not [string]::IsNullOrWhiteSpace($PackageCacheDirectory)) {
        $arguments += @(
            '-PackageCacheDirectory'
            $PackageCacheDirectory
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
            throw "$OperationName benchmark child host failed with exit code $($process.ExitCode).`n$stdout`n$stderr"
        }
    } finally {
        $tempPath = $environment['TEMP']
        if (-not [string]::IsNullOrWhiteSpace($tempPath) -and (Test-Path -LiteralPath $tempPath)) {
            Remove-Item -LiteralPath $tempPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

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

    [pscustomobject]@{
        Operation = $OperationName
        Engine = $EngineName
        Iteration = $Iteration
        Status = $status
        ModuleName = $ModuleName
        Version = $versionText
        UpdateBaselineVersion = if ($OperationName -eq 'Update') { $script:ResolvedUpdateBaselineVersion } else { '' }
        UpdateTargetVersion = if ($OperationName -eq 'Update') { $script:ResolvedUpdateTargetVersion } else { '' }
        ElapsedMilliseconds = [math]::Round($timer.Elapsed.TotalMilliseconds, 2)
        OutputCount = $outputCount
        OutputDirectoryCount = $metrics.DirectoryCount
        OutputFileCount = $metrics.FileCount
        OutputBytes = $metrics.TotalBytes
        OutputRoot = $OutputRoot
        DetailPath = if ($detail) { $DetailPath } else { '' }
        ManagedPackageCount = if ($detailSummary) { [int] $detailSummary.PackageCount } else { 0 }
        ManagedDependencyCount = if ($detailSummary) { [int] $detailSummary.DependencyCount } else { 0 }
        ManagedRootDependencyMilliseconds = if ($detailSummary) { [double] $detailSummary.RootDependencyMilliseconds } else { 0 }
        ManagedTotalDownloadMilliseconds = if ($detailSummary) { [double] $detailSummary.TotalDownloadMilliseconds } else { 0 }
        ManagedTotalExtractionMilliseconds = if ($detailSummary) { [double] $detailSummary.TotalExtractionMilliseconds } else { 0 }
        ManagedTotalPromotionMilliseconds = if ($detailSummary) { [double] $detailSummary.TotalPromotionMilliseconds } else { 0 }
        ManagedRepositoryRequestCount = if ($detailSummary) { [long] $detailSummary.TotalRepositoryRequestCount } else { 0 }
        ManagedDownloadBytes = if ($detailSummary) { [long] $detailSummary.TotalDownloadBytes } else { 0 }
        ManagedCacheHitCount = if ($detailSummary) { [int] $detailSummary.CacheHitCount } else { 0 }
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

function Invoke-SaveScenario {
    param([string] $EngineName, [int] $Iteration)

    $destination = Join-Path $workRoot ("save-{0}-{1}" -f $EngineName, $Iteration)
    New-Item -Path $destination -ItemType Directory -Force | Out-Null

    switch ($EngineName) {
        'ModuleFast' {
            return New-SkippedRow -OperationName 'Save' -EngineName $EngineName -Iteration $Iteration -Reason 'ModuleFast does not expose an equivalent save command.'
        }
        'Managed' {
            Invoke-TimedOperation -OperationName 'Save' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath '' -ScriptBlock {
                $parameters = @{
                    Name = $ModuleName
                    Path = $destination
                    Repository = $repositorySource
                    RepositoryName = $RepositoryName
                    AllowClobber = $true
                    Force = $true
                }
                if (-not [string]::IsNullOrWhiteSpace($Version)) {
                    $parameters.Version = $Version
                }
                Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Save-ManagedModule' -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
                Save-ManagedModule @parameters
            }
        }
        'PSResourceGet' {
            if (-not (Test-CommandAvailable 'Save-PSResource')) {
                return New-SkippedRow -OperationName 'Save' -EngineName $EngineName -Iteration $Iteration -Reason 'Save-PSResource is not available.'
            }

            Invoke-TimedOperation -OperationName 'Save' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath '' -ScriptBlock {
                $parameters = @{
                    Name = $ModuleName
                    Path = $destination
                    Repository = $RepositoryName
                    TrustRepository = $true
                }
                foreach ($entry in (Get-VersionParameter -CommandName 'Save-PSResource' -ExactVersion $Version).GetEnumerator()) {
                    $parameters[$entry.Key] = $entry.Value
                }
                Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Save-PSResource' -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
                Save-PSResource @parameters
            }
        }
        'PowerShellGet' {
            if (-not (Test-CommandAvailable 'Save-Module')) {
                return New-SkippedRow -OperationName 'Save' -EngineName $EngineName -Iteration $Iteration -Reason 'Save-Module is not available.'
            }

            Invoke-TimedOperation -OperationName 'Save' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath '' -ScriptBlock {
                $parameters = @{
                    Name = $ModuleName
                    Path = $destination
                    Repository = $RepositoryName
                    Force = $true
                }
                foreach ($entry in (Get-VersionParameter -CommandName 'Save-Module' -ExactVersion $Version).GetEnumerator()) {
                    $parameters[$entry.Key] = $entry.Value
                }
                Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Save-Module' -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
                Save-Module @parameters
            }
        }
    }
}

function Invoke-InstallScenario {
    param([string] $EngineName, [int] $Iteration)

    $destination = Join-Path $installWorkRoot ("install-{0}-{1}" -f $EngineName, $Iteration)
    New-Item -Path $destination -ItemType Directory -Force | Out-Null

    switch ($EngineName) {
        'ModuleFast' {
            if ($PSVersionTable.PSEdition -eq 'Desktop' -or $PSVersionTable.PSVersion -lt [version]'7.2') {
                return New-SkippedRow -OperationName 'Install' -EngineName $EngineName -Iteration $Iteration -Reason 'ModuleFast requires PowerShell 7.2 or newer.'
            }
            if (-not (Get-ProviderModulePath -EngineName $EngineName)) {
                return New-SkippedRow -OperationName 'Install' -EngineName $EngineName -Iteration $Iteration -Reason 'ModuleFast is not installed for this benchmark host.'
            }

            Invoke-TimedOperation -OperationName 'Install' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath '' -ScriptBlock {
                Invoke-IsolatedInstallHost -EngineName $EngineName -Destination $destination -DetailPath ''
            }
        }
        'Managed' {
            $detailPath = Join-Path $workRoot ("managed-install-details-{0}.json" -f $Iteration)
            Invoke-TimedOperation -OperationName 'Install' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath $detailPath -ScriptBlock {
                Invoke-IsolatedInstallHost -EngineName $EngineName -Destination $destination -DetailPath $detailPath
            }
        }
        'PSResourceGet' {
            if (-not (Test-CommandAvailable 'Install-PSResource')) {
                return New-SkippedRow -OperationName 'Install' -EngineName $EngineName -Iteration $Iteration -Reason 'Install-PSResource is not available.'
            }

            Invoke-TimedOperation -OperationName 'Install' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath '' -ScriptBlock {
                Invoke-IsolatedInstallHost -EngineName $EngineName -Destination $destination -DetailPath ''
            }
        }
        'PowerShellGet' {
            if (-not (Test-CommandAvailable 'Install-Module')) {
                return New-SkippedRow -OperationName 'Install' -EngineName $EngineName -Iteration $Iteration -Reason 'Install-Module is not available.'
            }

            Invoke-TimedOperation -OperationName 'Install' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath '' -ScriptBlock {
                Invoke-IsolatedInstallHost -EngineName $EngineName -Destination $destination -DetailPath ''
            }
        }
    }
}

function Invoke-UpdateScenario {
    param([string] $EngineName, [int] $Iteration)

    if ([string]::IsNullOrWhiteSpace($script:ResolvedUpdateBaselineVersion)) {
        $reason = if ([string]::IsNullOrWhiteSpace($script:UpdateBaselineResolutionError)) {
            'UpdateBaselineVersion could not be resolved for update benchmarks.'
        } else {
            $script:UpdateBaselineResolutionError
        }

        return New-SkippedRow -OperationName 'Update' -EngineName $EngineName -Iteration $Iteration -Reason $reason
    }

    if ($EngineName -eq 'ModuleFast') {
        return New-SkippedRow -OperationName 'Update' -EngineName $EngineName -Iteration $Iteration -Reason 'ModuleFast does not expose an equivalent update command.'
    }

    switch ($EngineName) {
        'PSResourceGet' {
            if (-not (Test-CommandAvailable 'Update-PSResource')) {
                return New-SkippedRow -OperationName 'Update' -EngineName $EngineName -Iteration $Iteration -Reason 'Update-PSResource is not available.'
            }
        }
        'PowerShellGet' {
            if (-not (Test-CommandAvailable 'Update-Module')) {
                return New-SkippedRow -OperationName 'Update' -EngineName $EngineName -Iteration $Iteration -Reason 'Update-Module is not available.'
            }
        }
    }

    $destination = Join-Path $installWorkRoot ("update-{0}-{1}" -f $EngineName, $Iteration)
    New-Item -Path $destination -ItemType Directory -Force | Out-Null
    $packageCacheDirectory = if ($CacheMode -eq 'Warm' -and $EngineName -eq 'Managed') {
        Join-Path $destination 'ManagedPackageCache'
    } else {
        ''
    }

    try {
        Invoke-IsolatedInstallHost -EngineName $EngineName -Destination $destination -DetailPath '' -OperationName 'Install' -VersionOverride $script:ResolvedUpdateBaselineVersion -PackageCacheDirectory $packageCacheDirectory
    } catch {
        return New-FailedRow -OperationName 'Update' -EngineName $EngineName -Iteration $Iteration -Reason "Baseline install failed: $($_.Exception.Message)" -OutputRoot $destination
    }
    if ($CacheMode -eq 'Cold') {
        Clear-IsolatedPackageCaches -Destination $destination
    }

    $detailPath = if ($EngineName -eq 'Managed') {
        Join-Path $workRoot ("managed-update-details-{0}.json" -f $Iteration)
    } else {
        ''
    }

    Invoke-TimedOperation -OperationName 'Update' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -DetailPath $detailPath -ScriptBlock {
        Invoke-IsolatedInstallHost -EngineName $EngineName -Destination $destination -DetailPath $detailPath -OperationName 'Update' -PackageCacheDirectory $packageCacheDirectory
    }
}

function Get-Median {
    param([double[]] $Values)

    if (-not $Values -or $Values.Count -eq 0) {
        return 0
    }

    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) {
        return [math]::Round($sorted[$middle], 2)
    }

    [math]::Round(($sorted[$middle - 1] + $sorted[$middle]) / 2, 2)
}

function New-Summary {
    param([object[]] $Rows)

    foreach ($group in ($Rows | Group-Object Operation, Engine)) {
        $passed = @($group.Group | Where-Object Status -eq 'Succeeded')
        [pscustomobject]@{
            Operation = [string]$group.Group[0].Operation
            Engine = [string]$group.Group[0].Engine
            Runs = $group.Count
            Succeeded = $passed.Count
            Failed = @($group.Group | Where-Object Status -eq 'Failed').Count
            Skipped = @($group.Group | Where-Object Status -eq 'Skipped').Count
            MedianMs = Get-Median -Values @($passed | ForEach-Object { [double]$_.ElapsedMilliseconds })
            MinMs = if ($passed.Count) { [math]::Round(($passed | Measure-Object ElapsedMilliseconds -Minimum).Minimum, 2) } else { 0 }
            MaxMs = if ($passed.Count) { [math]::Round(($passed | Measure-Object ElapsedMilliseconds -Maximum).Maximum, 2) } else { 0 }
        }
    }
}

function New-Comparison {
    param([object[]] $SummaryRows)

    foreach ($operationGroup in ($SummaryRows | Group-Object Operation)) {
        $successful = @($operationGroup.Group | Where-Object { $_.Succeeded -gt 0 -and $_.MedianMs -gt 0 } | Sort-Object MedianMs)
        $managed = @($successful | Where-Object Engine -eq 'Managed' | Select-Object -First 1)
        $fastest = @($successful | Select-Object -First 1)
        [pscustomobject]@{
            Operation = [string]$operationGroup.Name
            FastestEngine = if ($fastest.Count) { [string]$fastest[0].Engine } else { '' }
            FastestMs = if ($fastest.Count) { [double]$fastest[0].MedianMs } else { 0 }
            ManagedMs = if ($managed.Count) { [double]$managed[0].MedianMs } else { 0 }
            ManagedRank = if ($managed.Count -and $successful.Count) {
                1 + @($successful | Where-Object { $_.MedianMs -lt $managed[0].MedianMs }).Count
            } else {
                0
            }
            ManagedVsFastest = if ($managed.Count -and $fastest.Count -and $fastest[0].MedianMs -gt 0) {
                ('{0}x' -f ([math]::Round($managed[0].MedianMs / $fastest[0].MedianMs, 2)))
            } else {
                ''
            }
        }
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

if ($RepeatCount -lt 1) {
    throw 'RepeatCount must be greater than zero.'
}

if ($ListScenarios.IsPresent) {
    foreach ($operationName in $Operation) {
        foreach ($engineName in $Engine) {
            [pscustomobject]@{
                Operation = $operationName
                Engine = $engineName
            }
        }
    }
    return
}

New-Item -Path $workRoot -ItemType Directory -Force | Out-Null
Invoke-LocalBuild
$moduleBinary = Import-LocalModule
$updateBaselineResolution = Initialize-ManagedModuleBenchmarkUpdateBaseline -Operations $Operation -CurrentBaselineVersion $UpdateBaselineVersion -ModuleName $ModuleName -RequestedVersion $Version -RepositorySource $repositorySource
$script:ResolvedUpdateBaselineVersion = [string]$updateBaselineResolution.BaselineVersion
$script:ResolvedUpdateTargetVersion = [string]$updateBaselineResolution.TargetVersion
$script:UpdateBaselineResolutionError = [string]$updateBaselineResolution.Error
if (-not [string]::IsNullOrWhiteSpace([string]$updateBaselineResolution.Message)) {
    Write-Host ([string]$updateBaselineResolution.Message)
} elseif (-not [string]::IsNullOrWhiteSpace($script:UpdateBaselineResolutionError)) {
    Write-Warning $script:UpdateBaselineResolutionError
}

$results = [Collections.Generic.List[object]]::new()
foreach ($iteration in 1..$RepeatCount) {
    $engineOrder = Get-IterationEngineOrder -Iteration $iteration
    foreach ($operationName in $Operation) {
        foreach ($engineName in $engineOrder) {
            $row = switch ($operationName) {
                'Find' { Invoke-FindScenario -EngineName $engineName -Iteration $iteration }
                'Save' { Invoke-SaveScenario -EngineName $engineName -Iteration $iteration }
                'Install' { Invoke-InstallScenario -EngineName $engineName -Iteration $iteration }
                'InstallManaged' { Invoke-InstallScenario -EngineName $engineName -Iteration $iteration }
                'Update' { Invoke-UpdateScenario -EngineName $engineName -Iteration $iteration }
            }
            $results.Add($row)
        }
    }
}

$summary = @(New-Summary -Rows $results)
$comparison = @(New-Comparison -SummaryRows $summary)
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
    CacheMode = $CacheMode
    ValidateImport = $ValidateImport.IsPresent
    ImportTimeoutSeconds = $ImportTimeoutSeconds
    RotateEngineOrder = $RotateEngineOrder.IsPresent
    Suite = $Suite
    Engines = $Engine
    Operations = $Operation
    RepeatCount = $RepeatCount
    ModuleBinary = $moduleBinary
    OutputDirectory = $workRoot
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    PSEdition = $PSVersionTable.PSEdition
    OS = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    ProcessArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
}

$resultsPath = Join-Path $workRoot 'managed-module-results.csv'
$resultsJsonPath = Join-Path $workRoot 'managed-module-results.json'
$summaryPath = Join-Path $workRoot 'managed-module-summary.csv'
$comparisonPath = Join-Path $workRoot 'managed-module-comparison.csv'
$metadataPath = Join-Path $workRoot 'metadata.json'

$results | Export-Csv -LiteralPath $resultsPath -NoTypeInformation
$results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultsJsonPath -Encoding UTF8
$summary | Export-Csv -LiteralPath $summaryPath -NoTypeInformation
$comparison | Export-Csv -LiteralPath $comparisonPath -NoTypeInformation
$metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

$comparison
Write-Host "Benchmark output: $workRoot"
