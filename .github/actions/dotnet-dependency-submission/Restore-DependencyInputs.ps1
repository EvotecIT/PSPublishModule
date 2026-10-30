#Requires -Version 7.0
<#
.SYNOPSIS
Restores explicit .NET dependency graphs after installing required workloads.
.DESCRIPTION
Validates repository-relative inputs before invoking dotnet. A failed workload
installation or restore stops the operation; partial graphs are not submitted.
.PARAMETER ProjectsJson
JSON array of solution or project paths relative to the working directory.
.PARAMETER Workloads
Optional workload identifiers separated by whitespace.
.PARAMETER WorkloadVersion
Optional workload-set version to install instead of the bundled manifests.
.EXAMPLE
./Restore-DependencyInputs.ps1 -ProjectsJson '["Product.sln"]' -Workloads 'wasm-tools'
.OUTPUTS
Writes restore progress and the resulting asset-file count to the console.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ProjectsJson,
    [AllowEmptyString()][string] $Workloads = '',
    [AllowEmptyString()][string] $WorkloadVersion = ''
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = (Get-Location).ProviderPath
$prefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$parsed = ConvertFrom-Json -InputObject $ProjectsJson -NoEnumerate
if ($parsed -isnot [array] -or $parsed.Count -eq 0) {
    throw 'projects-json must be a nonempty JSON array of solution or project paths.'
}
$projects = foreach ($project in $parsed) {
    if ($project -isnot [string] -or [string]::IsNullOrWhiteSpace($project) -or [IO.Path]::IsPathRooted($project)) {
        throw 'Each dependency input must be an explicit repository-relative file path.'
    }
    $path = [IO.Path]::GetFullPath((Join-Path $root $project))
    if (!$path.StartsWith($prefix, $comparison) -or !(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Dependency input is missing or outside the repository: $project"
    }
    if ([IO.Path]::GetExtension($path) -notin @('.sln', '.slnx', '.csproj', '.fsproj', '.vbproj', '.vcxproj')) {
        throw "Unsupported dependency input: $project"
    }
    $path
}
$workloadIds = @($Workloads -split '\s+' | Where-Object { $_ })
if (@($workloadIds | Where-Object { $_ -cnotmatch '^[a-z][a-z0-9-]*$' }).Count) {
    throw 'Workload identifiers must contain only lowercase letters, digits and hyphens.'
}
if ($WorkloadVersion -and ($WorkloadVersion -cnotmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$' -or !$workloadIds.Count)) {
    throw 'workload-version requires workload identifiers and a valid workload-set version.'
}
if ($workloadIds.Count) {
    if ($WorkloadVersion) {
        dotnet workload install @workloadIds --version $WorkloadVersion
    } else {
        dotnet workload install @workloadIds --skip-manifest-update
    }
    if ($LASTEXITCODE -ne 0) { throw "Workload installation failed with exit code $LASTEXITCODE." }
}
foreach ($project in @($projects | Select-Object -Unique)) {
    dotnet restore $project
    if ($LASTEXITCODE -ne 0) { throw "Dependency restore failed for $project with exit code $LASTEXITCODE." }
}
$assets = @(Get-ChildItem -LiteralPath $root -Filter project.assets.json -File -Recurse)
if ($assets.Count -eq 0) {
    throw 'Restore completed without producing any NuGet asset graphs.'
}
Write-Host "Restored $(@($projects).Count) explicit inputs; found $($assets.Count) NuGet asset graphs."
