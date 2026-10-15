#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $EvidencePath,
    [ValidateSet('win-x64', 'linux-x64')] [string] $RuntimeIdentifier,
    [ValidateRange(1, 32)] [int] $MaxParallelThreads = 4,
    [ValidateSet('Fast', 'Full')] [string] $Lane = 'Fast',
    [ValidateSet('Enforce', 'RecordOnly')] [string] $PerformancePolicy = 'Enforce',
    [string] $ArtifactsPath,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture
$processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
$hostRuntimeIdentifier = if ($IsWindows) { 'win-x64' } elseif ($IsLinux) { 'linux-x64' } else { $null }
if (-not $hostRuntimeIdentifier -or
    $osArchitecture -ne [Runtime.InteropServices.Architecture]::X64 -or
    $processArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
    throw "Compiler qualification supports only Windows x64 and Linux x64 hosts running an x64 PowerShell process. Current OS: $([Runtime.InteropServices.RuntimeInformation]::OSDescription); OS architecture: $osArchitecture; process architecture: $processArchitecture."
}
if (-not $PSBoundParameters.ContainsKey('RuntimeIdentifier')) {
    $RuntimeIdentifier = $hostRuntimeIdentifier
}
if ($RuntimeIdentifier -ne $hostRuntimeIdentifier) {
    throw "Compiler qualification runtime '$RuntimeIdentifier' does not match the current host '$hostRuntimeIdentifier' (PowerShell process architecture: $processArchitecture). Use the matching runtime or omit -RuntimeIdentifier."
}
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$previousCliPath = $env:POWERFORGE_TEST_CLI_ASSEMBLY_PATH
$cliAssemblyPath = if ($PSBoundParameters.ContainsKey('ArtifactsPath')) {
    if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) { throw '-ArtifactsPath must identify the SDK artifacts root.' }
    $ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
    Join-Path $ArtifactsPath 'bin/PowerForge.Cli/release_net10.0/PowerForge.Cli.dll'
} elseif ($previousCliPath) { $previousCliPath }
else { Join-Path $repositoryRoot 'PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll' }
if (($NoBuild -and $ArtifactsPath) -or (-not $ArtifactsPath -and $previousCliPath)) {
    if (-not [IO.Path]::IsPathFullyQualified($cliAssemblyPath) -or
        [IO.Path]::GetFileName($cliAssemblyPath) -cne 'PowerForge.Cli.dll' -or
        -not (Test-Path -LiteralPath $cliAssemblyPath -PathType Leaf)) {
        throw "The selected prebuilt test CLI must be an absolute existing PowerForge.Cli.dll path: $cliAssemblyPath"
    }
}
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
New-Item -ItemType Directory -Path $EvidencePath -Force | Out-Null
$requiredFamilies = @{
    'project removed-target recovery' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_OfflineRestoreAuthenticatesRemovedTargetsBeforeRequalifyingCurrentSelection*'
    'empty executable entry' = 'PowerForge.Tests.PowerShellCompilationArtifactHardeningTests.Build_EmptyExecutableEntryPointExitsWithoutOutput*'
    'corpus performance disposition' = 'PowerForge.Tests.PowerShellCompilationArtifactHardeningTests.PublicCorpusPerformancePolicyRecordsOnlyKnownTimingBudgets*'
    'final emitted unit ownership' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.FailureMap*'
    'bound semantics' = 'PowerForge.Tests.PowerShellCompilationBoundPipelineTests.*'
    'census disposition' = 'PowerForge.Tests.PowerShellCompilationCensusTests.*'
    'supported arithmetic fuzz' = 'PowerForge.Tests.PowerShellCompilationFuzzTests.*'
    'stable vector output' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.CompleteWorkflow_PinnedStableVectorCapturesPreservePowerShellOutput*'
    'ABI compatibility' = 'PowerForge.Tests.PowerShellCompilationAbiCompatibilityTests.*'
    'package publication' = 'PowerForge.Tests.PowerShellCompilationPackagePublicationTests.*'
    'library packaging' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.LibraryPackage_*'
    'provider consumption' = 'PowerForge.Tests.PowerShellCompilationProviderPackageTests.ArtifactBuildRequiresReviewedProviderLockAndExecutesAdapter*'
    'provider lifecycle' = 'PowerForge.Tests.PowerShellCompilationProviderPackageTests.ExecutableProviderMatrixRoutesValuesCardinalityStreamsAndErrors*'
    'provider dependency closure' = 'PowerForge.Tests.PowerShellCompilationProviderPackageTests.ExecutableProviderCarriesAndInvokesItsLockedManagedDependencyClosure*'
    'project NuGet CLI' = 'PowerForge.Tests.PowerForgeCliPowerShellCompilationTests.ProjectPackCli_*'
    'project grouped diagnostics' = 'PowerForge.Tests.PowerShellCompilationProjectDiagnosticsTests.*'
    'project diagnostics CLI' = 'PowerForge.Tests.PowerForgeCliPowerShellCompilationTests.ProjectDiagnosticsCli_ShowsGroupedShapingCallsAndCorrectFailureStatus*'
    'project module lifecycle' = 'PowerForge.Tests.PowerShellCompilationProjectWorkflowTests.ProjectWorkflow_UsesReviewedLocksOfflineEnvironmentAndQualifiedPackage*'
    'project executable installation' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_ExecutablePackagePreservesQualifiedPermissionsAndRunsAfterInstall*'
    'hosted lifecycle metadata admission' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Plan_HostedLifecyclePromotesOnlyRepresentableValidationMetadata*'
    'hosted lifecycle validation compatibility' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Build_HybridLifecyclePreservesUnrepresentedValidationMetadata*'
    'conditional declaration execution' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.ConditionalDeclarations_ArtifactPreservesSelectedBodyReplacementAndNativeMetadata*'
    'retained executable console presentation' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Build_PackageConsolePreservesFormattingStreamsAndAuthoredErrors*'
}
$windowsRequiredFamilies = @{
    'native Hybrid executable console presentation' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Build_NativeHybridConsolePreservesFormattingStreamsAndAuthoredErrors*'
    'project Windows launch path' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_ExecutableInstallRejectsUnlaunchablePathBeforeCreatingInstallation*'
    'project run' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_RunRebuildsEditedSource*'
    'project launch integrity' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_RunRejectsArtifactReplacementAtLaunchBoundary*'
    'project watch lifecycle' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_WatchStopsRunningApplication*'
    'project CLI streams' = 'PowerForge.Tests.PowerForgeCliPowerShellCompilationTests.ProjectRunCli_*'
    'project NuGet consumption' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_NuGetPackRebuildsLockedLibraryAndRunsOrdinaryConsumer*'
    'project runtime-pack diagnostics CLI' = 'PowerForge.Tests.PowerForgeCliPowerShellCompilationTests.ProjectDiagnosticsCli_UsesAcquiredRuntimePackWithEmptyGlobalCache*'
    'native owned declarations' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Build_HybridNativeScriptRootPreservesOwnedFunctionDeclarations*'
    'project offline recovery integrity' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_OfflineRestoreRejectsTamperedHistoricalEnvironmentAfterInvocationEdit*'
    'project declared test invocation' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_TestAndInstallUseDeclaredArgumentsAndWorkingDirectoryWithRevisionBoundEvidence*'
    'project clean legacy import errors' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_CleanLegacyModuleImportCannotAcceptDeepPayloadErrors*'
    'project legacy payload path' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_LegacyModuleInstallRejectsDeepPayloadBeforeCreatingInstallation*'
    'native source load timing' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Project_NativeScriptClosureLoadsAtAuthoredPointWithArgumentsAndWorkingDirectory*'
    'native source operand integrity' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Build_NativeScriptClosureRejectsLiveJoinPathReplacementToExternalOrWrongEmbeddedFile*'
    'native source byte integrity' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Build_NativeScriptClosureRejectsHelperMutationAtAuthoredLoadPoint*'
    'native declaration timing' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Shape_GuardedExecutableDeclarationsRemainCompiledWithoutStartupCommandRegistration*'
    'native declaration closure' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Shape_ExecutableTimingQualificationDoesNotAdmitOtherSourceInitialization*'
    'native command region execution' = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.Build_HybridNativeScriptRootPreservesGenericCommandRegionsAndCallerBoundary*'
}
$excludedRequirements = @()
if ($IsWindows) {
    foreach ($family in $windowsRequiredFamilies.GetEnumerator()) {
        $requiredFamilies[$family.Key] = $family.Value
    }
} else {
    $excludedRequirements = @($windowsRequiredFamilies.GetEnumerator() | Sort-Object Key | ForEach-Object {
        @{
            family = $_.Key
            testPattern = $_.Value
            reason = 'The current workflow fixture requires Windows; no execution qualification is claimed on this host.'
        }
    })
}
@{
    runtimeIdentifier = $RuntimeIdentifier
    hostPlatform = if ($IsWindows) { 'Windows' } elseif ($IsLinux) { 'Linux' } else { 'Other' }
    powerShellVersion = $PSVersionTable.PSVersion.ToString()
    windowsPowerShell51 = if ($IsWindows) { 'selected by numeric artifact tests' } else { 'unavailable; Windows workflow fixtures are excluded or explicitly skipped' }
    maxParallelThreads = $MaxParallelThreads
    lane = $Lane
    performancePolicy = $PerformancePolicy
    requiredFamilies = @($requiredFamilies.Keys | Sort-Object)
    excludedRequirements = $excludedRequirements
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidencePath 'host-capabilities.json') -Encoding utf8
$fastPatterns = @(
    'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.CompleteWorkflow_PinnedBinaryToStringPreservesBindingAndOutput'
) + @($requiredFamilies.Values | ForEach-Object { $_.TrimEnd('*') })
$fastFilter = ($fastPatterns | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
$filter = if ($Lane -eq 'Full') { 'Category=PowerShellCompilerGate|' + $fastFilter } else { $fastFilter }
$testArguments = @('test', (Join-Path $repositoryRoot 'PowerForge.Tests/PowerForge.Tests.csproj'),
    '-c', 'Release', '--filter', $filter, '--logger', 'console;verbosity=normal',
    '--logger', 'trx;LogFileName=compiler-gate.trx', '--results-directory', $EvidencePath)
if ($NoBuild) { $testArguments += @('--no-build', '--no-restore') }
if ($ArtifactsPath) { $testArguments += @('--artifacts-path', $ArtifactsPath) }
$testArguments += @('--', "xUnit.MaxParallelThreads=$MaxParallelThreads")
Push-Location $repositoryRoot
try {
    $env:POWERFORGE_TEST_CLI_ASSEMBLY_PATH = $cliAssemblyPath
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { throw "Compiler contract tests failed with exit $LASTEXITCODE." }
    [xml] $testResults = Get-Content -LiteralPath (Join-Path $EvidencePath 'compiler-gate.trx') -Raw
    if ([int] $testResults.TestRun.ResultSummary.Counters.executed -eq 0) { throw 'The compiler gate did not execute any tests.' }
    # Require actual passing coverage for this host's artifact/API and development families in both lanes.
    $passedNames = @($testResults.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Passed' | ForEach-Object testName)
    foreach ($family in $requiredFamilies.GetEnumerator()) {
        if (-not ($passedNames -like $family.Value)) { throw "The compiler gate is missing passing $($family.Key) tests." }
    }
    $moduleWorkflow = 'PowerForge.Tests.PowerShellCompilationArtifactBuilderTests.CompleteWorkflow_PinnedBinaryToStringPreservesBindingAndOutput*'
    if ($IsWindows) {
        foreach ($pattern in @($moduleWorkflow, $requiredFamilies['conditional declaration execution'], $requiredFamilies['project module lifecycle'])) {
            foreach ($framework in @('net10.0', 'net472')) {
                $hostCases = @($passedNames | Where-Object { $_ -like $pattern -and $_ -like "*framework: `"$framework`"*" })
                if ($hostCases.Count -eq 0) { throw "The compiler gate is missing a passing $framework generated workflow: $pattern" }
            }
        }
    } elseif (-not ($passedNames -like $moduleWorkflow)) {
        throw 'The compiler gate is missing a passing generated module workflow.'
    }
    & (Join-Path $repositoryRoot 'Benchmarks/PowerShellCompilation/Corpus/Invoke-PublicCorpus.ps1') `
        -SkipModules -RuntimeIdentifier $RuntimeIdentifier -CliAssemblyPath $cliAssemblyPath -PerformancePolicy $PerformancePolicy `
        -WorkspacePath (Join-Path $EvidencePath 'corpus-workspace') `
        -EvidencePath (Join-Path $EvidencePath 'strict-corpus.json')
    if ($LASTEXITCODE -ne 0) { throw "Strict compiler corpus failed with exit $LASTEXITCODE." }
} finally {
    $env:POWERFORGE_TEST_CLI_ASSEMBLY_PATH = $previousCliPath
    Pop-Location
}
