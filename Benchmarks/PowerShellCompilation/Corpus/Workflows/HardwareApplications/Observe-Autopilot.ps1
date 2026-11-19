[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ImportPath,
    [Parameter(Mandatory)][ValidateSet('Original','Built','Installed')][string] $Surface,
    [Parameter(Mandatory)][string] $ScenarioRoot,
    [Parameter(Mandatory)][string] $ResultPath
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Hardware.Observer.Common.ps1')
$run=Initialize-HardwareObservation $ScenarioRoot $Surface
$global:HardwareSessions=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
function global:New-CimSession {
    [CmdletBinding()]param([string]$ComputerName='localhost',[pscredential]$Credential)
    if($ComputerName -notin @('DeviceA','DeviceB','MissingHash')){throw 'Unexpected CIM device.'}
    if(-not $global:HardwareSessions.Add($ComputerName)){throw 'The application leaked or reused an active fixture session.'}
    $global:HardwareTrace.Add('open:'+$ComputerName)
    [pscustomobject]@{ComputerName=$ComputerName}
}
function global:Get-CimInstance {
    [CmdletBinding()]param([Parameter(Mandatory)]$CimSession,[Alias('Class')][string]$ClassName,[string]$Namespace,[string]$Filter)
    $name=$CimSession.ComputerName
    if(-not $global:HardwareSessions.Contains($name)){throw 'CIM query uses an inactive session.'}
    $global:HardwareTrace.Add('query:'+$name+':'+$ClassName)
    switch($ClassName){
        Win32_BIOS {return [pscustomobject]@{SerialNumber='SER-'+$name}}
        MDM_DevDetail_Ext01 {
            if($Namespace -cne 'root/cimv2/mdm/dmmap' -or $Filter -cne "InstanceID='Ext' AND ParentID='./DevDetail'"){throw 'The hardware query contract differs.'}
            if($name -eq 'MissingHash'){return $null}
            return [pscustomobject]@{DeviceHardwareData='HASH-'+$name}
        }
        Win32_ComputerSystem {return [pscustomobject]@{Manufacturer=' FixtureMake ';Model=' FixtureModel '}}
        default {throw 'Unexpected CIM class.'}
    }
}
function global:Remove-CimSession {
    [CmdletBinding()]param([Parameter(Position=0,Mandatory)]$CimSession)
    if(-not $global:HardwareSessions.Remove($CimSession.ComputerName)){throw 'A fixture session was closed more than once.'}
    $global:HardwareTrace.Add('close:'+$CimSession.ComputerName)
}
function global:Install-Module {throw 'The offline hardware contract does not install external modules.'}
function global:Connect-MgGraph {throw 'The offline hardware contract does not contact Graph.'}
$entryPath=$ImportPath
$factory=$null
if($Surface -ne 'Original'){
    $assemblyPath=[IO.Path]::ChangeExtension([IO.Path]::GetFullPath($ImportPath),'.dll')
    if(-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)){throw 'The reusable native entry factory requires a delivered framework-dependent assembly.'}
    $assembly=[Reflection.Assembly]::LoadFrom($assemblyPath)
    $factory=$assembly.GetType('PowerForge.Compiled.CompiledNativeScriptEntry',$true)
    $stream=$assembly.GetManifestResourceStream('PowerForge.Compiled.Source.ps1')
    if($null -eq $stream){throw 'The delivered application has no authenticated entry source.'}
    $entryPath=Join-Path $run 'Get-WindowsAutopilotInfo.ps1'
    $file=[IO.File]::Create($entryPath)
    try {$stream.CopyTo($file)}finally{$file.Dispose();$stream.Dispose()}
}
function New-AutopilotEntry {
    if($null -eq $factory){return Get-Command $entryPath -CommandType ExternalScript}
    $method=$factory.GetMethod('Create',[type[]]@([Management.Automation.SessionState],[string]))
    if($null -eq $method){throw 'The delivered application lacks its in-process native script entry.'}
    $method.Invoke($null,[object[]]@($ExecutionContext.SessionState,[string]$entryPath))
}
function Assert-ClosedSessions {
    if($global:HardwareSessions.Count){throw 'The application did not close every completed fixture session.'}
}
$scenarios=[Collections.Generic.List[object]]::new()
$entry=New-AutopilotEntry
$global:HardwareTrace.Clear()
$rows=@(@('DeviceA','DeviceB')|& $entry)
Assert-ClosedSessions
if($rows.Count -ne 2 -or $rows[0].'Device Serial Number' -cne 'SER-DeviceA' -or $rows[1].'Hardware Hash' -cne 'HASH-DeviceB'){
    throw 'The real lifecycle did not preserve both pipeline records.'
}
$scenarios.Add((New-HardwareScenario 'pipeline-records' 'Get-WindowsAutopilotInfo.ps1' @('<script>') @() ([ordered]@{
    serials=@($rows|ForEach-Object {$_.'Device Serial Number'});hashes=@($rows|ForEach-Object {$_.'Hardware Hash'});trace=@($global:HardwareTrace.ToArray())
})))
$global:HardwareTrace.Clear()
$csv=Join-Path $run 'autopilot.csv'
& (New-AutopilotEntry) -Name DeviceA -OutputFile $csv -GroupTag OwnedGroup -AssignedUser 'fixture@example.invalid'|Out-Null
& (New-AutopilotEntry) -Name DeviceB -OutputFile $csv -GroupTag OwnedGroup -AssignedUser 'fixture@example.invalid' -Append|Out-Null
Assert-ClosedSessions
$saved=@(Import-Csv -LiteralPath $csv)
if($saved.Count -ne 2 -or $saved[0].'Device Serial Number' -cne 'SER-DeviceB' -or
    $saved[1].'Device Serial Number' -cne 'SER-DeviceA' -or $saved[0].'Assigned User' -cne 'fixture@example.invalid'){
    throw 'The application did not preserve its actual CSV append, field selection and ordering.'
}
$scenarios.Add((New-HardwareScenario 'csv-append' 'Get-WindowsAutopilotInfo.ps1' @('<script>') @() ([ordered]@{
    serials=@($saved|ForEach-Object {$_.'Device Serial Number'});groups=@($saved|ForEach-Object {$_.'Group Tag'});users=@($saved|ForEach-Object {$_.'Assigned User'});trace=@($global:HardwareTrace.ToArray())
})))
$global:HardwareTrace.Clear()
$partner=@(& (New-AutopilotEntry) -Name DeviceA -Partner -Force)
Assert-ClosedSessions
if($partner.Count -ne 1 -or $partner[0].'Manufacturer name' -cne 'FixtureMake' -or
    $partner[0].'Device model' -cne 'FixtureModel' -or $partner[0].'Hardware Hash' -cne ''){throw 'Forced partner fallback differs.'}
