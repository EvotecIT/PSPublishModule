[CmdletBinding()]
param(
    [Parameter(Mandatory)][Alias('ArtifactPath')][string] $ImportPath,
    [Parameter(Mandatory)][ValidateSet('Original','Built','Installed')][string] $Surface,
    [Parameter(Mandatory)][string] $ScenarioRoot,
    [Parameter(Mandatory)][string] $ResultPath,
    [string] $Instance = $Surface
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Hardware.Observer.Common.ps1')
$run=Initialize-HardwareObservation $ScenarioRoot $Instance
# This process-local ETS method supplies the privilege boundary. It changes no token or OS privileges.
Update-TypeData -TypeName System.Security.Principal.WindowsPrincipal -MemberType ScriptMethod -MemberName IsInRole -Value {
    param($Role)
    if($Role.ToString() -cne 'Administrator'){throw 'Unexpected principal role query.'}
    $global:HardwareAllowPrincipal
} -Force
$moduleRoot=Join-Path $run 'modules'
$dell=Join-Path $moduleRoot 'DellBIOSProvider'
New-Item -ItemType Directory -Path $dell -Force|Out-Null
Set-Content -LiteralPath (Join-Path $dell 'DellBIOSProvider.psd1') -Encoding utf8 -Value "@{RootModule='DellBIOSProvider.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Get-FixtureDellMarker')}"
Set-Content -LiteralPath (Join-Path $dell 'DellBIOSProvider.psm1') -Encoding utf8 -Value "function Get-FixtureDellMarker { 'owned' }; Export-ModuleMember -Function Get-FixtureDellMarker"
$env:PSModulePath=$moduleRoot+[IO.Path]::PathSeparator+(Join-Path $PSHOME 'Modules')
function global:Install-Module { throw 'The hardware contract does not install external modules.' }
function global:Get-WmiObject {
    [CmdletBinding()]param([Parameter(Position=0)][string]$Class,[string]$Namespace,[string]$Query)
    $global:HardwareTrace.Add('wmi:'+$Class+':'+$Namespace+':'+$Query)
    if($Class -eq 'Win32_Computersystem'){return [pscustomobject]@{manufacturer=$global:HardwareVendor}}
    if($Class -eq 'hp_biosEnumeration'){return [pscustomobject]@{Name='SecureBoot';currentvalue='Enabled';possiblevalues=@('Enabled','Disabled')}}
    if($Class -eq 'Lenovo_GetBiosSelections'){
        return ([pscustomobject]@{kind='selections'}|Add-Member -MemberType ScriptMethod -Name GetBiosSelections -Value {
            param($Name)
            if($Name -cne 'SecureBoot'){throw 'Unexpected Lenovo selection.'}
            [pscustomobject]@{Selections='Enabled,Disabled'}
        } -PassThru)
    }
    if($Class -eq 'Lenovo_BiosSetting'){return [pscustomobject]@{currentsetting='SecureBoot,Enabled'}}
    if($Query -ceq 'SELECT * FROM QueryBiosSettings'){
        if($global:HardwareProviderFailure){throw 'Controlled Toshiba provider failure.'}
        return [pscustomobject]@{CurrentSetting='SecureBoot';Currentvalue='Enabled'}
    }
    throw 'Unexpected WMI query.'
}
Set-Alias -Name gwmi -Value Get-WmiObject -Scope Global -Force
function global:Get-ChildItem {
    [CmdletBinding()]param([Parameter(Position=0)][string[]]$Path)
    if($Path.Count -eq 1 -and $Path[0] -ieq 'DellSmbios:\'){
        $global:HardwareTrace.Add('dell:categories');return [pscustomobject]@{category='Security'}
    }
    if($Path.Count -eq 1 -and $Path[0] -ieq 'DellSmbios:\Security'){
        $global:HardwareTrace.Add('dell:settings')
        return [pscustomobject]@{attribute='SecureBoot';currentvalue='Enabled';Description='Fixture secure boot';PossibleValues=@('Enabled','Disabled')}
    }
    throw 'The hardware fixture only exposes its declared Dell provider paths.'
}
function global:Get-Credential {
    $global:HardwareTrace.Add('credential:owned')
    [pscredential]::new('fixture-user',(ConvertTo-SecureString 'fixture-only' -AsPlainText -Force))
}
function global:Invoke-Command {
    [CmdletBinding()]param([string]$ComputerName,[scriptblock]$ScriptBlock,[pscredential]$Credential)
    if($ComputerName -cne 'FixtureRemote' -or $Credential.UserName -cne 'fixture-user'){throw 'Unexpected remote request.'}
    $global:HardwareTrace.Add('remote:'+ $ComputerName)
    & $ScriptBlock
}
function global:Get-Date { [datetime]'2026-01-02T03:04:05' }
function global:Invoke-Item {
    param([Parameter(Position=0)][string]$Path)
    if(-not [IO.Path]::GetFullPath($Path).StartsWith($global:HardwareRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
        throw 'Report launch escapes its owned directory.'
    }
    $global:HardwareTrace.Add('report-open:'+[IO.Path]::GetFileName($Path))
}
function global:Out-GridView { param([Parameter(ValueFromPipeline=$true)]$InputObject) process {$global:HardwareTrace.Add('grid:'+ $InputObject.Setting)} }
try {
    $module=Import-Module $ImportPath -Force -PassThru -DisableNameChecking -ErrorAction Stop
    $scenarios=[Collections.Generic.List[object]]::new()
    foreach($vendor in 'Dell','HP','Lenovo','Toshiba'){
        $global:HardwareVendor=$vendor;$global:HardwareTrace.Clear()
        $csv=Join-Path $run ($vendor+'.csv');$html=Join-Path $run ($vendor+'.html')
        $rows=@(Get-Bios -CSV_Path $csv -HTML_Path $html -ShowDescription -WarningAction SilentlyContinue)
        if($rows.Count -ne 1 -or $rows[0].Setting -cne 'SecureBoot'){throw 'The complete vendor entry returned the wrong setting.'}
        if(-not (Test-Path -LiteralPath $csv) -or -not (Test-Path -LiteralPath $html)){throw 'The actual application did not write both reports.'}
        $delimiter=if($vendor -in @('HP','Toshiba')){';'}else{','}
        $csvRows=@(Import-Csv -LiteralPath $csv -Delimiter $delimiter)
        if($csvRows.Count -ne 1 -or $csvRows[0].Setting -cne 'SecureBoot' -or
            [IO.File]::ReadAllText($html) -notlike '*SecureBoot*'){throw 'Report content differs from the owned provider data.'}
        $value=if($vendor -eq 'HP'){$rows[0].'Current Value'}else{$rows[0].Value}
        if($vendor -in @('Dell','Toshiba') -and $value -cne 'Enabled'){throw 'The vendor value differs.'}
        if($vendor -in @('HP','Lenovo') -and $null -ne $value){throw 'The pinned source no longer preserves its absent vendor value.'}
        $observation=[ordered]@{setting=$rows[0].Setting;value=$value;csvSetting=$csvRows[0].Setting;htmlContainsSetting=$true;trace=@($global:HardwareTrace.ToArray())}
        $scenarios.Add((New-HardwareScenario ('vendor-'+$vendor.ToLowerInvariant()) 'GetBIOS.psm1' @('Get-Bios') @('Get-Bios') $observation))
    }
    $global:HardwareVendor='HP';$global:HardwareTrace.Clear()
    $remote=@(Get-Bios -Computer FixtureRemote)
    if($remote.Count -ne 2 -or @($remote|Where-Object {$_.Setting -cne 'SecureBoot'}).Count){throw 'The remote entry did not preserve its two authored outputs.'}
    $scenarios.Add((New-HardwareScenario 'remote-provider' 'GetBIOS.psm1' @('Get-Bios') @('Get-Bios') ([ordered]@{settings=@($remote.Setting);trace=@($global:HardwareTrace.ToArray())})))
    $global:HardwareVendor='Toshiba';$global:HardwareProviderFailure=$true;$global:HardwareTrace.Clear()
    $empty=@(Get-Bios)
    if($empty.Count){throw 'The authored Toshiba catch did not preserve its empty result.'}
    $scenarios.Add((New-HardwareScenario 'provider-failure' 'GetBIOS.psm1' @('Get-Bios') @('Get-Bios') ([ordered]@{count=0;trace=@($global:HardwareTrace.ToArray())})))
    Write-HardwareObservation $ImportPath $Surface $module @($scenarios) $ResultPath `
        'All four nested vendor helpers execute through the unchanged public command. WMI, Dell provider, credentials, remoting, UI and privilege membership are controlled process-local boundaries. Actual CSV/HTML files are checked by parsed content. The pinned HP source selects an absent Current Value property and the Lenovo split yields an absent Value. No real BIOS, elevated token or live remote endpoint is qualified.'
} finally { Remove-TypeData -TypeName System.Security.Principal.WindowsPrincipal -ErrorAction SilentlyContinue }
