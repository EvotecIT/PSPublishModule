function Get-ADRootDSE {
 [CmdletBinding()]param($Server,$Credential)
 $script:ProviderTrace.Add("root:$Server");$script:OfflineRoot
}
function ConvertFrom-DistinguishedName {param($DistinguishedName,[switch]$ToDomainCN) 'offline.invalid'}
function Get-ADDomainController {
 [CmdletBinding()]param($DomainName,[switch]$Discover,$Credential)
 $script:ProviderTrace.Add("controller:$DomainName");[pscustomobject]@{HostName=@('dc.offline.invalid')}
}
function Get-ADObject {
 [CmdletBinding()]param($SearchBase,$LDAPFilter,$Properties,$Server,$Credential)
 $script:ProviderTrace.Add("objects:$SearchBase|$LDAPFilter|$Server")
 if($script:ProviderFailure){throw 'owned offline schema failure'}
 if($LDAPFilter -eq '(schemaidguid=*)'){$script:SchemaRows}else{$script:RightsRows}
}
