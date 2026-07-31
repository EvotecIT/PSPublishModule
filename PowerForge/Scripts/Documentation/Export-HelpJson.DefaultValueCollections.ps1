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
    $statements.Add('$array.SetValue((' + $itemExpression + '), [int[]]@(' + $indexText + '))')
    for ($dimension = $rank - 1; $dimension -ge 0; $dimension--) {
      $indices[$dimension]++
      if ($indices[$dimension] -lt
          ($value.GetLowerBound($dimension) + $value.GetLength($dimension))) { break }
      $indices[$dimension] = $value.GetLowerBound($dimension)
    }
  }
  $statements.Add('return ,$array')
  return ('& { ' + ($statements -join '; ') + ' }')
}

function ConvertDictionaryToPowerShellDefaultValue(
  [System.Collections.IDictionary]$value,
  [System.Collections.IList]$referenceStack
) {
  $statements = [System.Collections.Generic.List[string]]::new()
  $dictionaryTypeName = GetConstructibleDictionaryTypeName $value
  if ([string]::IsNullOrWhiteSpace($dictionaryTypeName)) {
    $dictionaryTypeName = 'System.Collections.Specialized.OrderedDictionary'
  }
  $statements.Add('$dictionary = [' + $dictionaryTypeName + ']::new()')
  foreach ($entry in $value.GetEnumerator()) {
    $keyExpression = ConvertToPowerShellDefaultValue $entry.Key $referenceStack
    $valueExpression = ConvertToPowerShellDefaultValue $entry.Value $referenceStack
    $statements.Add('$dictionary.Add((' + $keyExpression + '), (' + $valueExpression + '))')
  }
  $statements.Add('return ,$dictionary')
  return ('& { ' + ($statements -join '; ') + ' }')
}

function ConvertCollectionItemsToPowerShellDefaultValue(
  [System.Collections.Generic.IReadOnlyList[string]]$items,
  [bool]$containsNestedCollection
) {
  if (-not $containsNestedCollection) {
    return ('@(' + ($items -join ', ') + ')')
  }
  $statements = [System.Collections.Generic.List[string]]::new()
  $statements.Add('$array = [object[]]::new(' + $items.Count + ')')
  for ($index = 0; $index -lt $items.Count; $index++) {
    $statements.Add('$array.SetValue((' + $items[$index] + '), ' + $index + ')')
  }
  $statements.Add('return ,$array')
  return ('& { ' + ($statements -join '; ') + ' }')
}
