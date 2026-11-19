param([Parameter(Mandatory)] [string] $Manifest, [Parameter(Mandatory)] [string] $Evidence)
$Target = $env:POWERFORGE_EXTERNAL_AD_AUDIT_TARGET
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$report = [ordered]@{ host = $PSVersionTable.PSVersion.ToString(); imported = $false; invoked = $false; directoryContext = $false;
    successRecords = 0; errors = 0; warnings = 0; info = 0; exceptionType = $null; category = $null }
try {
    $loaded = Import-Module $Manifest -PassThru -ErrorAction Stop 6>$null |
        Where-Object { $_ -is [System.Management.Automation.PSModuleInfo] -and $_.Name -eq 'Locksmith2' } | Select-Object -Last 1
    if ($null -eq $loaded) { throw 'Import did not return the requested module.' }
    $report.imported = $true
    $records = @(Invoke-Locksmith2 -Forest $Target -SkipPowerShellCheck -SkipVersionCheck -Scans Auditing -Verbose:$false -ErrorAction Stop *>&1)
    $report.invoked = $true
    $report.directoryContext = ($null -ne $loaded.SessionState.PSVariable.GetValue('script:RootDSE'))
    $store = $loaded.SessionState.PSVariable.GetValue('script:AdcsObjectStore')
    $report.hasDirectoryObjects = ($null -ne $store -and $store.Count -gt 0)
    foreach ($record in $records) {
        if ($record -is [System.Management.Automation.ErrorRecord]) { $report.errors++ }
        elseif ($record -is [System.Management.Automation.WarningRecord]) { $report.warnings++ }
        elseif ($record -is [System.Management.Automation.InformationRecord]) { $report.info++ }
        elseif ($record -isnot [System.Management.Automation.VerboseRecord] -and $record -isnot [System.Management.Automation.DebugRecord]) { $report.successRecords++ }
    }
} catch {
    $report.exceptionType = $_.Exception.GetType().Name
    $report.category = $_.CategoryInfo.Category.ToString()
}
if (!$report.invoked -or !$report.directoryContext -or !$report.hasDirectoryObjects -or $report.errors -or $report.exceptionType) {
    throw "The read-only audit did not complete. Category: $($report.category). Exception: $($report.exceptionType)."
}
$report | ConvertTo-Json -Compress | Set-Content -LiteralPath $Evidence -Encoding utf8
