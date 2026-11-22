[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [hashtable] $Variable,
    [string[]] $Case,
    [string[]] $Engine,
    [int] $WarmupCount,
    [int] $IterationCount,
    [switch] $Plan
)

if ([string]::IsNullOrWhiteSpace($Variable.WorkRoot)) {
    throw 'Supply WorkRoot for temporary benchmark files.'
}

$workRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath([string] $Variable.WorkRoot)
$runRoot = Join-Path $workRoot ('module-cli-' + [guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $runRoot) {
    throw "Benchmark scratch directory already exists: $runRoot"
}

$inputs = $Variable.Clone()
$inputs.WorkRoot = $runRoot
if ($inputs.PSResourceGetPath) {
    $inputs.PSResourceGetWorkerPath = Join-Path $PSScriptRoot 'Invoke-ModuleCliPSResourceGet.ps1'
}
if ($Engine) {
    foreach ($candidate in @('PowerForge', 'PSResourceGet', 'ModuleFast')) {
        if ($candidate -in $Engine) { $inputs.ComparisonBaseline = $candidate; break }
    }
}
$parameters = @{
    Path = Join-Path $PSScriptRoot 'module-cli.benchmark.ps1'
    Variable = $inputs
}
foreach ($name in @('Case', 'Engine', 'WarmupCount', 'IterationCount', 'Plan')) {
    if ($PSBoundParameters.ContainsKey($name)) {
        $parameters[$name] = $PSBoundParameters[$name]
    }
}

try {
    if (-not $Plan -and $inputs.PSResourceGetPath -and (-not $Engine -or 'PSResourceGet' -in $Engine -or 'PowerForge' -in $Engine)) {
        $seed = Join-Path $runRoot 'psresourceget-seed'
        $data = Join-Path $seed 'data'
        $sandboxHome = Join-Path $seed 'home'
        $moduleRoot = Join-Path $sandboxHome '.local/share/powershell/Modules'
        $null = New-Item -ItemType Directory -Path $data, $sandboxHome
        $proof = Get-Content -LiteralPath $inputs.PackageProofPath -Raw | ConvertFrom-Json -AsHashtable
        $pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
        $seedArguments = @('-NoProfile', '-NonInteractive', '-File', $inputs.PSResourceGetWorkerPath,
            '-Operation', 'Prepare', '-ModulePath', $inputs.PSResourceGetPath, '-IsolatedHome', $sandboxHome,
            '-Destination', $moduleRoot, '-Source', $inputs.Source, '-Name', 'Pester', '-Version', $proof['Pester'].version)
        & /usr/bin/env -u PSModulePath "HOME=$sandboxHome" "XDG_DATA_HOME=$data" "XDG_CACHE_HOME=$seed/cache" "XDG_CONFIG_HOME=$seed/config" $pwsh @seedArguments
        if ($LASTEXITCODE -ne 0) { throw 'PSResourceGet benchmark fixture setup failed.' }
        $inputs.PSResourceGetRepositoryDirectory = Join-Path $data 'PSResourceGet'
        $inputs.PSResourceGetInstalledModules = $moduleRoot
    }
    Invoke-BenchmarkSuite @parameters
} finally {
    # Warmups skip validation. Keep the shared warm bundle until all lanes finish.
    if (Test-Path -LiteralPath $runRoot -PathType Container) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force -ErrorAction Stop
    }
}
