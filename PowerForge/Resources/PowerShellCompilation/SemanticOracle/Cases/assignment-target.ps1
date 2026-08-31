$Values = [string[]] ('value', 'old')
$Values[1] = '42'
$Holder = [System.UriBuilder]::new('https://example.test')
$Holder.Host = [string]::Concat($Values)
[string]::CompareOrdinal($Holder.Host, 'value42')
