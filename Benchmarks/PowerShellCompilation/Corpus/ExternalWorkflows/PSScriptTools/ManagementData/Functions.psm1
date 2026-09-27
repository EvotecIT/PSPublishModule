function Read-BorrowedCimSession {param([Microsoft.Management.Infrastructure.CimSession]$Session) $Session}
function Read-BorrowedCimSessions {[CmdletBinding()]param([Parameter(ValueFromPipeline)][Microsoft.Management.Infrastructure.CimSession[]]$Sessions) process {foreach($session in $Sessions){$session}}}
function Read-BorrowedCimSessionMap {param([Collections.Generic.Dictionary[string,Microsoft.Management.Infrastructure.CimSession]]$Sessions) $Sessions['one']}
function Read-BorrowedCimClass {param([Microsoft.Management.Infrastructure.CimClass]$Class) $Class}
function Read-HostCommonParameters {[System.Management.Automation.Cmdlet]::CommonParameters}
function Test-IsPSWindows {$script:OfflineWindows}
function Get-CimNamespace {param($CimSession) $script:ProviderTrace.Add("namespace");$script:OfflineNamespaces}

function New-OfflineOwnedCimSession {[Microsoft.Management.Infrastructure.CimSession]::Create("factory.compiler-offline.invalid")}
function Read-OfflineCimType {[Microsoft.Management.Infrastructure.CimClass]}
