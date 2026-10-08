param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$DefinitionPath,[Parameter(Mandatory)][string]$OutputPath,[ValidateSet('local','assembly-refusal')][string]$Case='local')
$ErrorActionPreference='Stop'
$module=Import-Module $ModulePath -Force -PassThru
$rows=[Collections.Generic.List[object]]::new()
function Get-Digest($Records){
 $json=ConvertTo-Json -InputObject @($Records) -Depth 8 -Compress
 $hash=[Security.Cryptography.SHA256]::Create()
 try {[BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($json))).Replace('-','').ToLowerInvariant()} finally {$hash.Dispose()}
}
function Convert-Record($Record){
 [ordered]@{name=$Record.Name;remark=$Record.Remark;type=[uint32]$Record.Type;typeIdentity=$Record.Type.GetType().FullName;computer=$Record.ComputerName;path=$Record.Path;properties=@($Record.PSObject.Properties.Name);psType=$Record.PSObject.TypeNames[0]}
}
try {
 if('Win32Share.NativeMethods' -as [type]){throw 'Use a fresh child without pre-existing Win32Share types'}
 if($Case -eq 'assembly-refusal'){
  & $module {function script:Add-Type {[CmdletBinding()]param($TypeDefinition) throw 'owned SMB assembly refusal'}}
  try {Get-ComputerSMBShareList -ComputerName '__PFC_NEVER_ENUMERATED__' -SkipDiskSpace;throw 'Expected assembly refusal'}
  catch {if($_.Exception.Message -ne 'owned SMB assembly refusal'){throw}}
  if('Win32Share.NativeMethods' -as [type]){throw 'Refused assembly unexpectedly loaded'}
  $rows.Add([ordered]@{case='assembly-refusal';message='owned SMB assembly refusal';nativeTypeLoaded=$false})
 }else{
  # Bootstrap only the extracted authored declaration; never call a remote server.
  Add-Type -TypeDefinition ([IO.File]::ReadAllText((Resolve-Path -LiteralPath $DefinitionPath).Path))
  if(-not ('Win32Share.NativeMethods' -as [type])){throw 'Authored native types missing'}
  $buffer=[IntPtr]::Zero;[uint32]$read=0;[uint32]$total=0;[uint32]$resume=0
  $baseline=@()
  try {
   $status=[Win32Share.NativeMethods]::NetShareEnum($null,1,[ref]$buffer,[uint32]::MaxValue,[ref]$read,[ref]$total,[ref]$resume)
   # Never feed the authored total-count loop a known partial native buffer.
   if($status -ne 0 -or $read -ne $total -or $read -eq 0){throw 'Local baseline must be successful, complete and nonempty'}
   $pointer=$buffer
   for($i=0;$i -lt $read;$i++){
    $entry=[Runtime.InteropServices.Marshal]::PtrToStructure($pointer,[type][Win32Share.NativeHelpers+SHARE_INFO_1])
    $record=[pscustomobject]@{PSTypeName='Win32Share.NativeMethods';ComputerName='';Path="\\\$($entry.shi1_netname)\";Name=$entry.shi1_netname;Type=$entry.shi1_type;Remark=$entry.shi1_remark}
    $baseline+=Convert-Record $record
    $pointer=[IntPtr]::Add($pointer,[Runtime.InteropServices.Marshal]::SizeOf($entry))
   }
  }finally {if($buffer -ne [IntPtr]::Zero){if([Win32Share.NativeMethods]::NetApiBufferFree($buffer) -ne 0){throw 'Independent native buffer free failed'}}}
  $selected=$baseline[0].name
  $cases=@(@{label='all';names=@();expected=$baseline},@{label='repeat';names=@();expected=$baseline},@{label='wildcard';names=@('*');expected=$baseline},@{label='exact';names=@([WildcardPattern]::Escape($selected));expected=@($baseline|Where-Object {$_.name -ceq $selected})},@{label='missing';names=@('__PFC_NO_SUCH_SHARE_61c8__');expected=@()})
  foreach($probe in $cases){
   $warnings=@();$result=@(Get-ComputerSMBInfo -ComputerName '' -SkipDiskSpace -Name $probe.names -WarningVariable warnings -WarningAction SilentlyContinue)
   $actual=@($result|ForEach-Object {Convert-Record $_})
   if($warnings.Count -or (Get-Digest $actual) -cne (Get-Digest $probe.expected)){throw "Local SMB mismatch: $($probe.label)"}
   $rows.Add([ordered]@{case=$probe.label;records=$result.Count;recordSha256=(Get-Digest $actual);matchesIndependentLocalBaseline=$true;warnings=0})
  }
 }
}finally {Remove-Module $module -Force}
[ordered]@{case=$Case;hostVersion=$PSVersionTable.PSVersion.ToString();scope='local null/empty server, SkipDiskSpace; no UNC access';observations=$rows.ToArray()}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $OutputPath -Encoding UTF8
