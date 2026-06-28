param(
    [Parameter(Mandatory)]
    [string] $ModuleName,

    [Parameter(Mandatory)]
    [string] $ModuleRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ManifestVersion {
    param([string] $Path)

    $text = Get-Content -LiteralPath $Path -Raw
    if ($text -match "ModuleVersion\s*=\s*['""]([^'""]+)['""]") {
        return $Matches[1]
    }

    $null
}

function Get-ImportModulePathEntries {
    param([string] $Root)

    $paths = @(
        $Root
        (Join-Path $Root 'Modules')
        (Join-Path $Root 'Home\Documents\PowerShell\Modules')
        (Join-Path $Root 'Home\Documents\WindowsPowerShell\Modules')
        (Join-Path $Root $ModuleName)
    )

    foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path) {
            [IO.Path]::GetFullPath($path)
        }
    }
}

$timer = [Diagnostics.Stopwatch]::StartNew()
$status = 'Succeeded'
$errorText = ''
$version = ''
$manifestPath = ''
$importedName = ''

try {
    $roots = @(Get-ImportModulePathEntries -Root $ModuleRoot | Select-Object -Unique)
    $separator = [IO.Path]::PathSeparator
    $currentModulePath = @($env:PSModulePath -split [Regex]::Escape($separator)) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $env:PSModulePath = (@($roots) + $currentModulePath | Select-Object -Unique) -join $separator

    $manifests = foreach ($root in $roots) {
        Get-ChildItem -LiteralPath $root -Filter "$ModuleName.psd1" -Recurse -File -ErrorAction SilentlyContinue
    }

    $selected = @(
        foreach ($manifest in $manifests) {
            $manifestVersion = Get-ManifestVersion -Path $manifest.FullName
            $parsedVersion = $null
            if ([version]::TryParse($manifestVersion, [ref] $parsedVersion)) {
                [pscustomobject]@{
                    Path = $manifest.FullName
                    Version = $manifestVersion
                    ParsedVersion = $parsedVersion
                }
            }
        }
    ) | Sort-Object ParsedVersion -Descending | Select-Object -First 1

    if (-not $selected) {
        throw "No importable manifest for '$ModuleName' was found under '$ModuleRoot'."
    }

    $manifestPath = $selected.Path
    $version = $selected.Version
    $imported = Import-Module -Name $manifestPath -Force -PassThru -ErrorAction Stop
    $importedName = if ($imported) { [string] $imported[0].Name } else { '' }
} catch {
    $status = 'Failed'
    $errorText = $_.Exception.Message
} finally {
    $timer.Stop()
}

[pscustomobject]@{
    Status = $status
    ModuleName = $ModuleName
    ImportedModuleName = $importedName
    Version = $version
    ManifestPath = $manifestPath
    ElapsedMilliseconds = [math]::Round($timer.Elapsed.TotalMilliseconds, 2)
    Error = $errorText
} | ConvertTo-Json -Depth 4 -Compress
