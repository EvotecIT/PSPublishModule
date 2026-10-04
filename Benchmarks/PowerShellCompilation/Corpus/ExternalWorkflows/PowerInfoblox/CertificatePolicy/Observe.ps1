param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$OutputPath,[ValidateSet('real','failure')][string]$Case='real')
$ErrorActionPreference='Stop'
$module=Import-Module $ModulePath -Force -PassThru
$modern=$PSVersionTable.PSVersion.Major -gt 5
$prior=if(-not $modern){[Net.ServicePointManager]::CertificatePolicy}else{$null}
$rows=[Collections.Generic.List[object]]::new()
try {
 & $module {param($failure) $script:InfobloxConfiguration=@{SkipCertificateValidation=$false};$script:AddTypeCalls=0;if($failure){function script:Add-Type {param($TypeDefinition) $script:AddTypeCalls++;throw 'owned offline type-definition failure'}}} ($Case -eq 'failure')
 foreach($iteration in 1,2) {
  $warnings=@();$caught=$null
  try {Hide-SelfSignedCerts -WarningVariable warnings -WarningAction SilentlyContinue}catch{$caught=[ordered]@{type=$_.Exception.GetType().FullName;id=$_.FullyQualifiedErrorId;message=$_.Exception.Message}}
  $state=& $module {[ordered]@{skip=$script:InfobloxConfiguration['SkipCertificateValidation'];addTypeCalls=$script:AddTypeCalls}}
  $policy=if(-not $modern){[Net.ServicePointManager]::CertificatePolicy}else{$null}
  $rows.Add([ordered]@{iteration=$iteration;state=$state;warnings=@($warnings|ForEach-Object {$_.Message});caught=$caught;policyType=if($policy){$policy.GetType().FullName}else{$null};inertValidation=if($policy -and $policy.GetType().FullName -eq 'TrustAllCertsPolicy'){$policy.CheckValidationResult($null,$null,$null,0)}else{$null};priorPreserved=[object]::ReferenceEquals($prior,$policy)})
 }
} finally {
 if(-not $modern){[Net.ServicePointManager]::CertificatePolicy=$prior}
 $restored=$modern -or [object]::ReferenceEquals($prior,[Net.ServicePointManager]::CertificatePolicy)
 Remove-Module $module -Force
}
if(-not $restored){throw 'Process-local certificate policy was not restored'}
foreach($row in $rows) {
 if($row.caught){throw 'Unexpected terminating error'}
 if($modern) {
  if(-not $row.state.skip -or $row.state.addTypeCalls -ne 0 -or $row.policyType -or $row.warnings.Count){throw 'Modern host must use only its configuration flag'}
 } elseif($Case -eq 'real') {
  if($row.state.skip -or $row.policyType -ne 'TrustAllCertsPolicy' -or -not $row.inertValidation -or $row.warnings.Count){throw 'Legacy host did not install the actual authored policy'}
 } else {
  if(-not $row.priorPreserved -or $row.state.skip -or $row.state.addTypeCalls -ne $row.iteration -or $row.warnings.Count -ne 1 -or $row.warnings[0] -notlike '*owned offline type-definition failure'){throw 'Legacy failure must warn and preserve the prior policy'}
 }
}
[ordered]@{case=$Case;hostVersion=$PSVersionTable.PSVersion.ToString();modern=$modern;restored=$restored;observations=$rows.ToArray()}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $OutputPath -Encoding UTF8
