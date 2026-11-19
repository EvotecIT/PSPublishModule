[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Start','Stop')][string] $Operation,
    [Parameter(Mandatory)][string] $ScenarioRoot,
    [Parameter(Mandatory)][string] $ResultPath
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($ScenarioRoot)
for($current=Get-Item -LiteralPath $root;$null -ne $current;$current=$current.Parent){
    if($current.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Hardware fixture roots cannot traverse links.'}
}
if($Operation -eq 'Start'){
    $acceptance=Join-Path $root 'acceptance'
    New-Item -ItemType Directory -Path $acceptance|Out-Null
    foreach($name in 'Observe-GetBIOS.ps1','Hardware.Observer.Common.ps1'){
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $acceptance $name)
    }
    $script=Join-Path $acceptance 'Test-HardwareModule.ps1'
    $body=@'
param([Parameter(Mandatory)][string] $ArtifactPath, [Parameter(Mandatory)][string] $ScenarioRoot)
$ErrorActionPreference='Stop'
if(-not $env:POWERFORGE_PWSH76_PATH){throw 'The pinned PowerShell host is required.'}
$result=Join-Path $ScenarioRoot ('project-test-'+[guid]::NewGuid().ToString('N')+'.json')
& $env:POWERFORGE_PWSH76_PATH -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Observe-GetBIOS.ps1') `
    -ImportPath $ArtifactPath -Surface Built -Instance ProjectTest -ScenarioRoot $ScenarioRoot -ResultPath $result
if($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $result)){throw 'The complete hardware module test failed.'}
'@
    [IO.File]::WriteAllText($script,$body,[Text.UTF8Encoding]::new($false))
    [ordered]@{schemaVersion=1;operation='Start';fixtureIdentity=[ordered]@{root=$root;kind='process-local-hardware-providers'}
        testArguments=@();moduleTest=[ordered]@{
            script='scenarios/acceptance/Test-HardwareModule.ps1';sha256=(Get-FileHash -LiteralPath $script).Hash.ToLowerInvariant()
            parameters=[ordered]@{ScenarioRoot=$root};workingDirectory='.'
        }}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $ResultPath -Encoding utf8
}else{
    [ordered]@{schemaVersion=1;operation='Stop';stopped=$true;fixtureIdentity=[ordered]@{root=$root;kind='process-local-hardware-providers'}
        backgroundProcessesStarted=0;scope='Provider overrides live only in completed observer child processes.'}|
        ConvertTo-Json -Depth 5|Set-Content -LiteralPath $ResultPath -Encoding utf8
}
