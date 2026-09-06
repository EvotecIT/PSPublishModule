#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $EvidencePath,
    [ValidateSet('win-x64', 'linux-x64')] [string] $RuntimeIdentifier = 'win-x64',
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
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidencePath 'host-capabilities.json') -Encoding utf8
$filter = 'Category=PowerShellCompilerGate|FullyQualifiedName~PowerShellCompilationCensusTests|FullyQualifiedName~PowerShellCompilationBoundPipelineTests|FullyQualifiedName~PowerShellCompilationFuzzTests'
$testArguments = @('test', (Join-Path $repositoryRoot 'PowerForge.Tests/PowerForge.Tests.csproj'),
    '-c', 'Release', '--filter', $filter, '--logger', 'trx;LogFileName=compiler-gate.trx', '--results-directory', $EvidencePath)
if ($NoBuild) { $testArguments += @('--no-build', '--no-restore') }
Push-Location $repositoryRoot
try {
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Compiler contract tests failed with exit $LASTEXITCODE." }
    [xml] $testResults = Get-Content -LiteralPath (Join-Path $EvidencePath 'compiler-gate.trx') -Raw
    if ([int] $testResults.TestRun.ResultSummary.Counters.executed -eq 0) { throw 'The compiler gate did not execute any tests.' }
    & (Join-Path $repositoryRoot 'Benchmarks/PowerShellCompilation/Corpus/Invoke-PublicCorpus.ps1') `
        -SkipModules -RuntimeIdentifier $RuntimeIdentifier `
        -WorkspacePath (Join-Path $EvidencePath 'corpus-workspace') `
        -EvidencePath (Join-Path $EvidencePath 'strict-corpus.json')
    if ($LASTEXITCODE -ne 0) { throw "Strict compiler corpus failed with exit $LASTEXITCODE." }
} finally { Pop-Location }
