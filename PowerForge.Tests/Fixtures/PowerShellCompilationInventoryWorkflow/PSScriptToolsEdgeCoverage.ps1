param([string]$ModulePath)
$ErrorActionPreference = 'Stop'
$module = Import-Module -Name $ModulePath -PassThru -ErrorAction Stop
function Capture([string]$Name, [scriptblock]$Action) {
    try {
        $values = @(& $Action)
        [pscustomobject]@{name=$Name;values=@($values | ForEach-Object { if ($null -eq $_) { '<null>' } else { [string]$_ } });types=@($values | ForEach-Object { if ($null -eq $_) { '<null>' } else { $_.GetType().FullName } });error=''}
    } catch {
        [pscustomobject]@{name=$Name;values=@();types=@();error=($_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName + ':' + $_.CategoryInfo.Category)}
    }
}
try {
    $observed = @(
        Capture 'first-zero' { 1,2 | PSScriptTools\Select-First -First 0 }
        Capture 'first-missing-property' { [pscustomobject]@{Other=1} | PSScriptTools\Select-First -First 1 -Property Name }
        Capture 'after-missing-property' { [pscustomobject]@{Other=1} | PSScriptTools\Select-After -After ([datetime]'2024-01-01') }
        Capture 'join-force' { $h=PSScriptTools\Join-Hashtable -First @{x=1} -Second @{x=2;y=3} -Force; @($h.Keys | Sort-Object | ForEach-Object { "$_=$($h[$_])" }) }
        Capture 'border-empty' { PSScriptTools\Add-Border -Text '' }
        Capture 'gradient-invalid' { PSScriptTools\New-RedGreenGradient -Percent 2 }
        Capture 'utc-null' { PSScriptTools\ConvertFrom-UTCTime -DateTime $null }
        Capture 'windows-repeat' { PSScriptTools\Test-IsPSWindows; PSScriptTools\Test-IsPSWindows }
    )
    $observed | ConvertTo-Json -Depth 5 -Compress
} finally {
    Remove-Module -Name $module.Name -ErrorAction Stop
}
