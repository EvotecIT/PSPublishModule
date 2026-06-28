param(
    [ValidateSet('Smoke', 'Standard')]
    [string] $Suite = 'Smoke',

    [string] $ModuleName = 'ThreadJob',

    [string] $Version = '2.1.0',

    [string] $Repository = 'PSGallery',

    [string] $RepositoryName = 'PSGallery',

    [string[]] $Engine = @('Managed', 'PSResourceGet', 'PowerShellGet'),

    [string[]] $Operation,

    [int] $RepeatCount = 1,

    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\Ignore\Benchmarks\ManagedModules'),

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipBuild,

    [switch] $AcceptLicense,

    [switch] $ListScenarios
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
[Threading.Thread]::CurrentThread.CurrentCulture = $invariantCulture
[Threading.Thread]::CurrentThread.CurrentUICulture = $invariantCulture

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$workRoot = Join-Path $OutputDirectory ('Run-{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $PID)
$validEngines = @('Managed', 'PSResourceGet', 'PowerShellGet')
$validOperations = @('Find', 'Save', 'InstallManaged')

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
        return @('Find', 'Save', 'InstallManaged')
    }

    @('Find', 'Save', 'InstallManaged')
}

function Get-ManagedRepositorySource {
    if ([string]::IsNullOrWhiteSpace($Repository) -or $Repository -eq 'PSGallery' -or $Repository -eq $RepositoryName) {
        return 'https://www.powershellgallery.com/api/v3/index.json'
    }

    $Repository
}

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

function Get-InstalledModuleVersion {
    param(
        [string] $Root,
        [string] $Name
    )

    $moduleDirectory = Join-Path $Root $Name
    if (-not (Test-Path -LiteralPath $moduleDirectory)) {
        return $null
    }

    $manifest = Get-ChildItem -LiteralPath $moduleDirectory -Filter "$Name.psd1" -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object FullName |
        Select-Object -First 1
    if (-not $manifest) {
        return $null
    }

    $text = Get-Content -LiteralPath $manifest.FullName -Raw
    if ($text -match "ModuleVersion\s*=\s*['""]([^'""]+)['""]") {
        return $Matches[1]
    }

    $null
}

function Invoke-TimedOperation {
    param(
        [string] $OperationName,
        [string] $EngineName,
        [int] $Iteration,
        [scriptblock] $ScriptBlock,
        [string] $OutputRoot
    )

    $timer = [Diagnostics.Stopwatch]::StartNew()
    $status = 'Succeeded'
    $errorText = ''
    $versionText = $null
    $outputCount = 0

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
        $timer.Stop()
    }

    [pscustomobject]@{
        Operation = $OperationName
        Engine = $EngineName
        Iteration = $Iteration
        Status = $status
        ModuleName = $ModuleName
        Version = $versionText
        ElapsedMilliseconds = [math]::Round($timer.Elapsed.TotalMilliseconds, 2)
        OutputCount = $outputCount
        OutputRoot = $OutputRoot
        Error = $errorText
    }
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
        ElapsedMilliseconds = 0
        OutputCount = 0
        OutputRoot = ''
        Error = $Reason
    }
}

function Invoke-FindScenario {
    param([string] $EngineName, [int] $Iteration)

    switch ($EngineName) {
        'Managed' {
            Invoke-TimedOperation -OperationName 'Find' -EngineName $EngineName -Iteration $Iteration -OutputRoot '' -ScriptBlock {
                Find-ManagedModule -Name $ModuleName -Repository (Get-ManagedRepositorySource) -RepositoryName $RepositoryName
            }
        }
        'PSResourceGet' {
            if (-not (Test-CommandAvailable 'Find-PSResource')) {
                return New-SkippedRow -OperationName 'Find' -EngineName $EngineName -Iteration $Iteration -Reason 'Find-PSResource is not available.'
            }

            Invoke-TimedOperation -OperationName 'Find' -EngineName $EngineName -Iteration $Iteration -OutputRoot '' -ScriptBlock {
                Find-PSResource -Name $ModuleName -Repository $RepositoryName
            }
        }
        'PowerShellGet' {
            if (-not (Test-CommandAvailable 'Find-Module')) {
                return New-SkippedRow -OperationName 'Find' -EngineName $EngineName -Iteration $Iteration -Reason 'Find-Module is not available.'
            }

            Invoke-TimedOperation -OperationName 'Find' -EngineName $EngineName -Iteration $Iteration -OutputRoot '' -ScriptBlock {
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
        'Managed' {
            Invoke-TimedOperation -OperationName 'Save' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -ScriptBlock {
                $parameters = @{
                    Name = $ModuleName
                    Path = $destination
                    Repository = Get-ManagedRepositorySource
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

            Invoke-TimedOperation -OperationName 'Save' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -ScriptBlock {
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

            Invoke-TimedOperation -OperationName 'Save' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -ScriptBlock {
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

function Invoke-ManagedInstallScenario {
    param([string] $EngineName, [int] $Iteration)

    if ($EngineName -ne 'Managed') {
        return New-SkippedRow -OperationName 'InstallManaged' -EngineName $EngineName -Iteration $Iteration -Reason 'Native install comparison needs a separate disposable-host lane.'
    }

    $destination = Join-Path $workRoot ("install-{0}-{1}" -f $EngineName, $Iteration)
    New-Item -Path $destination -ItemType Directory -Force | Out-Null

    Invoke-TimedOperation -OperationName 'InstallManaged' -EngineName $EngineName -Iteration $Iteration -OutputRoot $destination -ScriptBlock {
        $parameters = @{
            Name = $ModuleName
            Repository = Get-ManagedRepositorySource
            RepositoryName = $RepositoryName
            Scope = 'Custom'
            ModuleRoot = $destination
            AllowClobber = $true
            Force = $true
        }
        if (-not [string]::IsNullOrWhiteSpace($Version)) {
            $parameters.Version = $Version
        }
        Add-SwitchParameterIfSupported -Parameters $parameters -CommandName 'Install-ManagedModule' -ParameterName 'AcceptLicense' -Enabled $AcceptLicense.IsPresent
        Install-ManagedModule @parameters
    }
}

function Get-Median {
    param([double[]] $Values)

    if (-not $Values -or $Values.Count -eq 0) {
        return 0
    }

    $sorted = @($Values | Sort-Object)
    $middle = [int]($sorted.Count / 2)
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

$results = [Collections.Generic.List[object]]::new()
foreach ($iteration in 1..$RepeatCount) {
    foreach ($operationName in $Operation) {
        foreach ($engineName in $Engine) {
            $row = switch ($operationName) {
                'Find' { Invoke-FindScenario -EngineName $engineName -Iteration $iteration }
                'Save' { Invoke-SaveScenario -EngineName $engineName -Iteration $iteration }
                'InstallManaged' { Invoke-ManagedInstallScenario -EngineName $engineName -Iteration $iteration }
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
    Repository = $Repository
    RepositoryName = $RepositoryName
    AcceptLicense = $AcceptLicense.IsPresent
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
