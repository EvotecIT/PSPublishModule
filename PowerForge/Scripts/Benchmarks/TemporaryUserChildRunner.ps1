param(
    [Parameter(Mandatory = $true)] [string] $RequestPath
)

$ErrorActionPreference = 'Stop'
$request = [System.IO.File]::ReadAllText($RequestPath) | ConvertFrom-Json
Set-Location -LiteralPath $request.WorkingDirectory
[System.Environment]::CurrentDirectory = (Get-Location).ProviderPath
Add-Type -Path $request.PowerForgeAssemblyPath
Add-Type -Path $request.PowerForgePowerShellAssemblyPath
foreach ($modulePath in @($request.ModulePaths)) {
    if (-not [string]::IsNullOrWhiteSpace($modulePath) -and [System.IO.File]::Exists($modulePath)) {
        Import-Module -Name $modulePath -Force -ErrorAction Stop
    }
}
$scriptRoot = [System.IO.Path]::GetDirectoryName($request.SpecPath)
$block = [scriptblock]::Create([System.IO.File]::ReadAllText($request.SpecPath))
$suites = [PowerForge.PowerShellBenchmarkDslRuntime]::Evaluate($block, $scriptRoot)
if ($request.SuiteIndex -ge $suites.Length) {
    throw "Benchmark spec '$($request.SpecPath)' did not produce suite index $($request.SuiteIndex)."
}
$suite = $suites[$request.SuiteIndex]
$suite.Profile = [PowerForge.PowerShellBenchmarkProfileKind]::Current
$suite.OutputRoot = $request.OutputRoot
$suite.WarmupCount = [Math]::Max(0, [int]$request.WarmupCount)
$suite.IterationCount = [Math]::Max(1, [int]$request.IterationCount)
if (-not [string]::IsNullOrWhiteSpace($request.RunMode)) {
    $suite.RunMode = $request.RunMode
}
if (-not [string]::IsNullOrWhiteSpace($request.SuiteName)) {
    $suite.Name = $request.SuiteName
}
$readmePaths = @()
if ([System.IO.File]::Exists($request.ReadmePathFile)) {
    $readmePaths = @([System.IO.File]::ReadAllLines($request.ReadmePathFile))
}
for ($index = 0; $index -lt $suite.ReadmeBlocks.Count -and $index -lt $readmePaths.Count; $index++) {
    if (-not [string]::IsNullOrWhiteSpace($readmePaths[$index])) {
        $suite.ReadmeBlocks[$index].Path = $readmePaths[$index]
    }
}
$result = [PowerForge.PowerShellBenchmarkRunner]::new().Run($suite)
[PowerForge.BenchmarkJson]::Write($request.ResultPath, $result)
