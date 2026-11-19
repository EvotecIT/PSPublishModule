param([string]$Manifest,[string]$Evidence)
$ErrorActionPreference='Stop'
$module = Import-Module $Manifest -Force -PassThru 6>$null |
    Where-Object { $_ -is [Management.Automation.PSModuleInfo] -and $_.Name -eq 'Locksmith2' } |
    Select-Object -Last 1
if (!$module) { throw 'The requested module failed to import.' }
$result = & $module {
    param($commands)
    $observations = [ordered]@{}
    $fixturePass=ConvertTo-SecureString 'disposable-fixture' -AsPlainText -Force
    $fixtureCredential=[pscredential]::new('observer@fixture.invalid',$fixturePass)
    Set-LS2Forest -Forest 'fixture.invalid'
    Set-LS2Credential -Credential $fixtureCredential
    $connection = Resolve-LS2ConnectionContext -Forest 'fixture.invalid' -Credential $fixtureCredential
    $observations.connection = [ordered]@{
        forest = $script:Forest
        sameCredential = [object]::ReferenceEquals($fixtureCredential, $script:Credential)
        resolved = $connection.Method
        resolvedIdentity = [object]::ReferenceEquals($fixtureCredential, $connection.Credential)
        derived = (Resolve-LS2ConnectionContext -Credential $fixtureCredential).Forest
    }
    $observations.dns = @('CN=Example,DC=fixture,DC=invalid','CN=Example' | & $commands['Get-ForestNameFromDN'])
    $template = & $commands['Expand-IssueTemplate'] -Variables @{Name='sample'} -Config @{
        IssueTemplate = @('Readiness: ','$(Name)')
        FixTemplate = 'Review $(Name)'
        RevertTemplate = 'Archive $(Name)'
    }
    $observations.templating = [ordered]@{ issue = $template.Issue; fix = $template.Fix; revert = $template.Revert }
    $observations.hostShape = @(
        (& $commands['Test-IsPowerShellCore']), (Test-IsWindows), (Test-IsSupportedPS), (Test-IsSupportedOS),
        (Test-IsUtf8), (Test-IsInteractiveSession), (Test-IsLocalAdmin), (Test-IsModuleLoaded -Name 'Microsoft.PowerShell.Utility'),
        (Test-IsModuleAvailable -Name 'Microsoft.PowerShell.Utility'))
    function script:Install-Module { throw 'Unexpected host installation in observation.' }
    function script:Import-Module { throw 'Unexpected host import in observation.' }
    function script:Find-Module { throw 'Unexpected repository access in observation.' }
    $observations.loadedModuleNoInstall = Install-NeededModule -Name 'Microsoft.PowerShell.Utility'
    $moduleError = @(Test-IsLatestVersion -Name 'PowerForge.Observer.Missing' -ErrorAction Continue 2>&1)
    $observations.absentModule = @($moduleError|ForEach-Object {
        if ($_ -is [Management.Automation.ErrorRecord]) {
            [ordered]@{ category = $_.CategoryInfo.Category.ToString(); id = $_.FullyQualifiedErrorId }
        } else { $_ }
    })
    function script:Read-Host { param([string]$Prompt) 'n' }
    $observations.choice = Read-Choice -Question 'Fixture choice' -Options 'y','n'
    $repair = Repair-PowerShellEnvironment -EnvironmentTest @{IsWindows=$true;IsSupportedOS=$true;IsSupportedPS=$true;IsUtf8=$true} -SkipProfileUpdate
    $observations.repair = [ordered]@{success=$repair.Success; changedEncoding=$repair.EncodingRepaired; changedProfile=$repair.ProfileUpdated; issues=@($repair.RemainingIssues)}
    $observations
} $module.ExportedCommands
if (!$result.loadedModuleNoInstall -or !$result.repair.success -or !$result.connection.resolvedIdentity -or
    $result.choice -ne 'n' -or $result.dns -join ';' -ne 'fixture.invalid;Unknown' -or
    $result.templating.issue -cne 'Readiness: sample' -or $result.hostShape[0] -ne $true) {
    throw 'The module failed to meet the local utility fixture contract.'
}
$result | ConvertTo-Json -Depth 8 -Compress | Set-Content -LiteralPath $Evidence -Encoding utf8
