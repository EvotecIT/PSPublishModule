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

function ConvertRuntimeTypeTextToBase64([string]$text) {
  return [System.Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($text))
}

function TestExactRuntimeValueType([object]$value, [object]$expectedType) {
  return $null -ne $value -and
    $null -ne $expectedType -and
    [object]::ReferenceEquals($value.GetType(), $expectedType)
}

function TestRecreatableScriptBlock([scriptblock]$value) {
  if ($null -ne $value.Module) {
    return $false
  }

  try {
    $languageModeProperty = [scriptblock].GetProperty(
      'LanguageMode',
      [System.Reflection.BindingFlags]'Instance,Public,NonPublic')
    if ($null -eq $languageModeProperty -or -not $languageModeProperty.CanRead) {
      return $false
    }
    return $languageModeProperty.GetValue($value, $null) -eq
      [System.Management.Automation.PSLanguageMode]::FullLanguage
  } catch {
    return $false
  }
}

function GetCoreRuntimeType([string]$fullName) {
  return [datetime].Assembly.GetType($fullName, $false, $false)
}

function TestCollectionHasItemOnlyBackingStore([object]$value) {
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
      }) | Out-Null
      return $true
    }
    $text = $value.ToString('G17', [System.Globalization.CultureInfo]::InvariantCulture)
    if ($value -eq 0 -and [System.BitConverter]::DoubleToInt64Bits($value) -lt 0) { $text = '-0' }
    $tokens.Add([ordered]@{ kind = 'Double'; text = $text }) | Out-Null
    return $true
  }
  if ($value -is [single]) {
    if ([single]::IsNaN($value)) {
      $tokens.Add([ordered]@{
        kind = 'SingleBits'
        text = [System.BitConverter]::ToInt32(
          [System.BitConverter]::GetBytes([single]$value), 0).ToString(
            [System.Globalization.CultureInfo]::InvariantCulture)
      }) | Out-Null
      return $true
    }
    $text = $value.ToString('G9', [System.Globalization.CultureInfo]::InvariantCulture)
    if ($value -eq 0) {
      $bits = [System.BitConverter]::ToInt32([System.BitConverter]::GetBytes([single]$value), 0)
      if ($bits -lt 0) { $text = '-0' }
    }
    $tokens.Add([ordered]@{ kind = 'Single'; text = $text }) | Out-Null
    return $true
  }
  if ($value -is [decimal]) {
    $bits = [System.Decimal]::GetBits($value)
    $tokens.Add([ordered]@{
      kind = 'DecimalBits'
      text = ($bits | ForEach-Object {
        $_.ToString([System.Globalization.CultureInfo]::InvariantCulture)
      }) -join ','
    }) | Out-Null
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
