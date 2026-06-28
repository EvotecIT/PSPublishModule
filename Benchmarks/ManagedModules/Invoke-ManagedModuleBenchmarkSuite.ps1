param(
    [ValidateSet('Smoke', 'Graph', 'Az', 'Enterprise', 'All')]
    [string[]] $Suite = @('Smoke'),

    [ValidateSet('Current', 'PowerShell7', 'WindowsPowerShell')]
    [string[]] $HostName = @('Current'),

    [string[]] $Engine = @('Managed', 'PSResourceGet', 'PowerShellGet'),

    [string[]] $Operation = @('Find', 'Save'),

    [int] $RepeatCount = 1,

    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\Ignore\Benchmarks\ManagedModules\Suites'),

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipBuild,

    [switch] $IncludeInstallManaged,

    [switch] $ListScenarios
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$compareScript = Join-Path $PSScriptRoot 'Compare-ManagedModuleEngines.ps1'
$suiteRoot = Join-Path $OutputDirectory ('Suite-{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $PID)

function New-BenchmarkScenario {
    param(
        [string] $SuiteName,
        [string] $Name,
        [string] $ModuleName,
        [string] $Version = '',
        [bool] $AcceptLicense = $false,
        [string[]] $Operations = $Operation
    )

    [pscustomobject]@{
        Suite = $SuiteName
        Name = $Name
        ModuleName = $ModuleName
        Version = $Version
        AcceptLicense = $AcceptLicense
        Operations = $Operations
    }
}

function Get-ScenarioCatalog {
    @(
        New-BenchmarkScenario -SuiteName 'Smoke' -Name 'ThreadJob' -ModuleName 'ThreadJob' -Version '2.1.0'
        New-BenchmarkScenario -SuiteName 'Graph' -Name 'Graph.Authentication' -ModuleName 'Microsoft.Graph.Authentication' -AcceptLicense $true
        New-BenchmarkScenario -SuiteName 'Graph' -Name 'Graph.Full' -ModuleName 'Microsoft.Graph' -AcceptLicense $true
        New-BenchmarkScenario -SuiteName 'Graph' -Name 'Graph.Beta.Full' -ModuleName 'Microsoft.Graph.Beta' -AcceptLicense $true
        New-BenchmarkScenario -SuiteName 'Az' -Name 'Az.Accounts' -ModuleName 'Az.Accounts'
        New-BenchmarkScenario -SuiteName 'Az' -Name 'Az.Resources' -ModuleName 'Az.Resources'
        New-BenchmarkScenario -SuiteName 'Az' -Name 'Az.Full' -ModuleName 'Az'
        New-BenchmarkScenario -SuiteName 'Enterprise' -Name 'Teams' -ModuleName 'MicrosoftTeams'
        New-BenchmarkScenario -SuiteName 'Enterprise' -Name 'ExchangeOnlineManagement' -ModuleName 'ExchangeOnlineManagement'
    )
}

function Resolve-ScenarioList {
    $selectedSuites = if ($Suite -contains 'All') {
        @('Smoke', 'Graph', 'Az', 'Enterprise')
    } else {
        $Suite
    }

    @(Get-ScenarioCatalog | Where-Object { $selectedSuites -contains $_.Suite })
}

function Resolve-HostExecutable {
    param([string] $Name)

    switch ($Name) {
        'Current' {
            return (Get-Process -Id $PID).Path
        }
        'PowerShell7' {
            $command = Get-Command pwsh -ErrorAction SilentlyContinue
            if ($command) {
                return $command.Source
            }
            return $null
        }
        'WindowsPowerShell' {
            if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
                    [System.Runtime.InteropServices.OSPlatform]::Windows)) {
                return $null
            }

            $path = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
            if (Test-Path -LiteralPath $path) {
                return $path
            }
            return $null
        }
    }
}

function Invoke-LocalBuild {
    if ($SkipBuild.IsPresent) {
        return
    }

    $projectPath = Join-Path $repoRoot 'PSPublishModule\PSPublishModule.csproj'
    Write-Host "Building PSPublishModule ($Configuration) before suite..."
    & dotnet build $projectPath -c $Configuration --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for PSPublishModule ($Configuration)."
    }
}

function Get-ScenarioOperations {
    param([object] $Scenario)

    $operations = @($Scenario.Operations)
    if ($IncludeInstallManaged.IsPresent -and -not ($operations -contains 'InstallManaged')) {
        $operations += 'InstallManaged'
    }

    $operations
}

