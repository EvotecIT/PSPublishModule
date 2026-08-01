param(
  [string]$StagingPath,
  [string]$ManifestPath,
  [string]$OutputJsonPath
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# <PowerForgeCollectorProtocolHelpers />
$collectorHelperModule = New-Module -Name ('PowerForgeDocumentationCollectorHelpers_' +
  [guid]::NewGuid().ToString('N')) -ScriptBlock {
# <PowerForgeTypeIdentityHelpers />
# <PowerForgeRuntimeValueHelpers />
# <PowerForgeOutputSnapshotHelpers />
function AddRuntimeDefaultValueTokens(
  [object]$value,
  [System.Collections.IList]$tokens,
  [System.Collections.IList]$referenceStack = $null
) {
  if ($null -eq $referenceStack) {
    $referenceStack = [System.Collections.ArrayList]::new()
  }
  if ($null -eq $value) {
    $tokens.Add([ordered]@{ kind = 'Null' }) | Out-Null
    return
  }
  AddRuntimeDefaultValueReference $value $referenceStack
  if ($value -is [string]) {
    $tokens.Add([ordered]@{
      kind = 'StringCodeUnits'
      text = ConvertToUtf16CodeUnits ([string]$value)
      isInterned = [object]::ReferenceEquals(
        $value, [string]::IsInterned([string]$value))
    }) | Out-Null
    return
  }
  if ($value -is [char]) {
    $tokens.Add([ordered]@{
      kind = 'CharCodeUnit'
      text = ([int][char]$value).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }) | Out-Null
    return
  }
  if ($value -is [bool]) {
    $tokens.Add([ordered]@{ kind = 'Boolean'; text = [string]$value }) | Out-Null
    return
  }
  if (TestExactRuntimeValueType $value ([System.Management.Automation.SwitchParameter])) {
    $tokens.Add([ordered]@{
      kind = 'SwitchParameter'
      text = if ($value.IsPresent) { 'True' } else { 'False' }
    }) | Out-Null
    return
  }
  if ($value -is [enum]) {
    $enumType = $value.GetType()
    $underlyingType = [System.Enum]::GetUnderlyingType($enumType)
    $underlyingValue = [System.Convert]::ChangeType(
      $value,
      $underlyingType,
      [System.Globalization.CultureInfo]::InvariantCulture)
    $tokens.Add([ordered]@{
      kind = 'Enum'
      text = [System.Convert]::ToString($underlyingValue, [System.Globalization.CultureInfo]::InvariantCulture)
      name = GetPowerShellSafeEnumName $enumType $value
      canonicalTypeName = GetCanonicalTypeNameFromType $enumType
      underlyingTypeName = GetCanonicalTypeNameFromType $underlyingType
      runtimeTypeNameCodeUnits = ConvertToUtf16CodeUnits ([string]$enumType.FullName)
      assemblyNameCodeUnits = ConvertToUtf16CodeUnits ([string]$enumType.Assembly.FullName)
      runtimeTypeShape = GetRuntimeTypeShape $enumType
    }) | Out-Null
    return
  }
  if ($value -is [type]) {
    if (-not (TestGenuineRuntimeTypeValue $value)) {
      throw 'Delegated or custom Type defaults are not supported.'
    }
    if ($value.IsGenericParameter) {
      throw 'Generic-parameter Type defaults are not supported.'
    }
    $runtimeIdentityType = GetRuntimeIdentityType $value
    $tokens.Add([ordered]@{
      kind = 'Type'
      canonicalTypeName = GetCanonicalTypeNameFromType $value
      text = ConvertToUtf16CodeUnits ([string]$runtimeIdentityType.FullName)
      assemblyNameCodeUnits = ConvertToUtf16CodeUnits ([string]$runtimeIdentityType.Assembly.FullName)
      runtimeTypeShape = GetRuntimeTypeShape $value
    }) | Out-Null
    return
  }
  if (AddRuntimeNumericDefaultValueToken $value $tokens) { return }
  if (TestExactRuntimeValueType $value ([System.Numerics.BigInteger])) {
    $tokens.Add([ordered]@{
      kind = 'BigInteger'
      text = $value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }) | Out-Null
    return
  }
  if ($value -is [guid]) {
    $tokens.Add([ordered]@{
      kind = 'Guid'
      text = $value.ToString('D')
    }) | Out-Null
    return
  }
  if ($value -is [version]) {
    $tokens.Add([ordered]@{
      kind = 'Version'
      text = $value.ToString()
    }) | Out-Null
    return
  }
  if (TestExactRuntimeValueType $value ([uri])) {
    if (-not (TestRecreatableUri $value)) {
      throw 'Uri defaults with noncanonical reconstruction state are not supported.'
    }
    AddRuntimeDefaultValueReference $value.OriginalString $referenceStack
    $uriKind = if ($value.IsAbsoluteUri) { 'Absolute' } else { 'Relative' }
    $tokens.Add([ordered]@{
      kind = 'UriCodeUnits'
      text = ConvertToUtf16CodeUnits $value.OriginalString
      name = $uriKind
    }) | Out-Null
    return
  }
  if (TestExactRuntimeValueType $value (GetCoreRuntimeType 'System.DateOnly')) {
    $tokens.Add([ordered]@{
      kind = 'DateOnly'
      text = $value.DayNumber.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }) | Out-Null
    return
  }
  if (TestExactRuntimeValueType $value (GetCoreRuntimeType 'System.TimeOnly')) {
    $tokens.Add([ordered]@{
      kind = 'TimeOnly'
      text = $value.Ticks.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }) | Out-Null
    return
  }
  if ($value -is [datetime]) {
    if ($value.Kind -eq [System.DateTimeKind]::Local -and
        [System.TimeZoneInfo]::Local.IsAmbiguousTime($value)) {
      throw 'Ambiguous local DateTime defaults cannot be represented portably.'
    }
    $tokens.Add([ordered]@{
      kind = 'DateTime'
      text = $value.Ticks.ToString([System.Globalization.CultureInfo]::InvariantCulture)
      name = [string]$value.Kind
    }) | Out-Null
    return
  }
  if ($value -is [datetimeoffset]) {
    $tokens.Add([ordered]@{
      kind = 'DateTimeOffset'
      text = $value.ToString('O', [System.Globalization.CultureInfo]::InvariantCulture)
    }) | Out-Null
    return
  }
  if ($value -is [timespan]) {
    $tokens.Add([ordered]@{
      kind = 'TimeSpan'
      text = $value.ToString('c', [System.Globalization.CultureInfo]::InvariantCulture)
    }) | Out-Null
    return
  }
  if ($value -is [scriptblock]) {
    if (-not (TestRecreatableScriptBlock $value)) {
      throw 'Stateful, module-bound, or constrained ScriptBlock defaults are not supported.'
    }
    $tokens.Add([ordered]@{
      kind = 'ScriptBlockCodeUnits'
      text = ConvertToUtf16CodeUnits ([string]$value.ToString())
    }) | Out-Null
    return
  }
  if ($value -is [System.Collections.IDictionary] -or
      $value -is [System.Collections.IEnumerable]) {
      if ($value -is [System.Collections.IDictionary]) {
        $comparerType = $null
        $comparer = GetDictionaryComparer $value ([ref]$comparerType)
        $comparerName = GetKnownDictionaryComparerName $comparer $comparerType
        $dictionaryTypeName = GetConstructibleDictionaryTypeName $value
        if ([string]::IsNullOrWhiteSpace($dictionaryTypeName)) {
          throw ('Dictionary type has no supported constructor: ' + $value.GetType().FullName)
        }
        [void](GetDictionaryConstructorExpression $value $referenceStack)
        $tokens.Add([ordered]@{
          kind = 'DictionaryStart'
          text = GetDictionaryCapacity $value
          canonicalTypeName = $dictionaryTypeName
          name = $comparerName
          runtimeTypeNameCodeUnits = ConvertToUtf16CodeUnits ([string]$value.GetType().FullName)
          assemblyNameCodeUnits = ConvertToUtf16CodeUnits ([string]$value.GetType().Assembly.FullName)
          runtimeTypeShape = GetRuntimeTypeShape ($value.GetType())
        }) | Out-Null
        foreach ($entry in $value.GetEnumerator()) {
          $tokens.Add([ordered]@{ kind = 'DictionaryEntryStart' }) | Out-Null
          AddRuntimeDefaultValueTokens $entry.Key $tokens $referenceStack
          AddRuntimeDefaultValueTokens $entry.Value $tokens $referenceStack
          $tokens.Add([ordered]@{ kind = 'DictionaryEntryEnd' }) | Out-Null
        }
        $tokens.Add([ordered]@{ kind = 'DictionaryEnd' }) | Out-Null
        return
      }
      if ($value -isnot [System.Collections.IList] -and $value -isnot [System.Array]) {
        throw ('Unsupported enumerable default type: ' + $value.GetType().FullName)
      }
      if ($value -is [System.Array] -and
          ($value.Rank -gt 1 -or $value.GetType() -ne $value.GetType().GetElementType().MakeArrayType())) {
        $lengths = [System.Collections.Generic.List[string]]::new()
        $lowerBounds = [System.Collections.Generic.List[string]]::new()
        for ($dimension = 0; $dimension -lt $value.Rank; $dimension++) {
          $lengths.Add($value.GetLength($dimension).ToString([System.Globalization.CultureInfo]::InvariantCulture))
          $lowerBounds.Add($value.GetLowerBound($dimension).ToString([System.Globalization.CultureInfo]::InvariantCulture))
        }
        $elementType = $value.GetType().GetElementType()
        $runtimeElementType = GetRuntimeIdentityType $elementType
        $tokens.Add([ordered]@{
          kind = 'ArrayStart'
          text = $lengths -join ','
          name = $lowerBounds -join ','
          canonicalTypeName = GetCanonicalTypeNameFromType $elementType
          runtimeTypeNameCodeUnits = ConvertToUtf16CodeUnits ([string]$runtimeElementType.FullName)
          assemblyNameCodeUnits = ConvertToUtf16CodeUnits ([string]$runtimeElementType.Assembly.FullName)
          runtimeTypeShape = GetRuntimeTypeShape $elementType
        }) | Out-Null
        foreach ($item in $value) {
          AddRuntimeDefaultValueTokens $item $tokens $referenceStack
        }
        $tokens.Add([ordered]@{ kind = 'ArrayEnd' }) | Out-Null
        return
      }
      $collectionType = $value.GetType()
      if ($value -isnot [System.Array]) {
        $supportedItemOnlyList = [object]::ReferenceEquals(
          $collectionType,
          [System.Collections.ArrayList])
        if (-not $supportedItemOnlyList -and $collectionType.IsGenericType) {
          $genericDefinition = $collectionType.GetGenericTypeDefinition()
          $supportedItemOnlyList =
            [object]::ReferenceEquals($genericDefinition, [System.Collections.Generic.List``1]) -or
            ([object]::ReferenceEquals($genericDefinition, [System.Collections.ObjectModel.Collection``1]) -and
              (TestCollectionHasItemOnlyBackingStore $value $referenceStack))
        }
        if (-not $supportedItemOnlyList) {
          throw ('Collection type carries unsupported non-item state: ' + $collectionType.FullName)
        }
        $constructor = $collectionType.GetConstructor([System.Type]::EmptyTypes)
        if ($collectionType.IsAbstract -or $collectionType.IsInterface -or $null -eq $constructor) {
          throw ('Collection type has no supported constructor: ' + $collectionType.FullName)
        }
      }
      $elementType = if ($value -is [System.Array]) { $collectionType.GetElementType() } else { $null }
      $runtimeConstructionType = if ($null -ne $elementType) {
        GetRuntimeIdentityType $elementType
      } else {
        GetRuntimeIdentityType $collectionType
      }
      $tokens.Add([ordered]@{
        kind = 'CollectionStart'
        text = GetCollectionCapacity $value
        canonicalTypeName = GetCanonicalTypeNameFromType $collectionType
        elementTypeName = if ($null -ne $elementType) { GetCanonicalTypeNameFromType $elementType } else { $null }
        runtimeTypeNameCodeUnits = ConvertToUtf16CodeUnits ([string]$runtimeConstructionType.FullName)
        assemblyNameCodeUnits = ConvertToUtf16CodeUnits ([string]$runtimeConstructionType.Assembly.FullName)
        runtimeTypeShape = GetRuntimeTypeShape $(if ($null -ne $elementType) { $elementType } else { $collectionType })
        name = if ($value -is [System.Array]) { 'Array' } else { 'List' }
      }) | Out-Null
      foreach ($item in $value) {
        AddRuntimeDefaultValueTokens $item $tokens $referenceStack
      }
      $tokens.Add([ordered]@{ kind = 'CollectionEnd' }) | Out-Null
      return
  }
  $runtimeType = $value.GetType()
  if (@(
      [sbyte],
      [byte],
      [int16],
      [uint16],
      [int32],
      [uint32],
      [int64],
      [uint64],
      [intptr],
      [uintptr]) -contains $runtimeType) {
    if ($runtimeType -eq [intptr]) {
      $pointerValue = $value.ToInt64()
      if ($pointerValue -lt [int]::MinValue -or $pointerValue -gt [int]::MaxValue) {
        throw 'IntPtr defaults outside the 32-bit range are not portable.'
      }
    } elseif ($runtimeType -eq [uintptr]) {
      $pointerValue = $value.ToUInt64()
      if ($pointerValue -gt [uint32]::MaxValue) {
        throw 'UIntPtr defaults outside the 32-bit range are not portable.'
      }
    }
    $scalarText = if ($runtimeType -eq [intptr] -or $runtimeType -eq [uintptr]) { $pointerValue.ToString([System.Globalization.CultureInfo]::InvariantCulture) } else {
      ([System.IFormattable]$value).ToString($null, [System.Globalization.CultureInfo]::InvariantCulture)
    }
    $tokens.Add([ordered]@{
      kind = 'Formattable'
      text = $scalarText
      canonicalTypeName = GetCanonicalTypeNameFromType ($value.GetType())
    }) | Out-Null
    return
  }
  throw ('Unsupported PSDefaultValue runtime type: ' + $runtimeType.FullName)
}
Export-ModuleMember -Function @(
  'ConvertToRuntimeDefaultValue', 'ConvertToUtf16CodeUnits', 'ConvertToUtf8SafeJsonText',
  'GetCanonicalTypeNameFromType', 'GetOutputTypeSnapshot', 'GetText', 'ResolveCanonicalTypeName', 'TestPSDefaultValueContainsAutomationNull', 'TestValidateSetCaseSensitive')
}
$collectorProtocol = NewCollectorProtocol $collectorHelperModule
try {
  if ([string]::IsNullOrWhiteSpace($ManifestPath) -or -not (Test-Path -LiteralPath $ManifestPath)) {
    throw ('Manifest not found: ' + $ManifestPath)
  }

  $m = $null
  try { $m = Import-PowerShellDataFile -Path $ManifestPath -ErrorAction Stop } catch { $m = $null }

  $mod = & $collectorProtocol.ImportDocumentedModule $ManifestPath
  $moduleNameResolved = $mod.Name
  & $collectorProtocol.RemoveHelperAliases $moduleNameResolved $collectorProtocol.HelperFunctionNames

  $commands = @(& $collectorProtocol.GetDocumentedModuleCommands $mod)

  $result = & $collectorProtocol.NewModuleSnapshot $m ([string]$moduleNameResolved)

  foreach ($c in $commands) {
    $help = $null
    try { $help = Microsoft.PowerShell.Core\Get-Help -Name $c.Name -Full -ErrorAction SilentlyContinue } catch { $help = $null }

    $implType = $null
    $dllPath = $null
    try { if ($c -and $c.ImplementingType) { $implType = [string]$c.ImplementingType.FullName } } catch { $implType = $null }
    try { if ($c -and $c.Dll) { $dllPath = [string]$c.Dll } } catch { $dllPath = $null }

    $defaultSet = $null
    try { $defaultSet = $c.DefaultParameterSet } catch { $defaultSet = $null }

    $commandParameterSets = @()
    if ($null -ne $c -and $null -ne $c.ParameterSets) { $commandParameterSets = @($c.ParameterSets) }

    $syntax = @()
    foreach ($ps in $commandParameterSets) {
      $syntax += [ordered]@{
        name = [string]$ps.Name
        isDefault = if ($defaultSet) { [bool]($ps.Name -eq $defaultSet) } else { $false }
        text = ([string]$c.Name + ' ' + [string]$ps.ToString())
      }
    }

    $paramSets = @{}
    foreach ($ps in $commandParameterSets) {
      $psParameters = @()
      if ($null -ne $ps -and $null -ne $ps.Parameters) { $psParameters = @($ps.Parameters) }
      foreach ($pp in $psParameters) {
        $pn = [string]$pp.Name
        if (-not $paramSets.ContainsKey($pn)) { $paramSets[$pn] = Microsoft.PowerShell.Utility\New-Object System.Collections.Generic.List[string] }
        $null = $paramSets[$pn].Add([string]$ps.Name)
      }
    }

    $helpParameters = @()
    try {
      if ($help -and $help.Parameters -and $help.Parameters.Parameter) { $helpParameters = @($help.Parameters.Parameter) }
    } catch { $helpParameters = @() }

    $helpParamByName = @{}
    foreach ($hp in $helpParameters) {
      try {
        $n = [string]$hp.Name
        if ($n) { $helpParamByName[$n] = $hp }
      } catch {
        # best effort: some help parameter entries expose Name dynamically or not at all
      }
    }

    $commonParamNames = @('Verbose','Debug','ErrorAction','ErrorVariable','WarningAction','WarningVariable','InformationAction','InformationVariable','OutVariable','OutBuffer','PipelineVariable','WhatIf','Confirm','ProgressAction')
    $paramNames = @()
    try {
      if ($c -and $c.Parameters) {
        $paramNames = @($c.Parameters.GetEnumerator() | Microsoft.PowerShell.Core\ForEach-Object { [string]$_.Key })
      }
    } catch { $paramNames = @() }
    foreach ($hp in $helpParameters) { try { $paramNames += [string]$hp.Name } catch {
      # best effort: keep extracting other parameters even when one help node is incomplete
    } }
    $paramNames = @($paramNames | Microsoft.PowerShell.Core\Where-Object { $_ -and ($commonParamNames -notcontains $_) } | Microsoft.PowerShell.Utility\Sort-Object -Unique)

    $parameters = @()
    foreach ($pn in $paramNames) {
      $pmeta = $null
      try { $pmeta = $c.Parameters[$pn] } catch { $pmeta = $null }

      $aliases = @()
      try { if ($pmeta -and $pmeta.Aliases) { foreach ($a in @($pmeta.Aliases)) { $aliases += [string]$a } } } catch { $aliases = @() }

      $typeName = ''
      $parameterType = $null
      try {
        if ($pmeta -and $pmeta.ParameterType) {
          $parameterType = $pmeta.ParameterType
          $typeName = [string]$pmeta.ParameterType.Name
        }
      } catch {
        $parameterType = $null
        $typeName = ''
      }
      $possibleValues = @()
      $enumPossibleValues = @()
      $hasValidateSet = $false
      $validateSetCaseSensitive = $false

      $required = $false
      $parameterSetRequired = @{}
      $named = $true
      $pos = $null
      $pipeByValue = $false
      $pipeByProp = $false
      $defaultValue = ''
      $hasMetadataDefault = $false
      $metadataDefaultHelp = $null
      $metadataDefaultHelpCodeUnits = $null
      $metadataDefaultValue = $null
      $acceptWild = $false

      try {
        if ($pmeta -and $pmeta.ParameterSets) {
          foreach ($setEntry in @($pmeta.ParameterSets.GetEnumerator())) {
            $psm = $setEntry.Value
            if ($psm) {
              $setName = [string]$setEntry.Key
              if ($setName) { $parameterSetRequired[$setName] = [bool]$psm.IsMandatory }
              if ($psm.IsMandatory) { $required = $true }
              $pPos = [int]$psm.Position
              if ($pPos -ne -2147483648) {
                $named = $false
                if ($null -eq $pos -or $pPos -lt $pos) { $pos = $pPos }
              }
              if ($psm.ValueFromPipeline) { $pipeByValue = $true }
              if ($psm.ValueFromPipelineByPropertyName) { $pipeByProp = $true }
            }
          }
        }
      } catch {
        # best effort: parameter-set metadata can differ between hosts and proxy commands
      }
      try {
        if ($pmeta -and $pmeta.Attributes) {
          foreach ($attr in @($pmeta.Attributes)) {
            if ($null -eq $attr) { continue }
            if ($attr -is [System.Management.Automation.ValidateSetAttribute]) {
              $hasValidateSet = $true
              if (& $collectorProtocol.TestValidateSetCaseSensitive $attr) {
                $validateSetCaseSensitive = $true
              }
              foreach ($value in @($attr.ValidValues)) {
                if ($null -ne $value) { $possibleValues += [string]$value }
              }
            }
            if ($attr -is [System.Management.Automation.SupportsWildcardsAttribute]) {
              $acceptWild = $true
            }
            if ($attr -is [System.Management.Automation.PSDefaultValueAttribute]) {
              $hasMetadataDefault = $true
              try {
                $metadataDefaultHelp = [string]$attr.Help
                if (-not [string]::IsNullOrWhiteSpace($metadataDefaultHelp)) {
                  $metadataDefaultHelpCodeUnits = & $collectorProtocol.ConvertToUtf16CodeUnits $metadataDefaultHelp
                  $metadataDefaultHelp = $null
                } else {
                  if (& $collectorProtocol.TestPSDefaultValueContainsAutomationNull $attr) { throw 'AutomationNull defaults cannot be represented as PowerShell expressions.' }
                  $metadataDefaultValue = & $collectorProtocol.ConvertToRuntimeDefaultValue $attr.Value
                }
              } catch {
                $metadataDefaultHelp = $null
                $metadataDefaultHelpCodeUnits = $null
                $metadataDefaultValue = $null
              }
            }
          }
        }
      } catch {
        # best effort: not every parameter exposes validation attributes in help metadata
      }
      try {
        $enumType = $parameterType
        if ($enumType -and $enumType.IsArray) { $enumType = $enumType.GetElementType() }
        if ($enumType -and $enumType.IsEnum) {
          foreach ($enumName in [System.Enum]::GetNames($enumType)) {
            if ($enumName) { $enumPossibleValues += [string]$enumName }
          }
        }
      } catch {
        # best effort: enum reflection can fail for remoted/proxy metadata or unresolved types
      }

      $desc = ''
      $hp = $null
      try { if ($helpParamByName.ContainsKey($pn)) { $hp = $helpParamByName[$pn] } } catch { $hp = $null }
      if ($hp) {
        $desc = ''
        foreach ($d in @($hp.Description)) {
          $t = (& $collectorProtocol.GetText $d).Trim()
          if ($t) { if ($desc) { $desc += "`n`n" }; $desc += $t }
        }
        if (-not $typeName) { try { $typeName = [string]$hp.Type.Name } catch {
          # best effort: some help objects omit structured type metadata
        } }
        if ((-not $aliases -or $aliases.Count -eq 0) -and $hp.Aliases) {
          foreach ($a in @($hp.Aliases)) { $aliases += [string]$a }
        }
        try {
          if (-not $hasValidateSet -and $hp.ValidValues) {
            foreach ($value in @($hp.ValidValues)) {
              if ($null -ne $value) { $possibleValues += [string]$value }
            }
          }
        } catch {
          # best effort: ValidValues is not consistently present across Get-Help payloads
        }
        try {
          $helpDefaultValue = [string]$hp.DefaultValue
          if (-not $hasMetadataDefault -and -not [string]::IsNullOrWhiteSpace($helpDefaultValue)) {
            $defaultValue = $helpDefaultValue
          }
        } catch {
          # keep the metadata-derived default when Get-Help omits or reshapes DefaultValue
        }
        try {
          $globbingValue = $hp.Globbing
          if ($globbingValue -is [bool]) {
            $acceptWild = $globbingValue
          } elseif ($null -ne $globbingValue) {
            $parsedGlobbing = $false
            if ([bool]::TryParse(([string]$globbingValue).Trim(), [ref]$parsedGlobbing)) {
              $acceptWild = $parsedGlobbing
            }
          }
        } catch {
          # keep the metadata-derived default when Get-Help omits or reshapes Globbing
        }
      }
      $sets = @()
      if ($paramSets.ContainsKey($pn)) { $sets = @($paramSets[$pn]) }
      if (-not $sets -or $sets.Count -eq 0) { $sets = @('(All)') }

      $positionText = if ($named -or $null -eq $pos) { 'named' } else { [string]$pos }

      $pipelineInput = 'False'
      if ($pipeByValue -and $pipeByProp) { $pipelineInput = 'True (ByValue, ByPropertyName)' }
      elseif ($pipeByValue) { $pipelineInput = 'True (ByValue)' }
      elseif ($pipeByProp) { $pipelineInput = 'True (ByPropertyName)' }

      $parameters += [ordered]@{
        originalName = $pn
        name = $pn
        type = $typeName
        description = $desc
        parameterSets = @($sets)
        aliases = @($aliases)
        possibleValues = @($possibleValues)
        enumPossibleValues = @($enumPossibleValues)
        hasValidateSet = [bool]$hasValidateSet
        validateSetCaseSensitive = [bool]$validateSetCaseSensitive
        required = [bool]$required
        parameterSetRequired = $parameterSetRequired
        position = $positionText
        defaultValue = $defaultValue
        hasMetadataDefault = [bool]$hasMetadataDefault
        metadataDefaultHelp = $metadataDefaultHelp
        metadataDefaultHelpCodeUnits = $metadataDefaultHelpCodeUnits
        metadataDefaultValue = $metadataDefaultValue
        pipelineInput = $pipelineInput
        acceptWildcardCharacters = [bool]$acceptWild
      }
    }

    $helpExamples = @()
    try {
      if ($help -and $help.Examples -and $help.Examples.Example) { $helpExamples = @($help.Examples.Example) }
    } catch { $helpExamples = @() }

    $examples = @()
    foreach ($ex in $helpExamples) {
      $remarks = ''
      $introduction = ''
      foreach ($r in @($ex.Remarks)) {
        $t = (& $collectorProtocol.GetText $r).Trim()
        if ($t) { if ($remarks) { $remarks += "`n`n" }; $remarks += $t }      
      }
      foreach ($intro in @($ex.Introduction)) {
        $text = (& $collectorProtocol.GetText $intro)
        if ($null -eq $text) { continue }
        $value = [string]$text
        if ($value -eq '') { continue }
        if ($introduction) { $introduction += "`n`n" }
        $introduction += $value.Trim("`r", "`n")
      }

      $examples += [ordered]@{
        title = $(try { [string]$ex.Title } catch { '' })
        introduction = $introduction
        code = $(try { [string]$ex.Code } catch { '' })
        remarks = $remarks
      }
    }

    $descMain = ''
    $helpDescriptions = @()
    try { if ($help -and $help.Description) { $helpDescriptions = @($help.Description) } } catch { $helpDescriptions = @() }
    foreach ($d in $helpDescriptions) {
      $t = (& $collectorProtocol.GetText $d).Trim()
      if ($t) { if ($descMain) { $descMain += "`n`n" }; $descMain += $t }     
    }

    $inputs = @()
    try {
      $helpInputTypes = @()
      try { if ($help -and $help.InputTypes -and $help.InputTypes.InputType) { $helpInputTypes = @($help.InputTypes.InputType) } } catch { $helpInputTypes = @() }
      foreach ($it in $helpInputTypes) {
        $typeName = ''
        $typeClrName = ''
        try { $typeName = [string]$it.Type.Name } catch { $typeName = '' }
        if (-not $typeName) { try { $typeName = [string]$it.Type } catch { $typeName = '' } }
        try { $typeClrName = [string]$it.Type.Type.FullName } catch { $typeClrName = '' }
        if (-not $typeClrName) { try { $typeClrName = [string]$it.Type.FullName } catch { $typeClrName = '' } }
        if (-not $typeClrName) { $typeClrName = $typeName }

        $typeDesc = ''
        try {
          foreach ($d in @($it.Description)) {
            $t = (& $collectorProtocol.GetText $d).Trim()
            if ($t) { if ($typeDesc) { $typeDesc += "`n`n" }; $typeDesc += $t }
          }
        } catch {
          # best effort: description collections are not guaranteed on every input type entry
        }

        $inputs += [ordered]@{ name = $typeName; clrTypeName = $typeClrName; description = $typeDesc }
      }
    } catch {
      # best effort: older hosts can omit or reshape InputTypes entirely
    }
    if (-not $inputs -or $inputs.Count -eq 0) {
      $seenInputs = @{}
      foreach ($pn in $paramNames) {
        $pmeta = $null
        try { $pmeta = $c.Parameters[$pn] } catch { $pmeta = $null }
        if (-not $pmeta) { continue }

        $supportsPipeline = $false
        try {
          foreach ($setEntry in @($pmeta.ParameterSets.GetEnumerator())) {
            $psm = $setEntry.Value
            if ($psm -and ($psm.ValueFromPipeline -or $psm.ValueFromPipelineByPropertyName)) {
              $supportsPipeline = $true
              break
            }
          }
        } catch {
          # best effort: pipeline metadata can differ between hosts and proxy commands
        }

        if (-not $supportsPipeline) { continue }

        $inputTypeName = ''
        $inputTypeClrName = ''
        try {
          if ($pmeta.ParameterType) {
            $inputTypeName = [string]$pmeta.ParameterType.Name
            $inputTypeClrName = [string]$pmeta.ParameterType.FullName
          }
        } catch {
          # best effort: pipeline parameter type metadata is not always available on proxy commands
        }

        if (-not $inputTypeName) { continue }
        $key = if ($inputTypeClrName) { $inputTypeClrName } else { $inputTypeName }
        if ($seenInputs.ContainsKey($key)) { continue }
        $seenInputs[$key] = $true
        $inputs += [ordered]@{ name = $inputTypeName; clrTypeName = $inputTypeClrName; description = '' }
      }
    }

    $helpOutputs = @()
    try {
      $helpReturnValues = @()
      try { if ($help -and $help.ReturnValues -and $help.ReturnValues.ReturnValue) { $helpReturnValues = @($help.ReturnValues.ReturnValue) } } catch { $helpReturnValues = @() }
      foreach ($rv in $helpReturnValues) {
        $typeName = ''
        $typeClrName = ''
        $canonicalTypeName = ''
        $runtimeType = $null
        try { $typeName = [string]$rv.Type.Name } catch { $typeName = '' }
        if (-not $typeName) { try { $typeName = [string]$rv.Type } catch { $typeName = '' } }
        try { $runtimeType = $rv.Type.Type } catch { $runtimeType = $null }
        if ($runtimeType -is [type]) {
          $typeClrName = [string]$runtimeType.FullName
          $canonicalTypeName = & $collectorProtocol.GetCanonicalTypeNameFromType $runtimeType
        }
        if (-not $typeClrName) { try { $typeClrName = [string]$rv.Type.FullName } catch { $typeClrName = '' } }
        $typeName = $typeName.Trim()
        $typeClrName = $typeClrName.Trim()
        if (-not $typeClrName) { $typeClrName = $typeName }
        if (-not $typeName) { continue }
        if (-not $canonicalTypeName) {
          foreach ($candidate in @($typeClrName, $typeName)) {
            $canonicalTypeName = & $collectorProtocol.ResolveCanonicalTypeName $candidate
            if ($canonicalTypeName) { break }
          }
        }

        $typeDesc = ''
        try {
          foreach ($d in @($rv.Description)) {
            $t = (& $collectorProtocol.GetText $d).Trim()
            if ($t) { if ($typeDesc) { $typeDesc += "`n`n" }; $typeDesc += $t }
          }
        } catch {
          # best effort: description collections are not guaranteed on every output type entry
        }

        $helpOutputs += [ordered]@{
          name = $typeName
          nameCodeUnits = & $collectorProtocol.ConvertToUtf16CodeUnits $typeName
          clrTypeName = $typeClrName
          clrTypeNameCodeUnits = & $collectorProtocol.ConvertToUtf16CodeUnits $typeClrName
          canonicalTypeName = $canonicalTypeName
          canonicalTypeNameCodeUnits = & $collectorProtocol.ConvertToUtf16CodeUnits $canonicalTypeName
          description = $typeDesc
        }
      }
    } catch {
      # best effort: older hosts can omit or reshape ReturnValues entirely
    }

    $runtimeOutputs = @()
    try {
      foreach ($outputType in @($c.OutputType)) {
        $snapshot = & $collectorProtocol.GetOutputTypeSnapshot $outputType
        if ($snapshot) { $runtimeOutputs += $snapshot }
      }
    } catch {
      # best effort: command metadata may not expose OutputType uniformly across hosts
    }

    $links = @()
    try {
      $helpLinks = @()
      try { if ($help -and $help.RelatedLinks -and $help.RelatedLinks.NavigationLink) { $helpLinks = @($help.RelatedLinks.NavigationLink) } } catch { $helpLinks = @() }
      foreach ($l in $helpLinks) {
        $text = ''
        $uri = ''
        try { $text = (& $collectorProtocol.GetText $l.LinkText).Trim() } catch { $text = '' }
        try { $uri = (& $collectorProtocol.GetText $l.Uri).Trim() } catch { $uri = '' }
        if ($text -or $uri) {
          $links += [ordered]@{ text = $text; uri = $uri }
        }
      }
    } catch {
      # best effort: RelatedLinks is optional and shaped differently between PowerShell versions
    }

    $result.commands += [ordered]@{
      name = [string]$c.Name
      commandType = [string]$c.CommandType
      implementingType = $implType
      assemblyPath = $dllPath
      defaultParameterSet = if ($defaultSet) { [string]$defaultSet } else { $null }
      synopsis = if ($help -and $help.Synopsis) { [string]$help.Synopsis } else { '' }
      description = $descMain
      syntax = @($syntax)
      parameters = @($parameters)
      examples = @($examples)
      inputs = @($inputs)
      outputs = @()
      authoredOutputs = @($helpOutputs)
      runtimeOutputs = @($runtimeOutputs)
      relatedLinks = @($links)
      notes = @()
    }
  }

  $outDir = Split-Path -Path $OutputJsonPath -Parent
  if ($outDir) { [System.IO.Directory]::CreateDirectory($outDir) | Microsoft.PowerShell.Core\Out-Null }
  $json = & $collectorProtocol.ConvertToUtf8SafeJsonText ($result | Microsoft.PowerShell.Utility\ConvertTo-Json -Depth 100)
  [System.IO.File]::WriteAllText($OutputJsonPath, $json, [System.Text.UTF8Encoding]::new($false))

  Microsoft.PowerShell.Utility\Write-Output 'PFDOCS::OK'
  exit 0
} catch {
  & $collectorProtocol.EmitError $_.Exception.Message
  exit 1
}
