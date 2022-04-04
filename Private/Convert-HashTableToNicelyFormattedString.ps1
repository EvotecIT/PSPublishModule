function Convert-HashTableToNicelyFormattedString($hashTable) {
    [string] $nicelyFormattedString = $hashTable.Keys | ForEach-Object `
    {
        $key = $_
        $value = $hashTable.$key
        "  $key = $value$NewLine"
    }
    return $nicelyFormattedString
}