function Invoke-ScenarioHostRun {
    param(
        [object] $Scenario,
        [string] $HostLabel,
        [string] $Executable
    )

    $scenarioRoot = Join-Path $suiteRoot ('{0}\{1}' -f $HostLabel, $Scenario.Name)
    New-Item -Path $scenarioRoot -ItemType Directory -Force | Out-Null

    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $compareScript,
        '-ModuleName',
        $Scenario.ModuleName,
        '-Version',
        $Scenario.Version,
        '-Operation',
        ((Get-ScenarioOperations -Scenario $Scenario) -join ','),
        '-Engine',
        ($Engine -join ','),
        '-RepeatCount',
        ([string]$RepeatCount),
        '-OutputDirectory',
        $scenarioRoot,
        '-Configuration',
        $Configuration,
        '-SkipBuild'
    )

    if ($Scenario.AcceptLicense) {
        $arguments += '-AcceptLicense'
    }

    Write-Host "Running $($Scenario.Name) on $HostLabel..."
    $processOutput = @(& $Executable @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    foreach ($line in $processOutput) {
        Write-Host $line
    }

    $run = Get-ChildItem -LiteralPath $scenarioRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($exitCode -ne 0) {
        throw "Benchmark scenario '$($Scenario.Name)' failed on '$HostLabel' with exit code $exitCode."
    }

    if (-not $run) {
        throw "Benchmark scenario '$($Scenario.Name)' on '$HostLabel' did not produce an output run directory."
    }

    return $run
}

function Add-SummaryRows {
    param(
        [Collections.Generic.List[object]] $Rows,
        [object] $Scenario,
        [string] $HostLabel,
        [string] $RunPath
    )

    $comparisonPath = Join-Path $RunPath 'managed-module-comparison.csv'
    if (Test-Path -LiteralPath $comparisonPath) {
        foreach ($row in (Import-Csv -LiteralPath $comparisonPath)) {
            $Rows.Add([pscustomobject]@{
                Suite = $Scenario.Suite
                Scenario = $Scenario.Name
                ModuleName = $Scenario.ModuleName
                Host = $HostLabel
                Operation = $row.Operation
                FastestEngine = $row.FastestEngine
                FastestMs = $row.FastestMs
                ManagedMs = $row.ManagedMs
                ManagedRank = $row.ManagedRank
                ManagedVsFastest = $row.ManagedVsFastest
                RunPath = $RunPath
            })
        }
    }
}

$scenarios = Resolve-ScenarioList
if ($ListScenarios.IsPresent) {
    $scenarios | Select-Object Suite, Name, ModuleName, Version, AcceptLicense, Operations
    return
}

New-Item -Path $suiteRoot -ItemType Directory -Force | Out-Null
Invoke-LocalBuild

$summaryRows = [Collections.Generic.List[object]]::new()
$hostRows = [Collections.Generic.List[object]]::new()

foreach ($hostLabel in $HostName) {
    $executable = Resolve-HostExecutable -Name $hostLabel
    if (-not $executable) {
        $hostRows.Add([pscustomobject]@{
            Host = $hostLabel
            Status = 'Skipped'
            Executable = ''
            Reason = 'Host executable was not found on this machine.'
        })
        continue
    }

    $hostRows.Add([pscustomobject]@{
        Host = $hostLabel
        Status = 'Available'
        Executable = $executable
        Reason = ''
    })

    foreach ($scenario in $scenarios) {
        $run = Invoke-ScenarioHostRun -Scenario $scenario -HostLabel $hostLabel -Executable $executable
        Add-SummaryRows -Rows $summaryRows -Scenario $scenario -HostLabel $hostLabel -RunPath $run.FullName
    }
}

$summaryPath = Join-Path $suiteRoot 'suite-summary.csv'
$summaryJsonPath = Join-Path $suiteRoot 'suite-summary.json'
$hostsPath = Join-Path $suiteRoot 'suite-hosts.csv'
$metadataPath = Join-Path $suiteRoot 'metadata.json'

$summaryRows | Export-Csv -LiteralPath $summaryPath -NoTypeInformation
$summaryRows | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryJsonPath -Encoding UTF8
$hostRows | Export-Csv -LiteralPath $hostsPath -NoTypeInformation

$metadata = [ordered]@{
    Suites = $Suite
    Hosts = $HostName
    Engines = $Engine
    Operations = $Operation
    RepeatCount = $RepeatCount
    IncludeInstallManaged = $IncludeInstallManaged.IsPresent
    OutputDirectory = $suiteRoot
}
$metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

$summaryRows
Write-Host "Benchmark suite output: $suiteRoot"
