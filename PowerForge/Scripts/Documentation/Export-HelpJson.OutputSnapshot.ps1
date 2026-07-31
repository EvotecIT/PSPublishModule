function GetOutputTypeSnapshot([object]$outputType) {
  $outputTypeName = ''
  $outputTypeClrName = ''
  $canonicalTypeName = ''
  $runtimeType = $null
  try { $outputTypeName = [string]$outputType.Name } catch { $outputTypeName = '' }
  try { $runtimeType = $outputType.Type } catch { $runtimeType = $null }
  if ($runtimeType -is [type]) {
    $outputTypeClrName = [string]$runtimeType.FullName
    $canonicalTypeName = GetCanonicalTypeNameFromType $runtimeType
    if ($runtimeType.IsGenericType) { $outputTypeName = $canonicalTypeName }
  }
  if (-not $outputTypeClrName) {
    try { $outputTypeClrName = [string]$outputType.TypeName.FullName } catch { $outputTypeClrName = '' }
  }
  if (-not $outputTypeClrName) { $outputTypeClrName = $outputTypeName }
  if (-not $outputTypeName) { $outputTypeName = $outputTypeClrName }
  if (-not $outputTypeName) { return $null }
  if (-not $canonicalTypeName) {
    foreach ($candidate in @($outputTypeClrName, $outputTypeName)) {
      $canonicalTypeName = ResolveCanonicalTypeName $candidate
      if ($canonicalTypeName) { break }
    }
  }

  return [pscustomobject][ordered]@{
    name = $outputTypeName
    clrTypeName = $outputTypeClrName
    canonicalTypeName = $canonicalTypeName
    description = ''
  }
}
