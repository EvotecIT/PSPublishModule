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
    'TestPowerShellSimpleTypeName',
    'TestPowerShellTypeLiteral',
    'TestPowerShellTypeLiteralName',
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

function NewCollectorProtocol([System.Management.Automation.PSModuleInfo]$helperModule) {
  return [pscustomobject]@{
    ConvertToRuntimeDefaultValue = $helperModule.ExportedFunctions['ConvertToRuntimeDefaultValue']
    ConvertToUtf16CodeUnits = $helperModule.ExportedFunctions['ConvertToUtf16CodeUnits']
    ConvertToUtf8SafeJsonText = $helperModule.ExportedFunctions['ConvertToUtf8SafeJsonText']
    EmitError = (Microsoft.PowerShell.Core\Get-Command EmitError -CommandType Function).ScriptBlock
    GetCanonicalTypeNameFromType = $helperModule.ExportedFunctions['GetCanonicalTypeNameFromType']
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