$scenarios.Add((New-HardwareScenario 'partner-fallback' 'Get-WindowsAutopilotInfo.ps1' @('<script>') @() ([ordered]@{
    make=$partner[0].'Manufacturer name';model=$partner[0].'Device model';hash=$partner[0].'Hardware Hash';trace=@($global:HardwareTrace.ToArray())
})))
$global:HardwareTrace.Clear()
$mixed=@(@('MissingHash','DeviceA')|& (New-AutopilotEntry) -ErrorAction Continue 2>&1)
Assert-ClosedSessions
$failures=@($mixed|Where-Object {$_ -is [Management.Automation.ErrorRecord]})
$successes=@($mixed|Where-Object {$_ -isnot [Management.Automation.ErrorRecord]})
if($failures.Count -ne 1 -or $successes.Count -ne 1 -or $successes[0].'Device Serial Number' -cne 'SER-DeviceA'){
    throw 'Missing hardware data did not preserve failure, continuation and cleanup.'
}
$scenarios.Add((New-HardwareScenario 'failure-and-recovery' 'Get-WindowsAutopilotInfo.ps1' @('<script>') @() ([ordered]@{
    serial=$successes[0].'Device Serial Number';errorId=$failures[0].FullyQualifiedErrorId;category=$failures[0].CategoryInfo.Category.ToString();trace=@($global:HardwareTrace.ToArray())
})))
Write-HardwareObservation $ImportPath $Surface $null @($scenarios) $ResultPath `
    'The unchanged advanced script executes through the delivered public native script-entry factory on pinned PowerShell. CIM sessions and device data are owned provider fixtures; actual CSV files are verified. Console startup uses the native help path. No live device registration, Graph changes, group assignment, reboot, remote endpoint or hardware inventory is qualified.'
