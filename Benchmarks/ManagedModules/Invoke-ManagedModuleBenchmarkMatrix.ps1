#requires -Version 5.1
param(
    [ValidateSet('Full', 'ManagedVsModuleFast')]
    [string] $ComparisonProfile = 'ManagedVsModuleFast',

    [ValidateSet('SingleModule', 'GraphAuthentication', 'Graph', 'AzAccounts', 'Az')]
    [string[]] $ScenarioName = @('SingleModule', 'GraphAuthentication', 'Graph', 'AzAccounts', 'Az'),

    [ValidateSet('Find', 'Install', 'Save')]
    [string[]] $Operation = @('Find', 'Install', 'Save'),

    [ValidateSet('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')]
    [string[]] $Engine = @(),

    [int] $RepeatCount = 1,

    [string] $OutputPath = (Join-Path $PSScriptRoot '..\..\Ignore\Benchmarks\ManagedModules\managed-module-benchmark.csv'),

    [string] $OutputRoot = '',

    [string] $Repository = 'PSGallery',

    [string] $RepositoryUri = 'https://www.powershellgallery.com/api/v2',

    [string] $ModuleFastSource = 'https://pwsh.gallery/index.json',

    [string] $ModuleFastModulePath = '',

    [string] $ManagedModuleBinary,

    [ValidateSet('Current', 'PowerShell7', 'WindowsPowerShell')]
    [string] $BenchmarkHost = 'Current',

    [switch] $Append,

    [switch] $SkipTemporaryUserNativeInstall,

    [switch] $KeepTemporaryUserProfile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:IsWindowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT

function Test-IsWindowsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-PowerShellExecutableVersion {
    param([string] $Path)

    try {
        $versionText = & $Path -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()' 2>$null |
            Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($versionText)) {
            return $null
        }

        return [version]$versionText
    } catch {
        return $null
    }
}

function Resolve-PowerShell7Executable {
    $candidatePaths = New-Object System.Collections.Generic.List[string]
    foreach ($command in @(Get-Command pwsh -All -ErrorAction SilentlyContinue)) {
        if (-not [string]::IsNullOrWhiteSpace($command.Source)) {
            $candidatePaths.Add($command.Source)
        }
    }

    if (Test-Path -LiteralPath 'C:\Program Files\PowerShell\7\pwsh.exe') {
        $candidatePaths.Add('C:\Program Files\PowerShell\7\pwsh.exe')
    }

    $candidates = @($candidatePaths |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique |
        ForEach-Object {
            $version = Get-PowerShellExecutableVersion -Path $_
            if ($version -and $version.Major -ge 7) {
                [pscustomobject]@{
                    Path = $_
                    Version = $version
                }
            }
        })

    if ($candidates.Count -gt 0) {
        return ($candidates | Sort-Object Version -Descending | Select-Object -First 1).Path
    }

    return (Get-Command pwsh -ErrorAction Stop).Source
}

function Resolve-BenchmarkHostExecutable {
    switch ($BenchmarkHost) {
        'WindowsPowerShell' {
            return "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
        }
        'PowerShell7' {
            return Resolve-PowerShell7Executable
        }
        default {
            if ($PSVersionTable.PSEdition -eq 'Desktop') {
                return (Join-Path $PSHOME 'powershell.exe')
            }

            $currentPwsh = Join-Path $PSHOME 'pwsh.exe'
            if (Test-Path -LiteralPath $currentPwsh) {
                return $currentPwsh
            }

            return Resolve-PowerShell7Executable
        }
    }
}

function ConvertTo-SingleQuotedLiteral {
    param([string] $Value)
    "'" + $Value.Replace("'", "''") + "'"
}

function ConvertTo-ArrayLiteral {
    param([string[]] $Value)
    '@(' + (($Value | ForEach-Object { ConvertTo-SingleQuotedLiteral $_ }) -join ', ') + ')'
}

function ConvertTo-ParameterValueLiteral {
    param([string[]] $Value)
    if ($Value.Count -eq 1) {
        return ConvertTo-SingleQuotedLiteral $Value[0]
    }

    ConvertTo-ArrayLiteral $Value
}

function New-BenchmarkScratchRoot {
    param([string] $Name)

    $basePath = if ($script:IsWindowsHost -and -not [string]::IsNullOrWhiteSpace($env:ProgramData)) {
        Join-Path $env:ProgramData 'PSPublishModuleBenchmarks'
    } else {
        Join-Path ([System.IO.Path]::GetTempPath()) 'PSPublishModuleBenchmarks'
    }

    Join-Path $basePath $Name
}

