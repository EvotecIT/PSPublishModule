param([Parameter(Mandatory)][string]$ModulePath)
$ErrorActionPreference='Stop'
Import-Module $ModulePath -Force
$credential=[pscredential]::new('offline-user',(ConvertTo-SecureString 'owned-offline-value' -AsPlainText -Force))
foreach($schema in 'present','empty','warning','throw') {
 foreach($skip in $false,$true) { foreach($tls in $false,$true) { foreach($return in $false,$true) {
  $before=[Net.ServicePointManager]::SecurityProtocol
  $warnings=@();$errors=@();$outer=$null;$records=@()
  try {
   Set-OfflineSchema -Mode $schema
   try { $records=@(Connect-Infoblox -Server 'offline.invalid' -Credential $credential -ApiVersion '2.99' -TimeoutSec 27 -SkipInitialConnection:$skip -EnableTLS12:$tls -ReturnObject:$return -WarningVariable warnings -ErrorVariable errors 3>$null) }
   catch { $outer=$_.FullyQualifiedErrorId }
   [pscustomobject]@{kind='connection';schema=$schema;skip=$skip;tls=$tls;return=$return;records=$records;state=(Get-OfflineState);protocol=([Net.ServicePointManager]::SecurityProtocol -bor $before) -eq [Net.ServicePointManager]::SecurityProtocol;tls12=if($tls){([Net.ServicePointManager]::SecurityProtocol -band [Net.SecurityProtocolType]::Tls12) -eq [Net.SecurityProtocolType]::Tls12};warnings=@($warnings|ForEach-Object {[string]$_});errors=@($errors|ForEach-Object {$_.FullyQualifiedErrorId});outer=$outer}|ConvertTo-Json -Depth 10 -Compress
   Disconnect-Infoblox;Disconnect-Infoblox
   [pscustomobject]@{kind='disconnected';schema=$schema;skip=$skip;tls=$tls;return=$return;state=(Get-OfflineState)}|ConvertTo-Json -Depth 10 -Compress
  } finally {[Net.ServicePointManager]::SecurityProtocol=$before}
 }}}
}
foreach($encrypted in (ConvertFrom-SecureString $credential.Password),'invalid-encrypted-value') {
 foreach($action in 'Continue','Stop') {
  Set-OfflineSchema -Mode 'present';$warnings=@();$errors=@();$outer=$null;$records=@()
  try {$records=@(Connect-Infoblox -Server 'offline.invalid' -Username 'offline-user' -EncryptedPassword $encrypted -SkipInitialConnection -ReturnObject -ErrorAction $action -ErrorVariable errors -WarningVariable warnings 2>$null 3>$null)}
  catch {$outer=$_.FullyQualifiedErrorId}
  [pscustomobject]@{kind='encrypted-input';valid=($encrypted -ne 'invalid-encrypted-value');action=$action;records=$records;state=(Get-OfflineState);warnings=@($warnings|ForEach-Object {[string]$_});errors=@($errors|ForEach-Object {$_.FullyQualifiedErrorId});outer=$outer}|ConvertTo-Json -Depth 10 -Compress
  Disconnect-Infoblox
 }
}
