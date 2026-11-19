param([string]$Manifest,[string]$Evidence)
$ErrorActionPreference='Stop'
$module=Import-Module $Manifest -Force -PassThru 6>$null |
    Where-Object { $_ -is [Management.Automation.PSModuleInfo] -and $_.Name -eq 'Locksmith2' } | Select-Object -Last 1
if (!$module) { throw 'The requested module failed to import.' }
$result = & $module {
    param([string]$Evidence)
    $credential=[pscredential]::new('observer@fixture.invalid',(ConvertTo-SecureString 'disposable-fixture' -AsPlainText -Force))
    $directory=[DirectoryServices.DirectoryEntry]::new('observer-invalid-path')
    try {
        $malformed=@(Initialize-DirectoryConnections -RootDSE $directory -Credential $credential 3>&1)
    } finally {
        [void][IDisposable].GetMethod('Dispose').Invoke($directory,@())
    }
    function script:Initialize-LS2Scan { $true }
    $script:IssueStore = @{}
    $emptyPrincipal=@(Find-LS2RiskyPrincipal -MinimumIssueCount 2 3>&1)
    function script:Get-Command {
        param([string]$Name)
        if ($Name -ne 'New-HTML') { throw 'Unexpected discovery in dashboard fixture.' }
        $null
    }
    $dashboard=@(New-LS2Dashboard -FilePath ([IO.Path]::Combine([IO.Path]::GetDirectoryName($Evidence),'unused-observer-dashboard.html')) -Show:$false -ErrorAction Continue 2>&1)
    [ordered]@{
        malformedDirectory = @($malformed | ForEach-Object {$_.ToString()})
        emptyPrincipal = @($emptyPrincipal | ForEach-Object {$_.ToString()})
        dashboardErrors = @($dashboard | Where-Object { $_ -is [Management.Automation.ErrorRecord] } | ForEach-Object { $_.Exception.Message })
        localDirectoryMembership = Test-IsDomainComputer
    }
} $Evidence
$result | ConvertTo-Json -Depth 8 -Compress | Set-Content -LiteralPath $Evidence -Encoding utf8
if ($result.malformedDirectory.Count -ne 1 -or $result.emptyPrincipal.Count -ne 2 -or
    $result.dashboardErrors.Count -ne 1 -or $result.localDirectoryMembership -isnot [bool]) {
    throw 'Unexpected result from the defensive application fixture.'
}
