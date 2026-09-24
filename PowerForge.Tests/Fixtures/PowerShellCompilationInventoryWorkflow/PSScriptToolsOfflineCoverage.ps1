param([string]$ModulePath)
$ErrorActionPreference = 'Stop'
$module = Import-Module -Name $ModulePath -PassThru -ErrorAction Stop
try {
    $rows = @(
        [pscustomobject]@{Name='a'; LastWriteTime=[datetime]'2024-01-01'},
        [pscustomobject]@{Name='b'; LastWriteTime=[datetime]'2024-02-01'},
        [pscustomobject]@{Name='c'; LastWriteTime=[datetime]'2024-03-01'}
    )
    $first = @($rows | PSScriptTools\Select-First -First 2 | ForEach-Object Name)
    $last = @($rows | PSScriptTools\Select-Last -Last 2 | ForEach-Object Name)
    $after = @($rows | PSScriptTools\Select-After -After ([datetime]'2024-02-01') | ForEach-Object Name)
    $before = @($rows | PSScriptTools\Select-Before -Before ([datetime]'2024-02-01') | ForEach-Object Name)
    $newest = @($rows | PSScriptTools\Select-Newest -Newest 2 | ForEach-Object Name)
    $oldest = @($rows | PSScriptTools\Select-Oldest -Oldest 2 | ForEach-Object Name)
    $joined = PSScriptTools\Join-Hashtable -First @{alpha=1} -Second @{beta=2}
    $gradient = PSScriptTools\New-RedGreenGradient -Percent 0.04 -Step 10 -Character 'x'
    $border = @(PSScriptTools\Add-Border -Text 'ok' -Character '=')
    $utc = [datetime]::SpecifyKind([datetime]'2024-01-02T03:04:05', [DateTimeKind]::Utc)
    $local = PSScriptTools\ConvertFrom-UTCTime -DateTime $utc
    $windows = PSScriptTools\Test-IsPSWindows
    [pscustomobject]@{
        first=$first; last=$last; after=$after; before=$before; newest=$newest; oldest=$oldest
        joined=@($joined.Keys | Sort-Object | ForEach-Object { "$_=$($joined[$_])" })
        gradient=[string]$gradient; border=$border
        local=$local.ToString('o'); localType=$local.GetType().FullName; localKind=$local.Kind.ToString()
        windows=$windows; windowsType=$windows.GetType().FullName
        errorCount=@($Error).Count
    } | ConvertTo-Json -Depth 6 -Compress
} finally {
    Remove-Module -Name $module.Name -ErrorAction Stop
}
