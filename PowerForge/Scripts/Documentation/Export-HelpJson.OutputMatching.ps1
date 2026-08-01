function GetRuntimeTypeInstanceIdentity([type]$type) {
  if ($type.IsPointer) { return 'pointer:' + (GetRuntimeTypeInstanceIdentity ($type.GetElementType())) }
  if ($type.IsByRef) { return 'byref:' + (GetRuntimeTypeInstanceIdentity ($type.GetElementType())) }
  if ($type.IsArray) {
    $arrayKind = if ($type.GetArrayRank() -eq 1 -and $type -eq $type.GetElementType().MakeArrayType()) { 'sz' } else { [string]$type.GetArrayRank() }
    return 'array:' + $arrayKind + ':' + (GetRuntimeTypeInstanceIdentity ($type.GetElementType()))
  }
  if ($type.IsGenericType -and -not $type.IsGenericTypeDefinition) {
    $parts = [System.Collections.Generic.List[string]]::new()
    $parts.Add((GetRuntimeTypeInstanceIdentity ($type.GetGenericTypeDefinition())))
    foreach ($argument in $type.GetGenericArguments()) {
      $parts.Add((GetRuntimeTypeInstanceIdentity $argument))
    }
    return 'generic:' + ($parts -join '|')
  }

  $assemblies = [System.AppDomain]::CurrentDomain.GetAssemblies()
  for ($index = 0; $index -lt $assemblies.Count; $index++) {
    if ([object]::ReferenceEquals($assemblies[$index], $type.Assembly)) {
      return 'type:' + $index.ToString([System.Globalization.CultureInfo]::InvariantCulture) + ':' + (GetCanonicalTypeNameFromType $type)
    }
  }
  throw ('Runtime type assembly is not loaded: ' + [string]$type.FullName)
}

function GetOutputTypeMetadata([object]$outputType) {
  $outputTypeName = ''
  $outputTypeClrName = ''
  $outputRuntimeType = $null
  try { $outputTypeName = [string]$outputType.Name } catch { $outputTypeName = '' }
  try { $outputRuntimeType = $outputType.Type } catch { $outputRuntimeType = $null }
  if ($outputRuntimeType -is [type]) { $outputTypeClrName = [string]$outputRuntimeType.FullName }
  if (-not $outputTypeClrName) {
    try { $outputTypeClrName = [string]$outputType.TypeName.FullName } catch { $outputTypeClrName = '' }
  }
  if (-not $outputTypeClrName) {
    try { $outputTypeClrName = [string]$outputType.Type.FullName } catch {
      # best effort: OutputType wrappers differ between hosts and command kinds
    }
  }
  if (-not $outputTypeClrName) { $outputTypeClrName = $outputTypeName }
  if (-not $outputTypeName) { $outputTypeName = $outputTypeClrName }
  if (-not $outputTypeName) { return $null }
  $outputIdentity = if ($outputRuntimeType -is [type]) {
    GetCanonicalTypeNameFromType $outputRuntimeType
  } else {
    if (-not [string]::IsNullOrWhiteSpace($outputTypeClrName)) {
      $outputTypeClrName
    } else {
      $outputTypeName
    }
  }
  if ($outputRuntimeType -is [type] -and $outputRuntimeType.IsGenericType) {
    $outputTypeName = $outputIdentity
  }
  return [pscustomobject][ordered]@{
    name = $outputTypeName
    clrTypeName = $outputTypeClrName
    identity = $outputIdentity
    runtimeIdentity = if ($outputRuntimeType -is [type]) { GetRuntimeTypeInstanceIdentity $outputRuntimeType } else { '' }
    keys = @(GetTypeKeys $outputTypeName $outputTypeClrName)
  }
}

function ConvertToXmlSafeIdentityText([string]$text) {
  $builder = [System.Text.StringBuilder]::new()
  for ($index = 0; $index -lt $text.Length; $index++) {
    $character = $text[$index]
    if ($character -eq '%') {
      [void]$builder.Append('%25')
    } elseif ([char]::IsHighSurrogate($character) -and
        $index + 1 -lt $text.Length -and
        [char]::IsLowSurrogate($text[$index + 1])) {
      [void]$builder.Append($character)
      [void]$builder.Append($text[++$index])
    } elseif ([System.Xml.XmlConvert]::IsXmlChar($character)) {
      [void]$builder.Append($character)
    } else {
      [void]$builder.Append('%u' + ([int]$character).ToString('X4', [System.Globalization.CultureInfo]::InvariantCulture))
    }
  }
  return $builder.ToString()
}

