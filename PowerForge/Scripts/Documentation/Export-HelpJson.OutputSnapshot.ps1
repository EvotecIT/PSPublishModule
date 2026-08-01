function GetOutputTypeSnapshot([object]$outputType) {
  $outputTypeName = ''
  $outputTypeClrName = ''
  $canonicalTypeName = ''
  $runtimeIdentity = ''
  $runtimeType = $null
  try { $outputTypeName = [string]$outputType.Name } catch { $outputTypeName = '' }
  try { $runtimeType = $outputType.Type } catch { $runtimeType = $null }
  if ($runtimeType -is [type]) {
    $outputTypeClrName = [string]$runtimeType.FullName
    $canonicalTypeName = GetCanonicalTypeNameFromType $runtimeType
    $runtimeIdentity = GetRuntimeTypeInstanceIdentity $runtimeType
    if ($runtimeType.IsGenericType) { $outputTypeName = $canonicalTypeName }
  }
  if (-not $outputTypeClrName) {
    try { $outputTypeClrName = [string]$outputType.TypeName.FullName } catch { $outputTypeClrName = '' }
  }
  if (-not $outputTypeClrName) { $outputTypeClrName = $outputTypeName }
  if (-not $outputTypeName) { $outputTypeName = $outputTypeClrName }
  if (-not $outputTypeName) { return $null }
  if (-not $canonicalTypeName) {
    $canonicalTypeName = if (-not [string]::IsNullOrWhiteSpace($outputTypeClrName)) {
      $outputTypeClrName
    } else {
      $outputTypeName
    }
  }

  return [pscustomobject][ordered]@{
    name = $outputTypeName
    nameCodeUnits = ConvertToUtf16CodeUnits $outputTypeName
    clrTypeName = $outputTypeClrName
    clrTypeNameCodeUnits = ConvertToUtf16CodeUnits $outputTypeClrName
    canonicalTypeName = $canonicalTypeName
    canonicalTypeNameCodeUnits = ConvertToUtf16CodeUnits $canonicalTypeName
    runtimeIdentity = $runtimeIdentity
    description = ''
  }
}

function GetRuntimeTypeInstanceIdentity([type]$type) {
  if ($type.IsPointer) {
    return ((GetRuntimeTypeInstanceIdentity $type.GetElementType()) + '*')
  }
  if ($type.IsByRef) {
    return ((GetRuntimeTypeInstanceIdentity $type.GetElementType()) + '&')
  }
  if ($type.IsArray) {
    $elementIdentity = GetRuntimeTypeInstanceIdentity $type.GetElementType()
    $rank = $type.GetArrayRank()
    if ($rank -eq 1 -and $type -eq $type.GetElementType().MakeArrayType()) {
      return ($elementIdentity + '[]')
    }
    if ($rank -eq 1) {
      return ($elementIdentity + '[*]')
    }
    return ($elementIdentity + '[' + (',' * ($rank - 1)) + ']')
  }
  if ($type.IsGenericType -and -not $type.IsGenericTypeDefinition) {
    $definitionIdentity = GetRuntimeTypeInstanceIdentity $type.GetGenericTypeDefinition()
    $argumentIdentities = foreach ($argument in $type.GetGenericArguments()) {
      GetRuntimeTypeInstanceIdentity $argument
    }
    return ($definitionIdentity + '[' + ($argumentIdentities -join ',') + ']')
  }

  $assemblyIndex = 0
  $matchedAssemblyIndex = -1
  foreach ($assembly in [System.AppDomain]::CurrentDomain.GetAssemblies()) {
    if ([object]::ReferenceEquals($assembly, $type.Assembly)) {
      $matchedAssemblyIndex = $assemblyIndex
      break
    }
    $assemblyIndex++
  }
  if ($matchedAssemblyIndex -lt 0) {
    throw ('Runtime output assembly identity is unavailable: ' + [string]$type.FullName)
  }
  return ([string]$type.Assembly.FullName + '#' +
    $matchedAssemblyIndex.ToString([System.Globalization.CultureInfo]::InvariantCulture) + '::' +
    [string]$type.FullName)
}
