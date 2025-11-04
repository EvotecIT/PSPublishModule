
$JSON = Get-Content -LiteralPath "C\"    


$Test = $JSON | ConvertFrom-Json
$Test