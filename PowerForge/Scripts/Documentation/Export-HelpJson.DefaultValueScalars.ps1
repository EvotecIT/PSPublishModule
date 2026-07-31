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
    return $languageModeProperty.GetValue($value, $null) -eq
      [System.Management.Automation.PSLanguageMode]::FullLanguage
  } catch {
    return $false
  }
}

function GetCoreRuntimeType([string]$fullName) {
  return [datetime].Assembly.GetType($fullName, $false, $false)
}

function ConvertScalarToPowerShellDefaultValue([object]$value) {
  $scalarText = if ($value -is [System.IFormattable]) {
    ([System.IFormattable]$value).ToString($null, [System.Globalization.CultureInfo]::InvariantCulture)
  } else {
    ''
  }
  $runtimeType = $value.GetType()
  if ($runtimeType -eq [sbyte]) { return ('([System.SByte]' + $scalarText + ')') }
  if ($runtimeType -eq [byte]) { return ('([System.Byte]' + $scalarText + ')') }
  if ($runtimeType -eq [int16]) { return ('([System.Int16]' + $scalarText + ')') }
  if ($runtimeType -eq [uint16]) { return ('([System.UInt16]' + $scalarText + ')') }
  if ($runtimeType -eq [int32]) { return $scalarText }
  if ($runtimeType -eq [uint32]) { return ('([System.UInt32]' + $scalarText + ')') }
  if ($runtimeType -eq [int64]) { return ('([System.Int64]' + $scalarText + ')') }
  if ($runtimeType -eq [uint64]) { return ('([System.UInt64]' + $scalarText + ')') }
  if ($runtimeType -eq [intptr]) {
    $pointerValue = $value.ToInt64()
    if ($pointerValue -lt [int]::MinValue -or $pointerValue -gt [int]::MaxValue) {
      throw 'IntPtr defaults outside the 32-bit range are not portable.'
    }
    return ('[System.IntPtr]::new(([System.Int64]' + $scalarText + '))')
  }
  if ($runtimeType -eq [uintptr]) {
    $pointerValue = $value.ToUInt64()
    if ($pointerValue -gt [uint32]::MaxValue) {
      throw 'UIntPtr defaults outside the 32-bit range are not portable.'
    }
    return ('[System.UIntPtr]::new(([System.UInt64]' + $scalarText + '))')
  }
  throw ('Unsupported PSDefaultValue runtime type: ' + $runtimeType.FullName)
}

function ConvertToXmlSafeDefaultHelpText([string]$text) {
  $builder = [System.Text.StringBuilder]::new()
  for ($index = 0; $index -lt $text.Length; $index++) {
    $character = $text[$index]
    if ([char]::IsHighSurrogate($character) -and
        $index + 1 -lt $text.Length -and
        [char]::IsLowSurrogate($text[$index + 1])) {
      [void]$builder.Append($character)
      [void]$builder.Append($text[++$index])
    } elseif ([System.Xml.XmlConvert]::IsXmlChar($character)) {
      [void]$builder.Append($character)
    } else {
      [void]$builder.Append('([char]' + [int]$character + ')')
    }
  }
  return $builder.ToString()
}
