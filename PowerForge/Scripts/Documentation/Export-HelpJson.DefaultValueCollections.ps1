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

  $elementType = $value.GetType().GetElementType()
  $elementTypeName = GetCanonicalTypeNameFromType $elementType
  $elementTypeExpression = GetPowerShellTypeDefaultExpression $elementType
  if (-not (TestPowerShellTypeLiteral $elementType)) {
    $elementTypeExpression = '(' + $elementTypeExpression + ')'
  }
  $statements = [System.Collections.Generic.List[string]]::new()
  $statements.Add(
    '$array = [System.Array]::CreateInstance(' + $elementTypeExpression +
    ', [int[]]@(' + ($lengths -join ', ') + '), [int[]]@(' + ($lowerBounds -join ', ') + '))')
  for ($position = 0; $position -lt $value.Length; $position++) {
    $indexText = ($indices | ForEach-Object {
      $_.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }) -join ', '
    $itemExpression = ConvertToPowerShellDefaultValue ($value.GetValue($indices)) $referenceStack
    $statements.Add('$array.SetValue((' + $itemExpression + '), [int[]]@(' + $indexText + '))')
    for ($dimension = $rank - 1; $dimension -ge 0; $dimension--) {
      if ($indices[$dimension] -lt $value.GetUpperBound($dimension)) {
        $indices[$dimension]++
        break
      }
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
  $constructorExpression = GetDictionaryConstructorExpression $value $referenceStack
  $statements.Add('$dictionary = ' + $constructorExpression)
  foreach ($entry in $value.GetEnumerator()) {
    $keyExpression = ConvertToPowerShellDefaultValue $entry.Key $referenceStack
    $valueExpression = ConvertToPowerShellDefaultValue $entry.Value $referenceStack
    $statements.Add('([System.Collections.IDictionary]$dictionary).Add((' + $keyExpression + '), (' + $valueExpression + '))')
  }
  $statements.Add('return ,$dictionary')
  return ('& { ' + ($statements -join '; ') + ' }')
}

function TestCollectionHasItemOnlyBackingStore(
  [object]$value,
  [System.Collections.IList]$referenceStack = $null
) {
  $collectionType = $value.GetType()
  if (-not $collectionType.IsGenericType -or
      $collectionType.GetGenericTypeDefinition() -ne [System.Collections.ObjectModel.Collection``1]) {
    return $true
  }
  $itemsProperty = $collectionType.GetProperty(
    'Items',
    [System.Reflection.BindingFlags]'Instance,NonPublic')
  if ($null -eq $itemsProperty) { return $false }
  $backingStore = $null
  try { $backingStore = $itemsProperty.GetValue($value, $null) } catch { return $false }
  if ($null -eq $backingStore) { return $false }
  $expectedType = [System.Collections.Generic.List``1].MakeGenericType(
    $collectionType.GetGenericArguments()[0])
  if ($backingStore.GetType() -ne $expectedType) { return $false }
  if ($null -ne $referenceStack) {
    AddDefaultValueReference $backingStore $referenceStack
  }
  $expectedCollection = [System.Activator]::CreateInstance($collectionType)
  foreach ($item in $value) {
    [void]([System.Collections.IList]$expectedCollection).Add($item)
  }
  $expectedBackingStore = $itemsProperty.GetValue($expectedCollection, $null)
  return $backingStore.Capacity -eq $expectedBackingStore.Capacity
}

function GetCollectionCapacity([object]$value) {
  $collectionType = $value.GetType()
  if ($collectionType -eq [System.Collections.ArrayList]) {
    return [int]$value.Capacity
  }
  if ($collectionType.IsGenericType -and
      $collectionType.GetGenericTypeDefinition() -eq [System.Collections.Generic.List``1]) {
    return [int]$value.Capacity
  }
  return $null
}

function ConvertCollectionItemsToPowerShellDefaultValue(
  [System.Collections.Generic.IReadOnlyList[string]]$items,
  [object]$value,
  [System.Collections.IList]$referenceStack = $null
) {
  $collectionType = $value.GetType()
  $collectionTypeName = GetCanonicalTypeNameFromType $collectionType
  if ($value -isnot [System.Array]) {
    $supportedItemOnlyList = [object]::ReferenceEquals(
      $collectionType,
      [System.Collections.ArrayList])
    if (-not $supportedItemOnlyList -and $collectionType.IsGenericType) {
      $genericDefinition = $collectionType.GetGenericTypeDefinition()
      $supportedItemOnlyList =
        [object]::ReferenceEquals($genericDefinition, [System.Collections.Generic.List``1]) -or
        ([object]::ReferenceEquals($genericDefinition, [System.Collections.ObjectModel.Collection``1]) -and
          (TestCollectionHasItemOnlyBackingStore $value $referenceStack))
    }
    if (-not $supportedItemOnlyList) {
      throw ('Collection type carries unsupported non-item state: ' + $collectionType.FullName)
    }
    $constructor = $collectionType.GetConstructor([System.Type]::EmptyTypes)
    if ($collectionType.IsAbstract -or $collectionType.IsInterface -or $null -eq $constructor) {
      throw ('Collection type has no supported constructor: ' + $collectionType.FullName)
    }
  }
  $statements = [System.Collections.Generic.List[string]]::new()
  if ($value -is [System.Array]) {
    $elementTypeName = GetCanonicalTypeNameFromType ($collectionType.GetElementType())
    if (TestPowerShellTypeLiteral ($collectionType.GetElementType())) {
      $statements.Add('$collection = [' + $collectionTypeName + ']::new(' + $items.Count + ')')
    } else {
      $elementTypeExpression = GetPowerShellTypeDefaultExpression ($collectionType.GetElementType())
      $statements.Add('$collection = [System.Array]::CreateInstance((' + $elementTypeExpression + '), ' + $items.Count + ')')
    }
    for ($index = 0; $index -lt $items.Count; $index++) {
      $statements.Add('$collection.SetValue((' + $items[$index] + '), ' + $index + ')')
    }
  } else {
    $capacity = GetCollectionCapacity $value
    $constructorArgument = if ($null -eq $capacity) {
      ''
    } else {
      '([int]' + $capacity.ToString([System.Globalization.CultureInfo]::InvariantCulture) + ')'
    }
    if (TestPowerShellTypeLiteral $collectionType) {
      $statements.Add('$collection = [' + $collectionTypeName + ']::new(' + $constructorArgument + ')')
    } else {
      $collectionTypeExpression = GetPowerShellTypeDefaultExpression $collectionType
      if ([string]::IsNullOrWhiteSpace($constructorArgument)) {
        $statements.Add('$collection = [System.Activator]::CreateInstance((' + $collectionTypeExpression + '))')
      } else {
        $statements.Add('$collection = [System.Activator]::CreateInstance((' + $collectionTypeExpression +
          '), [object[]]@((' + $constructorArgument + ')))')
      }
    }
    foreach ($item in $items) {
      $statements.Add('[void]([System.Collections.IList]$collection).Add((' + $item + '))')
    }
  }
  $statements.Add('return ,$collection')
  return ('& { ' + ($statements -join '; ') + ' }')
}