function New-ProviderRoot {
    param([string] $ScratchRoot)

    $providerRoot = Join-Path $ScratchRoot 'ProviderModules'
    Copy-ProviderModule -Name 'Microsoft.PowerShell.PSResourceGet' -ProviderRoot $providerRoot
    Copy-ProviderModule -Name 'PackageManagement' -ProviderRoot $providerRoot
    Copy-ProviderModule -Name 'PowerShellGet' -ProviderRoot $providerRoot
    $providerRoot
}

function New-MeasureCommand {
    param(
        [string[]] $SelectedOperation,
        [string[]] $SelectedEngine,
        [string] $SelectedOutputPath,
        [string] $SelectedOutputRoot,
        [string] $ProviderRoot,
        [switch] $AppendOutput,
        [switch] $SkipNativeCurrentUserInstall
    )

    $measureLiteral = ConvertTo-SingleQuotedLiteral $measureScript
    $scenarioLiteral = ConvertTo-ParameterValueLiteral $ScenarioName
    $operationLiteral = ConvertTo-ParameterValueLiteral $SelectedOperation
    $engineLiteral = ConvertTo-ParameterValueLiteral $SelectedEngine
    $repositoryLiteral = ConvertTo-SingleQuotedLiteral $Repository
    $repositoryUriLiteral = ConvertTo-SingleQuotedLiteral $RepositoryUri
    $outputPathLiteral = ConvertTo-SingleQuotedLiteral $SelectedOutputPath
    $outputRootLiteral = ConvertTo-SingleQuotedLiteral $SelectedOutputRoot
    $moduleFastSourceLiteral = ConvertTo-SingleQuotedLiteral $ModuleFastSource
    $moduleFastModulePathLiteral = ConvertTo-SingleQuotedLiteral $ModuleFastModulePath
    $managedModuleBinaryLiteral = ConvertTo-SingleQuotedLiteral $ManagedModuleBinary
    $appendSwitch = if ($AppendOutput.IsPresent) { ' -Append' } else { '' }
    $skipSwitch = if ($SkipNativeCurrentUserInstall.IsPresent) { ' -SkipNativeCurrentUserInstall' } else { '' }
    $prefix = if (-not [string]::IsNullOrWhiteSpace($ProviderRoot)) {
        $providerRootLiteral = ConvertTo-SingleQuotedLiteral $ProviderRoot
        "`$env:PSModulePath = $providerRootLiteral + [IO.Path]::PathSeparator + `$env:PSModulePath; "
    } else {
        ''
    }

    $command = "`$ErrorActionPreference = 'Stop'; $prefix& $measureLiteral -ScenarioName $scenarioLiteral -Operation $operationLiteral -Engine $engineLiteral -RepeatCount $RepeatCount -OutputPath $outputPathLiteral -OutputRoot $outputRootLiteral -Repository $repositoryLiteral -RepositoryUri $repositoryUriLiteral$appendSwitch$skipSwitch"
    if (-not [string]::IsNullOrWhiteSpace($ModuleFastSource)) {
        $command += " -ModuleFastSource $moduleFastSourceLiteral"
    }
    if (-not [string]::IsNullOrWhiteSpace($ModuleFastModulePath)) {
        $command += " -ModuleFastModulePath $moduleFastModulePathLiteral"
    }
    if (-not [string]::IsNullOrWhiteSpace($ManagedModuleBinary)) {
        $command += " -ManagedModuleBinary $managedModuleBinaryLiteral"
    }

    $command
}

