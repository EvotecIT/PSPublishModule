function GetCanonicalTypeNameFromType([type]$type) {
  if ($null -eq $type) { return '' }
  if ($type.IsArray) {
    $elementName = GetCanonicalTypeNameFromType ($type.GetElementType())
    $rank = $type.GetArrayRank()
    if ($rank -le 1) {
      if ($type -eq $type.GetElementType().MakeArrayType()) { return ($elementName + '[]') }
      return ($elementName + '[*]')
    }
    return ($elementName + '[' + (',' * ($rank - 1)) + ']')
  }
  if ($type.IsGenericTypeDefinition) {
    if ($type.FullName) { return [string]$type.FullName }
    return [string]$type.Name
  }
  if ($type.IsGenericType) {
    $definition = $type.GetGenericTypeDefinition()
    $definitionName = [string]$definition.FullName
    if (-not $definitionName) { $definitionName = [string]$definition.Name }
    if ($definitionName.IndexOf('+') -lt 0) {
      $definitionName = $definitionName -replace '`\d+$', ''
    }
    $arguments = @()
    foreach ($argument in $type.GetGenericArguments()) {
      $arguments += GetCanonicalTypeNameFromType $argument
    }
    return ($definitionName + '[' + ($arguments -join ',') + ']')
  }
  if ($type.FullName) { return [string]$type.FullName }
  return [string]$type.Name
}

