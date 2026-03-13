$script:SourceRoot = $PSScriptRoot

function Test-ModulePayloadUsableForCurrentHost {
    param(
        [Parameter(Mandatory)][string] $ModuleRoot
    )

    $libRoot = Join-Path $ModuleRoot 'Lib'
    if (-not (Test-Path -LiteralPath $libRoot -PathType Container)) {
        return $false
    }

    $directories = Get-ChildItem -Path $libRoot -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name
    $hasCore = $directories -contains 'Core'
    $hasDefault = $directories -contains 'Default'
    $hasStandard = $directories -contains 'Standard'

    if ($PSEdition -eq 'Core') {
        return $hasCore -or $hasStandard
    }

    return $hasDefault -or $hasStandard
}

function Get-ModulePayloadRootForTests {
    param(
        [Parameter(Mandatory)][string] $SourceRoot
    )

    $sourceManifest = Get-ChildItem -Path $SourceRoot -Filter '*.psd1' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $sourceManifest) {
        throw "Path $SourceRoot doesn't contain a PSD1 file. Failing tests."
    }

    $moduleName = $sourceManifest.BaseName
    $artefactModule = Get-ChildItem -Path (Join-Path $SourceRoot 'Artefacts\Unpacked') -Filter '*.psd1' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.BaseName -eq $moduleName } |
        Sort-Object -Property @(
            @{ Expression = 'LastWriteTimeUtc'; Descending = $true },
            @{ Expression = 'FullName'; Descending = $true }
        ) |
        Select-Object -First 1
    if ($artefactModule) {
        $artefactRoot = Split-Path -Path $artefactModule.FullName -Parent
        if (Test-ModulePayloadUsableForCurrentHost -ModuleRoot $artefactRoot) {
            return $artefactRoot
        }
    }

    if (Test-ModulePayloadUsableForCurrentHost -ModuleRoot $SourceRoot) {
        return $SourceRoot
    }

    if ($env:PSPUBLISHMODULE_TEST_ALLOW_INSTALLED_FALLBACK -eq '1') {
        $installedModule = Get-Module -ListAvailable -Name $moduleName |
            Sort-Object Version -Descending |
            Where-Object { $_.ModuleBase -and $_.ModuleBase -ne $SourceRoot } |
            Select-Object -First 1
        if ($installedModule) {
            Write-Warning "Falling back to installed module payload for tests: $($installedModule.ModuleBase)"
            return $installedModule.ModuleBase
        }
    }

    throw "No usable module payload found for $moduleName on PowerShell edition '$PSEdition'. Build the module first so the host-compatible payload exists."
}

function Get-ResolvedModuleManifestPath {
    param(
        [Parameter(Mandatory)][string] $ModuleRoot
    )

    $manifestPath = Get-ChildItem -Path $ModuleRoot -Filter '*.psd1' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $manifestPath) {
        throw "Path $ModuleRoot doesn't contain a PSD1 file. Failing tests."
    }

    return $manifestPath.FullName
}

$PayloadRoot = Get-ModulePayloadRootForTests -SourceRoot $script:SourceRoot
$ModuleRoot = $PayloadRoot
$PrimaryModulePath = Get-ResolvedModuleManifestPath -ModuleRoot $ModuleRoot
$PrimaryModule = Get-Item -LiteralPath $PrimaryModulePath

