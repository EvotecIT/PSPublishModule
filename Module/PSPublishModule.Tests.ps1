$script:SourceRoot = $PSScriptRoot
$script:CopiedSourceLib = $false

function Get-ModulePayloadRootForTests {
    param(
        [Parameter(Mandatory)][string] $SourceRoot
    )

    $sourceManifest = Get-ChildItem -Path $SourceRoot -Filter '*.psd1' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $sourceManifest) {
        throw "Path $SourceRoot doesn't contain a PSD1 file. Failing tests."
    }

    $sourceLibRoot = Join-Path $SourceRoot 'Lib'
    $hasSourceLibraries = (Test-Path -LiteralPath $sourceLibRoot -PathType Container) -and
        (Get-ChildItem -Path $sourceLibRoot -Directory -ErrorAction SilentlyContinue | Select-Object -First 1)
    if ($hasSourceLibraries) {
        return $SourceRoot
    }

    $moduleName = $sourceManifest.BaseName
    $installedModule = Get-Module -ListAvailable -Name $moduleName |
        Sort-Object Version -Descending |
        Where-Object { $_.ModuleBase -and $_.ModuleBase -ne $SourceRoot } |
        Select-Object -First 1
    if ($installedModule) {
        return $installedModule.ModuleBase
    }

    $artefactModule = Get-ChildItem -Path (Join-Path $SourceRoot 'Artefacts\Unpacked') -Filter '*.psd1' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.BaseName -eq $moduleName } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($artefactModule) {
        return Split-Path -Path $artefactModule.FullName -Parent
    }

    return $SourceRoot
}

$PayloadRoot = Get-ModulePayloadRootForTests -SourceRoot $script:SourceRoot
$sourceLibRoot = Join-Path $script:SourceRoot 'Lib'
$payloadLibRoot = Join-Path $PayloadRoot 'Lib'

if (($PayloadRoot -ne $script:SourceRoot) -and (Test-Path -LiteralPath $payloadLibRoot -PathType Container) -and (-not (Test-Path -LiteralPath $sourceLibRoot))) {
    Copy-Item -Path $payloadLibRoot -Destination $sourceLibRoot -Recurse -Force
    $script:CopiedSourceLib = $true
}

$ModuleRoot = $script:SourceRoot
$PrimaryModule = Get-ChildItem -Path $ModuleRoot -Filter '*.psd1' -File -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $PrimaryModule) {
    throw "Path $ModuleRoot doesn't contain a PSD1 file. Failing tests."
}

$ModuleName = $PrimaryModule.BaseName
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
    if ($script:CopiedSourceLib -and (Test-Path -LiteralPath $sourceLibRoot)) {
        Remove-Item -LiteralPath $sourceLibRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
