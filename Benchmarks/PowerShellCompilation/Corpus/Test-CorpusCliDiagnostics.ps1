[CmdletBinding()]
param([Parameter(Mandatory)] [string] $WorkspacePath, [string] $EvidencePath)

$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Corpus.Runner.Common.ps1')
. (Join-Path $PSScriptRoot 'Corpus.Qualification.Workflow.ps1')
$realOwnedProcess=${function:Invoke-OwnedProcess}
$root=Join-Path ([IO.Path]::GetFullPath($WorkspacePath)) ('cli-diagnostics-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root|Out-Null
$checks=[Collections.Generic.List[string]]::new()

function New-ProcessFixture {
    param([string]$Stdout,[string]$Stderr='',[int]$ExitCode=1)
    [pscustomobject]@{ExitCode=$ExitCode;StandardOutput=$Stdout;StandardError=$Stderr;DurationMilliseconds=7}
}

function Assert-CliFailure {
    param([string]$Name,$Process,[string]$ExpectedMessage)
    $path=Join-Path $root ($Name+'.process.json')
    try {ConvertFrom-CorpusCliProcess $Process -ProcessEvidencePath $path|Out-Null}
    catch {
        if($_.Exception.Message -notlike "*$ExpectedMessage*"){throw "Unexpected diagnostic for ${Name}: $($_.Exception.Message)"}
        $failure=Get-CorpusCliFailureEvidence $_.Exception
        $retained=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
        foreach($value in @($failure.process,$retained)) {
            if($value.StandardOutput -cne $Process.StandardOutput -or $value.StandardError -cne $Process.StandardError -or
                $value.ExitCode -ne $Process.ExitCode){throw "Raw CLI evidence changed for $Name."}
        }
        $checks.Add($Name)
        return
    }
    throw "$Name unexpectedly accepted a failed CLI process."
}

Assert-CliFailure 'root-error' (New-ProcessFixture '{"success":false,"error":"Dependency lock differs"}' 'raw root stderr') 'Dependency lock differs'
Assert-CliFailure 'nested-target-error' (New-ProcessFixture '{"success":false,"result":{"succeeded":false,"targets":[{"name":"executable","succeeded":false,"error":"Native source map rejected"}]}}') "Target 'executable': Native source map rejected"
Assert-CliFailure 'nested-target-message' (New-ProcessFixture '{"success":false,"result":{"targets":[{"name":"script","succeeded":false,"message":"Generated project failed: CS1234"}]}}') 'Generated project failed: CS1234'
Assert-CliFailure 'malformed-json' (New-ProcessFixture "{broken`nfull unparsed stdout" "native stderr`r`nwith detail" 2) 'did not return a JSON object'
Assert-CliFailure 'nonzero-empty' (New-ProcessFixture '' 'host exited before JSON' 9) 'host exited before JSON'
Assert-CliFailure 'missing-success' (New-ProcessFixture '{"result":{"targets":[]}}' '' 0) 'Boolean success'
Assert-CliFailure 'missing-result' (New-ProcessFixture '{"success":true}' '' 0) 'lacks its result payload'
Assert-CliFailure 'nonzero-success-envelope' (New-ProcessFixture '{"success":true,"result":{"value":7}}' 'native exit disagrees' 3) 'native exit disagrees'
$process=New-ProcessFixture '{"success":true,"result":{"value":7}}' '' 0
$json=ConvertFrom-CorpusCliProcess $process
if($json.result.value -ne 7){throw 'Successful result mapping changed.'}
$checks.Add('successful-envelope-preserves-result')

# Use each real thin adapter body with the process launch boundary mocked. No CLI/SDK is started.
function Invoke-OwnedProcess {param($FileName,$Arguments,$WorkingDirectory,$Environment,$TimeoutSeconds) return $script:fixtureProcess}
$script:fixtureProcess=New-ProcessFixture '{"success":false,"result":{"targets":[{"name":"script","succeeded":false,"message":"Actionable generated build failure"}]}}' 'exact SDK stderr' 1
$CliAssemblyPath='fixture-never-launched.dll'
foreach($adapter in @(
    @{file='Invoke-ExternalQualification.ps1';name='Invoke-CliJson'},
    @{file='Invoke-ExternalAssessment.ps1';name='Invoke-Census'},
    @{file='Invoke-PublicCorpus.ps1';name='Invoke-PowerForgeJson'}
)) {
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $adapter.file),[ref]$tokens,[ref]$errors)
    if(@($errors).Count){throw 'Adapter parse failed.'}
    $function=@($ast.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)|Where-Object Name -EQ $adapter.name)
    if($function.Count -ne 1){throw 'Adapter function identity is ambiguous.'}
    . ([scriptblock]::Create($function[0].Extent.Text))
    try {
        if($adapter.name -eq 'Invoke-Census'){Invoke-Census 'unused.ps1' 'net10.0' $root|Out-Null}
        else {& $adapter.name -Arguments @('unused') -WorkingDirectory $root|Out-Null}
    } catch {
        $failure=Get-CorpusCliFailureEvidence $_.Exception
        if($_.Exception.Message -notlike '*Actionable generated build failure*' -or
            $null -eq $failure -or $failure.process.StandardOutput -cne $script:fixtureProcess.StandardOutput){
            throw 'Thin adapter lost the canonical actionable/raw CLI failure.'
        }
        $checks.Add('real-adapter-'+$adapter.name)
        continue
    }
    throw 'Thin adapter accepted the failed process.'
}

