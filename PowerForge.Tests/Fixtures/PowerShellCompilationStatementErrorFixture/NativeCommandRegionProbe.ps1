param([Parameter(Mandatory)][string]$Assembly)
$ErrorActionPreference='Stop'
Add-Type -Path $Assembly
$regions=@(
    'Write-Output $Value',
    '$Value | ForEach-Object { $Count++; $Seen="changed"; "item=$_;count=$Count;seed=$Seed" }',
    'Set-Variable -Name Seen -Value changed; Get-Variable -Name Seen -ValueOnly; Write-Output $PSBoundParameters.Count',
    '$Value | ForEach-Object { Test-PrivateRegion $_; Set-Variable -Scope 1 -Name Seen -Value callback }',
    'Write-Output before; Write-Error "region failure"; Write-Output "status=$?"; Write-Output after',
    '$Value | ForEach-Object { $Count++; Write-Output $_; Write-Error "item error $_" }; Write-Output "status=$?"',
    'Get-Command DefinitelyMissingNativeRegionCommand; Write-Output after',
    '$Value | ForEach-Object { if ($_ -eq 2) { throw "item failure" }; $_ }; Write-Output after',
    'Write-Output (1/0); Write-Output after',
    '$Value | ForEach-Object { Write-Output $_; Write-Warning "warning $_" }',
    '$Value | ForEach-Object { [Generic.Compiler.StatementErrors.NativeCommandRegionFixture]::Trace.Add("before:$_"); $_; [Generic.Compiler.StatementErrors.NativeCommandRegionFixture]::Trace.Add("after:$_") }'
)
foreach($source in $regions) {
  foreach($preference in 'Continue','SilentlyContinue','Stop','Ignore') {
   foreach($first in $false,$true) {
    $observations=@()
    foreach($compiled in $false,$true) {
        $module=New-Module -ScriptBlock { function Test-PrivateRegion([object]$Item) { "private=$Item" } }
        if($compiled) {
            $body=[Generic.Compiler.StatementErrors.NativeCommandRegionFixture]::Create($module,$source)
        } else {
            $text='[CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1,[object]$Value)'+"`n"+
                '$Seen="before"; $Count=0; $OFS=":"'+"`n`n`n"+$source+"`n"+
                '"after=$Seen;count=$Count"'
            $tokens=$null; $parseErrors=$null
            $parsed=[System.Management.Automation.Language.Parser]::ParseInput($text,'native-region.psm1',[ref]$tokens,[ref]$parseErrors)
            $body=$module.NewBoundScriptBlock($parsed.GetScriptBlock())
        }
        & $module { param($Body) Set-Item -LiteralPath Function:script:Read-Region -Value $Body } $body
        $Error.Clear(); $records=@(); $global:NativeRegionEmitted=@(); $global:NativeRegionErrors=@(); $caught=$null; $global:NativeRegionWarnings=@()
        [Generic.Compiler.StatementErrors.NativeCommandRegionFixture]::Trace.Clear()
        try {
            $consumer={ [Generic.Compiler.StatementErrors.NativeCommandRegionFixture]::Trace.Add("consume:$_"); $_ }
            if($first) {
                $records=@(& $module { param($Preference) Read-Region -Value @(1,2,3) -ErrorAction $Preference -OutVariable global:NativeRegionEmitted -ErrorVariable global:NativeRegionErrors -WarningVariable global:NativeRegionWarnings } $preference 2>$null 3>$null | ForEach-Object $consumer | Select-Object -First 1)
            } else {
                $records=@(& $module { param($Preference) Read-Region -Value @(1,2,3) -ErrorAction $Preference -OutVariable global:NativeRegionEmitted -ErrorVariable global:NativeRegionErrors -WarningVariable global:NativeRegionWarnings } $preference 2>$null 3>$null | ForEach-Object $consumer)
            }
        }
        catch { $caught=$_.FullyQualifiedErrorId; $details=$_.Exception.ToString() }
        $observations+=ConvertTo-Json -InputObject ([ordered]@{records=$records;emitted=@($global:NativeRegionEmitted);warnings=@($global:NativeRegionWarnings | ForEach-Object { $_.Message });trace=@([Generic.Compiler.StatementErrors.NativeCommandRegionFixture]::Trace);caught=$caught;
            errors=@($Error | ForEach-Object { [ordered]@{id=$_.FullyQualifiedErrorId;category=[string]$_.CategoryInfo.Category;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine} })}) -Depth 12 -Compress
    }
    if($observations[0] -cne $observations[1]) { throw "Region mismatch: $source preference=$preference first=$first`nOriginal: $($observations[0])`nGenerated: $($observations[1])`n$details" }
    $observations[1]
   }
  }
}
'Native command-region qualification passed.'
