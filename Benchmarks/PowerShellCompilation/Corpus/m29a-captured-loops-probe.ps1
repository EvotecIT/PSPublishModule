foreach($name in 'Read-CapturedDirect','Read-CapturedArray','Read-CapturedNested','Read-CapturedSwitch','Read-CapturedMember') {
    foreach($mode in 'none','break','continue','throw','break-finally','continue-finally') {
        $errors=@()
        $records=@(& $name -Mode $mode -ErrorVariable errors 2>$null)
        [pscustomobject]@{name=$name;mode=$mode;records=$records;errors=@($errors | ForEach-Object {$_.FullyQualifiedErrorId})} | ConvertTo-Json -Depth 9 -Compress
    }
}
$warnings=@()
$records=@(Read-ForestRecovery -WarningAction SilentlyContinue -WarningVariable warnings)
[pscustomobject]@{name='Read-ForestRecovery';records=$records;warnings=@($warnings | ForEach-Object {$_.Message})} | ConvertTo-Json -Depth 9 -Compress