function Invoke-CurrentUserMeasure {
    param(
        [string[]] $SelectedOperation,
        [string[]] $SelectedEngine,
        [string] $SelectedOutputRoot,
        [switch] $SkipNativeCurrentUserInstall
    )

    if ($SelectedOperation.Count -eq 0 -or $SelectedEngine.Count -eq 0) {
        return
    }

    $scratchRoot = $null
    $providerRoot = ''
    try {
        if ($SelectedEngine -contains 'PSResourceGet' -or $SelectedEngine -contains 'PowerShellGet') {
            $scratchRoot = New-BenchmarkScratchRoot -Name ('Provider-' + [guid]::NewGuid().ToString('N'))
            Grant-BenchmarkPath -Path $scratchRoot
            $providerRoot = New-ProviderRoot -ScratchRoot $scratchRoot
        }

        $command = New-MeasureCommand -SelectedOperation $SelectedOperation -SelectedEngine $SelectedEngine -SelectedOutputPath $OutputPath -SelectedOutputRoot $SelectedOutputRoot -ProviderRoot $providerRoot -AppendOutput:($Append.IsPresent -or (Test-Path -LiteralPath $OutputPath)) -SkipNativeCurrentUserInstall:$SkipNativeCurrentUserInstall.IsPresent
        & $script:BenchmarkHostExecutable -NoLogo -NoProfile -ExecutionPolicy Bypass -Command $command
        if ($LASTEXITCODE -ne 0) {
            throw "Benchmark host '$script:BenchmarkHostExecutable' failed with exit code $LASTEXITCODE."
        }
    } finally {
        if (-not [string]::IsNullOrWhiteSpace($scratchRoot)) {
            Remove-Item -LiteralPath $scratchRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Resolve-ProviderModuleBase {
    param([string] $Name)

    if (-not [string]::IsNullOrWhiteSpace($script:BenchmarkHostExecutable)) {
        $escapedName = $Name.Replace("'", "''")
        $moduleBaseOutput = @(& $script:BenchmarkHostExecutable -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "(Get-Module -ListAvailable -Name '$escapedName' | Sort-Object Version -Descending | Select-Object -First 1).ModuleBase")
        $moduleBase = ($moduleBaseOutput | Select-Object -First 1)
        if ($null -ne $moduleBase) {
            $moduleBase = $moduleBase.Trim()
        }
        if (-not [string]::IsNullOrWhiteSpace($moduleBase) -and (Test-Path -LiteralPath $moduleBase)) {
            return $moduleBase
        }
    }

    $module = Get-Module -ListAvailable -Name $Name | Sort-Object Version -Descending | Select-Object -First 1
    if ($null -ne $module) {
        return $module.ModuleBase
    }

    if ($Name -eq 'Microsoft.PowerShell.PSResourceGet') {
        $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
        if ($null -ne $pwsh) {
            $moduleBase = & $pwsh.Source -NoLogo -NoProfile -Command "(Get-Module -ListAvailable -Name '$Name' | Sort-Object Version -Descending | Select-Object -First 1).ModuleBase"
            $moduleBase = ($moduleBase | Select-Object -First 1).Trim()
            if (-not [string]::IsNullOrWhiteSpace($moduleBase) -and (Test-Path -LiteralPath $moduleBase)) {
                return $moduleBase
            }
        }
    }

    throw "Provider module '$Name' is not available."
}

function Copy-ProviderModule {
    param(
        [string] $Name,
        [string] $ProviderRoot
    )

    $moduleBase = Resolve-ProviderModuleBase -Name $Name
    $leaf = Split-Path -Leaf $moduleBase
    $parentLeaf = Split-Path -Leaf (Split-Path -Parent $moduleBase)
    $destination = if ($leaf.Equals($Name, [StringComparison]::OrdinalIgnoreCase)) {
        Join-Path $ProviderRoot $leaf
    } elseif ($parentLeaf.Equals($Name, [StringComparison]::OrdinalIgnoreCase)) {
        Join-Path (Join-Path $ProviderRoot $parentLeaf) $leaf
    } else {
        Join-Path $ProviderRoot $leaf
    }

    if (-not (Test-Path -LiteralPath $destination)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $moduleBase -Destination $destination -Recurse -Force
    }
}

function Grant-BenchmarkPath {
    param([string] $Path)

    if (-not $script:IsWindowsHost) {
        return
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    & icacls.exe $Path /grant 'Users:(OI)(CI)M' | Out-Null
}

function Join-BenchmarkOutputRoot {
    param([string] $Leaf)

    if ([string]::IsNullOrWhiteSpace($script:EffectiveOutputRoot)) {
        return ''
    }

    Join-Path $script:EffectiveOutputRoot $Leaf
}

function Add-ResultCsv {
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Expected temporary benchmark result was not created: $Path"
    }

    $rows = @(Import-Csv -LiteralPath $Path)
    if ($rows.Count -eq 0) {
        return
    }

    if ($Append.IsPresent -or (Test-Path -LiteralPath $OutputPath)) {
        $rows | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Append
    } else {
        $rows | Export-Csv -LiteralPath $OutputPath -NoTypeInformation
    }
}

function Invoke-TemporaryUserMeasure {
    param(
        [string[]] $SelectedEngine,
        [string] $SelectedOutputRoot
    )

    if ($SelectedEngine.Count -eq 0) {
        return
    }
    if (-not $script:IsWindowsHost) {
        throw 'Temporary Windows user benchmark isolation is only available on Windows.'
    }
    if (-not (Test-IsWindowsAdministrator)) {
        throw 'Temporary Windows user benchmark isolation requires an elevated PowerShell session.'
    }

    $user = 'PFBench' + ([guid]::NewGuid().ToString('N').Substring(0, 8))
    $passwordPlain = 'PFb!' + ([guid]::NewGuid().ToString('N')) + '9a'
    $secure = ConvertTo-SecureString $passwordPlain -AsPlainText -Force
    $credential = [pscredential]::new("$env:COMPUTERNAME\$user", $secure)
    $scratchRoot = New-BenchmarkScratchRoot -Name $user
    $providerRoot = Join-Path $scratchRoot 'ProviderModules'
    $temporaryOutputPath = Join-Path $scratchRoot 'native-results.csv'
    $temporaryOutputRoot = Join-Path $scratchRoot 'Runs'
    $stdoutPath = Join-Path $scratchRoot 'stdout.txt'
    $stderrPath = Join-Path $scratchRoot 'stderr.txt'

    Grant-BenchmarkPath -Path $scratchRoot

    $providerRoot = New-ProviderRoot -ScratchRoot $scratchRoot
    $invokeCommand = New-MeasureCommand -SelectedOperation @('Install') -SelectedEngine $SelectedEngine -SelectedOutputPath $temporaryOutputPath -SelectedOutputRoot $temporaryOutputRoot -ProviderRoot $providerRoot

    try {
        New-LocalUser -Name $user -Password $secure -Description 'Temporary PSPublishModule benchmark user' -AccountNeverExpires -PasswordNeverExpires | Out-Null
        $process = Start-Process -FilePath $script:BenchmarkHostExecutable -ArgumentList @('-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $invokeCommand) -Credential $credential -LoadUserProfile -UseNewEnvironment -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        if ($process.ExitCode -ne 0) {
            $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
            $stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { '' }
            throw "Temporary benchmark user run failed with exit code $($process.ExitCode). STDOUT: $stdout STDERR: $stderr"
        }

        Add-ResultCsv -Path $temporaryOutputPath
    } finally {
        Remove-LocalUser -Name $user -ErrorAction SilentlyContinue
        if (-not $KeepTemporaryUserProfile.IsPresent) {
            Start-Sleep -Seconds 1
            Get-CimInstance Win32_UserProfile |
                Where-Object { $_.LocalPath -like "*\$user" } |
                Remove-CimInstance -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $scratchRoot -Recurse -Force -ErrorAction SilentlyContinue
        } else {
            Write-Warning "Temporary benchmark profile was kept for inspection: $user"
        }
    }
}

$measureScript = Join-Path $PSScriptRoot 'Measure-ManagedModuleBenchmark.ps1'
if (-not (Test-Path -LiteralPath $measureScript)) {
    throw "Benchmark script was not found: $measureScript"
}
$script:BenchmarkHostExecutable = Resolve-BenchmarkHostExecutable
$script:EffectiveOutputRoot = $OutputRoot
if ([string]::IsNullOrWhiteSpace($script:EffectiveOutputRoot) -and $script:BenchmarkHostExecutable -notlike '*\WindowsPowerShell\*') {
    $script:EffectiveOutputRoot = Join-Path $PSScriptRoot '..\..\Ignore\Benchmarks\ManagedModules\Runs'
}

if ($ComparisonProfile -eq 'ManagedVsModuleFast') {
    $Operation = @('Install')
    if (-not $PSBoundParameters.ContainsKey('Engine') -or $Engine.Count -eq 0) {
        $Engine = @('Managed', 'ModuleFast')
    }
    if ($BenchmarkHost -ne 'PowerShell7') {
        Write-Warning "ManagedVsModuleFast compares ModuleFast on PowerShell 7; rerun with -BenchmarkHost PowerShell7 for a direct row."
    }
} elseif (-not $PSBoundParameters.ContainsKey('Engine') -or $Engine.Count -eq 0) {
    $Engine = @('Managed', 'ModuleFast', 'PSResourceGet', 'PowerShellGet')
}

$safeOperations = @($Operation | Where-Object { $_ -ne 'Install' })
$safeInstallEngines = @($Engine | Where-Object { $_ -ne 'PSResourceGet' -and $_ -ne 'PowerShellGet' })
$nativeInstallEngines = @($Engine | Where-Object { $_ -eq 'PSResourceGet' -or $_ -eq 'PowerShellGet' })

Invoke-CurrentUserMeasure -SelectedOperation $safeOperations -SelectedEngine $Engine -SelectedOutputRoot (Join-BenchmarkOutputRoot -Leaf 'CurrentUser')

if ($Operation -contains 'Install') {
    Invoke-CurrentUserMeasure -SelectedOperation @('Install') -SelectedEngine $safeInstallEngines -SelectedOutputRoot (Join-BenchmarkOutputRoot -Leaf 'CurrentUser')

    if ($SkipTemporaryUserNativeInstall.IsPresent) {
        Invoke-CurrentUserMeasure -SelectedOperation @('Install') -SelectedEngine $nativeInstallEngines -SelectedOutputRoot (Join-BenchmarkOutputRoot -Leaf 'CurrentUser') -SkipNativeCurrentUserInstall
    } else {
        foreach ($nativeInstallEngine in $nativeInstallEngines) {
            Invoke-TemporaryUserMeasure -SelectedEngine @($nativeInstallEngine) -SelectedOutputRoot (Join-BenchmarkOutputRoot -Leaf ('TemporaryUser-' + $nativeInstallEngine))
        }
    }
}
