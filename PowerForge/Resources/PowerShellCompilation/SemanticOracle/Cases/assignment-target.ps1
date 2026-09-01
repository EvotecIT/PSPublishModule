$Values = [string[]] ('value', 'old')
$Values[1] = '42'
$Holder = [System.UriBuilder]::new('https://example.test')
$Holder.Host = [string]::Concat($Values)
[System.Globalization.CultureInfo]::DefaultThreadCurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('en-US')
if ([System.Globalization.CultureInfo]::DefaultThreadCurrentCulture.Name -ne 'en-US') {
    throw [System.InvalidOperationException]::new('Static culture assignment did not persist.')
}
[string]::CompareOrdinal($Holder.Host, 'value42')