$stepPath=Join-Path $root 'build.json'
try {Invoke-WorkflowCliOperation @('powershell','project','build') $root $stepPath|Out-Null}
catch {
    $step=Get-Content -LiteralPath $stepPath -Raw|ConvertFrom-Json
    $raw=Get-Content -LiteralPath ([IO.Path]::ChangeExtension($stepPath,'.cli.process.json')) -Raw|ConvertFrom-Json
    if($step.error -notlike '*Actionable generated build failure*' -or
        $step.cliFailure.process.StandardOutput -cne $script:fixtureProcess.StandardOutput -or
        $raw.StandardOutput -cne $script:fixtureProcess.StandardOutput -or $raw.StandardError -cne $script:fixtureProcess.StandardError){
        throw 'Canonical workflow failure lost its durable raw process or nested error.'
    }
    $checks.Add('canonical-workflow-retains-actionable-error-and-exact-raw-process')
}
if($checks.Count -ne 13){throw 'Expected diagnostic control did not reach its failure assertion.'}
# Preserve the actual timeout payload through the real CLI adapter and workflow catch.
# Only the process launch boundary substitutes this test-owned pwsh for dotnet.
$script:ownedTimeoutException=$null
$script:ownedTimeoutInvocation=$realOwnedProcess
$script:ownedTimeoutStdout="owned timeout stdout π 中文`n"
$script:ownedTimeoutStderr="owned timeout stderr café Ω`n"
$script:ownedTimeoutCommand='[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); [Console]::Out.Write('+
    "'"+$script:ownedTimeoutStdout.Replace("'","''")+"'"+'); [Console]::Error.Write('+
    "'"+$script:ownedTimeoutStderr.Replace("'","''")+"'"+'); [Console]::Out.Flush(); [Console]::Error.Flush(); Start-Sleep -Seconds 60'
