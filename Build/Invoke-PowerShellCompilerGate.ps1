#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $EvidencePath,
    [ValidateSet('win-x64', 'linux-x64')] [string] $RuntimeIdentifier = 'win-x64',
    [ValidateRange(1, 32)] [int] $MaxParallelThreads = 4,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Path $EvidencePath -Force | Out-Null
@{
    runtimeIdentifier = $RuntimeIdentifier
    powerShellVersion = $PSVersionTable.PSVersion.ToString()
    windowsPowerShell51 = if ($IsWindows) { 'selected by numeric artifact tests' } else { 'unavailable; net472 numeric artifact cases not selected' }
    maxParallelThreads = $MaxParallelThreads
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidencePath 'host-capabilities.json') -Encoding utf8
$filter = 'Category=PowerShellCompilerGate|FullyQualifiedName~PowerShellCompilationCensusTests|FullyQualifiedName~PowerShellCompilationBoundPipelineTests|FullyQualifiedName~PowerShellCompilationFuzzTests'
$testArguments = @('test', (Join-Path $repositoryRoot 'PowerForge.Tests/PowerForge.Tests.csproj'),
    '-c', 'Release', '--filter', $filter, '--logger', 'trx;LogFileName=compiler-gate.trx', '--results-directory', $EvidencePath)
if ($NoBuild) { $testArguments += @('--no-build', '--no-restore') }
$testArguments += @('--', "xUnit.MaxParallelThreads=$MaxParallelThreads")
Push-Location $repositoryRoot
try {
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Compiler contract tests failed with exit $LASTEXITCODE." }
    [xml] $testResults = Get-Content -LiteralPath (Join-Path $EvidencePath 'compiler-gate.trx') -Raw
    if ([int] $testResults.TestRun.ResultSummary.Counters.executed -eq 0) { throw 'The compiler gate did not execute any tests.' }
    # These artifact/API and development families are required compiler evidence. An accidentally removed
    # category must fail the gate even when unrelated compiler tests still pass.
    $requiredFamilies = @{
        'ABI compatibility' = 'PowerForge.Tests.PowerShellCompilationAbiCompatibilityTests.*'
        'package publication' = 'PowerForge.Tests.PowerShellCompilationPackagePublicationTests.*'
        'library packaging' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.LibraryPackage_*'
        'provider consumption' = 'PowerForge.Tests.PowerShellCompilationProviderPackageTests.ArtifactBuildRequiresReviewedProviderLockAndExecutesAdapter*'
        'provider lifecycle' = 'PowerForge.Tests.PowerShellCompilationProviderPackageTests.ExecutableProviderMatrixRoutesValuesCardinalityStreamsAndErrors*'
        'provider dependency closure' = 'PowerForge.Tests.PowerShellCompilationProviderPackageTests.ExecutableProviderCarriesAndInvokesItsLockedManagedDependencyClosure*'
        'project run' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_RunRebuildsEditedSource*'
        'project watch lifecycle' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_WatchStopsRunningApplication*'
        'project CLI streams' = 'PowerForge.Tests.PowerForgeCliPowerShellCompilationTests.ProjectRunCli_*'
    }
    $passedNames = @($testResults.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Passed' | ForEach-Object testName)
    foreach ($family in $requiredFamilies.GetEnumerator()) {
        if (-not ($passedNames -like $family.Value)) { throw "The compiler gate is missing passing $($family.Key) tests." }
    }
    & (Join-Path $repositoryRoot 'Benchmarks/PowerShellCompilation/Corpus/Invoke-PublicCorpus.ps1') `
        -SkipModules -RuntimeIdentifier $RuntimeIdentifier `
        -WorkspacePath (Join-Path $EvidencePath 'corpus-workspace') `
        -EvidencePath (Join-Path $EvidencePath 'strict-corpus.json')
    if ($LASTEXITCODE -ne 0) { throw "Strict compiler corpus failed with exit $LASTEXITCODE." }
} finally { Pop-Location }
