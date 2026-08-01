function GetText([object]$obj) {
  if ($null -eq $obj) { return '' }
  if ($obj -is [string]) { return [string]$obj }
  try { if ($obj.PSObject -and $obj.PSObject.Properties['Text']) { return [string]$obj.Text } } catch {
    # best effort: Get-Help payload shapes vary across PowerShell versions and object types
  }
  try { return [string]$obj } catch { return '' }
}

function ConvertToUtf16CodeUnits([string]$text) {
  $builder = [System.Text.StringBuilder]::new()
  for ($index = 0; $index -lt $text.Length; $index++) {
    if ($index -gt 0) { [void]$builder.Append(',') }
    [void]$builder.Append(
      ([int]$text[$index]).ToString([System.Globalization.CultureInfo]::InvariantCulture))
  }
  return $builder.ToString()
}

# Escape every surrogate code unit in the JSON text before the UTF-8 file boundary.
# JSON readers reconstruct valid pairs and retain isolated surrogates without U+FFFD loss.
function ConvertToUtf8SafeJsonText([string]$json) {
  $builder = [System.Text.StringBuilder]::new($json.Length)
  for ($index = 0; $index -lt $json.Length; $index++) {
    $codeUnit = [int]$json[$index]
    if ($codeUnit -ge 0xD800 -and $codeUnit -le 0xDFFF) {
      [void]$builder.Append('\u')
      [void]$builder.Append(
        $codeUnit.ToString('X4', [System.Globalization.CultureInfo]::InvariantCulture))
    } else {
      [void]$builder.Append($json[$index])
    }
  }
  return $builder.ToString()
}

function ConvertRuntimeTypeTextToBase64([string]$text) {
  return [System.Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($text))
}

function TestExactRuntimeValueType([object]$value, [object]$expectedType) {
  return $null -ne $value -and
    $null -ne $expectedType -and
    [object]::ReferenceEquals($value.GetType(), $expectedType)
}

function AddRuntimeDefaultValueReference(
  [object]$value,
  [System.Collections.IList]$references
) {
  if ($null -eq $value -or $value -is [type]) {
    return
  }
  if ($value -is [string] -and
      [object]::ReferenceEquals($value, [string]::IsInterned([string]$value))) {
    return
  }
  if ($value -is [System.Array] -and (TestPublicEmptyArraySingleton $value)) {
    return
  }
  foreach ($seenReference in $references) {
    if ([object]::ReferenceEquals($seenReference, $value)) {
      throw 'Repeated or circular default-value object references are not supported.'
    }
  }
  [void]$references.Add($value)
}

function TestPublicEmptyArraySingleton([System.Array]$value) {
  if ($null -eq $value -or $value.Rank -ne 1 -or $value.GetLowerBound(0) -ne 0 -or $value.Length -ne 0) {
    return $false
  }

  $emptyMethod = $null
  foreach ($candidate in [System.Array].GetMethods(
      [System.Reflection.BindingFlags]'Public,Static')) {
    if ($candidate.Name -ceq 'Empty' -and $candidate.IsGenericMethodDefinition -and
        $candidate.GetGenericArguments().Length -eq 1 -and $candidate.GetParameters().Length -eq 0) {
      $emptyMethod = $candidate
      break
    }
  }
  if ($null -eq $emptyMethod) { return $false }
  $singleton = $emptyMethod.MakeGenericMethod($value.GetType().GetElementType()).Invoke(
    $null, [object[]]@())
  return [object]::ReferenceEquals($value, $singleton)
}

function TestValidateSetCaseSensitive(
  [System.Management.Automation.ValidateSetAttribute]$attribute
) {
  try {
    return $attribute.PSObject.Properties['IgnoreCase'] -and -not [bool]$attribute.IgnoreCase
  } catch {
    return $false
  }
}

function TestRecreatableUri([uri]$value) {
  if ($value.UserEscaped) { return $false }
  $uriKind = if ($value.IsAbsoluteUri) { 'Absolute' } else { 'Relative' }
  $reconstructed = [uri]::new($value.OriginalString, [System.UriKind]$uriKind)
  $matches =
    $reconstructed.OriginalString -ceq $value.OriginalString -and
    $reconstructed.ToString() -ceq $value.ToString() -and
    $reconstructed.UserEscaped -eq $value.UserEscaped
  if ($value.IsAbsoluteUri) {
    $matches = $matches -and
      $reconstructed.AbsoluteUri -ceq $value.AbsoluteUri -and
      $reconstructed.PathAndQuery -ceq $value.PathAndQuery
  }
  return $matches
}