function Invoke-OwnedProcess {
    param($FileName,$Arguments,$WorkingDirectory,$Environment,$TimeoutSeconds)
    try {
        & $script:ownedTimeoutInvocation -FileName ([Environment]::ProcessPath) -Arguments @('-NoLogo','-NoProfile','-NonInteractive','-EncodedCommand',
            [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script:ownedTimeoutCommand))) -WorkingDirectory $WorkingDirectory -TimeoutSeconds 10
    } catch { $script:ownedTimeoutException=$_.Exception; throw }
}
$timeoutStep=Join-Path $root 'owned-timeout-step.json'
$caughtTimeout=$false
try {Invoke-WorkflowCliOperation @('powershell','project','build') $root $timeoutStep|Out-Null}
catch {
    $caughtTimeout=$true
    $raw=$_.Exception.Data['PowerForgeOwnedProcess']
    if(-not [object]::ReferenceEquals($_.Exception,$script:ownedTimeoutException) -or
        $_.Exception.GetType().FullName -cne 'System.Management.Automation.RuntimeException' -or
        $_.Exception.Message -cne "Owned process '$([Environment]::ProcessPath)' exceeded 10 seconds."){throw 'Timeout rethrow contract changed.'}
    if($null -eq $raw -or -not $raw.TimedOut -or -not $raw.ProcessExited -or -not $raw.OutputDrained -or
        $raw.TimeoutSeconds -ne 10 -or -not $raw.ProcessStartTimeAvailable -or -not $raw.ProcessStartUtc -or
        $raw.StandardOutput -cne $script:ownedTimeoutStdout -or $raw.StandardError -cne $script:ownedTimeoutStderr){throw 'Real owned timeout lost exact streams or process identity.'}
    if(Get-Process -Id $raw.ProcessId -ErrorAction SilentlyContinue){throw 'Timed-out owned child is still present.'}
    Write-Utf8Json -Path (Join-Path $root 'owned-timeout-raw.json') -Value $raw
    $stepNode=[Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($timeoutStep))
    $expectedNode=[Text.Json.Nodes.JsonNode]::Parse((ConvertTo-Json -InputObject $raw -Depth 30 -Compress))
    if($null -eq $stepNode['ownedProcess'] -or -not [Text.Json.Nodes.JsonNode]::DeepEquals($stepNode['ownedProcess'],$expectedNode)){
        throw 'Canonical workflow catch lost the real owned timeout payload.'
    }
    $checks.Add('canonical-workflow-retains-real-owned-timeout-and-rethrow')
}
if(-not $caughtTimeout){throw 'Real owned timeout control did not reach the workflow catch.'}
${function:Invoke-OwnedProcess}=$realOwnedProcess
$normalIdentity=Join-Path $root 'normal-child-identity.json'
$normalCommand='[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); [Console]::Out.Write(''normal stdout π''); [Console]::Error.Write(''normal stderr Ω''); '+
    '[IO.File]::WriteAllText('+("'"+$normalIdentity.Replace("'","''")+"'")+',(@{pid=$PID;startUtc=(Get-Process -Id $PID).StartTime.ToUniversalTime().ToString(''o'')}|ConvertTo-Json),[Text.UTF8Encoding]::new($false)); exit 0'
$normal=Invoke-OwnedProcess -FileName ([Environment]::ProcessPath) -Arguments @('-NoLogo','-NoProfile','-NonInteractive','-EncodedCommand',
    [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($normalCommand))) -WorkingDirectory $root -TimeoutSeconds 10
if((@($normal.PSObject.Properties.Name|Sort-Object) -join ',') -cne 'DurationMilliseconds,ExitCode,StandardError,StandardOutput' -or
    $normal.ExitCode -ne 0 -or $normal.StandardOutput -cne 'normal stdout π' -or $normal.StandardError -cne 'normal stderr Ω'){throw 'Normal owned-process four-property contract changed.'}
$normalChild=Get-Content -LiteralPath $normalIdentity -Raw|ConvertFrom-Json
if(Get-Process -Id $normalChild.pid -ErrorAction SilentlyContinue){throw 'Normal owned child is still present.'}
Write-Utf8Json -Path (Join-Path $root 'normal-process.json') -Value $normal
$checks.Add('normal-owned-child-preserves-four-properties-and-unicode-streams')
if($checks.Count -ne 15){throw 'Expected original13 plus two real process contract controls.'}

$result=[ordered]@{schemaVersion=1;passed=$true;checkCount=$checks.Count;checks=@($checks);retainedProofRoot=$root
    scope='Process-envelope fixtures, actual adapter bodies, one real owned-child timeout and one normal child; no SDK/build/workload/fixture process executed.'}
if($EvidencePath){Write-Utf8Json -Path $EvidencePath -Value $result}
$result|ConvertTo-Json -Depth 8
