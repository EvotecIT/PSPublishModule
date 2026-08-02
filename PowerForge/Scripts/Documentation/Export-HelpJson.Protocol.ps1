function EmitError([string]$msg) {
  try {
    $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$msg))
    Microsoft.PowerShell.Utility\Write-Output ('PFDOCS::ERROR::' + $b64)
  } catch {
    Microsoft.PowerShell.Utility\Write-Output 'PFDOCS::ERROR::'
  }
}

function GetCollectorHelperFunctionNames {
  return [string[]]@(
    'AddRuntimeDefaultValueReference',
    'AddRuntimeDefaultValueTokens',
    'AddRuntimeNumericDefaultValueToken',
    'AddRuntimeTypeShapeTokens',
    'AssertExactLoadedTypeIdentity',
    'ConvertRuntimeTypeTextToBase64',
    'ConvertToPowerShellTypeIdentityText',
    'ConvertToRuntimeDefaultValue',
    'ConvertToUtf16CodeUnits',
    'ConvertToUtf8SafeJsonText',
    'EmitError',
    'GetCanonicalTypeNameFromType',
    'GetAutomationNullArrayPredicate',
    'GetAutomationNullDictionaryEntryPredicate',
    'GetAutomationNullListPredicate',
    'GetAutomationNullValueProperty',
    'GetCollectionCapacity',
    'GetCollectorHelperFunctionNames',
    'GetConstructibleDictionaryTypeName',
    'GetCoreRuntimeType',
    'GetDictionaryCapacity',
    'GetDictionaryComparer',
    'GetDictionaryConstructorExpression',
    'GetDocumentedModuleCommands',
    'GetDocumentationParameterDeclaringMetadata',
    'GetDocumentationParameterNames',
    'GetDocumentationParameterRuntimeMetadata',
    'GetDocumentationRuntimeInputs',
    'GetExactLoadedTypeMatches',
    'GetKnownDictionaryComparerExpression',
    'GetKnownDictionaryComparerName',
    'GetOutputTypeSnapshot',
    'GetPowerShellSafeEnumName',
    'GetPowerShellTypeDefaultExpression',
    'GetRuntimeIdentityType',
    'GetRuntimeTypeInstanceIdentity',
    'GetRuntimeTypeShape',
    'GetText',
    'ImportDocumentedModule',
    'NewDocumentationModuleSnapshot',
    'RemoveCollectorHelperAliases',
    'ResolveCanonicalTypeName',
    'ResolveExactType',
    'ResolveUniqueNestedType',
    'ResolveUniqueTypeCaseInsensitive',
    'TestCollectionHasItemOnlyBackingStore',
    'TestExactRuntimeValueType',
    'TestGenuineRuntimeTypeValue',
    'TestDocumentationParameterDontShow',
    'TestPowerShellSimpleTypeName',
    'TestPowerShellTypeLiteral',
    'TestPowerShellTypeLiteralName',
    'TestPublicEmptyArraySingleton',
    'TestPSDefaultValueAutomationNull',
    'TestPSDefaultValueContainsAutomationNull',
    'TestRecreatableUri',
    'TestRecreatableScriptBlock',
    'TestValidateSetCaseSensitive'
  )
}

function ImportDocumentedModule([string]$manifestPath) {
  $variableImportFilter = '__PowerForgeDocumentationCollector_' +
    [guid]::NewGuid().ToString('N')
  $aliasImportFilter = '__PowerForgeDocumentationCollector_' +
    [guid]::NewGuid().ToString('N')
  return Microsoft.PowerShell.Core\Import-Module -Name $manifestPath -Force -PassThru -Scope Global `
    -Function '*' -Cmdlet '*' -Alias $aliasImportFilter -Variable $variableImportFilter `
    -ErrorAction Stop
}

function GetDocumentedModuleCommands([System.Management.Automation.PSModuleInfo]$module) {
  return @(@($module.ExportedCmdlets.Values) + @($module.ExportedFunctions.Values)) |
    Microsoft.PowerShell.Core\Where-Object {
      $_.CommandType -eq 'Cmdlet' -or $_.CommandType -eq 'Function'
    } | Microsoft.PowerShell.Utility\Sort-Object -Property Name
}

function NewDocumentationModuleSnapshot([object]$manifest, [string]$moduleName) {
  return [ordered]@{
    moduleName = $moduleName
    moduleVersion = if ($manifest -and $manifest.ModuleVersion) { [string]$manifest.ModuleVersion } else { $null }
    moduleGuid = if ($manifest -and $manifest.GUID) { [string]$manifest.GUID } else { $null }
    moduleDescription = if ($manifest -and $manifest.Description) { [string]$manifest.Description } else { $null }
    helpInfoUri = if ($manifest -and $manifest.HelpInfoURI) { [string]$manifest.HelpInfoURI } else { $null }
    projectUri = $(try {
      if ($manifest -and $manifest.PrivateData -and $manifest.PrivateData.PSData -and $manifest.PrivateData.PSData.ProjectUri) {
        [string]$manifest.PrivateData.PSData.ProjectUri
      } else { $null }
    } catch { $null })
    commands = @()
  }
}

