param([string]$Manifest,[string]$ScratchRoot,[string]$Evidence)
$ErrorActionPreference='Stop'
$localRoot=(New-Item -ItemType Directory -Path $ScratchRoot -Force).FullName
$profilePath=Join-Path $localRoot 'DisposableProfile.ps1'
Set-Content -LiteralPath $profilePath -Encoding utf8NoBOM -Value "Write-Output 'fixture'"
Set-Variable -Scope Global -Name PROFILE -Value $profilePath -Force
$loaded=Import-Module $Manifest -Force -PassThru -ErrorAction Stop 6>$null |
    Where-Object {$_ -is [Management.Automation.PSModuleInfo] -and $_.Name -eq 'Locksmith2'}|Select-Object -Last 1
if(!$loaded){throw 'Expected module did not load.'}
$results=& $loaded {
    param($testProfile, $commands)
    Set-Variable -Scope Script -Name PROFILE -Value $testProfile -Force
    if($PROFILE -cne $testProfile){throw 'Profile path was not isolated.'}
    $oldTerminal=$env:WT_SESSION
    $oldEncoding=[Console]::OutputEncoding
    try {
        $env:WT_SESSION=$null
        $missing=Test-IsWindowsTerminal
        $env:WT_SESSION='fixture'
        $present=Test-IsWindowsTerminal
        $null=Update-OutputEncoding
        $encoding=[Console]::OutputEncoding.CodePage
        $null=Update-DollarSignProfile
        $after=[IO.File]::ReadAllText($PROFILE)
        $null=Update-DollarSignProfile
        $same=[IO.File]::ReadAllText($PROFILE) -ceq $after
        $diagnostic=@(Show-ConnectionDiagnostics -Forest 'fixture.invalid' -Credential $null -RootDSE $null 6>&1)
        [ordered]@{
            colors=@((& $commands['ConvertTo-ConsoleColor'] -R 0 -G 0 -B 0).ToString(),
                (& $commands['ConvertTo-ConsoleColor'] -R 255 -G 255 -B 255).ToString(),
                (& $commands['Get-TrueColorFg'] -R 1 -G 2 -B 3), (& $commands['Get-TrueColorBg'] -R 4 -G 5 -B 6))
            terminal=@($missing,$present);encoding=$encoding;profile=$after;idempotent=$same
            diagnostic=@($diagnostic|ForEach-Object {$_.MessageData.ToString()})
        }
    } finally {
        [Console]::OutputEncoding=$oldEncoding
        $env:WT_SESSION=$oldTerminal
    }
} $profilePath $loaded.ExportedCommands
if ($results.encoding -ne 65001 -or !$results.idempotent -or
    $results.colors[0] -cne 'Black' -or $results.colors[1] -cne 'White' -or
    !$results.colors[2].StartsWith([string][char]27) -or !$results.colors[3].StartsWith([string][char]27) -or
    $results.terminal[0] -or !$results.terminal[1] -or !$results.diagnostic.Count) {
    throw 'The console and disposable profile observation did not meet the fixture contract.'
}
$results|ConvertTo-Json -Depth 7 -Compress | Set-Content -LiteralPath $Evidence -Encoding utf8
