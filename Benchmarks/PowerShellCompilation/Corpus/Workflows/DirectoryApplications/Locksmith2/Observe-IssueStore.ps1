param([string]$Manifest,[string]$Evidence)
$ErrorActionPreference='Stop'
$module=Import-Module $Manifest -Force -PassThru 6>$null |
    Where-Object { $_ -is [Management.Automation.PSModuleInfo] -and $_.Name -eq 'Locksmith2' } | Select-Object -Last 1
if (!$module) { throw 'The requested module failed to import.' }
$result = & $module {
    param($commands)
    $observation=[ordered]@{}
    $account=[Security.Principal.NTAccount]::new('Fixture','User')
    $observation.account=(& $commands['Convert-IdentityReferenceToNTAccount'] -SecurityIdentifier $account).Value
    $observation.display=@([pscustomobject]@{displayName='Friendly';name='Internal'},[pscustomobject]@{cn='Common'} | & $commands['Get-AdcsObjectName'])
    $type=([System.Management.Automation.PSTypeName]'LS2AdcsObject').Type
    $object=[Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($type)
    $observation.override=(& $commands['Get-IssueTextOverride'] -Config @{marker='default';Overrides=@()} -AdcsObject $object).marker
    $distinguishedName='CN=Example,DC=fixture,DC=invalid'
    $issue=[LS2Issue]::new(@{DistinguishedName=$distinguishedName;Technique='Fixture';Name='Example'})
    $script:IssueStore=@{}
    $script:PrincipalStore=@{}
    & $commands['Add-ToIssueStore'] -DistinguishedName $distinguishedName -Technique 'Fixture' -Issue $issue
    $observation.issueCount = & $commands['Get-IssueCount'] -Technique 'Fixture'
    $stores = & $commands['Get-LS2Stores']
    $observation.storeRetainsIssue = [object]::ReferenceEquals($issue, $stores.IssueStore[$distinguishedName]['Fixture'][0])
    $observation.issueExists=Test-IssueExists -Issue $issue -DistinguishedName $distinguishedName -Technique 'Fixture'
    $observation.expansionKeepsIdentity=[object]::ReferenceEquals($issue,($issue | Expand-IssueByGroup))
    $script:ESCDefinitions = @{ Fixture = @{ BaseScore = 2; CrossESCModifiers = @() } }
    $null = & $commands['Set-LS2RiskRating'] -Issues @($issue)
    $observation.riskScore=$issue.RiskValue
    $observation.principalScore=(& $commands['Get-PrincipalRiskBonus']).Score
    $report=@(Show-IssueReport -Issues @($issue) -Mode 0 6>&1)
    $observation.issueReportRecords=$report.Count
    $observation.callerModule=Get-ModuleThatLoadedThisFunction
    $environment=Test-PowerShellEnvironment
    $observation.environment=@($environment.IsWindows,$environment.IsSupportedOS,$environment.IsSupportedPS)
    function script:Initialize-LS2Scan { $true }
    $script:IssueStore=@{}
    $observation.objectRecords=@(Find-LS2VulnerableObject 3>&1).Count
    $observation.templateRecords=@(Find-LS2VulnerableTemplate 3>&1).Count
    function script:New-AuthenticatedDirectoryEntry { param([string]$Path) [DirectoryServices.DirectoryEntry]::new() }
    $script:Server='fixture.invalid'
    $searcher=New-LDAPSearcher -DomainDN 'DC=fixture,DC=invalid' -Filter '(objectClass=*)' -PropertiesToLoad 'distinguishedName'
    try {
        $observation.searcher=[ordered]@{filter=$searcher.Filter;fields=@($searcher.PropertiesToLoad)}
    } finally {
        $searcher.Dispose()
    }
    $observation
} $module.ExportedCommands
if (!$result.issueExists -or !$result.expansionKeepsIdentity -or !$result.issueReportRecords -or
    $result.display -join ',' -cne 'Friendly,Common' -or $result.searcher.filter -cne '(objectClass=*)' -or
    $result.searcher.fields.Count -ne 1 -or $result.searcher.fields[0] -cne 'distinguishedName' -or
    $result.issueCount -ne 1 -or !$result.storeRetainsIssue -or $result.riskScore -ne 2 -or
    $result.principalScore -ne 1 -or $result.account -cne 'Fixture\User' -or $result.override -cne 'default') {
    throw 'The issue store and LDAP searcher observation did not meet the fixture contract.'
}
$result | ConvertTo-Json -Depth 8 -Compress | Set-Content -LiteralPath $Evidence -Encoding utf8
