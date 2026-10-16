[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ImportPath,
    [ValidateSet('Original','Built','Installed')][string] $Surface = 'Original',
    [Parameter(Mandatory)][string] $ScenarioRoot,
    [Parameter(Mandatory)][string] $ResultPath
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$SourceVariant = 'CorrectedOriginal'
$configuration = @{ PowerShellHost = (Get-Process -Id $PID).Path }
. (Join-Path $PSScriptRoot '../../Corpus.Runner.Common.ps1')
$ScenarioRoot = [IO.Path]::GetFullPath($ScenarioRoot)
if (-not [IO.Path]::IsPathFullyQualified($ScenarioRoot) -or -not (Test-Path -LiteralPath $ScenarioRoot -PathType Container)) { throw 'The harness must supply an existing owned scenario root.' }
$surfaceRoot = Join-Path $ScenarioRoot $Surface
if (Test-Path -LiteralPath $surfaceRoot) { throw 'Surface evidence already exists; use a fresh canonical run root.' }
New-Item -ItemType Directory -Path $surfaceRoot | Out-Null
$selectedInventoryPath = Join-Path $PSScriptRoot 'SourceIdentity.json'
$selectedInventory = Get-Content -LiteralPath $selectedInventoryPath -Raw | ConvertFrom-Json
if ($selectedInventory.SourceFiles.Count -ne 2) { throw 'The source identity must contain both complete application files.' }
foreach ($identity in $selectedInventory.SourceFiles) { if ($identity.Sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Every source identity requires a nonempty exact SHA256.' } }
$stagedBefore = @()
if ($Surface -eq 'Original') {
    $stagedBefore = @($selectedInventory.SourceFiles | ForEach-Object {
        $path = Join-Path (Split-Path -Parent $ImportPath) $_.Path
        $hash = (Get-FileHash -LiteralPath $path).Hash
        if ($hash -ne $_.Sha256) { throw 'The complete original application differs from its selected pinned bytes.' }
        [ordered]@{ path = $path; sha256 = $hash }
    })
}
$inputRoot = Join-Path $ScenarioRoot 'inputs'
$fixture = Get-Content -LiteralPath (Join-Path $ScenarioRoot 'fixture/state.json') -Raw | ConvertFrom-Json
$identity = $fixture.fixtureIdentity
$server = Get-Process -Id $identity.pid -ErrorAction Stop
if ($server.StartTime.ToUniversalTime() -ne ([datetime]$identity.startUtc).ToUniversalTime() -or $identity.address -ne '127.0.0.1' -or $identity.origin -cne ('http://127.0.0.1:' + $identity.port)) { throw 'The sealed fixture identity is not the live owned loopback process.' }
foreach ($input in $fixture.inputHashes) { if ((Get-FileHash -LiteralPath (Join-Path $ScenarioRoot $input.path)).Hash -ne $input.sha256) { throw 'A sealed fixture input changed.' } }
$origin = $identity.origin
$requestsPath = Join-Path $ScenarioRoot 'fixture/requests.jsonl'
$newSitemap = Join-Path $inputRoot 'site/sitemap.xml'
$emptySitemap = Join-Path $inputRoot 'same.xml'
$badSitemap = Join-Path $inputRoot 'malformed.xml'
$scenarios = [Collections.Generic.List[object]]::new()
function Get-ApplicationFiles([string] $root){
    @(Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName | ForEach-Object {
        $text=[IO.File]::ReadAllText($_.FullName)
        if($_.Extension -eq '.json' -and $_.Name -eq 'report.json'){
            try{$data=$text | ConvertFrom-Json -DateKind String; if($data.PSObject.Properties.Name -contains 'GeneratedAt'){$data.GeneratedAt='<timestamp>'; $text=$data | ConvertTo-Json -Depth 20 -Compress}}catch{}
        }
        [ordered]@{path=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/');content=$text}
    })
}
function ConvertTo-ApplicationStderrObservation([string] $Text,[string] $ScenarioId,[string] $OutputRoot){
    if($ScenarioId -ne 'output-refusal'){return $Text}
    # This case deliberately passes a directory as its JSON output. Only that
    # scenario-owned physical output root varies between artifact surfaces.
    # Keep the blocked-json suffix, authored source header/line and all error
    # rendering intact; source/input paths and other scenarios remain exact.
    return $Text.Replace($OutputRoot + [IO.Path]::DirectorySeparatorChar,
        '<scenario-output>' + [IO.Path]::DirectorySeparatorChar)
}
function Save-ApplicationTimeoutEvidence($ErrorRecord,[string] $ScenarioId,[string] $Phase){
    $timeout = $ErrorRecord.Exception
    while ($null -ne $timeout -and -not $timeout.Data.Contains('PowerForgeOwnedProcess')) { $timeout = $timeout.InnerException }
    if ($null -eq $timeout) { return }
    try {
        [ordered]@{schemaVersion=1;surface=$Surface;scenarioId=$ScenarioId;phase=$Phase;
            process=$timeout.Data['PowerForgeOwnedProcess'];fixtureIdentity=$identity;
            requestLogPath=$requestsPath;requests=@(if(Test-Path -LiteralPath $requestsPath -PathType Leaf){Get-Content -LiteralPath $requestsPath})} |
            ConvertTo-Json -Depth 8 | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($ResultPath,'timeout.json')) -Encoding utf8NoBOM
    } catch { Write-Verbose "Failed to retain timeout evidence beside '$ResultPath': $($_.Exception.Message)" }
}
function Invoke-ApplicationCase([string] $id,[string] $legacy,[string] $newSitemap,[string] $caseKind,[switch] $Amp,[switch] $PassThru,[switch] $NoAdditionalUrls){
    $caseRoot=Join-Path $surfaceRoot $id; $outputs=Join-Path $caseRoot 'outputs'
    New-Item -ItemType Directory -Path $outputs -Force | Out-Null
    $parameters=[ordered]@{LegacySitemapUrls=$legacy;NewSitemapPath=$newSitemap;OutputJsonPath=(Join-Path $outputs 'report.json');OutputCsvPath=(Join-Path $outputs 'missing.csv');OutputRedirectCsvPath=(Join-Path $outputs 'redirects.csv');OutputReviewCsvPath=(Join-Path $outputs 'review.csv');TimeoutSec=3;MaxSitemapDepth=4}
    if($Amp){if(-not $NoAdditionalUrls){$parameters.AdditionalLegacyUrls=$origin+'/hub/scripts/sample/'};$parameters.DiscoverAmpHtml=$true;$parameters.IncludeAmpListingRoots=$true}
    if($PassThru){$parameters.PassThru=$true}
    if($caseKind -eq 'Depth'){$parameters.MaxSitemapDepth=0}
    if($caseKind -ne 'Positive'){
        foreach($name in @('report.json','missing.csv','redirects.csv','review.csv')){[IO.File]::WriteAllText((Join-Path $outputs $name),'sentinel:'+ $name)}
    }
    if($caseKind -eq 'Output'){
        $parameters.OutputJsonPath=Join-Path $outputs 'blocked-json'
        New-Item -ItemType Directory -Path $parameters.OutputJsonPath | Out-Null
        [IO.File]::WriteAllText((Join-Path $parameters.OutputJsonPath 'sentinel.txt'),'owned-directory')
    }
    $before=Get-ApplicationFiles $outputs
    $arguments=[Collections.Generic.List[string]]::new()
    if($Surface -eq 'Original'){foreach($token in @('-NoLogo','-NoProfile','-NonInteractive','-File',$ImportPath)){$arguments.Add($token)};$hostPath=$configuration.PowerShellHost}else{$hostPath=$ImportPath}
    foreach($pair in $parameters.GetEnumerator()){$arguments.Add('-'+$pair.Key);if($pair.Value -isnot [bool]){$arguments.Add([string]$pair.Value)}}
    try {
        $run=Invoke-OwnedProcess -FileName $hostPath -Arguments $arguments.ToArray() -WorkingDirectory $ScenarioRoot -Environment @{NO_PROXY='127.0.0.1,localhost,::1';NO_COLOR='1'} -TimeoutSeconds 30
    } catch {
        Save-ApplicationTimeoutEvidence $_ $id 'Application'
        throw
    }
    [IO.File]::WriteAllText((Join-Path $caseRoot 'stdout.txt'),$run.StandardOutput,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $caseRoot 'stderr.txt'),$run.StandardError,[Text.UTF8Encoding]::new($false))
    $rawProcess=[ordered]@{exitCode=$run.ExitCode;stdout=$run.StandardOutput;stderr=$run.StandardError;stdoutSha256=(Get-FileHash -LiteralPath (Join-Path $caseRoot 'stdout.txt')).Hash;stderrSha256=(Get-FileHash -LiteralPath (Join-Path $caseRoot 'stderr.txt')).Hash}
    $rawProcess | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $caseRoot 'raw-process.json') -Encoding utf8NoBOM
    $after=Get-ApplicationFiles $outputs
    $rawFiles=@(Get-ChildItem -LiteralPath $outputs -File -Recurse | Sort-Object FullName | ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($outputs,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash;bytes=$_.Length}})
    $rawFiles | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $caseRoot 'raw-files.json') -Encoding utf8NoBOM
    $normalizedStdout=$run.StandardOutput
    $report=$null
    if($caseKind -eq 'Positive' -and(Test-Path -LiteralPath $parameters.OutputJsonPath -PathType Leaf)){
        $report=Get-Content -LiteralPath $parameters.OutputJsonPath -Raw | ConvertFrom-Json -DateKind String
        if($report.GeneratedAt -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$'){throw 'Unexpected authored GeneratedAt format.'}
        $normalizedStdout=$normalizedStdout.Replace([string]$report.GeneratedAt,'<timestamp>')
    }
    $expectedSuccess=$caseKind -eq 'Positive'
    $preserved=($before | ConvertTo-Json -Depth 5 -Compress) -ceq ($after | ConvertTo-Json -Depth 5 -Compress)
    $succeeded=if($expectedSuccess){$run.ExitCode -eq 0 -and [string]::IsNullOrEmpty($run.StandardError) -and $null -ne $report}else{$run.ExitCode -ne 0 -and -not [string]::IsNullOrWhiteSpace($run.StandardError) -and $preserved}
    if($id -eq 'full-migration' -or $id -eq 'full-migration-passthru'){$succeeded=$succeeded -and $report.RedirectRowCount -gt 0 -and $report.ReviewRowCount -gt 0 -and $report.AmpDiscoveryCount -gt 0 -and 'category-normalized' -in $report.MissingLegacy.MatchKind -and 'docs-to-projects' -in $report.MissingLegacy.MatchKind}
    if($SourceVariant -eq 'CorrectedOriginal' -and $id -in @('full-migration','full-migration-passthru')){$succeeded=$succeeded -and $report.LegacyUrlCount -eq 9 -and $report.ExactOverlapCount -eq 1}
    if($id -eq 'amp-migration-eight-urls'){$succeeded=$succeeded -and $report.LegacyUrlCount -eq 8 -and $report.ExactOverlapCount -eq 1 -and $report.AmpDiscoveryCount -gt 0}
    if($id -eq 'plain-migration'){$succeeded=$succeeded -and $report.ExactOverlapCount -eq 1 -and $report.LegacyUrlCount -eq 8 -and $report.RedirectRowCount -gt 0 -and $report.ReviewRowCount -gt 0}
    if($id -eq 'empty-difference'){$succeeded=$succeeded -and $report.MissingLegacyCount -eq 0 -and $report.RedirectRowCount -eq 0 -and $report.ReviewRowCount -eq 0}
    $observation=[ordered]@{exitCode=$run.ExitCode;stdout=$normalizedStdout;stderr=(ConvertTo-ApplicationStderrObservation $run.StandardError $id $outputs);files=$after;priorOutputPreserved=if($expectedSuccess){$null}else{$preserved}}
    $scenarioResult=[ordered]@{scenarioId=$id;succeeded=[bool]$succeeded;executedExports=@();observation=$observation;rawEvidence=[ordered]@{stdoutSha256=$rawProcess.stdoutSha256;stderrSha256=$rawProcess.stderrSha256;process='raw-process.json';files='raw-files.json';stderrOutputRoot=if($id -eq 'output-refusal'){$outputs}else{$null};stderrOutputPlaceholder=if($id -eq 'output-refusal'){'<scenario-output>'}else{$null}}}
    $scenarioResult | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $caseRoot 'scenario.json') -Encoding utf8NoBOM
    $scenarios.Add($scenarioResult)
    return $parameters
}
    $fixtureBefore=@(Get-ChildItem -LiteralPath $inputRoot -File -Recurse | Sort-Object FullName | ForEach-Object {[ordered]@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
    $fullParameters=Invoke-ApplicationCase 'full-migration' ($origin+'/index.xml') $newSitemap 'Positive' -Amp
    $null=Invoke-ApplicationCase 'full-migration-passthru' ($origin+'/index.xml') $newSitemap 'Positive' -Amp -PassThru
    $null=Invoke-ApplicationCase 'plain-migration' ($origin+'/index.xml') $newSitemap 'Positive'
    if($SourceVariant -eq 'CorrectedOriginal'){$null=Invoke-ApplicationCase 'amp-migration-eight-urls' ($origin+'/index.xml') $newSitemap 'Positive' -Amp -NoAdditionalUrls}
    $null=Invoke-ApplicationCase 'empty-difference' ($origin+'/empty-old.xml') $emptySitemap 'Positive'
    $null=Invoke-ApplicationCase 'malformed-remote' ($origin+'/malformed.xml') $newSitemap 'Malformed'
    $null=Invoke-ApplicationCase 'malformed-local' ($origin+'/index.xml') $badSitemap 'Malformed'
    $null=Invoke-ApplicationCase 'missing-local' ($origin+'/index.xml') (Join-Path $inputRoot 'absent.xml') 'Missing'
    $null=Invoke-ApplicationCase 'depth-refusal' ($origin+'/index.xml') $newSitemap 'Depth'
    $null=Invoke-ApplicationCase 'output-refusal' ($origin+'/index.xml') $newSitemap 'Output'
    if($Surface -eq 'Original'){
        $diagnostic=Join-Path $surfaceRoot 'helper-diagnostic';New-Item -ItemType Directory -Path $diagnostic | Out-Null
        foreach($key in @('OutputJsonPath','OutputCsvPath','OutputRedirectCsvPath','OutputReviewCsvPath')){$fullParameters[$key]=Join-Path $diagnostic ([IO.Path]::GetFileName($fullParameters[$key]))}
        $parameterPath=Join-Path $diagnostic 'parameters.json';$fullParameters | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $parameterPath -Encoding utf8NoBOM
        $tracePath=Join-Path $diagnostic 'helper-counts.json'
        try {
            $trace=Invoke-OwnedProcess -FileName $configuration.PowerShellHost -Arguments @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $taskRoot 'Trace-SitemapHelpers.ps1'),'-ImportPath',$ImportPath,'-ParametersPath',$parameterPath,'-InventoryPath',$selectedInventoryPath,'-ResultPath',$tracePath) -WorkingDirectory $ScenarioRoot -Environment @{NO_PROXY='127.0.0.1,localhost,::1';NO_COLOR='1'} -TimeoutSeconds 30
        } catch {
            Save-ApplicationTimeoutEvidence $_ 'helper-diagnostic' 'OriginalHelperDiagnostic'
            throw
        }
        [IO.File]::WriteAllText((Join-Path $diagnostic 'stdout.txt'),$trace.StandardOutput);[IO.File]::WriteAllText((Join-Path $diagnostic 'stderr.txt'),$trace.StandardError)
        $coverage=Get-Content -LiteralPath $tracePath -Raw | ConvertFrom-Json
        if($trace.ExitCode -ne 0 -or -not $coverage.all19Observed){throw 'The separate original helper diagnostic did not observe all 19 helpers.'}
        $normalFiles=Get-ApplicationFiles (Join-Path $surfaceRoot 'full-migration/outputs')
        $diagnosticFiles=@(Get-ApplicationFiles $diagnostic | Where-Object {$_.path -in @('report.json','missing.csv','redirects.csv','review.csv')})
        if(($normalFiles | ConvertTo-Json -Depth 5 -Compress) -cne ($diagnosticFiles | ConvertTo-Json -Depth 5 -Compress)){throw 'External diagnostic breakpoints changed application reports.'}
    }

    foreach($entry in $stagedBefore){if((Get-FileHash -LiteralPath $entry.path).Hash -ne $entry.sha256){throw 'Staged authored source changed during observation.'}}
    foreach($entry in $fixtureBefore){if((Get-FileHash -LiteralPath $entry.path).Hash -ne $entry.sha256){throw 'Immutable fixture input changed during observation.'}}
    $requestRecords=@(Get-Content -LiteralPath $requestsPath | ForEach-Object {$_ | ConvertFrom-Json})
    if(@($requestRecords | Where-Object {-not $_.accepted -or -not $_.known}).Count -ne 0){throw 'The application requested an unknown/refused fixture route.'}
    $limitations=@('Computed origin/synthetic AMP URLs omit nondefault ports')
    $result=[ordered]@{schemaVersion=1;surface=$Surface;sourceVariant=$SourceVariant;sourceIdentityPath=$selectedInventoryPath;importPath=$ImportPath;actualExports=@();actualAliases=@();scenarios=@($scenarios);sourceInputs=@($selectedInventory.SourceFiles | Where-Object {$_.Path -ne 'LICENSE'} | ForEach-Object {[ordered]@{path=$_.Path;sha256=$_.Sha256}});sourceAndInputHashesStable=$true;volatileNormalization='Only report GeneratedAt and its exact stdout occurrence';physicalPathNormalization='Only output-refusal stderr replaces its exact scenario output-root prefix with <scenario-output>; source/input paths, relative blocked-json suffix and all diagnostic rendering remain exact. Raw process/file bytes and hashes are retained.';helperCoverage=if($Surface -eq 'Original'){$coverage}else{$null};authoredLimitations=$limitations;allScenariosPassed=(@($scenarios | Where-Object {-not $_.succeeded}).Count -eq 0);performanceClaim=$false}
    $result | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $ResultPath -Encoding utf8NoBOM
    [pscustomobject]@{surface=$Surface;sourceVariant=$SourceVariant;scenarios=$scenarios.Count;passed=@($scenarios | Where-Object {$_.succeeded}).Count;all19HelpersObserved=if($Surface -eq 'Original'){$coverage.all19Observed}else{$null};resultPath=$ResultPath} | ConvertTo-Json -Compress
