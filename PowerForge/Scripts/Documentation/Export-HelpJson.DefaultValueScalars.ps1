function TestExactRuntimeValueType([object]$value, [object]$expectedType) {
  return $null -ne $value -and
    $null -ne $expectedType -and
    [object]::ReferenceEquals($value.GetType(), $expectedType)
}

function ConvertPSDefaultValueAttribute(
  [System.Management.Automation.PSDefaultValueAttribute]$attribute
) {
  $help = [string]$attribute.Help
  if (-not [string]::IsNullOrWhiteSpace($help)) {
    return ConvertToXmlSafeDefaultHelpText $help
  }

  if (TestPSDefaultValueAutomationNull $attribute) {
    throw 'AutomationNull defaults cannot be represented as PowerShell expressions.'
  }
  return ConvertToPowerShellDefaultValue $attribute.Value
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
    'Value',
    [System.Reflection.BindingFlags]'Static,Public')
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

function GetCollectorHelperFunctionSnapshot {
  $snapshot = @{}
  foreach ($command in Get-Command -CommandType Function) {
    if ($null -ne $command.ScriptBlock -and
        $command.ScriptBlock.File -eq $PSCommandPath) {
      $snapshot[$command.Name] = $command.ScriptBlock
    }
  }
  return ,$snapshot
}

function RestoreCollectorHelperFunctions([hashtable]$snapshot) {
  foreach ($entry in $snapshot.GetEnumerator()) {
    Microsoft.PowerShell.Management\Set-Item `
      -LiteralPath ('Function:' + $entry.Key) -Value $entry.Value -Force
  }
}

function AddDefaultValueReference(
  [object]$value,
  [System.Collections.IList]$references
) {
  if ($null -eq $value -or $value -is [type]) {
    return
  }
  foreach ($seenReference in $references) {
    if ([object]::ReferenceEquals($seenReference, $value)) {
      throw 'Repeated or circular default-value object references are not supported.'
    }
  }
  [void]$references.Add($value)
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
    $pointerText = $pointerValue.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    return ('[System.IntPtr]::new(([System.Int64]' + $pointerText + '))')
  }
  if ($runtimeType -eq [uintptr]) {
    $pointerValue = $value.ToUInt64()
    if ($pointerValue -gt [uint32]::MaxValue) {
      throw 'UIntPtr defaults outside the 32-bit range are not portable.'
    }
    $pointerText = $pointerValue.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    return ('[System.UIntPtr]::new(([System.UInt64]' + $pointerText + '))')
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