function GetPowerShellSafeEnumName([type]$enumType, [object]$value) {
  $enumName = [System.Enum]::GetName($enumType, $value)
  if ([string]::IsNullOrWhiteSpace($enumName)) { return '' }
  if ($enumName -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { return '' }
  $caseInsensitiveMatches = 0
  foreach ($candidate in [System.Enum]::GetNames($enumType)) {
    if ($candidate -ieq $enumName) { $caseInsensitiveMatches++ }
  }
  if ($caseInsensitiveMatches -eq 1) { return $enumName }
  return ''
}

function TestGenuineRuntimeTypeValue([object]$value) {
  if ($value -isnot [type]) { return $false }
  return [object]::ReferenceEquals($value.GetType(), [string].GetType())
}

function GetConstructibleDictionaryTypeName([System.Collections.IDictionary]$value) {
  $dictionaryType = $value.GetType()
  $supported = [object]::ReferenceEquals(
    $dictionaryType,
    [System.Collections.Specialized.OrderedDictionary])
  if (-not $supported -and $dictionaryType.IsGenericType) {
    $definition = $dictionaryType.GetGenericTypeDefinition()
    $supported = [object]::ReferenceEquals($definition, [System.Collections.Generic.Dictionary``2]) -or
      [object]::ReferenceEquals($definition, [System.Collections.Generic.SortedDictionary``2]) -or
      [object]::ReferenceEquals($definition, [System.Collections.Generic.SortedList``2])
  }
  if (-not $supported) { return '' }
  if ($dictionaryType.IsAbstract -or $dictionaryType.IsInterface) { return '' }
  $constructor = $null
  try { $constructor = $dictionaryType.GetConstructor([System.Type]::EmptyTypes) } catch { $constructor = $null }
  if ($null -eq $constructor) { return '' }
  return GetCanonicalTypeNameFromType $dictionaryType
}

function GetDictionaryCapacity([System.Collections.IDictionary]$value) {
  $dictionaryType = $value.GetType()
  $isExactGenericDictionary = $dictionaryType.IsGenericType -and
    [object]::ReferenceEquals(
      $dictionaryType.GetGenericTypeDefinition(),
      [System.Collections.Generic.Dictionary``2])
  if ($isExactGenericDictionary) {
    $flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
    $freeCountField = $null
    foreach ($fieldName in @('_freeCount', 'freeCount')) {
      $freeCountField = $dictionaryType.GetField($fieldName, $flags)
      if ($null -ne $freeCountField) { break }
    }
    if ($null -eq $freeCountField) {
      throw ('Dictionary free-list state is unavailable: ' + $dictionaryType.FullName)
    }
    if ([int]$freeCountField.GetValue($value) -ne 0) {
      throw ('Dictionary defaults with removed slots are not supported: ' + $dictionaryType.FullName)
    }

    $versionField = $null
    foreach ($fieldName in @('_version', 'version')) {
      $versionField = $dictionaryType.GetField($fieldName, $flags)
      if ($null -ne $versionField) { break }
    }
    if ($null -eq $versionField) {
      throw ('Dictionary serialization version is unavailable: ' + $dictionaryType.FullName)
    }
    if ([int]$versionField.GetValue($value) -ne $value.Count) {
      throw ('Dictionary defaults with non-reconstructible serialization versions are not supported: ' +
        $dictionaryType.FullName)
    }
  }

  if ([object]::ReferenceEquals(
      $dictionaryType,
      [System.Collections.Specialized.OrderedDictionary])) {
    $initialCapacityField = $dictionaryType.GetField(
      '_initialCapacity',
      [System.Reflection.BindingFlags]'Instance,NonPublic')
    if ($null -eq $initialCapacityField) {
      throw ('OrderedDictionary initial capacity is unavailable: ' + $dictionaryType.FullName)
    }
    return [int]$initialCapacityField.GetValue($value)
  }

  $capacityProperty = $dictionaryType.GetProperty(
    'Capacity',
    [System.Reflection.BindingFlags]'Instance,Public')
  if ($null -ne $capacityProperty -and
      $capacityProperty.PropertyType -eq [int] -and
      $capacityProperty.GetIndexParameters().Count -eq 0) {
    try { return [int]$capacityProperty.GetValue($value, $null) } catch {
      throw ('Dictionary capacity is unavailable: ' + $dictionaryType.FullName)
    }
  }

  if ($isExactGenericDictionary) {
    $bucketsField = $null
    foreach ($fieldName in @('_buckets', 'buckets')) {
      $bucketsField = $dictionaryType.GetField(
        $fieldName,
        [System.Reflection.BindingFlags]'Instance,NonPublic')
      if ($null -ne $bucketsField) { break }
    }
    if ($null -eq $bucketsField) {
      throw ('Dictionary bucket state is unavailable: ' + $dictionaryType.FullName)
    }
    $buckets = $bucketsField.GetValue($value)
    if ($null -eq $buckets) { return 0 }
    return [int]$buckets.Length
  }
  return $null
}

function GetDictionaryComparer([System.Collections.IDictionary]$value, [ref]$comparerType) {
  $comparerType.Value = $null
  $dictionaryType = $value.GetType()
  $flags = [System.Reflection.BindingFlags]'Instance,Public,NonPublic'
  foreach ($propertyName in @('Comparer', 'EqualityComparer', 'comparer')) {
    $property = $dictionaryType.GetProperty($propertyName, $flags)
    if ($null -eq $property -or $property.GetIndexParameters().Count -ne 0) { continue }
    try {
      $propertyComparer = $property.GetValue($value, $null)
      if ($null -ne $propertyComparer) {
        $comparerType.Value = $property.PropertyType
        return $propertyComparer
      }
    } catch {
      # Try the runtime's backing field when a protected comparer getter is unavailable.
    }
  }
  foreach ($fieldName in @('_keycomparer', '_comparer', 'm_keycomparer', 'm_comparer', 'comparer')) {
    $field = $dictionaryType.GetField($fieldName, $flags)
    if ($null -eq $field) { continue }
    try {
      $fieldComparer = $field.GetValue($value)
      if ($null -ne $fieldComparer) {
        $comparerType.Value = $field.FieldType
        return $fieldComparer
      }
    } catch {
      # Keep probing known runtime layouts.
    }
  }
  foreach ($containerField in $dictionaryType.GetFields($flags)) {
    if ($containerField.Name -notmatch 'tables?$') { continue }
    $container = $null
    try { $container = $containerField.GetValue($value) } catch { $container = $null }
    if ($null -eq $container) { continue }
    foreach ($fieldName in @('_keycomparer', '_comparer', 'm_keycomparer', 'm_comparer', 'comparer')) {
      $field = $container.GetType().GetField($fieldName, $flags)
      if ($null -eq $field) { continue }
      try {
        $fieldComparer = $field.GetValue($container)
        if ($null -ne $fieldComparer) {
          $comparerType.Value = $field.FieldType
          return $fieldComparer
        }
      } catch {
        # Keep probing known nested runtime layouts.
      }
    }
  }
  return $null
}

function GetKnownDictionaryComparerName([object]$comparer, [type]$comparerType) {
  if ($null -eq $comparer) { return '' }
  if ($comparerType -and $comparerType.IsGenericType) {
    $definition = $comparerType.GetGenericTypeDefinition()
    $argument = $comparerType.GetGenericArguments()[0]
    $defaultComparerType = $null
    if ($definition -eq [System.Collections.Generic.IEqualityComparer``1]) {
      $defaultComparerType = [System.Collections.Generic.EqualityComparer``1].MakeGenericType($argument)
    } elseif ($definition -eq [System.Collections.Generic.IComparer``1]) {
      $defaultComparerType = [System.Collections.Generic.Comparer``1].MakeGenericType($argument)
    }
    if ($defaultComparerType) {
      $defaultComparer = $defaultComparerType.GetProperty('Default').GetValue($null, $null)
      if ([object]::ReferenceEquals($comparer, $defaultComparer)) { return '' }
    }
  }
  foreach ($name in @('Ordinal', 'OrdinalIgnoreCase', 'InvariantCulture', 'InvariantCultureIgnoreCase')) {
    $knownComparer = [System.StringComparer].GetProperty($name).GetValue($null, $null)
    if ([object]::ReferenceEquals($comparer, $knownComparer)) {
      return $name
    }
  }
  $cultureComparerType = [System.StringComparer]::Create(
    [System.Globalization.CultureInfo]::InvariantCulture, $false).GetType()
  if (-not [object]::ReferenceEquals($comparer.GetType(), $cultureComparerType)) {
    throw ('Unsupported dictionary comparer: ' + $comparer.GetType().FullName)
  }
  $flags = [System.Reflection.BindingFlags]'Instance,Public,NonPublic'
  $compareInfoField = $comparer.GetType().GetField('_compareInfo', $flags)
  $ignoreCaseField = $comparer.GetType().GetField('_ignoreCase', $flags)
  $optionsField = $comparer.GetType().GetField('_options', $flags)
  if ($compareInfoField -and ($ignoreCaseField -or $optionsField)) {
    $compareInfo = $compareInfoField.GetValue($comparer)
    $ignoreCase = $false
    if ($ignoreCaseField) {
      $ignoreCase = [bool]$ignoreCaseField.GetValue($comparer)
    } else {
      $options = [System.Globalization.CompareOptions]$optionsField.GetValue($comparer)
      $supportedOptions = [System.Globalization.CompareOptions]::None -bor [System.Globalization.CompareOptions]::IgnoreCase
      if (($options -band (-bnot $supportedOptions)) -ne 0) {
        throw ('Unsupported dictionary culture comparer options: ' + [string]$options)
      }
      $ignoreCase = ($options -band [System.Globalization.CompareOptions]::IgnoreCase) -ne 0
    }
    if ($compareInfo) {
      if ([string]::IsNullOrWhiteSpace($compareInfo.Name)) {
        if ($compareInfo.LCID -eq [System.Globalization.CultureInfo]::InvariantCulture.LCID) {
          if ($ignoreCase) { return 'InvariantCultureIgnoreCase' }
          return 'InvariantCulture'
        }
      } else {
        return ('Culture|' + $compareInfo.Name + '|' + [string]$ignoreCase)
      }
    }
  }
  throw ('Unsupported dictionary comparer: ' + $comparer.GetType().FullName)
}

function TestDictionaryComparerIsSingleton([object]$comparer, [type]$comparerType) {
  if ($null -eq $comparer) { return $true }
  if ($comparerType -and $comparerType.IsGenericType) {
    $definition = $comparerType.GetGenericTypeDefinition()
    $argument = $comparerType.GetGenericArguments()[0]
    $defaultComparerType = if ($definition -eq [System.Collections.Generic.IEqualityComparer``1]) {
      [System.Collections.Generic.EqualityComparer``1].MakeGenericType($argument)
    } elseif ($definition -eq [System.Collections.Generic.IComparer``1]) {
      [System.Collections.Generic.Comparer``1].MakeGenericType($argument)
    } else { $null }
    if ($defaultComparerType) {
      $defaultComparer = $defaultComparerType.GetProperty('Default').GetValue($null, $null)
      if ([object]::ReferenceEquals($comparer, $defaultComparer)) { return $true }
    }
  }
  foreach ($name in @('Ordinal', 'OrdinalIgnoreCase', 'InvariantCulture', 'InvariantCultureIgnoreCase')) {
    $knownComparer = [System.StringComparer].GetProperty($name).GetValue($null, $null)
    if ([object]::ReferenceEquals($comparer, $knownComparer)) { return $true }
  }
  return $false
}

function GetKnownDictionaryComparerExpression(
  [object]$comparer,
  [type]$comparerType,
  [System.Collections.IList]$referenceStack = $null
) {
  $name = GetKnownDictionaryComparerName $comparer $comparerType
  if ($null -ne $referenceStack -and
      -not (TestDictionaryComparerIsSingleton $comparer $comparerType)) {
    AddDefaultValueReference $comparer $referenceStack
  }
  if ([string]::IsNullOrWhiteSpace($name)) { return '' }
  if ($name.StartsWith('Culture|', [System.StringComparison]::Ordinal)) {
    $parts = $name.Split('|')
    if ($parts.Count -ne 3) { throw ('Invalid culture comparer metadata: ' + $name) }
    $cultureName = $parts[1].Replace("'", "''")
    $ignoreCase = if ([bool]::Parse($parts[2])) { '$true' } else { '$false' }
    return ("[System.StringComparer]::Create([System.Globalization.CultureInfo]::GetCultureInfo('" +
      $cultureName + "'), " + $ignoreCase + ')')
  }
  return ('[System.StringComparer]::' + $name)
}

function GetDictionaryConstructorExpression(
  [System.Collections.IDictionary]$value,
  [System.Collections.IList]$referenceStack = $null
) {
  if ($value.IsReadOnly -or $value.IsFixedSize) {
    throw ('Read-only or fixed-size dictionary defaults are not supported: ' + $value.GetType().FullName)
  }
  $dictionaryTypeName = GetConstructibleDictionaryTypeName $value
  if ([string]::IsNullOrWhiteSpace($dictionaryTypeName)) {
    throw ('Dictionary type has no supported constructor: ' + $value.GetType().FullName)
  }
  $comparerType = $null
  $comparer = GetDictionaryComparer $value ([ref]$comparerType)
  $comparerExpression = GetKnownDictionaryComparerExpression $comparer $comparerType $referenceStack
  $dictionaryTypeExpression = GetPowerShellTypeDefaultExpression $value.GetType()
  $dictionaryTypeIsLiteral = TestPowerShellTypeLiteral $value.GetType()
  $capacity = GetDictionaryCapacity $value
  $capacityExpression = if ($null -eq $capacity) {
    ''
  } else {
    '([int]' + $capacity.ToString([System.Globalization.CultureInfo]::InvariantCulture) + ')'
  }
  if ([string]::IsNullOrWhiteSpace($comparerExpression)) {
    if ([string]::IsNullOrWhiteSpace($capacityExpression)) {
      if ($dictionaryTypeIsLiteral) { return ('[' + $dictionaryTypeName + ']::new()') }
      return ('[System.Activator]::CreateInstance((' + $dictionaryTypeExpression + '))')
    }
    $capacityConstructor = $value.GetType().GetConstructor([type[]]@([int]))
    if ($null -eq $capacityConstructor) {
      throw ('Dictionary type cannot reconstruct capacity: ' + $value.GetType().FullName)
    }
    if ($dictionaryTypeIsLiteral) {
      return ('[' + $dictionaryTypeName + ']::new(' + $capacityExpression + ')')
    }
    return ('[System.Activator]::CreateInstance((' + $dictionaryTypeExpression +
      '), [object[]]@((' + $capacityExpression + ')))')
  }
  $constructor = $null
  foreach ($candidate in $value.GetType().GetConstructors()) {
    $parameters = $candidate.GetParameters()
    $matchesComparerOnly = [string]::IsNullOrWhiteSpace($capacityExpression) -and
      $parameters.Count -eq 1 -and
      $parameters[0].ParameterType.IsInstanceOfType($comparer)
    $matchesCapacityAndComparer = -not [string]::IsNullOrWhiteSpace($capacityExpression) -and
      $parameters.Count -eq 2 -and
      $parameters[0].ParameterType -eq [int] -and
      $parameters[1].ParameterType.IsInstanceOfType($comparer)
    if ($matchesComparerOnly -or $matchesCapacityAndComparer) {
      $constructor = $candidate
      break
    }
  }
  if ($null -eq $constructor) {
    throw ('Dictionary type cannot reconstruct comparer and capacity: ' + $value.GetType().FullName)
  }
  $constructorArguments = if ([string]::IsNullOrWhiteSpace($capacityExpression)) {
    $comparerExpression
  } else {
    $capacityExpression + ', ' + $comparerExpression
  }
  if ($dictionaryTypeIsLiteral) {
    return ('[' + $dictionaryTypeName + ']::new(' + $constructorArguments + ')')
  }
  $activatorArguments = if ([string]::IsNullOrWhiteSpace($capacityExpression)) {
    '(' + $comparerExpression + ')'
  } else {
    '(' + $capacityExpression + '), (' + $comparerExpression + ')'
  }
  return ('[System.Activator]::CreateInstance((' + $dictionaryTypeExpression +
    '), [object[]]@(' + $activatorArguments + '))')
}

function TestPowerShellSimpleTypeName([string]$typeName) {
  if ([string]::IsNullOrWhiteSpace($typeName)) { return $false }
  foreach ($segment in $typeName.Split([char[]]@('.', '+'))) {
    if ($segment -notmatch '^[A-Za-z_][A-Za-z0-9_]*(?:`\d+)?$') { return $false }
  }
  return $true
}

function TestPowerShellTypeLiteralName([string]$typeName) {
  if ([string]::IsNullOrWhiteSpace($typeName) -or $typeName -cne $typeName.Trim()) { return $false }
  $lastOpen = $typeName.LastIndexOf('[')
  if ($lastOpen -gt 0 -and $typeName.EndsWith(']', [System.StringComparison]::Ordinal)) {
    $suffix = $typeName.Substring($lastOpen + 1, $typeName.Length - $lastOpen - 2)
    if ($suffix -match '^(?:\*|,+)?$') {
      return TestPowerShellTypeLiteralName $typeName.Substring(0, $lastOpen)
    }
  }
  $genericOpen = $typeName.IndexOf('[')
  if ($genericOpen -lt 0) { return TestPowerShellSimpleTypeName $typeName }
  if (-not $typeName.EndsWith(']', [System.StringComparison]::Ordinal) -or
      -not (TestPowerShellSimpleTypeName $typeName.Substring(0, $genericOpen))) { return $false }
  $arguments = [System.Collections.Generic.List[string]]::new()
  $depth = 0
  $start = $genericOpen + 1
  for ($index = $start; $index -lt ($typeName.Length - 1); $index++) {
    $character = $typeName[$index]
    if ($character -eq '[') { $depth++; continue }
    if ($character -eq ']') {
      if ($depth -eq 0) { return $false }
      $depth--
      continue
    }
    if ($character -eq ',' -and $depth -eq 0) {
      $arguments.Add($typeName.Substring($start, $index - $start))
      $start = $index + 1
    }
  }
  if ($depth -ne 0) { return $false }
  $arguments.Add($typeName.Substring($start, $typeName.Length - $start - 1))
  if ($arguments.Count -eq 0) { return $false }
  foreach ($argument in $arguments) {
    if (-not (TestPowerShellTypeLiteralName $argument)) { return $false }
  }
  return $true
}

function TestPowerShellTypeLiteral([type]$type) {
  if ($null -eq $type) { return $false }
  $canonicalTypeName = GetCanonicalTypeNameFromType $type
  if (-not (TestPowerShellTypeLiteralName $canonicalTypeName)) { return $false }
  $resolvedType = $null
  try { $resolvedType = $canonicalTypeName -as [type] } catch { $resolvedType = $null }
  return $null -ne $resolvedType -and [object]::ReferenceEquals($resolvedType, $type)
}

function GetExactLoadedTypeMatches([string]$assemblyName, [string]$typeName) {
  $matches = [System.Collections.Generic.List[type]]::new()
  foreach ($assembly in [System.AppDomain]::CurrentDomain.GetAssemblies()) {
    if ($assembly.FullName -ne $assemblyName) { continue }
    $candidate = $null
    try { $candidate = $assembly.GetType($typeName, $false, $false) } catch { $candidate = $null }
    if ($null -eq $candidate) {
      try {
        $candidate = $assembly.GetTypes() |
          Where-Object { $_.FullName -ceq $typeName } |
          Select-Object -First 1
      } catch {
        $candidate = $null
      }
    }
    if ($null -ne $candidate) { $matches.Add($candidate) }
  }
  return @($matches.ToArray())
}

function AssertExactLoadedTypeIdentity([type]$type) {
  $matches = @(GetExactLoadedTypeMatches ([string]$type.Assembly.FullName) ([string]$type.FullName))
  if ($matches.Count -ne 1 -or -not [object]::ReferenceEquals($matches[0], $type)) {
    throw ('Type identity is unavailable or ambiguous across loaded assemblies: ' + [string]$type.FullName)
  }
}

function ConvertToPowerShellTypeIdentityText([string]$text) {
  $parts = [System.Collections.Generic.List[string]]::new()
  $segment = [System.Text.StringBuilder]::new()
  foreach ($character in $text.ToCharArray()) {
    if ($character -ne "`r" -and
        $character -ne "`n" -and
        [System.Xml.XmlConvert]::IsXmlChar($character)) {
      [void]$segment.Append($character)
      continue
    }
    if ($segment.Length -gt 0) {
      $parts.Add("'" + $segment.ToString().Replace("'", "''") + "'")
      [void]$segment.Clear()
    }
    $parts.Add('([char]' + [int]$character + ')')
  }
  if ($segment.Length -gt 0) {
    $parts.Add("'" + $segment.ToString().Replace("'", "''") + "'")
  }
  if ($parts.Count -eq 1 -and -not $parts[0].StartsWith('([char]', [System.StringComparison]::Ordinal)) {
    return $parts[0]
  }
  return ('(-join @(' + ($parts -join ', ') + '))')
}

function GetPowerShellTypeDefaultExpression([type]$type) {
  if ($type.IsGenericParameter) {
    throw 'Generic-parameter Type defaults are not supported.'
  }
  if ($type.IsPointer) {
    $elementExpression = GetPowerShellTypeDefaultExpression ($type.GetElementType())
    if ($elementExpression.StartsWith('& {', [System.StringComparison]::Ordinal)) {
      $elementExpression = '(' + $elementExpression + ')'
    }
    return ($elementExpression + '.MakePointerType()')
  }
  if ($type.IsByRef) {
    $elementExpression = GetPowerShellTypeDefaultExpression ($type.GetElementType())
    if ($elementExpression.StartsWith('& {', [System.StringComparison]::Ordinal)) {
      $elementExpression = '(' + $elementExpression + ')'
    }
    return ($elementExpression + '.MakeByRefType()')
  }
  if ($type.IsArray) {
    $elementExpression = GetPowerShellTypeDefaultExpression ($type.GetElementType())
    if ($elementExpression.StartsWith('& {', [System.StringComparison]::Ordinal)) {
      $elementExpression = '(' + $elementExpression + ')'
    }
    $rank = $type.GetArrayRank()
    if ($rank -eq 1 -and $type -eq $type.GetElementType().MakeArrayType()) {
      return ($elementExpression + '.MakeArrayType()')
    }
    return ($elementExpression + '.MakeArrayType(' +
      $rank.ToString([System.Globalization.CultureInfo]::InvariantCulture) + ')')
  }
  $canonicalTypeName = GetCanonicalTypeNameFromType $type
  if (TestPowerShellTypeLiteral $type) {
    return ('[' + $canonicalTypeName + ']')
  }
  if ($type.IsGenericType -and -not $type.IsGenericTypeDefinition) {
    $definitionExpression = GetPowerShellTypeDefaultExpression ($type.GetGenericTypeDefinition())
    if ($definitionExpression.StartsWith('& {', [System.StringComparison]::Ordinal)) {
      $definitionExpression = '(' + $definitionExpression + ')'
    }
    $argumentExpressions = [System.Collections.Generic.List[string]]::new()
    foreach ($argumentType in $type.GetGenericArguments()) {
      $argumentExpression = GetPowerShellTypeDefaultExpression $argumentType
      if ($argumentExpression.StartsWith('& {', [System.StringComparison]::Ordinal)) {
        $argumentExpression = '(' + $argumentExpression + ')'
      }
      $argumentExpressions.Add($argumentExpression)
    }
    return ($definitionExpression + '.MakeGenericType([type[]]@(' +
      ($argumentExpressions -join ', ') + '))')
  }
  if ([string]::IsNullOrWhiteSpace($type.FullName) -or
      [string]::IsNullOrWhiteSpace($type.Assembly.FullName)) {
    throw ('Type has no safely resolvable runtime identity: ' + $canonicalTypeName)
  }
  AssertExactLoadedTypeIdentity $type
  $typeNameExpression = ConvertToPowerShellTypeIdentityText ([string]$type.FullName)
  $assemblyNameExpression = ConvertToPowerShellTypeIdentityText ([string]$type.Assembly.FullName)
  return ("& { `$assembly = [System.AppDomain]::CurrentDomain.GetAssemblies() | " +
    "Where-Object { `$_.FullName -eq " + $assemblyNameExpression + " }; " +
    "`$matches = [System.Collections.Generic.List[type]]::new(); " +
    "foreach (`$candidateAssembly in @(`$assembly)) { " +
    "`$type = `$candidateAssembly.GetType(" + $typeNameExpression + ", `$false, `$false); " +
    "if (`$null -eq `$type) { try { `$type = `$candidateAssembly.GetTypes() | " +
    "Where-Object { `$_.FullName -ceq " + $typeNameExpression + " } | Select-Object -First 1 } catch { `$type = `$null } }; " +
    "if (`$null -ne `$type) { `$matches.Add(`$type) } }; " +
    "if (`$matches.Count -ne 1) { throw 'Type identity is unavailable or ambiguous across loaded assemblies.' }; " +
    "return `$matches[0] }")
}

function ResolveExactType([string]$candidate) {
  if ([string]::IsNullOrWhiteSpace($candidate)) { return $null }
  $resolvedType = $null
  try { $resolvedType = [System.Type]::GetType($candidate, $false, $false) } catch { $resolvedType = $null }
  if ($resolvedType) { return $resolvedType }
  foreach ($assembly in [System.AppDomain]::CurrentDomain.GetAssemblies()) {
    try { $resolvedType = $assembly.GetType($candidate, $false, $false) } catch { $resolvedType = $null }
    if ($resolvedType) { return $resolvedType }
  }
  return $null
}

function ResolveUniqueTypeCaseInsensitive([string]$candidate, [ref]$isAmbiguous) {
  $isAmbiguous.Value = $false
  if ([string]::IsNullOrWhiteSpace($candidate)) { return $null }
  $matches = [System.Collections.Generic.Dictionary[string,System.Type]]::new([System.StringComparer]::Ordinal)
  $ambiguous = $false
  $resolvedType = $null
  try {
    $resolvedType = [System.Type]::GetType($candidate, $false, $true)
  } catch [System.Reflection.AmbiguousMatchException] {
    $ambiguous = $true
  } catch {
    $resolvedType = $null
  }
  if ($resolvedType) {
    $matches[(GetCanonicalTypeNameFromType $resolvedType)] = $resolvedType
  }
  foreach ($assembly in [System.AppDomain]::CurrentDomain.GetAssemblies()) {
    $resolvedType = $null
    try {
      $resolvedType = $assembly.GetType($candidate, $false, $true)
    } catch [System.Reflection.AmbiguousMatchException] {
      $ambiguous = $true
    } catch {
      $resolvedType = $null
    }
    if ($resolvedType) {
      $matches[(GetCanonicalTypeNameFromType $resolvedType)] = $resolvedType
    }
  }
  $isAmbiguous.Value = $ambiguous
  if ($ambiguous -or $matches.Count -ne 1) { return $null }
  foreach ($match in $matches.Values) { return $match }
  return $null
}

function ResolveUniqueNestedType([string]$candidate, [ref]$isAmbiguous) {
  $isAmbiguous.Value = $false
  if ([string]::IsNullOrWhiteSpace($candidate) -or -not $candidate.Contains('.')) { return $null }

  $nestedCandidates = [System.Collections.Generic.List[string]]::new()
  $seenCandidates = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  for ($index = $candidate.Length - 1; $index -ge 0; $index--) {
    if ($candidate[$index] -ne '.') { continue }
    $nestedCandidate = $candidate.Substring(0, $index) + '+' + $candidate.Substring($index + 1).Replace('.', '+')
    if ($seenCandidates.Add($nestedCandidate)) {
      $nestedCandidates.Add($nestedCandidate)
    }
  }

  $matches = [System.Collections.Generic.Dictionary[string,System.Type]]::new([System.StringComparer]::Ordinal)
  foreach ($nestedCandidate in $nestedCandidates) {
    $resolvedType = ResolveExactType $nestedCandidate
    if ($resolvedType) {
      $matches[(GetCanonicalTypeNameFromType $resolvedType)] = $resolvedType
    }
  }
  if ($matches.Count -eq 1) {
    foreach ($match in $matches.Values) { return $match }
  }
  if ($matches.Count -gt 1) {
    $isAmbiguous.Value = $true
    return $null
  }

  $ambiguous = $false
  foreach ($nestedCandidate in $nestedCandidates) {
    $candidateAmbiguous = $false
    $resolvedType = ResolveUniqueTypeCaseInsensitive $nestedCandidate ([ref]$candidateAmbiguous)
    if ($candidateAmbiguous) { $ambiguous = $true }
    if ($resolvedType) {
      $matches[(GetCanonicalTypeNameFromType $resolvedType)] = $resolvedType
    }
  }
  $isAmbiguous.Value = $ambiguous -or $matches.Count -gt 1
  if ($isAmbiguous.Value -or $matches.Count -ne 1) { return $null }
  foreach ($match in $matches.Values) { return $match }
  return $null
}

function ResolveCanonicalTypeName([string]$candidate) {
  if ([string]::IsNullOrWhiteSpace($candidate)) { return '' }
  $trimmed = $candidate.Trim()
  $resolvedType = ResolveExactType $trimmed
  $ambiguous = $false
  if (-not $resolvedType) { $resolvedType = ResolveUniqueNestedType $trimmed ([ref]$ambiguous) }
  if (-not $resolvedType -and -not $ambiguous) { $resolvedType = ResolveUniqueTypeCaseInsensitive $trimmed ([ref]$ambiguous) }
  if (-not $resolvedType -and -not $ambiguous) {
    try { $resolvedType = $trimmed -as [type] } catch { $resolvedType = $null }
  }
  if ($resolvedType) { return GetCanonicalTypeNameFromType $resolvedType }
  return $trimmed
}

function GetCanonicalTypeName([string]$candidate) {
  return ResolveCanonicalTypeName $candidate
}

function GetTypeKeys([string]$name, [string]$clrName) {
  $keys = @()
  foreach ($candidate in @($name, $clrName)) {
    if (-not [string]::IsNullOrWhiteSpace($candidate)) {
      $trimmed = $candidate.Trim()
      $keys += $trimmed
      $canonical = GetCanonicalTypeName $trimmed
      if ($canonical) {
        $keys += $canonical
        $genericIndex = $canonical.IndexOf('[')
        $baseName = if ($genericIndex -ge 0) { $canonical.Substring(0, $genericIndex) } else { $canonical }
        $genericSuffix = if ($genericIndex -ge 0) { $canonical.Substring($genericIndex) } else { '' }
        $separatorIndex = [System.Math]::Max($baseName.LastIndexOf('.'), $baseName.LastIndexOf('+'))
        if ($separatorIndex -ge 0 -and $separatorIndex -lt ($baseName.Length - 1)) {
          $keys += ($baseName.Substring($separatorIndex + 1) + $genericSuffix)
        }
      }
    }
  }
  $uniqueKeys = [System.Collections.Generic.List[string]]::new()
  $seenKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($key in $keys) {
    if ($seenKeys.Add([string]$key)) {
      $uniqueKeys.Add([string]$key)
    }
  }
  return @($uniqueKeys.ToArray())
}

function GetTypeIdentity([string]$name, [string]$clrName) {
  foreach ($candidate in @($clrName, $name)) {
    if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
    $trimmed = $candidate.Trim()
    $resolvedType = ResolveExactType $trimmed
    $ambiguous = $false
    if (-not $resolvedType) { $resolvedType = ResolveUniqueNestedType $trimmed ([ref]$ambiguous) }
    if (-not $resolvedType -and -not $ambiguous) { $resolvedType = ResolveUniqueTypeCaseInsensitive $trimmed ([ref]$ambiguous) }
    if ($resolvedType) { return GetCanonicalTypeNameFromType $resolvedType }
    $identity = GetCanonicalTypeName $trimmed
    if ($identity) { return $identity }
  }
  return ''
}

function TestQualifiedTypeIdentity([string]$identity) {
  if ([string]::IsNullOrWhiteSpace($identity)) { return $false }
  $baseName = $identity
  $genericIndex = $baseName.IndexOf('[')
  if ($genericIndex -ge 0) { $baseName = $baseName.Substring(0, $genericIndex) }
  return $baseName.Contains('.') -or $baseName.Contains('+')
}

function TestConflictingQualifiedTypeIdentity([string]$left, [string]$right) {
  return $left -cne $right -and
    (TestQualifiedTypeIdentity $left) -and
    (TestQualifiedTypeIdentity $right)
}
