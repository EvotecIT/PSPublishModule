param(
  [string]$StagingPath,
  [string]$ManifestPath,
  [string]$OutputJsonPath
)
# <PowerForgeTypeIdentityHelpers />
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function EmitError([string]$msg) {
  try {
    $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$msg))
    Write-Output ('PFDOCS::ERROR::' + $b64)
  } catch {
    Write-Output 'PFDOCS::ERROR::'
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

function GetCanonicalTypeNameFromType([type]$type) {
  if ($null -eq $type) { return '' }
  if ($type.IsArray) {
    $elementName = GetCanonicalTypeNameFromType ($type.GetElementType())
    $rank = $type.GetArrayRank()
    if ($rank -le 1) { return ($elementName + '[]') }
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

function ResolveCanonicalTypeName([string]$candidate) {
  if ([string]::IsNullOrWhiteSpace($candidate)) { return '' }
  $trimmed = $candidate.Trim()
  $resolvedType = ResolveExactType $trimmed
  $ambiguous = $false
  if (-not $resolvedType) { $resolvedType = ResolveUniqueTypeCaseInsensitive $trimmed ([ref]$ambiguous) }
  if (-not $resolvedType -and -not $ambiguous) {
    try { $resolvedType = $trimmed -as [type] } catch { $resolvedType = $null }
  }
  if ($resolvedType) { return GetCanonicalTypeNameFromType $resolvedType }
  return ($trimmed -replace '\s+', '')
}

function ConvertToRuntimeDefaultValue([object]$value) {
  if ($null -eq $value) {
    return [ordered]@{ kind = 'Null'; text = $null; name = $null; canonicalTypeName = $null; items = @() }
  }

  $kind = 'Text'
  $text = $null
  $name = $null
  $canonicalTypeName = $null
  $items = @()

  if ($value -is [string]) {
    $kind = 'String'
    $text = [string]$value
  } elseif ($value -is [char]) {
    $kind = 'Char'
    $text = [string]$value
  } elseif ($value -is [bool]) {
    $kind = 'Boolean'
    $text = [string]$value
  } elseif ($value -is [enum]) {
    $kind = 'Enum'
    $enumType = $value.GetType()
    $canonicalTypeName = GetCanonicalTypeNameFromType $enumType
    $name = [System.Enum]::GetName($enumType, $value)
    $underlyingValue = [System.Convert]::ChangeType(
      $value,
      [System.Enum]::GetUnderlyingType($enumType),
      [System.Globalization.CultureInfo]::InvariantCulture)
<<<<<<< HEAD
    $underlyingTypeName = GetCanonicalTypeNameFromType ([System.Enum]::GetUnderlyingType($enumType))
    $enumTypeArgument = if ($enumTypeIsLiteral) { $enumTypeExpression } else { '(' + $enumTypeExpression + ')' }
    return ('[System.Enum]::ToObject(' + $enumTypeArgument + ', ([' + $underlyingTypeName + ']' + $underlyingText + '))')
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
    foreach ($seenReference in $referenceStack) {
      if ([object]::ReferenceEquals($seenReference, $value)) {
        throw 'Repeated or circular default-value collection references are not supported.'
      }
    }
    [void]$referenceStack.Add($value)
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
=======
    $text = [System.Convert]::ToString($underlyingValue, [System.Globalization.CultureInfo]::InvariantCulture)
  } elseif ($value -is [type]) {
    $kind = 'Type'
    $canonicalTypeName = GetCanonicalTypeNameFromType $value
  } elseif ($value -is [double]) {
    $kind = 'Double'
    $text = $value.ToString($null, [System.Globalization.CultureInfo]::InvariantCulture)
  } elseif ($value -is [single]) {
    $kind = 'Single'
    $text = $value.ToString($null, [System.Globalization.CultureInfo]::InvariantCulture)
  } elseif ($value -is [System.Collections.IEnumerable]) {
    $kind = 'Collection'
>>>>>>> e2bda78a (Move documentation normalization into C#)
    foreach ($item in $value) {
      $items += ConvertToRuntimeDefaultValue $item
    }
  } elseif ($value -is [System.IFormattable]) {
    $kind = 'Formattable'
    $text = $value.ToString($null, [System.Globalization.CultureInfo]::InvariantCulture)
  } else {
    $text = [string]$value
  }
  return [ordered]@{
    kind = $kind
    text = $text
    name = $name
    canonicalTypeName = $canonicalTypeName
    items = @($items)
  }
}

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

try {
  if ([string]::IsNullOrWhiteSpace($ManifestPath) -or -not (Test-Path -LiteralPath $ManifestPath)) {
    throw ('Manifest not found: ' + $ManifestPath)
  }

  $m = $null
  try { $m = Import-PowerShellDataFile -Path $ManifestPath -ErrorAction Stop } catch { $m = $null }

  $collectorHelperFunctions = GetCollectorHelperFunctionSnapshot
  $restoreCollectorHelpers = Get-Command RestoreCollectorHelperFunctions -CommandType Function
  $targetVariableImportFilter = '__PowerForgeDocumentationCollector_' + [guid]::NewGuid().ToString('N')
  $targetAliasImportFilter = '__PowerForgeDocumentationCollector_' + [guid]::NewGuid().ToString('N')
  $mod = Import-Module -Name $ManifestPath -Force -PassThru -Function '*' -Cmdlet '*' -Alias $targetAliasImportFilter -Variable $targetVariableImportFilter -ErrorAction Stop
  & $restoreCollectorHelpers $collectorHelperFunctions
  $moduleNameResolved = $mod.Name

  $commands = Get-Command -Module $moduleNameResolved -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandType -eq 'Cmdlet' -or $_.CommandType -eq 'Function'
  } | Sort-Object -Property Name

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
    $help = $null
    try { $help = Get-Help -Name $c.Name -Full -ErrorAction SilentlyContinue } catch { $help = $null }

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
        if (-not $paramSets.ContainsKey($pn)) { $paramSets[$pn] = New-Object System.Collections.Generic.List[string] }
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
        $paramNames = @($c.Parameters.GetEnumerator() | ForEach-Object { [string]$_.Key })
      }
    } catch { $paramNames = @() }
    foreach ($hp in $helpParameters) { try { $paramNames += [string]$hp.Name } catch {
      # best effort: keep extracting other parameters even when one help node is incomplete
    } }
    $paramNames = @($paramNames | Where-Object { $_ -and ($commonParamNames -notcontains $_) } | Sort-Object -Unique)

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

      $required = $false
      $parameterSetRequired = @{}
      $named = $true
      $pos = $null
      $pipeByValue = $false
      $pipeByProp = $false
      $defaultValue = ''
      $hasMetadataDefault = $false
      $metadataDefaultHelp = $null
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
              foreach ($value in @($attr.ValidValues)) {
                if ($null -ne $value) { $possibleValues += [string]$value }
              }
            }
            if ($attr -is [System.Management.Automation.SupportsWildcardsAttribute]) {
              $acceptWild = $true
            }
            if ($attr -is [System.Management.Automation.PSDefaultValueAttribute]) {
              $hasMetadataDefault = $true
              $metadataDefaultHelp = [string]$attr.Help
              if ([string]::IsNullOrWhiteSpace($metadataDefaultHelp)) {
                $metadataDefaultValue = ConvertToRuntimeDefaultValue $attr.Value
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
          if ($hp.ValidValues) {
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
<<<<<<< HEAD
      $possibleValues = @(MergeParameterPossibleValues @($possibleValues) @($enumPossibleValues))

      $sets = @()
=======
      $sets = @()
>>>>>>> e2bda78a (Move documentation normalization into C#)
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
        hasMetadataDefault = [bool]$hasMetadataDefault
        metadataDefaultHelp = $metadataDefaultHelp
        metadataDefaultValue = $metadataDefaultValue
        pipelineInput = $pipelineInput
        acceptWildcardCharacters = [bool]$acceptWild
      }
    }

    $parameters = @(ConvertParametersToXmlSafeDocumentationText @($parameters))

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
          $canonicalTypeName = GetCanonicalTypeNameFromType $runtimeType
        }
        if (-not $typeClrName) { try { $typeClrName = [string]$rv.Type.FullName } catch { $typeClrName = '' } }
        $typeName = $typeName.Trim()
        $typeClrName = $typeClrName.Trim()
        if (-not $typeClrName) { $typeClrName = $typeName }
        if (-not $typeName) { continue }
        if (-not $canonicalTypeName) {
          foreach ($candidate in @($typeClrName, $typeName)) {
            $canonicalTypeName = ResolveCanonicalTypeName $candidate
            if ($canonicalTypeName) { break }
          }
        }

        $typeDesc = ''
        try {
          foreach ($d in @($rv.Description)) {
            $t = (GetText $d).Trim()
            if ($t) { if ($typeDesc) { $typeDesc += "`n`n" }; $typeDesc += $t }
          }
        } catch {
          # best effort: description collections are not guaranteed on every output type entry
        }

        $helpOutputs += [ordered]@{
          name = $typeName
          clrTypeName = $typeClrName
          canonicalTypeName = $canonicalTypeName
          description = $typeDesc
        }
      }
    } catch {
      # best effort: older hosts can omit or reshape ReturnValues entirely
    }

