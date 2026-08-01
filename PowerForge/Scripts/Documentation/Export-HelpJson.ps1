param(
  [string]$StagingPath,
  [string]$ManifestPath,
  [string]$OutputJsonPath
)
# <PowerForgeTypeIdentityHelpers />
# <PowerForgeOutputMatchingHelpers />
# <PowerForgeParameterMetadataHelpers />
# <PowerForgeDefaultValueCollectionHelpers />
# <PowerForgeDefaultValueScalarHelpers />
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function EmitError([string]$msg) {
  try {
    $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$msg))
    Microsoft.PowerShell.Utility\Write-Output ('PFDOCS::ERROR::' + $b64)
  } catch {
    Microsoft.PowerShell.Utility\Write-Output 'PFDOCS::ERROR::'
  }
}

function GetText([object]$obj) {
  if ($null -eq $obj) { return '' }
  if ($obj -is [string]) { return [string]$obj }
  try { if ($obj.PSObject -and $obj.PSObject.Properties['Text']) { return [string]$obj.Text } } catch {
    # best effort: Get-Help payload shapes vary across PowerShell versions and object types
  }
  try { return [string]$obj } catch { return '' }
}

function ConvertToPowerShellDefaultValue(
  [object]$value,
  [System.Collections.IList]$referenceStack = $null
) {
  if ($null -eq $referenceStack) {
    $referenceStack = [System.Collections.ArrayList]::new()
  }
  if ($null -eq $value) { return '$null' }
  AddDefaultValueReference $value $referenceStack
  if ($value -is [char]) {
    return ('([char]' + [int][char]$value + ')')
  }
  if ($value -is [string]) {
    $text = [string]$value
    $isInterned = [object]::ReferenceEquals($value, [string]::IsInterned($text))
    return ConvertStringToPowerShellDefaultValue $text $isInterned
  }
  if ($value -is [bool]) {
    if ($value) { return '$true' }
    return '$false'
  }
  if (TestExactRuntimeValueType $value ([System.Management.Automation.SwitchParameter])) {
    $switchValue = if ($value.IsPresent) { '$true' } else { '$false' }
    return ('[System.Management.Automation.SwitchParameter]::new(' + $switchValue + ')')
  }
  if ($value -is [enum]) {
    $enumType = $value.GetType()
    $enumName = GetPowerShellSafeEnumName $enumType $value
    $enumTypeExpression = GetPowerShellTypeDefaultExpression $enumType
    $enumTypeIsLiteral = TestPowerShellTypeLiteral $enumType
    if ($enumName -and $enumTypeIsLiteral) {
      return ($enumTypeExpression + '::' + $enumName)
    }
    $underlyingValue = [System.Convert]::ChangeType(
      $value,
      [System.Enum]::GetUnderlyingType($enumType),
      [System.Globalization.CultureInfo]::InvariantCulture)
    $underlyingTypeName = GetCanonicalTypeNameFromType ([System.Enum]::GetUnderlyingType($enumType))
    $enumTypeArgument = if ($enumTypeIsLiteral) { $enumTypeExpression } else { '(' + $enumTypeExpression + ')' }
    return ('[System.Enum]::ToObject(' + $enumTypeArgument + ', ([' + $underlyingTypeName + ']' + [string]$underlyingValue + '))')
  }
  if ($value -is [type]) {
    if (-not (TestGenuineRuntimeTypeValue $value)) {
      throw 'Delegated or custom Type defaults are not supported.'
    }
    return GetPowerShellTypeDefaultExpression $value
  }
  if ($value -is [double]) {
    if ([double]::IsNaN($value)) {
      $bits = [System.BitConverter]::DoubleToInt64Bits($value)
      return ('[System.BitConverter]::Int64BitsToDouble(([long]' +
        $bits.ToString([System.Globalization.CultureInfo]::InvariantCulture) + '))')
    }
    if ([double]::IsPositiveInfinity($value)) { return '[double]::PositiveInfinity' }
    if ([double]::IsNegativeInfinity($value)) { return '[double]::NegativeInfinity' }
    if ($value -eq 0) {
      if ([System.BitConverter]::DoubleToInt64Bits($value) -lt 0) { return '([double]-0.0)' }
      return '([double]0.0)'
    }
    return ('([double]' + $value.ToString('G17', [System.Globalization.CultureInfo]::InvariantCulture) + ')')
  }
  if ($value -is [single]) {
    if ([single]::IsNaN($value)) {
      $bits = [System.BitConverter]::ToInt32([System.BitConverter]::GetBytes([single]$value), 0)
      return ('[System.BitConverter]::ToSingle([System.BitConverter]::GetBytes(([int]' +
        $bits.ToString([System.Globalization.CultureInfo]::InvariantCulture) + ')), 0)')
    }
    if ([single]::IsPositiveInfinity($value)) { return '[single]::PositiveInfinity' }
    if ([single]::IsNegativeInfinity($value)) { return '[single]::NegativeInfinity' }
    if ($value -eq 0) {
      $bits = [System.BitConverter]::ToInt32([System.BitConverter]::GetBytes([single]$value), 0)
      if ($bits -lt 0) { return '([single]-0.0)' }
      return '([single]0.0)'
    }
    return ('([single]' + $value.ToString('G9', [System.Globalization.CultureInfo]::InvariantCulture) + ')')
  }
  if ($value -is [decimal]) {
    $bits = [System.Decimal]::GetBits($value)
    $isNegative = if ($bits[3] -lt 0) { '$true' } else { '$false' }
    $scale = (($bits[3] -shr 16) -band 0xFF)
    return ('[System.Decimal]::new(([int]' + $bits[0].ToString([System.Globalization.CultureInfo]::InvariantCulture) +
      '), ([int]' + $bits[1].ToString([System.Globalization.CultureInfo]::InvariantCulture) +
      '), ([int]' + $bits[2].ToString([System.Globalization.CultureInfo]::InvariantCulture) +
      '), ' + $isNegative + ', ([byte]' + $scale.ToString([System.Globalization.CultureInfo]::InvariantCulture) + '))')
  }
  if (TestExactRuntimeValueType $value ([System.Numerics.BigInteger])) {
    $integerText = $value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    return ("[System.Numerics.BigInteger]::Parse('" + $integerText + "', [System.Globalization.CultureInfo]::InvariantCulture)")
  }
  if ($value -is [guid]) {
    return ("[System.Guid]::ParseExact('" + $value.ToString('D') + "', 'D')")
  }
  if ($value -is [version]) {
    $versionText = $value.ToString().Replace("'", "''")
    return ("[System.Version]::Parse('" + $versionText + "')")
  }
  if (TestExactRuntimeValueType $value ([uri])) {
    if ($value.UserEscaped) {
      throw 'User-escaped Uri defaults are not supported.'
    }
    $uriText = ConvertToPowerShellDefaultValue $value.OriginalString $referenceStack
    $uriKind = if ($value.IsAbsoluteUri) { 'Absolute' } else { 'Relative' }
    $reconstructedUri = [System.Uri]::new($value.OriginalString, [System.UriKind]$uriKind)
    $uriStateMatches =
      $reconstructedUri.OriginalString -ceq $value.OriginalString -and
      $reconstructedUri.ToString() -ceq $value.ToString() -and
      $reconstructedUri.UserEscaped -eq $value.UserEscaped
    if ($value.IsAbsoluteUri) {
      $uriStateMatches = $uriStateMatches -and
        $reconstructedUri.AbsoluteUri -ceq $value.AbsoluteUri -and
        $reconstructedUri.PathAndQuery -ceq $value.PathAndQuery
    }
    if (-not $uriStateMatches) {
      throw 'Uri defaults with noncanonical reconstruction state are not supported.'
    }
    return ('[System.Uri]::new(' + $uriText + ', [System.UriKind]::' + $uriKind + ')')
  }
  if (TestExactRuntimeValueType $value (GetCoreRuntimeType 'System.DateOnly')) {
    $dayNumber = $value.DayNumber.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    return ('[System.DateOnly]::FromDayNumber(([int]' + $dayNumber + '))')
  }
  if (TestExactRuntimeValueType $value (GetCoreRuntimeType 'System.TimeOnly')) {
    $ticks = $value.Ticks.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    return ('[System.TimeOnly]::new(([long]' + $ticks + '))')
  }
  if ($value -is [datetime]) {
    if ($value.Kind -eq [System.DateTimeKind]::Local -and
        [System.TimeZoneInfo]::Local.IsAmbiguousTime($value)) {
      throw 'Ambiguous local DateTime defaults cannot be represented portably.'
    }
    $ticks = $value.Ticks.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    return ('[System.DateTime]::new(([long]' + $ticks + '), [System.DateTimeKind]::' + $value.Kind + ')')
  }
  if ($value -is [datetimeoffset]) {
    $dateText = $value.ToString('O', [System.Globalization.CultureInfo]::InvariantCulture)
    return ("[System.DateTimeOffset]::ParseExact('" + $dateText + "', 'O', [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)")
  }
  if ($value -is [timespan]) {
    $timeText = $value.ToString('c', [System.Globalization.CultureInfo]::InvariantCulture)
    return ("[System.TimeSpan]::ParseExact('" + $timeText + "', 'c', [System.Globalization.CultureInfo]::InvariantCulture)")
  }
  if ($value -is [scriptblock]) {
    if (-not (TestRecreatableScriptBlock $value)) {
      throw 'Stateful, module-bound, or constrained ScriptBlock defaults are not supported.'
    }
    $scriptText = ConvertToPowerShellDefaultValue ([string]$value.ToString())
    return ('[scriptblock]::Create(' + $scriptText + ')')
  }
  if ($value -is [System.Collections.IDictionary] -or
      $value -is [System.Collections.IEnumerable]) {
    if ($value -is [System.Collections.IDictionary]) {
      return ConvertDictionaryToPowerShellDefaultValue $value $referenceStack
    }
    if ($value -isnot [System.Collections.IList] -and $value -isnot [System.Array]) {
      throw ('Unsupported enumerable default type: ' + $value.GetType().FullName)
    }
    if ($value -is [System.Array] -and
        ($value.Rank -gt 1 -or $value.GetType() -ne $value.GetType().GetElementType().MakeArrayType())) {
      return ConvertMultidimensionalArrayToPowerShellDefaultValue $value $referenceStack
    }
    $items = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $value) {
      $items.Add((ConvertToPowerShellDefaultValue $item $referenceStack))
    }
    return ConvertCollectionItemsToPowerShellDefaultValue $items $value $referenceStack
  }
  return ConvertScalarToPowerShellDefaultValue $value
}

