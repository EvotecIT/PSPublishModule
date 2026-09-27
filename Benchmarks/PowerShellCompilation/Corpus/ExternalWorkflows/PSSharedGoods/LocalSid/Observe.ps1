param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$OutputPath,[ValidateSet('real','assembly-failure')][string]$Case='real')
$ErrorActionPreference='Stop'
$module=Import-Module $ModulePath -Force -PassThru
$rows=[Collections.Generic.List[object]]::new()
$expected=$null;$correct=$null;$context=$null;$filter=$null;$searcher=$null;$collection=$null
try {
 if($Case -eq 'real') {
  Add-Type -AssemblyName System.DirectoryServices.AccountManagement
  $context=[DirectoryServices.AccountManagement.PrincipalContext]::new([DirectoryServices.AccountManagement.ContextType]::Machine)
  $filter=[DirectoryServices.AccountManagement.UserPrincipal]::new($context)
  $searcher=[DirectoryServices.AccountManagement.PrincipalSearcher]::new($filter)
  $collection=$searcher.FindAll()
  foreach($principal in $collection) {
   try {if($principal.Sid.Value.EndsWith('-500')){$correct=$principal.Sid.AccountDomainSid.Value;$expected=$principal.Sid.Value.TrimEnd('-500');break}}
   finally {$principal.Dispose()}
  }
  if(-not $correct){throw 'Independent local-SAM baseline has no RID-500 account'}
 } else {
  & $module {function script:Add-Type {[CmdletBinding()]param($AssemblyName) throw 'owned local-SAM assembly refusal'}}
 }
 foreach($iteration in 1,2) {
  $warnings=@();$result=@(Get-LocalComputerSid -WarningVariable warnings -WarningAction SilentlyContinue)
  if($Case -eq 'real') {
   if($result.Count -ne 1 -or $result[0] -isnot [string] -or $result[0] -cne $expected -or $warnings.Count){throw 'Local-SAM result differs from the authored algorithm'}
   $hash=[Security.Cryptography.SHA256]::Create()
   try{$digest=[BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($result[0]))).Replace('-','').ToLowerInvariant()}finally{$hash.Dispose()}
   $rows.Add([ordered]@{iteration=$iteration;type=$result[0].GetType().FullName;valueSha256=$digest;matchesAuthoredAlgorithm=$true;matchesCorrectAccountDomainSid=($result[0] -ceq $correct);warnings=@()})
  } else {
   if($result.Count -or $warnings.Count -ne 1 -or $warnings[0].Message -notlike '*owned local-SAM assembly refusal'){throw 'Assembly refusal must warn and emit no SID'}
   $rows.Add([ordered]@{iteration=$iteration;records=$result;warnings=@($warnings|ForEach-Object {$_.Message})})
  }
 }
 $example='S-1-5-21-111-222-123450-500'
 $exampleSid=[Security.Principal.SecurityIdentifier]::new($example)
 $counterexample=[ordered]@{ownedInput=$example;authored=$example.TrimEnd('-500');correct=$exampleSid.AccountDomainSid.Value}
 if($counterexample.authored -ceq $counterexample.correct){throw 'Expected authored TrimEnd counterexample'}
 $identity=if($Case -eq 'real'){[DirectoryServices.AccountManagement.PrincipalContext].Assembly.FullName}else{$null}
} finally {
 if($collection){$collection.Dispose()};if($searcher){$searcher.Dispose()};if($filter){$filter.Dispose()};if($context){$context.Dispose()}
 Remove-Module $module -Force
}
[ordered]@{case=$Case;hostVersion=$PSVersionTable.PSVersion.ToString();directoryAssembly=$identity;context='Machine (local SAM only)';observations=$rows.ToArray();ownedTrimEndCounterexample=$counterexample}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $OutputPath -Encoding UTF8