<<<<<<< HEAD
    $outputs = @()
    $seenOutputIdentities = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $seenRuntimeOutputIdentities = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $runtimeOutputKeys = [System.Collections.Generic.Dictionary[string,bool]]::new([System.StringComparer]::Ordinal)
    $runtimeOutputByKey = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::Ordinal)
    $runtimeOutputKeyCounts = [System.Collections.Generic.Dictionary[string,int]]::new([System.StringComparer]::Ordinal)
    $runtimeOutputByFoldedKey = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $runtimeOutputFoldedKeyCounts = [System.Collections.Generic.Dictionary[string,int]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $runtimeOutputMetadata = @()
    $runtimeOutputIdentityCounts = [System.Collections.Generic.Dictionary[string,int]]::new([System.StringComparer]::Ordinal)
    try {
      foreach ($outputType in @($c.OutputType)) {
        $metadata = GetOutputTypeMetadata $outputType
        if (-not $metadata -or -not $metadata.identity) { continue }
        $runtimeIdentity = if ($metadata.runtimeIdentity) { [string]$metadata.runtimeIdentity } else { [string]$metadata.identity }
        if (-not $seenRuntimeOutputIdentities.Add($runtimeIdentity)) { continue }
        [void]$seenOutputIdentities.Add([string]$metadata.identity)
        $runtimeOutputMetadata += $metadata
        $runtimeOutputIdentityCounts[[string]$metadata.identity] =
          if ($runtimeOutputIdentityCounts.ContainsKey([string]$metadata.identity)) {
            [int]$runtimeOutputIdentityCounts[[string]$metadata.identity] + 1
          } else { 1 }
        foreach ($key in @($metadata.keys)) { $runtimeOutputKeys[$key] = $true }
        AddTypeKeysToIndexes $metadata @($metadata.keys) `
          $runtimeOutputByKey $runtimeOutputKeyCounts $runtimeOutputByFoldedKey $runtimeOutputFoldedKeyCounts
      }

      foreach ($metadata in $runtimeOutputMetadata) {
        $typeDesc = ''
        $displayName = [string]$metadata.name
        $displayClrTypeName = [string]$metadata.clrTypeName
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

        if ([int]$runtimeOutputIdentityCounts[[string]$metadata.identity] -gt 1 -and
            -not [string]::IsNullOrWhiteSpace([string]$metadata.assemblyQualifiedName)) {
          $displayName = [string]$metadata.assemblyQualifiedName
          $displayClrTypeName = [string]$metadata.assemblyQualifiedName
        }

        $outputs += [ordered]@{
          name = $displayName
          clrTypeName = $displayClrTypeName
          description = $typeDesc
        }
=======
    $runtimeOutputs = @()
    try {
      foreach ($outputType in @($c.OutputType)) {
        $snapshot = GetOutputTypeSnapshot $outputType
        if ($snapshot) { $runtimeOutputs += $snapshot }
>>>>>>> e2bda78a (Move documentation normalization into C#)
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
      outputs = @()
      authoredOutputs = @($helpOutputs)
      runtimeOutputs = @($runtimeOutputs)
      relatedLinks = @($links)
      notes = @()
    }
  }

  $outDir = Split-Path -Path $OutputJsonPath -Parent
  if ($outDir) { [System.IO.Directory]::CreateDirectory($outDir) | Out-Null }
  $json = $result | ConvertTo-Json -Depth 100
  [System.IO.File]::WriteAllText($OutputJsonPath, $json, [System.Text.UTF8Encoding]::new($false))

  Write-Output 'PFDOCS::OK'
  exit 0
} catch {
  EmitError $_.Exception.Message
  exit 1
}