function TestXmlSafeIdentityText([string]$text) {
  try {
    [void][System.Xml.XmlConvert]::VerifyXmlChars($text)
    return $true
  } catch {
    return $false
  }
}

function GetDocumentedModuleCommands([System.Management.Automation.PSModuleInfo]$module) {
  return @(@($module.ExportedCmdlets.Values) + @($module.ExportedFunctions.Values)) | Microsoft.PowerShell.Core\Where-Object {
    $_.CommandType -eq 'Cmdlet' -or $_.CommandType -eq 'Function'
  } | Microsoft.PowerShell.Utility\Sort-Object -Property Name
}

function GetDocumentedModuleCommandSnapshot(
  [System.Management.Automation.PSModuleInfo]$module,
  [System.Management.Automation.CommandInfo]$testXmlSafeIdentityText
) {
  $commands = @(GetDocumentedModuleCommands $module)
  $helpByCommandName = [System.Collections.Generic.Dictionary[string,object]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
  foreach ($command in $commands) {
    if (-not (& $testXmlSafeIdentityText ([string]$command.Name))) {
      throw ('Command name contains XML-invalid characters: ' + [string]$command.Name)
    }
    $help = $null
    try {
      $help = Microsoft.PowerShell.Core\Get-Help -Name $command.Name -Full -ErrorAction SilentlyContinue
    } catch {
      $help = $null
    }
    $helpByCommandName[[string]$command.Name] = $help
  }
  return [pscustomobject]@{ Commands = @($commands); HelpByCommandName = $helpByCommandName }
}

function ConvertOutputsToXmlSafeDocumentationText([object[]]$outputs) {
  foreach ($output in @($outputs)) {
    $output.name = ConvertToXmlSafeIdentityText ([string]$output.name)
    $output.clrTypeName = ConvertToXmlSafeIdentityText ([string]$output.clrTypeName)
    $output.description = ConvertToXmlSafeDefaultHelpText ([string]$output.description)
  }
  return @($outputs)
}

function AddTypeKeysToIndexes(
  [object]$value,
  [string[]]$keys,
  [object]$byExactKey,
  [object]$exactCounts,
  [object]$byFoldedKey,
  [object]$foldedCounts
) {
  $seenExact = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  $seenFolded = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  foreach ($key in $keys) {
    if ([string]::IsNullOrWhiteSpace($key)) { continue }
    if ($seenExact.Add($key)) {
      $exactCounts[$key] = if ($exactCounts.ContainsKey($key)) { [int]$exactCounts[$key] + 1 } else { 1 }
      if (-not $byExactKey.ContainsKey($key)) { $byExactKey[$key] = $value }
    }
    if ($seenFolded.Add($key)) {
      $foldedCounts[$key] = if ($foldedCounts.ContainsKey($key)) { [int]$foldedCounts[$key] + 1 } else { 1 }
      if (-not $byFoldedKey.ContainsKey($key)) { $byFoldedKey[$key] = $value }
    }
  }
}

function GetUniqueUnqualifiedCaseInsensitiveTypeMatch(
  [string[]]$keys,
  [object]$candidateByFoldedKey,
  [object]$candidateFoldedCounts,
  [object]$sourceFoldedCounts
) {
  foreach ($key in $keys) {
    if ([string]::IsNullOrWhiteSpace($key)) { continue }
    $genericIndex = $key.IndexOf('[')
    $baseName = if ($genericIndex -ge 0) { $key.Substring(0, $genericIndex) } else { $key }
    if ($baseName.Contains('.') -or $baseName.Contains('+')) { continue }
    if ($candidateByFoldedKey.ContainsKey($key) -and
        [int]$candidateFoldedCounts[$key] -eq 1 -and
        [int]$sourceFoldedCounts[$key] -eq 1) {
      return $candidateByFoldedKey[$key]
    }
  }
  return $null
}
