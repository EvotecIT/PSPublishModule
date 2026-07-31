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

function GetConstructibleDictionaryTypeName([System.Collections.IDictionary]$value) {
  $dictionaryType = $value.GetType()
  if ($dictionaryType.IsAbstract -or $dictionaryType.IsInterface) { return '' }
  $constructor = $null
  try { $constructor = $dictionaryType.GetConstructor([System.Type]::EmptyTypes) } catch { $constructor = $null }
  if ($null -eq $constructor) { return '' }
  return GetCanonicalTypeNameFromType $dictionaryType
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
  $flags = [System.Reflection.BindingFlags]'Instance,Public,NonPublic'
  $compareInfoField = $comparer.GetType().GetField('_compareInfo', $flags)
  $ignoreCaseField = $comparer.GetType().GetField('_ignoreCase', $flags)
  if ($compareInfoField -and $ignoreCaseField) {
    $compareInfo = $compareInfoField.GetValue($comparer)
    $ignoreCase = [bool]$ignoreCaseField.GetValue($comparer)
    if ($compareInfo -and -not [string]::IsNullOrWhiteSpace($compareInfo.Name)) {
      return ('Culture|' + $compareInfo.Name + '|' + [string]$ignoreCase)
    }
  }
  throw ('Unsupported dictionary comparer: ' + $comparer.GetType().FullName)
}

function GetKnownDictionaryComparerExpression([object]$comparer, [type]$comparerType) {
  $name = GetKnownDictionaryComparerName $comparer $comparerType
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

function GetDictionaryConstructorExpression([System.Collections.IDictionary]$value) {
  if ($value.IsReadOnly -or $value.IsFixedSize) {
    throw ('Read-only or fixed-size dictionary defaults are not supported: ' + $value.GetType().FullName)
  }
  $dictionaryTypeName = GetConstructibleDictionaryTypeName $value
  if ([string]::IsNullOrWhiteSpace($dictionaryTypeName)) {
    throw ('Dictionary type has no supported constructor: ' + $value.GetType().FullName)
  }
  $comparerType = $null
  $comparer = GetDictionaryComparer $value ([ref]$comparerType)
  $comparerExpression = GetKnownDictionaryComparerExpression $comparer $comparerType
  if ([string]::IsNullOrWhiteSpace($comparerExpression)) {
    return ('[' + $dictionaryTypeName + ']::new()')
  }
  $constructor = $null
  foreach ($candidate in $value.GetType().GetConstructors()) {
    $parameters = $candidate.GetParameters()
    if ($parameters.Count -eq 1 -and $parameters[0].ParameterType.IsInstanceOfType($comparer)) {
      $constructor = $candidate
      break
    }
  }
  if ($null -eq $constructor) {
    throw ('Dictionary type cannot reconstruct comparer: ' + $value.GetType().FullName)
  }
  return ('[' + $dictionaryTypeName + ']::new(' + $comparerExpression + ')')
}

function GetPowerShellTypeDefaultExpression([type]$type) {
  if ($type.IsGenericParameter) {
    throw 'Generic-parameter Type defaults are not supported.'
  }
  if ($type.IsPointer) {
    return ((GetPowerShellTypeDefaultExpression ($type.GetElementType())) + '.MakePointerType()')
  }
  if ($type.IsByRef) {
    return ((GetPowerShellTypeDefaultExpression ($type.GetElementType())) + '.MakeByRefType()')
  }
  if ($type.IsArray -and
      $type.GetArrayRank() -eq 1 -and
      $type -ne $type.GetElementType().MakeArrayType()) {
    return ((GetPowerShellTypeDefaultExpression ($type.GetElementType())) + '.MakeArrayType(1)')
  }
  $canonicalTypeName = GetCanonicalTypeNameFromType $type
  if ($canonicalTypeName -match '^[A-Za-z_][A-Za-z0-9_.+`]*(?:\[[A-Za-z0-9_.+`,\[\]]+\])?$') {
    return ('[' + $canonicalTypeName + ']')
  }
  if ([string]::IsNullOrWhiteSpace($type.AssemblyQualifiedName)) {
    throw ('Type has no safely resolvable assembly-qualified name: ' + $canonicalTypeName)
  }
  $assemblyQualifiedName = $type.AssemblyQualifiedName.Replace("'", "''")
  return ("[System.Type]::GetType('" + $assemblyQualifiedName + "', `$true, `$false)")
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
  return ($trimmed -replace '\s+', '')
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
