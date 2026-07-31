function ConvertMultidimensionalArrayToPowerShellDefaultValue(
  [System.Array]$value,
  [System.Collections.IList]$referenceStack
) {
  $rank = $value.Rank
  $lengths = [System.Collections.Generic.List[string]]::new()
  $lowerBounds = [System.Collections.Generic.List[string]]::new()
  $indices = [int[]]::new($rank)
  for ($dimension = 0; $dimension -lt $rank; $dimension++) {
    $lengths.Add($value.GetLength($dimension).ToString([System.Globalization.CultureInfo]::InvariantCulture))
    $indices[$dimension] = $value.GetLowerBound($dimension)
    $lowerBounds.Add($indices[$dimension].ToString([System.Globalization.CultureInfo]::InvariantCulture))
  }

  $elementTypeName = GetCanonicalTypeNameFromType ($value.GetType().GetElementType())
  $statements = [System.Collections.Generic.List[string]]::new()
  $statements.Add(
    '$array = [System.Array]::CreateInstance([' + $elementTypeName +
    '], [int[]]@(' + ($lengths -join ', ') + '), [int[]]@(' + ($lowerBounds -join ', ') + '))')
  for ($position = 0; $position -lt $value.Length; $position++) {
    $indexText = ($indices | ForEach-Object {
      $_.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }) -join ', '
    $itemExpression = ConvertToPowerShellDefaultValue ($value.GetValue($indices)) $referenceStack
    $statements.Add('$array.SetValue(' + $itemExpression + ', [int[]]@(' + $indexText + '))')
    for ($dimension = $rank - 1; $dimension -ge 0; $dimension--) {
      $indices[$dimension]++
      if ($indices[$dimension] -lt
          ($value.GetLowerBound($dimension) + $value.GetLength($dimension))) { break }
      $indices[$dimension] = $value.GetLowerBound($dimension)
    }
  }
  $statements.Add('Write-Output -NoEnumerate $array')
  return ('& { ' + ($statements -join '; ') + ' }')
}