function TestRecreatableScriptBlock([scriptblock]$value) {
  if ($null -ne $value.Module) {
    return $false
  }

  try {
    if (-not [string]::IsNullOrEmpty([string]$value.File)) {
      return $false
    }
  } catch {
    return $false
  }

  try {
    $isFilterProperty = [scriptblock].GetProperty(
      'IsFilter',
      [System.Reflection.BindingFlags]'Instance,Public,NonPublic')
    if ($null -eq $isFilterProperty -or -not $isFilterProperty.CanRead -or
        [bool]$isFilterProperty.GetValue($value, $null)) {
      return $false
    }

    $languageModeProperty = [scriptblock].GetProperty(
      'LanguageMode',
      [System.Reflection.BindingFlags]'Instance,Public,NonPublic')
    if ($null -eq $languageModeProperty -or -not $languageModeProperty.CanRead) {
      return $false
    }
    if ($languageModeProperty.GetValue($value, $null) -ne
        [System.Management.Automation.PSLanguageMode]::FullLanguage) {
      return $false
    }

    $sessionStateProperty = [scriptblock].GetProperty(
      'SessionStateInternal',
      [System.Reflection.BindingFlags]'Instance,Public,NonPublic')
    return $null -ne $sessionStateProperty -and
      $sessionStateProperty.CanRead -and
      $null -eq $sessionStateProperty.GetValue($value, $null)
  } catch {
    return $false
  }
}

function GetCoreRuntimeType([string]$fullName) {
  return [datetime].Assembly.GetType($fullName, $false, $false)
}

