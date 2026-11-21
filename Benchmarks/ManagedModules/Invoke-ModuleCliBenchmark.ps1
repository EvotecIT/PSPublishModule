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
    Invoke-BenchmarkSuite @parameters
} finally {
    # Warmups skip validation. Keep the shared warm bundle until all lanes finish.
    if (Test-Path -LiteralPath $runRoot -PathType Container) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force -ErrorAction Stop
    }
}
