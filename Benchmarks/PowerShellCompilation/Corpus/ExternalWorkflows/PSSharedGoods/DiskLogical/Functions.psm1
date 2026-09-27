function Get-CimData {
 [CmdletBinding()]
 param($ComputerName,$Protocol,$Credential,$Class,$Properties)
 $script:ProviderTrace.Add([ordered]@{computers=@($ComputerName);protocol=$Protocol;credential=($null -ne $Credential);class=$Class;properties=@($Properties)})
 if($script:ProviderFailure){throw 'owned offline disk provider failure'}
 $script:DiskRows
}