function TestCollectionHasItemOnlyBackingStore(
  [object]$value,
  [System.Collections.IList]$referenceStack = $null
) {
  $collectionType = $value.GetType()
  if (-not $collectionType.IsGenericType -or
      -not [object]::ReferenceEquals(
        $collectionType.GetGenericTypeDefinition(),
        [System.Collections.ObjectModel.Collection``1])) {
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
  $flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
  $versionField = $expectedType.GetField('_version', $flags)
  if ($null -eq $versionField) { $versionField = $expectedType.GetField('version', $flags) }
  if ($null -eq $versionField -or [int]$versionField.GetValue($backingStore) -ne $value.Count) {
    return $false
  }
  if ($null -ne $referenceStack) {
    AddRuntimeDefaultValueReference $backingStore $referenceStack
  }
  $expectedCollection = [System.Activator]::CreateInstance($collectionType)
  foreach ($item in $value) {
    [void]([System.Collections.IList]$expectedCollection).Add($item)
  }
  $expectedBackingStore = $itemsProperty.GetValue($expectedCollection, $null)
  return $backingStore.Capacity -eq $expectedBackingStore.Capacity
}

function TestPSDefaultValueAutomationNull(
  [System.Management.Automation.PSDefaultValueAttribute]$attribute
) {
  $flags = [System.Reflection.BindingFlags]'Instance,Public,NonPublic'
  $valueField = $attribute.GetType().GetField('<Value>k__BackingField', $flags)
  $automationNullType = [System.Management.Automation.PSObject].Assembly.GetType(
    'System.Management.Automation.Internal.AutomationNull', $false)
  if ($null -eq $valueField -or $null -eq $automationNullType) { return $false }
  $valueProperty = $automationNullType.GetProperty(
    'Value', [System.Reflection.BindingFlags]'Static,Public')
  if ($null -eq $valueProperty) { return $false }

  $attributeParameter = [System.Linq.Expressions.Expression]::Parameter(
    [System.Management.Automation.PSDefaultValueAttribute], 'attribute')
  $fieldExpression = [System.Linq.Expressions.Expression]::Field(
    $attributeParameter, $valueField)
  $sentinelExpression = [System.Linq.Expressions.Expression]::Property(
    $null, $valueProperty)
  $body = [System.Linq.Expressions.Expression]::ReferenceEqual(
    $fieldExpression, $sentinelExpression)
  $delegateType = [System.Func[System.Management.Automation.PSDefaultValueAttribute,bool]]
  $lambda = [System.Linq.Expressions.Expression]::Lambda(
    $delegateType, $body, [System.Linq.Expressions.ParameterExpression[]]@($attributeParameter))
  return $lambda.Compile().Invoke($attribute)
}

function GetAutomationNullValueProperty {
  $automationNullType = [System.Management.Automation.PSObject].Assembly.GetType(
    'System.Management.Automation.Internal.AutomationNull', $false)
  if ($null -eq $automationNullType) { return $null }
  return $automationNullType.GetProperty(
    'Value', [System.Reflection.BindingFlags]'Static,Public')
}

function GetAutomationNullListPredicate {
  if ($null -eq $script:PowerForgeAutomationNullListPredicate) {
    $sentinelProperty = GetAutomationNullValueProperty
    if ($null -eq $sentinelProperty) { return $null }
    $listParameter = [System.Linq.Expressions.Expression]::Parameter(
      [System.Collections.IList], 'list')
    $indexParameter = [System.Linq.Expressions.Expression]::Parameter([int], 'index')
    $itemExpression = [System.Linq.Expressions.Expression]::MakeIndex(
      $listParameter, [System.Collections.IList].GetProperty('Item'),
      [System.Linq.Expressions.Expression[]]@($indexParameter))
    $body = [System.Linq.Expressions.Expression]::ReferenceEqual(
      $itemExpression, [System.Linq.Expressions.Expression]::Property($null, $sentinelProperty))
    $delegateType = [System.Func``3].MakeGenericType(
      [System.Collections.IList], [int], [bool])
    $script:PowerForgeAutomationNullListPredicate = [System.Linq.Expressions.Expression]::Lambda(
      $delegateType, $body,
      [System.Linq.Expressions.ParameterExpression[]]@($listParameter, $indexParameter)).Compile()
  }
  return $script:PowerForgeAutomationNullListPredicate
}

function GetAutomationNullArrayPredicate {
  if ($null -eq $script:PowerForgeAutomationNullArrayPredicate) {
    $sentinelProperty = GetAutomationNullValueProperty
    if ($null -eq $sentinelProperty) { return $null }
    $arrayParameter = [System.Linq.Expressions.Expression]::Parameter([System.Array], 'array')
    $indicesParameter = [System.Linq.Expressions.Expression]::Parameter([int[]], 'indices')
    $itemExpression = [System.Linq.Expressions.Expression]::Call(
      $arrayParameter, [System.Array].GetMethod('GetValue', [type[]]@([int[]])),
      [System.Linq.Expressions.Expression[]]@($indicesParameter))
    $body = [System.Linq.Expressions.Expression]::ReferenceEqual(
      $itemExpression, [System.Linq.Expressions.Expression]::Property($null, $sentinelProperty))
    $delegateType = [System.Func``3].MakeGenericType([System.Array], [int[]], [bool])
    $script:PowerForgeAutomationNullArrayPredicate = [System.Linq.Expressions.Expression]::Lambda(
      $delegateType, $body,
      [System.Linq.Expressions.ParameterExpression[]]@($arrayParameter, $indicesParameter)).Compile()
  }
  return $script:PowerForgeAutomationNullArrayPredicate
}

function GetAutomationNullDictionaryEntryPredicate([bool]$key) {
  $predicateName = if ($key) { 'PowerForgeAutomationNullDictionaryKeyPredicate' } else {
    'PowerForgeAutomationNullDictionaryValuePredicate'
  }
  $predicate = Microsoft.PowerShell.Utility\Get-Variable -Scope Script -Name $predicateName -ValueOnly -ErrorAction SilentlyContinue
  if ($null -eq $predicate) {
    $sentinelProperty = GetAutomationNullValueProperty
    if ($null -eq $sentinelProperty) { return $null }
    $entryParameter = [System.Linq.Expressions.Expression]::Parameter(
      [System.Collections.DictionaryEntry], 'entry')
    $itemExpression = [System.Linq.Expressions.Expression]::Property(
      $entryParameter, $(if ($key) { 'Key' } else { 'Value' }))
    $body = [System.Linq.Expressions.Expression]::ReferenceEqual(
      $itemExpression, [System.Linq.Expressions.Expression]::Property($null, $sentinelProperty))
    $delegateType = [System.Func[System.Collections.DictionaryEntry,bool]]
    $predicate = [System.Linq.Expressions.Expression]::Lambda(
      $delegateType, $body,
      [System.Linq.Expressions.ParameterExpression[]]@($entryParameter)).Compile()
    Microsoft.PowerShell.Utility\Set-Variable -Scope Script -Name $predicateName -Value $predicate
  }
  return $predicate
}

function TestPSDefaultValueContainsAutomationNull(
  [System.Management.Automation.PSDefaultValueAttribute]$attribute
) {
  if (TestPSDefaultValueAutomationNull $attribute) { return $true }
  $flags = [System.Reflection.BindingFlags]'Instance,Public,NonPublic'
  $valueField = $attribute.GetType().GetField('<Value>k__BackingField', $flags)
  if ($null -eq $valueField) { return $false }
  $root = $valueField.GetValue($attribute)
  if ($null -eq $root) { return $false }

  $listPredicate = GetAutomationNullListPredicate
  $arrayPredicate = GetAutomationNullArrayPredicate
  $dictionaryKeyPredicate = GetAutomationNullDictionaryEntryPredicate $true
  $dictionaryValuePredicate = GetAutomationNullDictionaryEntryPredicate $false
  $pending = [System.Collections.ArrayList]::new()
  $seen = [System.Collections.ArrayList]::new()
  [void]$pending.Add($root)
  while ($pending.Count -gt 0) {
    $current = $pending[$pending.Count - 1]
    $pending.RemoveAt($pending.Count - 1)
    $alreadySeen = $false
    foreach ($seenValue in $seen) {
      if ([object]::ReferenceEquals($seenValue, $current)) { $alreadySeen = $true; break }
    }
    if ($alreadySeen) { continue }
    [void]$seen.Add($current)

    $children = [System.Collections.ArrayList]::new()
    if ($current -is [System.Array]) {
      $indices = [int[]]::new($current.Rank)
      for ($dimension = 0; $dimension -lt $current.Rank; $dimension++) {
        $indices[$dimension] = $current.GetLowerBound($dimension)
      }
      for ($position = 0; $position -lt $current.Length; $position++) {
        if ($arrayPredicate.Invoke($current, $indices)) { return $true }
        [void]$children.Add($current.GetValue($indices))
        for ($dimension = $current.Rank - 1; $dimension -ge 0; $dimension--) {
          if ($indices[$dimension] -lt $current.GetUpperBound($dimension)) {
            $indices[$dimension]++
            break
          }
          $indices[$dimension] = $current.GetLowerBound($dimension)
        }
      }
    } elseif ($current -is [System.Collections.IDictionary]) {
      $enumerator = ([System.Collections.IDictionary]$current).GetEnumerator()
      while ($enumerator.MoveNext()) {
        $entry = $enumerator.Entry
        if ($dictionaryKeyPredicate.Invoke($entry) -or
            $dictionaryValuePredicate.Invoke($entry)) { return $true }
        [void]$children.Add($entry.Key)
        [void]$children.Add($entry.Value)
      }
    } elseif ($current -is [System.Collections.IList]) {
      for ($index = 0; $index -lt $current.Count; $index++) {
        if ($listPredicate.Invoke($current, $index)) { return $true }
        [void]$children.Add($current[$index])
      }
    }
    foreach ($child in $children) {
      if ($null -ne $child -and
          ($child -is [System.Array] -or
           $child -is [System.Collections.IDictionary] -or
           $child -is [System.Collections.IList])) {
        [void]$pending.Add($child)
      }
    }
  }
  return $false
}

function GetCollectionCapacity([object]$value) {
  $collectionType = $value.GetType()
  if ([object]::ReferenceEquals($collectionType, [System.Collections.ArrayList])) {
    $flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
    $versionField = $collectionType.GetField('_version', $flags)
    if ($null -eq $versionField) { $versionField = $collectionType.GetField('version', $flags) }
    if ($null -eq $versionField) {
      throw 'ArrayList serialization version is unavailable.'
    }
    if ([int]$versionField.GetValue($value) -ne $value.Count) {
      throw 'ArrayList defaults with non-reconstructible serialization versions are not supported.'
    }
    return [int]$value.Capacity
  }
  if ($collectionType.IsGenericType -and
      [object]::ReferenceEquals(
        $collectionType.GetGenericTypeDefinition(),
        [System.Collections.Generic.List``1])) {
    $flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
    $versionField = $collectionType.GetField('_version', $flags)
    if ($null -eq $versionField) { $versionField = $collectionType.GetField('version', $flags) }
    if ($null -eq $versionField) {
      throw 'List serialization version is unavailable.'
    }
    if ([int]$versionField.GetValue($value) -ne $value.Count) {
      throw 'List defaults with non-reconstructible serialization versions are not supported.'
    }
    return [int]$value.Capacity
  }
  return $null
}

function AddRuntimeTypeShapeTokens([type]$type, [System.Collections.IList]$tokens) {
  if ($type.IsGenericParameter) { throw 'Generic-parameter runtime type shapes are not supported.' }
  if (TestPowerShellTypeLiteral $type) {
    [void]$tokens.Add('N:L:' +
      (ConvertRuntimeTypeTextToBase64 (GetCanonicalTypeNameFromType $type)) + ':' +
      (ConvertRuntimeTypeTextToBase64 ([string]$type.Assembly.FullName)))
    return
  }
  if ($type.IsPointer) {
    [void]$tokens.Add('P')
    AddRuntimeTypeShapeTokens ($type.GetElementType()) $tokens
    return
  }
  if ($type.IsByRef) {
    [void]$tokens.Add('R')
    AddRuntimeTypeShapeTokens ($type.GetElementType()) $tokens
    return
  }
  if ($type.IsArray) {
    $isSzArray = $type.GetArrayRank() -eq 1 -and $type -eq $type.GetElementType().MakeArrayType()
    [void]$tokens.Add('A:' + $type.GetArrayRank().ToString([System.Globalization.CultureInfo]::InvariantCulture) +
      ':' + $(if ($isSzArray) { '1' } else { '0' }))
    AddRuntimeTypeShapeTokens ($type.GetElementType()) $tokens
    return
  }
  if ($type.IsGenericType -and -not $type.IsGenericTypeDefinition) {
    $arguments = $type.GetGenericArguments()
    [void]$tokens.Add('G:' + $arguments.Count.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    AddRuntimeTypeShapeTokens ($type.GetGenericTypeDefinition()) $tokens
    foreach ($argument in $arguments) { AddRuntimeTypeShapeTokens $argument $tokens }
    return
  }
  if ([string]::IsNullOrWhiteSpace($type.FullName) -or
      [string]::IsNullOrWhiteSpace($type.Assembly.FullName)) {
    throw ('Runtime type shape has no exact loaded-assembly identity: ' + [string]$type)
  }
  AssertExactLoadedTypeIdentity $type
  [void]$tokens.Add('N:E:' +
    (ConvertRuntimeTypeTextToBase64 ([string]$type.FullName)) + ':' +
    (ConvertRuntimeTypeTextToBase64 ([string]$type.Assembly.FullName)))
}

function GetRuntimeTypeShape([type]$type) {
  $tokens = [System.Collections.Generic.List[string]]::new()
  AddRuntimeTypeShapeTokens $type $tokens
  return ($tokens -join ';')
}

function AddRuntimeNumericDefaultValueToken(
  [object]$value,
  [System.Collections.IList]$tokens
) {
  if ($value -is [double]) {
    if ([double]::IsNaN($value)) {
      $tokens.Add([ordered]@{
        kind = 'DoubleBits'
        text = [System.BitConverter]::DoubleToInt64Bits($value).ToString([System.Globalization.CultureInfo]::InvariantCulture)
      }) | Microsoft.PowerShell.Core\Out-Null
      return $true
    }
    $text = $value.ToString('G17', [System.Globalization.CultureInfo]::InvariantCulture)
    if ($value -eq 0 -and [System.BitConverter]::DoubleToInt64Bits($value) -lt 0) { $text = '-0' }
    $tokens.Add([ordered]@{ kind = 'Double'; text = $text }) | Microsoft.PowerShell.Core\Out-Null
    return $true
  }
  if ($value -is [single]) {
    if ([single]::IsNaN($value)) {
      $tokens.Add([ordered]@{
        kind = 'SingleBits'
        text = [System.BitConverter]::ToInt32(
          [System.BitConverter]::GetBytes([single]$value), 0).ToString(
            [System.Globalization.CultureInfo]::InvariantCulture)
      }) | Microsoft.PowerShell.Core\Out-Null
      return $true
    }
    $text = $value.ToString('G9', [System.Globalization.CultureInfo]::InvariantCulture)
    if ($value -eq 0) {
      $bits = [System.BitConverter]::ToInt32([System.BitConverter]::GetBytes([single]$value), 0)
      if ($bits -lt 0) { $text = '-0' }
    }
    $tokens.Add([ordered]@{ kind = 'Single'; text = $text }) | Microsoft.PowerShell.Core\Out-Null
    return $true
  }
  if ($value -is [decimal]) {
    $bits = [System.Decimal]::GetBits($value)
    $tokens.Add([ordered]@{
      kind = 'DecimalBits'
      text = ($bits | Microsoft.PowerShell.Core\ForEach-Object {
        $_.ToString([System.Globalization.CultureInfo]::InvariantCulture)
      }) -join ','
    }) | Microsoft.PowerShell.Core\Out-Null
    return $true
  }
  return $false
}

function ConvertToRuntimeDefaultValue([object]$value) {
  $tokens = [System.Collections.ArrayList]::new()
  AddRuntimeDefaultValueTokens $value $tokens
  return [ordered]@{
    tokens = @($tokens)
  }
}