function GetDocumentationParameterDeclaringMetadata([type]$implementingType, [string]$parameterName) {
  if ($null -eq $implementingType -or [string]::IsNullOrWhiteSpace($parameterName)) { return $null }
  try {
    $property = $implementingType.GetProperty(
      $parameterName,
      [System.Reflection.BindingFlags]'Instance,Public,FlattenHierarchy')
    if ($null -ne $property -and $null -ne $property.DeclaringType) {
      return [pscustomobject]@{
        TypeName = [string]$property.DeclaringType.FullName
        AssemblyPath = $(try { [string]$property.DeclaringType.Assembly.Location } catch { $null })
      }
    }
  } catch {
    return $null
  }
  return $null
}

function TestDocumentationParameterDontShow([object[]]$attributes) {
  foreach ($attribute in @($attributes)) {
    if ($attribute -is [System.Management.Automation.ParameterAttribute] -and $attribute.DontShow) {
      return $true
    }
  }
  return $false
}

function GetDocumentationParameterRuntimeMetadata(
  [System.Management.Automation.CommandInfo]$command,
  [string]$parameterName,
  [scriptblock]$getDeclaringMetadata,
  [scriptblock]$testDontShow,
  [scriptblock]$getCanonicalTypeName
) {
  $metadata = $null
  try { $metadata = $command.Parameters[$parameterName] } catch { $metadata = $null }
  $parameterType = if ($null -ne $metadata) { $metadata.ParameterType } else { $null }
  $nullableArrayRanks = Microsoft.PowerShell.Utility\New-Object System.Collections.Generic.List[int]
  $elementType = $parameterType
  while ($null -ne $elementType -and $elementType.IsArray) {
    $nullableArrayRanks.Add([int]$elementType.GetArrayRank())
    $elementType = $elementType.GetElementType()
  }
  $nullableUnderlyingType = if ($null -ne $elementType) {
    [System.Nullable]::GetUnderlyingType($elementType)
  } else { $null }
  $enumType = if ($null -ne $nullableUnderlyingType) { $nullableUnderlyingType } else { $elementType }
  $attributes = if ($null -ne $metadata) { @($metadata.Attributes) } else { @() }
  $declaringMetadata = & $getDeclaringMetadata $command.ImplementingType $parameterName
  return [pscustomobject]@{
    Metadata = $metadata
    ParameterType = $parameterType
    RuntimeTypeName = $(if ($null -ne $parameterType) {
      & $getCanonicalTypeName $parameterType
    } else { '' })
    RuntimeClrTypeName = $(if ($null -ne $parameterType -and -not [string]::IsNullOrEmpty($parameterType.FullName)) {
      [string]$parameterType.FullName
    } else { '' })
    EnumType = $enumType
    DisplayName = $(if ($null -ne $parameterType) { [string]$parameterType.Name } else { '' })
    NullableUnderlyingTypeName = $(if ($null -ne $nullableUnderlyingType) {
      if ($nullableUnderlyingType.IsGenericType) {
        & $getCanonicalTypeName $nullableUnderlyingType
      } else {
        [string]$nullableUnderlyingType.Name
      }
    } else { $null })
    NullableArrayRanks = @($nullableArrayRanks)
    DeclaringType = $(if ($null -ne $declaringMetadata) { $declaringMetadata.TypeName } else { $null })
    DeclaringAssemblyPath = $(if ($null -ne $declaringMetadata) { $declaringMetadata.AssemblyPath } else { $null })
    DontShow = & $testDontShow $attributes
  }
}

function GetDocumentationParameterNames(
  [System.Management.Automation.CommandInfo]$command,
  [object[]]$helpParameters
) {
  $commonNames = @(
    'Verbose','Debug','ErrorAction','ErrorVariable','WarningAction','WarningVariable',
    'InformationAction','InformationVariable','OutVariable','OutBuffer','PipelineVariable',
    'WhatIf','Confirm','ProgressAction')
  $names = @()
  try { $names = @($command.Parameters.Keys) } catch { $names = @() }
  foreach ($helpParameter in @($helpParameters)) {
    try { $names += [string]$helpParameter.Name } catch { }
  }
  return @($names | Microsoft.PowerShell.Core\Where-Object {
    $_ -and ($commonNames -notcontains $_)
  } | Microsoft.PowerShell.Utility\Sort-Object -Unique)
}

