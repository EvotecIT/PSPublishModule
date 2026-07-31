function EmitError([string]$msg) {
  try {
    $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$msg))
    Write-Output ('PFDOCS::ERROR::' + $b64)
  } catch {
    Write-Output 'PFDOCS::ERROR::'
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
    'EmitError',
    'GetCanonicalTypeNameFromType',
    'GetCollectionCapacity',
    'GetCollectorHelperFunctionNames',
    'GetConstructibleDictionaryTypeName',
    'GetCoreRuntimeType',
    'GetDictionaryCapacity',
    'GetDictionaryComparer',
    'GetDictionaryConstructorExpression',
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
    'TestRecreatableUri',
    'TestRecreatableScriptBlock'
  )
}

function NewCollectorProtocol([System.Management.Automation.PSModuleInfo]$helperModule) {
  return [pscustomobject]@{
    ConvertToRuntimeDefaultValue = $helperModule.ExportedFunctions['ConvertToRuntimeDefaultValue']
    ConvertToUtf16CodeUnits = $helperModule.ExportedFunctions['ConvertToUtf16CodeUnits']
    EmitError = Get-Command EmitError -CommandType Function
    GetCanonicalTypeNameFromType = $helperModule.ExportedFunctions['GetCanonicalTypeNameFromType']
    GetOutputTypeSnapshot = $helperModule.ExportedFunctions['GetOutputTypeSnapshot']
    GetText = $helperModule.ExportedFunctions['GetText']
    HelperFunctionNames = GetCollectorHelperFunctionNames
    RemoveHelperAliases = Get-Command RemoveCollectorHelperAliases -CommandType Function
    ResolveCanonicalTypeName = $helperModule.ExportedFunctions['ResolveCanonicalTypeName']
  }
}

function RemoveCollectorHelperAliases(
  [string]$targetModuleName,
  [string[]]$helperNames
) {
  foreach ($helperName in $helperNames) {
    $alias = Get-Command -Name $helperName -CommandType Alias -ErrorAction SilentlyContinue
    if ($null -ne $alias -and
        ($alias.ModuleName -eq $targetModuleName -or $alias.Source -eq $targetModuleName)) {
      Remove-Item -LiteralPath ('Alias:' + $helperName) -Force
    }
  }
}