try {
  if ([string]::IsNullOrWhiteSpace($ManifestPath) -or -not (Test-Path -LiteralPath $ManifestPath)) {
    throw ('Manifest not found: ' + $ManifestPath)
  }

  $m = $null
  try { $m = Import-PowerShellDataFile -Path $ManifestPath -ErrorAction Stop } catch { $m = $null }

  $collectorHelperFunctions = GetCollectorHelperFunctionSnapshot
  $restoreCollectorHelpers = Get-Command RestoreCollectorHelperFunctions -CommandType Function
  $testXmlSafeIdentityText = Get-Command TestXmlSafeIdentityText -CommandType Function
  $getDocumentedModuleCommandSnapshot = Get-Command GetDocumentedModuleCommandSnapshot -CommandType Function
  $targetVariableImportFilter = '__PowerForgeDocumentationCollector_' + [guid]::NewGuid().ToString('N')
  $targetAliasImportFilter = '__PowerForgeDocumentationCollector_' + [guid]::NewGuid().ToString('N')
  $mod = Import-Module -Name $ManifestPath -Force -PassThru -Function '*' -Cmdlet '*' -Alias $targetAliasImportFilter -Variable $targetVariableImportFilter -ErrorAction Stop
  $commandSnapshot = & $getDocumentedModuleCommandSnapshot $mod $testXmlSafeIdentityText
  $commands = @($commandSnapshot.Commands)
  $helpByCommandName = $commandSnapshot.HelpByCommandName
  & $restoreCollectorHelpers $collectorHelperFunctions
  $moduleNameResolved = $mod.Name

  $result = [ordered]@{
    moduleName = [string]$moduleNameResolved
    moduleVersion = if ($m -and $m.ModuleVersion) { [string]$m.ModuleVersion } else { $null }
    moduleGuid = if ($m -and $m.GUID) { [string]$m.GUID } else { $null }        
    moduleDescription = if ($m -and $m.Description) { [string]$m.Description } else { $null }
    helpInfoUri = if ($m -and $m.HelpInfoURI) { [string]$m.HelpInfoURI } else { $null }
    projectUri = $(try { if ($m -and $m.PrivateData -and $m.PrivateData.PSData -and $m.PrivateData.PSData.ProjectUri) { [string]$m.PrivateData.PSData.ProjectUri } else { $null } } catch { $null })
    commands = @()
  }

  foreach ($c in $commands) {
    $help = $helpByCommandName[[string]$c.Name]

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
              if (TestValidateSetCaseSensitive $attr) { $validateSetCaseSensitive = $true }
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
                $defaultValue = ConvertPSDefaultValueAttribute $attr
              } catch {
                $defaultValue = ''
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
            if ($enumName -and
                (ConvertToXmlSafeDefaultHelpText ([string]$enumName)) -ceq [string]$enumName) {
              $enumPossibleValues += [string]$enumName
            }
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
          $t = (GetText $d).Trim()
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
      if ($hasValidateSet) { $enumPossibleValues = @() }
      $possibleValues = @(MergeParameterPossibleValues @($possibleValues) @($enumPossibleValues) $validateSetCaseSensitive $hasValidateSet)

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
        required = [bool]$required
        parameterSetRequired = $parameterSetRequired
        position = $positionText
        defaultValue = $defaultValue
        pipelineInput = $pipelineInput
        acceptWildcardCharacters = [bool]$acceptWild
      }
    }

    $parameters = @(ConvertParametersToXmlSafeDocumentationText @($parameters))
    $parameterSetIdentities = ConvertParameterSetIdentitiesToXmlSafeDocumentationText $defaultSet @($syntax) @($parameters)
    $defaultSet = $parameterSetIdentities.DefaultSet
    $syntax = @($parameterSetIdentities.Syntax)
    $parameters = @($parameterSetIdentities.Parameters)

    $helpExamples = @()
    try {
      if ($help -and $help.Examples -and $help.Examples.Example) { $helpExamples = @($help.Examples.Example) }
    } catch { $helpExamples = @() }

    $examples = @()
    foreach ($ex in $helpExamples) {
      $remarks = ''
      $introduction = ''
      foreach ($r in @($ex.Remarks)) {
        $t = (GetText $r).Trim()
        if ($t) { if ($remarks) { $remarks += "`n`n" }; $remarks += $t }      
      }
      foreach ($intro in @($ex.Introduction)) {
        $text = GetText $intro
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
      $t = (GetText $d).Trim()
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
            $t = (GetText $d).Trim()
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
    $helpOutputByKey = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::Ordinal)
    $helpOutputKeyCounts = [System.Collections.Generic.Dictionary[string,int]]::new([System.StringComparer]::Ordinal)
    $helpOutputByFoldedKey = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $helpOutputFoldedKeyCounts = [System.Collections.Generic.Dictionary[string,int]]::new([System.StringComparer]::OrdinalIgnoreCase)
    try {
      $helpReturnValues = @()
      try { if ($help -and $help.ReturnValues -and $help.ReturnValues.ReturnValue) { $helpReturnValues = @($help.ReturnValues.ReturnValue) } } catch { $helpReturnValues = @() }
      foreach ($rv in $helpReturnValues) {
        $typeName = ''
        $typeClrName = ''
        try { $typeName = [string]$rv.Type.Name } catch { $typeName = '' }
        if (-not $typeName) { try { $typeName = [string]$rv.Type } catch { $typeName = '' } }
        try { $typeClrName = [string]$rv.Type.Type.FullName } catch { $typeClrName = '' }
        if (-not $typeClrName) { try { $typeClrName = [string]$rv.Type.FullName } catch { $typeClrName = '' } }
        $typeName = $typeName.Trim()
        $typeClrName = $typeClrName.Trim()
        if (-not $typeClrName) { $typeClrName = $typeName }
        if (-not $typeName) { continue }

        $typeDesc = ''
        try {
          foreach ($d in @($rv.Description)) {
            $t = (GetText $d).Trim()
            if ($t) { if ($typeDesc) { $typeDesc += "`n`n" }; $typeDesc += $t }
          }
        } catch {
          # best effort: description collections are not guaranteed on every output type entry
        }

        $helpOutput = [pscustomobject][ordered]@{ name = $typeName; clrTypeName = $typeClrName; description = $typeDesc }
        $helpOutputs += $helpOutput
        AddTypeKeysToIndexes $helpOutput @(GetTypeKeys $typeName $typeClrName) `
          $helpOutputByKey $helpOutputKeyCounts $helpOutputByFoldedKey $helpOutputFoldedKeyCounts
      }
    } catch {
      # best effort: older hosts can omit or reshape ReturnValues entirely
    }

    $outputs = @()
    $seenOutputIdentities = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $seenRuntimeOutputIdentities = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $runtimeOutputKeys = [System.Collections.Generic.Dictionary[string,bool]]::new([System.StringComparer]::Ordinal)
    $runtimeOutputByKey = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::Ordinal)
    $runtimeOutputKeyCounts = [System.Collections.Generic.Dictionary[string,int]]::new([System.StringComparer]::Ordinal)
    $runtimeOutputByFoldedKey = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $runtimeOutputFoldedKeyCounts = [System.Collections.Generic.Dictionary[string,int]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $runtimeOutputMetadata = @()
    try {
      foreach ($outputType in @($c.OutputType)) {
        $metadata = GetOutputTypeMetadata $outputType
        if (-not $metadata -or -not $metadata.identity) { continue }
        $runtimeIdentity = if ($metadata.runtimeIdentity) { [string]$metadata.runtimeIdentity } else { [string]$metadata.identity }
        if (-not $seenRuntimeOutputIdentities.Add($runtimeIdentity)) { continue }
        [void]$seenOutputIdentities.Add([string]$metadata.identity)
        $runtimeOutputMetadata += $metadata
        foreach ($key in @($metadata.keys)) { $runtimeOutputKeys[$key] = $true }
        AddTypeKeysToIndexes $metadata @($metadata.keys) `
          $runtimeOutputByKey $runtimeOutputKeyCounts $runtimeOutputByFoldedKey $runtimeOutputFoldedKeyCounts
      }

      foreach ($metadata in $runtimeOutputMetadata) {
        $typeDesc = ''
        $displayName = [string]$metadata.name
        $exactHelpMatch = $null
        foreach ($key in @($metadata.keys)) {
          if ($helpOutputByKey.ContainsKey($key) -and
              [int]$helpOutputKeyCounts[$key] -eq 1 -and
              [int]$runtimeOutputKeyCounts[$key] -eq 1) {
            $matchedHelpOutput = $helpOutputByKey[$key]
            $matchedHelpIdentity = GetTypeIdentity ([string]$matchedHelpOutput.name) ([string]$matchedHelpOutput.clrTypeName)
            if (TestConflictingQualifiedTypeIdentity ([string]$metadata.identity) $matchedHelpIdentity) {
              continue
            }
            $exactHelpMatch = $matchedHelpOutput
            $typeDesc = [string]$matchedHelpOutput.description
            if (-not [string]::IsNullOrWhiteSpace([string]$matchedHelpOutput.name)) {
              $displayName = [string]$matchedHelpOutput.name
            }
            break
          }
        }
        if ($null -eq $exactHelpMatch) {
          $matchedHelpOutput = GetUniqueUnqualifiedCaseInsensitiveTypeMatch @($metadata.keys) `
            $helpOutputByFoldedKey $helpOutputFoldedKeyCounts $runtimeOutputFoldedKeyCounts
          if ($matchedHelpOutput) {
            $matchedHelpIdentity = GetTypeIdentity ([string]$matchedHelpOutput.name) ([string]$matchedHelpOutput.clrTypeName)
            if (-not (TestConflictingQualifiedTypeIdentity ([string]$metadata.identity) $matchedHelpIdentity)) {
              $typeDesc = [string]$matchedHelpOutput.description
              if (-not [string]::IsNullOrWhiteSpace([string]$matchedHelpOutput.name)) {
                $displayName = [string]$matchedHelpOutput.name
              }
            }
          }
        }

        $outputs += [ordered]@{
          name = $displayName
          clrTypeName = [string]$metadata.clrTypeName
          description = $typeDesc
        }
      }
    } catch {
      # best effort: command metadata may not expose OutputType uniformly across hosts
    }
    $allowHelpOnlyOutputs = [string]$c.CommandType -ne 'Cmdlet' -or $runtimeOutputMetadata.Count -eq 0
    if ($allowHelpOnlyOutputs) {
      foreach ($helpOutput in $helpOutputs) {
        $helpOutputIdentity = GetTypeIdentity ([string]$helpOutput.name) ([string]$helpOutput.clrTypeName)
        if ([string]$c.CommandType -eq 'Cmdlet' -and
            $runtimeOutputMetadata.Count -eq 0 -and
            $helpOutputIdentity -eq 'System.Object' -and
            [string]::IsNullOrWhiteSpace([string]$helpOutput.description)) {
          continue
        }

        $matchesRuntimeOutput = $false
        foreach ($key in @(GetTypeKeys ([string]$helpOutput.name) ([string]$helpOutput.clrTypeName))) {
          if ($runtimeOutputKeys.ContainsKey($key) -and
              [int]$helpOutputKeyCounts[$key] -eq 1 -and
              [int]$runtimeOutputKeyCounts[$key] -eq 1) {
            $matchedRuntimeOutput = $runtimeOutputByKey[$key]
            if (TestConflictingQualifiedTypeIdentity ([string]$matchedRuntimeOutput.identity) $helpOutputIdentity) {
              continue
            }
            $matchesRuntimeOutput = $true
            break
          }
        }
        if (-not $matchesRuntimeOutput) {
          $matchedRuntimeOutput = GetUniqueUnqualifiedCaseInsensitiveTypeMatch `
            @(GetTypeKeys ([string]$helpOutput.name) ([string]$helpOutput.clrTypeName)) `
            $runtimeOutputByFoldedKey $runtimeOutputFoldedKeyCounts $helpOutputFoldedKeyCounts
          if ($matchedRuntimeOutput -and
              -not (TestConflictingQualifiedTypeIdentity ([string]$matchedRuntimeOutput.identity) $helpOutputIdentity)) {
            $matchesRuntimeOutput = $true
          }
        }
        if ($matchesRuntimeOutput) { continue }

        if (-not $seenOutputIdentities.Add([string]$helpOutputIdentity)) { continue }
        $outputs += [ordered]@{
          name = [string]$helpOutput.name
          clrTypeName = [string]$helpOutput.clrTypeName
          description = [string]$helpOutput.description
        }
      }
    }
    $outputs = @(ConvertOutputsToXmlSafeDocumentationText @($outputs))

    $links = @()
    try {
      $helpLinks = @()
      try { if ($help -and $help.RelatedLinks -and $help.RelatedLinks.NavigationLink) { $helpLinks = @($help.RelatedLinks.NavigationLink) } } catch { $helpLinks = @() }
      foreach ($l in $helpLinks) {
        $text = ''
        $uri = ''
        try { $text = (GetText $l.LinkText).Trim() } catch { $text = '' }
        try { $uri = (GetText $l.Uri).Trim() } catch { $uri = '' }
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
      outputs = @($outputs)
      relatedLinks = @($links)
      notes = @()
    }
  }

  $outDir = Split-Path -Path $OutputJsonPath -Parent
  if ($outDir) { [System.IO.Directory]::CreateDirectory($outDir) | Microsoft.PowerShell.Core\Out-Null }
  $json = $result | Microsoft.PowerShell.Utility\ConvertTo-Json -Depth 8
  [System.IO.File]::WriteAllText($OutputJsonPath, $json, [System.Text.UTF8Encoding]::new($false))

  Microsoft.PowerShell.Utility\Write-Output 'PFDOCS::OK'
  exit 0
} catch {
  EmitError $_.Exception.Message
  exit 1
}
