param(
  [string]$StagingPath,
  [string]$ManifestPath,
  [string]$OutputJsonPath
)
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
  try { if ($obj.PSObject -and $obj.PSObject.Properties['Text']) { return [string]$obj.Text } } catch { }
  try { return [string]$obj } catch { return '' }
}

try {
  if ([string]::IsNullOrWhiteSpace($ManifestPath) -or -not (Test-Path -LiteralPath $ManifestPath)) {
    throw ('Manifest not found: ' + $ManifestPath)
  }

  $m = $null
  try { $m = Import-PowerShellDataFile -Path $ManifestPath -ErrorAction Stop } catch { $m = $null }

  $mod = Import-Module -Name $ManifestPath -Force -PassThru -ErrorAction Stop
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
      } catch { }
    }

    $commonParamNames = @('Verbose','Debug','ErrorAction','ErrorVariable','WarningAction','WarningVariable','InformationAction','InformationVariable','OutVariable','OutBuffer','PipelineVariable','WhatIf','Confirm','ProgressAction')
    $paramNames = @()
    try { if ($c -and $c.Parameters) { $paramNames = @($c.Parameters.Keys) } } catch { $paramNames = @() }
    foreach ($hp in $helpParameters) { try { $paramNames += [string]$hp.Name } catch { } }
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

      $required = $false
      $named = $true
      $pos = $null
      $pipeByValue = $false
      $pipeByProp = $false
      $defaultValue = ''
      $acceptWild = $false

      try {
        if ($pmeta -and $pmeta.ParameterSets) {
          foreach ($setName in @($pmeta.ParameterSets.Keys)) {
            $psm = $pmeta.ParameterSets[$setName]
            if ($psm) {
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
      } catch { }
      try {
        if ($pmeta -and $pmeta.Attributes) {
          foreach ($attr in @($pmeta.Attributes)) {
            if ($null -eq $attr) { continue }
            if ($attr -is [System.Management.Automation.ValidateSetAttribute]) {
              foreach ($value in @($attr.ValidValues)) {
                if ($null -ne $value) { $possibleValues += [string]$value }
              }
            }
          }
        }
      } catch { }
      try {
        $enumType = $parameterType
        if ($enumType -and $enumType.IsArray) { $enumType = $enumType.GetElementType() }
        if ($enumType -and $enumType.IsEnum) {
          foreach ($enumName in [System.Enum]::GetNames($enumType)) {
            if ($enumName) { $possibleValues += [string]$enumName }
          }
        }
      } catch { }

      $desc = ''
      $hp = $null
      try { if ($helpParamByName.ContainsKey($pn)) { $hp = $helpParamByName[$pn] } } catch { $hp = $null }
      if ($hp) {
        $desc = ''
        foreach ($d in @($hp.Description)) {
          $t = (GetText $d).Trim()
          if ($t) { if ($desc) { $desc += "`n`n" }; $desc += $t }
        }
        if (-not $typeName) { try { $typeName = [string]$hp.Type.Name } catch { } }
        if ((-not $aliases -or $aliases.Count -eq 0) -and $hp.Aliases) {
          foreach ($a in @($hp.Aliases)) { $aliases += [string]$a }
        }
        try {
          if ($hp.ValidValues) {
            foreach ($value in @($hp.ValidValues)) {
              if ($null -ne $value) { $possibleValues += [string]$value }
            }
          }
        } catch { }
        try { $defaultValue = [string]$hp.DefaultValue } catch { $defaultValue = '' }
        try { $acceptWild = [bool]$hp.Globbing } catch { $acceptWild = $false }
      }
      $possibleValuesNormalized = @()
      $seenPossibleValues = @{}
      foreach ($value in @($possibleValues)) {
        if (-not $value) { continue }
        $normalized = ([string]$value).Trim()
        if (-not $normalized) { continue }
        $key = $normalized.ToLowerInvariant()
        if (-not $seenPossibleValues.ContainsKey($key)) {
          $seenPossibleValues[$key] = $true
          $possibleValuesNormalized += $normalized
        }
      }
      $possibleValues = @($possibleValuesNormalized)

      $sets = @()
      if ($paramSets.ContainsKey($pn)) { $sets = @($paramSets[$pn]) }
      if (-not $sets -or $sets.Count -eq 0) { $sets = @('(All)') }

      $positionText = if ($named -or $null -eq $pos) { 'named' } else { [string]$pos }

      $pipelineInput = 'False'
      if ($pipeByValue -and $pipeByProp) { $pipelineInput = 'True (ByValue, ByPropertyName)' }
      elseif ($pipeByValue) { $pipelineInput = 'True (ByValue)' }
      elseif ($pipeByProp) { $pipelineInput = 'True (ByPropertyName)' }

      $parameters += [ordered]@{
        name = $pn
        type = $typeName
        description = $desc
        parameterSets = @($sets)
        aliases = @($aliases)
        possibleValues = @($possibleValues)
        required = [bool]$required
        position = $positionText
        defaultValue = $defaultValue
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
        } catch { }

        $inputs += [ordered]@{ name = $typeName; clrTypeName = $typeClrName; description = $typeDesc }
      }
    } catch { }
    if (-not $inputs -or $inputs.Count -eq 0) {
      $seenInputs = @{}
      foreach ($pn in $paramNames) {
        $pmeta = $null
        try { $pmeta = $c.Parameters[$pn] } catch { $pmeta = $null }
        if (-not $pmeta) { continue }

        $supportsPipeline = $false
        try {
          foreach ($setName in @($pmeta.ParameterSets.Keys)) {
            $psm = $pmeta.ParameterSets[$setName]
            if ($psm -and ($psm.ValueFromPipeline -or $psm.ValueFromPipelineByPropertyName)) {
              $supportsPipeline = $true
              break
            }
          }
        } catch { }

        if (-not $supportsPipeline) { continue }

        $inputTypeName = ''
        $inputTypeClrName = ''
        try {
          if ($pmeta.ParameterType) {
            $inputTypeName = [string]$pmeta.ParameterType.Name
            $inputTypeClrName = [string]$pmeta.ParameterType.FullName
          }
        } catch { }

        if (-not $inputTypeName) { continue }
        $key = if ($inputTypeClrName) { $inputTypeClrName } else { $inputTypeName }
        if ($seenInputs.ContainsKey($key)) { continue }
        $seenInputs[$key] = $true
        $inputs += [ordered]@{ name = $inputTypeName; clrTypeName = $inputTypeClrName; description = '' }
      }
    }

    $outputs = @()
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
        if (-not $typeClrName) { $typeClrName = $typeName }

        $typeDesc = ''
        try {
          foreach ($d in @($rv.Description)) {
            $t = (GetText $d).Trim()
            if ($t) { if ($typeDesc) { $typeDesc += "`n`n" }; $typeDesc += $t }
          }
        } catch { }

        $outputs += [ordered]@{ name = $typeName; clrTypeName = $typeClrName; description = $typeDesc }
      }
    } catch { }
    if (-not $outputs -or $outputs.Count -eq 0) {
      $seenOutputs = @{}
      try {
        foreach ($outputType in @($c.OutputType)) {
          $outputTypeName = ''
          $outputTypeClrName = ''
          try { $outputTypeName = [string]$outputType.Name } catch { $outputTypeName = '' }
          try { $outputTypeClrName = [string]$outputType.Type.FullName } catch { $outputTypeClrName = '' }
          if (-not $outputTypeClrName) { try { $outputTypeClrName = [string]$outputType.TypeName.FullName } catch { $outputTypeClrName = '' } }
          if (-not $outputTypeClrName) { try { $outputTypeClrName = [string]$outputType.Type.FullName } catch { } }
          if (-not $outputTypeClrName) { $outputTypeClrName = $outputTypeName }
          if (-not $outputTypeName) { $outputTypeName = $outputTypeClrName }
          if (-not $outputTypeName) { continue }

          $key = if ($outputTypeClrName) { $outputTypeClrName } else { $outputTypeName }
          if ($seenOutputs.ContainsKey($key)) { continue }
          $seenOutputs[$key] = $true
          $outputs += [ordered]@{ name = $outputTypeName; clrTypeName = $outputTypeClrName; description = '' }
        }
      } catch { }
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
    } catch { }

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
  if ($outDir) { [System.IO.Directory]::CreateDirectory($outDir) | Out-Null }
  $json = $result | ConvertTo-Json -Depth 8
  [System.IO.File]::WriteAllText($OutputJsonPath, $json, [System.Text.UTF8Encoding]::new($false))

  Write-Output 'PFDOCS::OK'
  exit 0
} catch {
  EmitError $_.Exception.Message
  exit 1
}
