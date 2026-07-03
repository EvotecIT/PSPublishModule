param(
    [Parameter(Mandatory = $true)] [string] $RequestPath
)

$ErrorActionPreference = 'Stop'
$request = [System.IO.File]::ReadAllText($RequestPath) | ConvertFrom-Json
Set-Location -LiteralPath $request.WorkingDirectory
[System.Environment]::CurrentDirectory = (Get-Location).ProviderPath
foreach ($modulePath in @($request.ModulePaths)) {
    if (-not [string]::IsNullOrWhiteSpace($modulePath) -and [System.IO.File]::Exists($modulePath)) {
        Import-Module -Name $modulePath -Force -ErrorAction SilentlyContinue
    }
}
function Add-PowerForgeBenchmarkAssembly {
    param([string] $Path)

    try {
        Add-Type -Path $Path -ErrorAction Stop
    } catch [System.Reflection.ReflectionTypeLoadException] {
        $messages = @($_.Exception.LoaderExceptions | ForEach-Object { $_.Message })
        throw "Failed to load benchmark assembly '$Path'. Loader exceptions: $($messages -join '; ')"
    }
}
if ($null -eq ('PowerForge.PowerShellBenchmarkDslRuntime' -as [type])) {
    Add-PowerForgeBenchmarkAssembly -Path $request.PowerForgeAssemblyPath
    Add-PowerForgeBenchmarkAssembly -Path $request.PowerForgePowerShellAssemblyPath
}
$benchmarkDslAliases = @(
    'benchmark', 'cases', 'case', 'caseSource', 'from', 'axis', 'setup', 'data', 'skip', 'validate', 'policy', 'profile', 'cleanup', 'engine', 'operation', 'metric', 'comparison', 'readme', 'artifacts',
    'assertPath', 'assertValue'
)
foreach ($aliasName in $benchmarkDslAliases) {
    $alias = Get-Alias -Name $aliasName -ErrorAction SilentlyContinue
    if ($null -ne $alias -and $alias.Source -eq 'PSPublishModule') {
        Remove-Item -LiteralPath "Alias:$aliasName" -Force -ErrorAction SilentlyContinue
    }
}
$scriptRoot = [System.IO.Path]::GetDirectoryName($request.SpecPath)
$block = [scriptblock]::Create([System.IO.File]::ReadAllText($request.SpecPath))
$benchmarkVariables = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::OrdinalIgnoreCase)
if ($null -ne $request.BenchmarkVariables) {
    foreach ($property in @($request.BenchmarkVariables.PSObject.Properties)) {
        $benchmarkVariables[$property.Name] = [string] $property.Value
    }
}
$suites = [PowerForge.PowerShellBenchmarkDslRuntime]::Evaluate($block, $scriptRoot, $benchmarkVariables)
if ($request.SuiteIndex -ge $suites.Length) {
    throw "Benchmark spec '$($request.SpecPath)' did not produce suite index $($request.SuiteIndex)."
}
$suite = $suites[$request.SuiteIndex]
if (-not [string]::IsNullOrWhiteSpace($request.PlanningProfile)) {
    $suite.PlanningProfile = [PowerForge.PowerShellBenchmarkProfileKind] $request.PlanningProfile
}
$suite.Profile = [PowerForge.PowerShellBenchmarkProfileKind]::Current
$suite.OutputRoot = $request.OutputRoot
$suite.WarmupCount = [Math]::Max(0, [int]$request.WarmupCount)
$suite.IterationCount = [Math]::Max(1, [int]$request.IterationCount)
$suite.CooldownMilliseconds = [Math]::Max(0, [int]$request.CooldownMilliseconds)
if (-not [string]::IsNullOrWhiteSpace($request.RunMode)) {
    $suite.RunMode = $request.RunMode
}
if (-not [string]::IsNullOrWhiteSpace($request.RunOrder)) {
    $suite.RunOrder = [PowerForge.PowerShellBenchmarkRunOrder] $request.RunOrder
}
if (-not [string]::IsNullOrWhiteSpace($request.OutlierMode)) {
    $suite.OutlierMode = [PowerForge.PowerShellBenchmarkOutlierMode] $request.OutlierMode
}
if (-not [string]::IsNullOrWhiteSpace($request.SuiteName)) {
    $suite.Name = $request.SuiteName
}
if ($null -ne $request.Selection) {
    $selection = [PowerForge.PowerShellBenchmarkSelection]::new()
    $selection.Cases = @($request.Selection.Cases)
    $selection.Engines = @($request.Selection.Engines)
    $selection.Operations = @($request.Selection.Operations)
    $selection.Hosts = @($request.Selection.Hosts)
    [PowerForge.PowerShellBenchmarkSuiteFilter]::Apply($suite, $selection)
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
if ($request.PSObject.Properties.Name -contains 'UpdateReadmeBlocks' -and -not $request.UpdateReadmeBlocks) {
    $suite.ReadmeBlocks.Clear()
}
try {
    $result = [PowerForge.PowerShellBenchmarkRunner]::new().Run($suite)
    [PowerForge.BenchmarkJson]::Write($request.ResultPath, $result)
} catch {
    $runStartedUtc = [DateTimeOffset]::Parse([string] $request.RunStartedUtc, [System.Globalization.CultureInfo]::InvariantCulture)
    [PowerForge.PowerShellBenchmarkTemporaryUserExecutor]::TryCopyLatestRunReport($request.OutputRoot, $request.ResultPath, $runStartedUtc) | Out-Null
    throw
}