function GetDocumentationRuntimeInputs(
  [System.Management.Automation.CommandInfo]$command,
  [string[]]$parameterNames,
  [scriptblock]$getCanonicalTypeName
) {
  $runtimeInputs = @()
  $seenInputs = @{}
  foreach ($parameterName in @($parameterNames)) {
    $metadata = $null
    try { $metadata = $command.Parameters[$parameterName] } catch { $metadata = $null }
    if (-not $metadata) { continue }

    $supportsPipeline = $false
    try {
      foreach ($setEntry in @($metadata.ParameterSets.GetEnumerator())) {
        $setMetadata = $setEntry.Value
        if ($setMetadata -and ($setMetadata.ValueFromPipeline -or $setMetadata.ValueFromPipelineByPropertyName)) {
          $supportsPipeline = $true
          break
        }
      }
    } catch { }
    if (-not $supportsPipeline) { continue }

    $typeName = ''
    $clrTypeName = ''
    $canonicalTypeName = ''
    try {
      if ($metadata.ParameterType) {
        $typeName = [string]$metadata.ParameterType.Name
        $clrTypeName = [string]$metadata.ParameterType.FullName
        $canonicalTypeName = & $getCanonicalTypeName $metadata.ParameterType
      }
    } catch { }
    if (-not $typeName) { continue }

    $key = if ($canonicalTypeName) { $canonicalTypeName } elseif ($clrTypeName) { $clrTypeName } else { $typeName }
    if ($seenInputs.ContainsKey($key)) { continue }
    $seenInputs[$key] = $true
    $runtimeInputs += [ordered]@{
      name = $typeName
      clrTypeName = $clrTypeName
      canonicalTypeName = $canonicalTypeName
      description = ''
    }
  }
  return @($runtimeInputs)
}

function NewCollectorProtocol([System.Management.Automation.PSModuleInfo]$helperModule) {
  return [pscustomobject]@{
    ConvertToRuntimeDefaultValue = $helperModule.ExportedFunctions['ConvertToRuntimeDefaultValue']
    ConvertToUtf16CodeUnits = $helperModule.ExportedFunctions['ConvertToUtf16CodeUnits']
    ConvertToUtf8SafeJsonText = $helperModule.ExportedFunctions['ConvertToUtf8SafeJsonText']
    EmitError = (Microsoft.PowerShell.Core\Get-Command EmitError -CommandType Function).ScriptBlock
    GetCanonicalTypeNameFromType = $helperModule.ExportedFunctions['GetCanonicalTypeNameFromType'].ScriptBlock
    GetParameterDeclaringMetadata = (Microsoft.PowerShell.Core\Get-Command GetDocumentationParameterDeclaringMetadata -CommandType Function).ScriptBlock
    GetParameterRuntimeMetadata = (Microsoft.PowerShell.Core\Get-Command GetDocumentationParameterRuntimeMetadata -CommandType Function).ScriptBlock
    GetParameterNames = (Microsoft.PowerShell.Core\Get-Command GetDocumentationParameterNames -CommandType Function).ScriptBlock
    GetRuntimeInputs = (Microsoft.PowerShell.Core\Get-Command GetDocumentationRuntimeInputs -CommandType Function).ScriptBlock
    GetDocumentedModuleCommands = (Microsoft.PowerShell.Core\Get-Command GetDocumentedModuleCommands -CommandType Function).ScriptBlock
    GetOutputTypeSnapshot = $helperModule.ExportedFunctions['GetOutputTypeSnapshot']
    GetText = $helperModule.ExportedFunctions['GetText']
    HelperFunctionNames = GetCollectorHelperFunctionNames
    ImportDocumentedModule = (Microsoft.PowerShell.Core\Get-Command ImportDocumentedModule -CommandType Function).ScriptBlock
    NewModuleSnapshot = (Microsoft.PowerShell.Core\Get-Command NewDocumentationModuleSnapshot -CommandType Function).ScriptBlock
    RemoveHelperAliases = (Microsoft.PowerShell.Core\Get-Command RemoveCollectorHelperAliases -CommandType Function).ScriptBlock
    ResolveCanonicalTypeName = $helperModule.ExportedFunctions['ResolveCanonicalTypeName']
    TestValidateSetCaseSensitive = $helperModule.ExportedFunctions['TestValidateSetCaseSensitive']
    TestPSDefaultValueContainsAutomationNull = $helperModule.ExportedFunctions['TestPSDefaultValueContainsAutomationNull']
    TestParameterDontShow = (Microsoft.PowerShell.Core\Get-Command TestDocumentationParameterDontShow -CommandType Function).ScriptBlock
  }
}

function RemoveCollectorHelperAliases(
  [string]$targetModuleName,
  [string[]]$helperNames
) {
  foreach ($helperName in $helperNames) {
    $alias = Microsoft.PowerShell.Core\Get-Command -Name $helperName -CommandType Alias -ErrorAction SilentlyContinue
    if ($null -ne $alias -and
        ($alias.ModuleName -eq $targetModuleName -or $alias.Source -eq $targetModuleName)) {
      Microsoft.PowerShell.Management\Remove-Item -LiteralPath ('Alias:' + $helperName) -Force
    }
  }
}