$ModuleName = $PrimaryModule.BaseName
$env:PSPUBLISHMODULE_TEST_MANIFEST_PATH = $PrimaryModule.FullName
$env:PSPUBLISHMODULE_TEST_MODULE_ROOT = $ModuleRoot
$env:PSPUBLISHMODULE_TEST_SOURCE_ROOT = $script:SourceRoot
$PSDInformation = Import-PowerShellDataFile -Path $PrimaryModule.FullName
$RequiredModules = @(
    'Pester'
    'PSWriteColor'
    if ($PSDInformation.RequiredModules) {
        $PSDInformation.RequiredModules
    }
)
foreach ($Module in $RequiredModules) {
    if ($Module -is [System.Collections.IDictionary]) {
        $Exists = Get-Module -ListAvailable -Name $Module.ModuleName
        if (-not $Exists) {
            Write-Warning "$ModuleName - Downloading $($Module.ModuleName) from PSGallery"
            Install-Module -Name $Module.ModuleName -Force -SkipPublisherCheck
        }
    } else {
        $Exists = Get-Module -ListAvailable $Module -ErrorAction SilentlyContinue
        if (-not $Exists) {
            Install-Module -Name $Module -Force -SkipPublisherCheck
        }
    }
}

Write-Color 'ModuleName: ', $ModuleName, ' Version: ', $PSDInformation.ModuleVersion -Color Yellow, Green, Yellow, Green -LinesBefore 2
Write-Color 'PowerShell Version: ', $PSVersionTable.PSVersion -Color Yellow, Green
Write-Color 'PowerShell Edition: ', $PSVersionTable.PSEdition -Color Yellow, Green
Write-Color 'Module payload root: ', $PayloadRoot -Color Yellow, Green
Write-Color 'Required modules: ' -Color Yellow
foreach ($Module in $PSDInformation.RequiredModules) {
    if ($Module -is [System.Collections.IDictionary]) {
        Write-Color '   [>] ', $Module.ModuleName, ' Version: ', $Module.ModuleVersion -Color Yellow, Green, Yellow, Green
    } else {
        Write-Color '   [>] ', $Module -Color Yellow, Green
    }
}

try {
    try {
        Import-Module -Name $PrimaryModule.FullName -Force -ErrorAction Stop
    } catch {
        Write-Color 'Failed to import module', $_.Exception.Message -Color Red
        exit 1
    }

    Write-Color 'Running tests...' -Color Yellow
    Write-Color

    $testsRoot = Join-Path $script:SourceRoot 'Tests'
    $isolatedTestPath = Join-Path $testsRoot 'PrivateGallery.Commands.Tests.ps1'
    $sharedTests = Get-ChildItem -Path $testsRoot -Filter '*.Tests.ps1' -File -ErrorAction Stop |
        Where-Object { $_.FullName -ne $isolatedTestPath } |
        Sort-Object FullName

    $result = Invoke-Pester -Script $sharedTests.FullName -Verbose -PassThru

    if ($result.FailedCount -gt 0) {
        throw "$($result.FailedCount) tests failed."
    }

    if (Test-Path -LiteralPath $isolatedTestPath) {
        $currentShellPath = (Get-Process -Id $PID).Path
        $isolatedRunnerPath = Join-Path ([System.IO.Path]::GetTempPath()) ("{0}.ps1" -f [System.IO.Path]::GetRandomFileName())

        @'
param(
    [Parameter(Mandatory)]
    [string] $TestPath
)

$result = Invoke-Pester -Script $TestPath -Verbose -PassThru
if ($result.FailedCount -gt 0) {
    throw "$($result.FailedCount) tests failed."
}
'@ | Set-Content -LiteralPath $isolatedRunnerPath -Encoding UTF8

        try {
            & $currentShellPath -NoLogo -NoProfile -File $isolatedRunnerPath -TestPath $isolatedTestPath
            if ($LASTEXITCODE -ne 0) {
                throw "Isolated test run failed for $isolatedTestPath with exit code $LASTEXITCODE."
            }
        } finally {
            Remove-Item -LiteralPath $isolatedRunnerPath -Force -ErrorAction SilentlyContinue
        }
    }
} finally {
    Remove-Item Env:PSPUBLISHMODULE_TEST_MANIFEST_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:PSPUBLISHMODULE_TEST_MODULE_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:PSPUBLISHMODULE_TEST_SOURCE_ROOT -ErrorAction SilentlyContinue
}
