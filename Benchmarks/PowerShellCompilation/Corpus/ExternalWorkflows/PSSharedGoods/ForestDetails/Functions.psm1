function Get-ADForest {
 [CmdletBinding()]param($Identity,$Credential)
 $script:Trace.Add("forest|credential:$($null -ne $Credential)")
 if($script:Case -eq 'forest-failure'){throw 'owned forest failure'}
 if($script:Case -eq 'empty-forest'){return}
 [ordered]@{Name='root.offline.invalid';RootDomain='root.offline.invalid';Domains=@('root.offline.invalid','child.offline.invalid')}
}
function Get-ADDomainController {
 [CmdletBinding()]param($DomainName,[switch]$Discover,[switch]$Writable,$Filter,$Server,$Identity,$Credential)
 if($Discover){$script:Trace.Add("discover:$DomainName|writable:$Writable|credential:$($null -ne $Credential)");if($script:Case -eq 'discovery-failure' -and $DomainName -like 'child*'){throw 'owned discovery failure'};return $script:Controllers[$DomainName][0]}
 $domain=if($Server -like '*child*'){'child.offline.invalid'}else{'root.offline.invalid'}
 if($Identity){$script:Trace.Add("replica:$Identity");if($script:Case -eq 'fallback-partial' -and $Identity -like 'rodc*'){throw 'owned replica failure'};return @($script:Controllers[$domain]|Where-Object HostName -eq $Identity)}
 $script:Trace.Add("list:$domain|credential:$($null -ne $Credential)")
 if($script:Case -in 'typed-fallback','fallback-partial'){throw [Microsoft.ActiveDirectory.Management.ADIdentityNotFoundException]::new('owned typed listing failure')}
 if($script:Case -eq 'listing-failure'){throw [InvalidOperationException]::new('owned ordinary listing failure')}
 $script:Controllers[$domain]
}
function Get-ADDomain {
 [CmdletBinding()]param($Identity,$Server,$Credential)
 $domain=if($Identity){$Identity}elseif($Server -like '*child*'){'child.offline.invalid'}else{'root.offline.invalid'}
 $script:Trace.Add("domain:$domain")
 [pscustomobject]@{ReplicaDirectoryServers=@($script:Controllers[$domain]|ForEach-Object {$_.HostName});NetBIOSName=if($domain -like 'child*'){'CHILD'}else{'ROOT'};DNSRoot=$domain}
}
function Get-ADObject {[CmdletBinding()]param($Identity,$Server,$Credential) $script:Trace.Add("object:$Identity");[pscustomobject]@{ObjectGUID=[guid]'12345678-1234-1234-1234-1234567890ab'}}
function ConvertTo-OperatingSystem {param($OperatingSystem,$OperatingSystemVersion) 'owned OS'}
function Test-Connection {param($ComputerName,[switch]$Quiet,$Count) $script:Trace.Add("ping:$ComputerName");$true}
function Test-WinRM {param($ComputerName) $script:Trace.Add("winrm:$ComputerName");[pscustomobject]@{Status=$true}}
function Test-ComputerPort {param($Server,$PortTCP,$Timeout) $script:Trace.Add("port:$Server");[pscustomobject]@{Status=$true}}
