# Explicit observations for the PowerShell help model, authored log clock prefixes,
# parser diagnostic JSON, and one two-locale HelpInfo map authored from a Hashtable.
# Raw records/file bytes remain in rawScenarioEvidence. This is not a generic normalizer.
function Convert-PlatyHelpValue {
    param($Value, [string] $Kind)
    if ($null -eq $Value -or $Value -is [string] -or $Value -is [ValueType]) { return $Value }
    if ($Value -is [array]) { return ,@(foreach ($item in $Value) { Convert-PlatyHelpValue $item $Kind }) }
    $schema = switch ($Kind) {
        'root' { [ordered]@{ details='details'; relatedLinks='links'; description='paragraph'; syntax='syntax'; parameters='parameters'; inputTypes='inputTypes'; examples='examples'; alertSet='alerts'; returnValues='returnValues'; 'xmlns:maml'='scalar'; 'xmlns:command'='scalar'; 'xmlns:dev'='scalar'; 'xmlns:MSHelp'='scalar'; Name='scalar'; Category='scalar'; Synopsis='scalar'; Component='scalar'; Role='scalar'; Functionality='scalar'; ModuleName='previewModule' } }
        'details' { [ordered]@{ name='scalar'; noun='scalar'; verb='scalar'; description='paragraph' } }
        'links' { [ordered]@{ navigationLink='link' } }
        'link' { [ordered]@{ linkText='scalar'; uri='scalar' } }
        'paragraph' { [ordered]@{ Text='scalar' } }
        'syntax' { [ordered]@{ syntaxItem='syntaxItem' } }
        'syntaxItem' { [ordered]@{ name='scalar'; parameter='parameter' } }
        'parameters' { [ordered]@{ parameter='parameter' } }
        'parameter' { [ordered]@{ name='scalar'; parameterValue='parameterValue'; description='paragraph'; defaultValue='scalar'; type='type'; required='scalar'; variableLength='scalar'; globbing='scalar'; pipelineInput='scalar'; position='scalar'; aliases='scalar'; parameterValueGroup='parameterValueGroup' } }
        'parameterValueGroup' { [ordered]@{ parameterValue='parameterValue' } }
        'parameterValue' { [ordered]@{ value='scalar'; required='scalar'; variableLength='scalar' } }
        'type' { [ordered]@{ name='scalar'; uri='scalar' } }
        'inputTypes' { [ordered]@{ inputType='inputType' } }
        'inputType' { [ordered]@{ type='type'; description='paragraph' } }
        'returnValues' { [ordered]@{ returnValue='returnValue' } }
        'returnValue' { [ordered]@{ type='type'; description='paragraph' } }
        'examples' { [ordered]@{ example='example' } }
        'example' { [ordered]@{ title='scalar'; introduction='paragraph'; code='scalar'; remarks='paragraph' } }
        'alerts' { [ordered]@{ alert='paragraph' } }
        default { throw "Unexpected structured help value for schema '$Kind' with fields '$(@($Value.PSObject.Properties.Name) -join ',')'." }
    }
    foreach ($property in $Value.PSObject.Properties) {
        if (-not $schema.Contains($property.Name)) { throw "Unreviewed help property '$Kind/$($property.Name)' must be added explicitly." }
    }
    $fields = [ordered]@{}
    foreach ($name in $schema.Keys) {
        $property = $Value.PSObject.Properties[$name]
        if ($null -eq $property) { continue }
        if ($schema[$name] -eq 'previewModule') {
            $moduleName = [string] $property.Value
            if ($moduleName -notmatch '^__DynamicModule_([0-9a-fA-F-]{36})$') { throw 'Preview help module identity is not an authored temporary module.' }
            [void][Guid]::Parse($Matches[1])
            $fields[$name] = '<PREVIEW_MODULE>'
        } else { $fields[$name] = Convert-PlatyHelpValue $property.Value $schema[$name] }
    }
    return [ordered]@{ kind=$Kind; fields=$fields }
}

function Convert-PlatyPreviewRecord {
    param($Record)
    $types = @(foreach ($name in $Record.typeNames) {
        if ($name -match '^MamlCommandHelpInfo#([^#]+)(#.*)?$' -and $Matches[1] -notin @('FullView')) {
            $path = $Matches[1]; $suffix = $Matches[2]
            if ((Split-Path -Parent $path) -ine $env:TEMP -or [IO.Path]::GetExtension($path) -ine '.tmp' -or (Test-Path -LiteralPath $path)) {
                throw 'Preview help did not identify a cleaned contained temporary MAML file.'
            }
            'MamlCommandHelpInfo#<PREVIEW_TEMP>' + $suffix
        } else { $name }
    })
    return [ordered]@{ type=$Record.type; typeNames=$types; value=Convert-PlatyHelpValue $Record.value 'root' }
}

function Convert-PlatyCabHelpInfoObservation {
    param($Content)
    if ($Content.kind -cne 'file' -or $Content.prefixHex -cne 'EFBBBF') { throw 'CAB HelpInfo must retain its authored UTF-8 BOM encoding.' }
    # Both exact byte layouts were reproduced by fresh original-only processes.
    # Keep all XML scaffolding and element formatting strict; only these two
    # UICulture siblings may exchange positions. Raw XML/hash stays in raw evidence.
    $prefix = '<?xml version="1.0" encoding="utf-8"?>' + "`r`n" +
        '<HelpInfo xmlns="http://schemas.microsoft.com/powershell/help/2010/05">' + "`r`n" +
        '  <HelpContentURI>https://example.invalid/help</HelpContentURI>' + "`r`n" +
        '  <SupportedUICultures>' + "`r`n"
    $suffix = '  </SupportedUICultures>' + "`r`n" + '</HelpInfo>'
    $en = '    <UICulture>' + "`r`n" +
        '      <UICultureName>en-US</UICultureName>' + "`r`n" +
        '      <UICultureVersion>1.0.0.1</UICultureVersion>' + "`r`n" +
        '    </UICulture>' + "`r`n"
    $pl = '    <UICulture>' + "`r`n" +
        '      <UICultureName>pl-PL</UICultureName>' + "`r`n" +
        '      <UICultureVersion>2.0.0.0</UICultureVersion>' + "`r`n" +
        '    </UICulture>' + "`r`n"
    if ($Content.text -cne ($prefix + $en + $pl + $suffix) -and $Content.text -cne ($prefix + $pl + $en + $suffix)) {
        throw 'CAB HelpInfo structure, URI, locale/version mapping, element formatting or exact two-locale inventory changed.'
    }
    $document = [Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $document.LoadXml($Content.text)
    $namespace = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespace.AddNamespace('h','http://schemas.microsoft.com/powershell/help/2010/05')
    $cultureMap = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($culture in $document.SelectNodes('/h:HelpInfo/h:SupportedUICultures/h:UICulture',$namespace)) {
        $cultureMap.Add($culture.SelectSingleNode('h:UICultureName',$namespace).InnerText,
            $culture.SelectSingleNode('h:UICultureVersion',$namespace).InnerText)
    }
    if ($cultureMap.Count -ne 2 -or $cultureMap['en-US'] -cne '1.0.0.1' -or $cultureMap['pl-PL'] -cne '2.0.0.0') { throw 'CAB HelpInfo locale/version map changed.' }
    return [ordered]@{
        kind='platyps-helpinfo-culture-map'
        prefixHex=$Content.prefixHex
        xmlVersion=$document.FirstChild.Version
        xmlEncoding=$document.FirstChild.Encoding
        namespace=$document.DocumentElement.NamespaceURI
        helpContentUri=$document.SelectSingleNode('/h:HelpInfo/h:HelpContentURI',$namespace).InnerText
        cultures=@([ordered]@{name='en-US';version=$cultureMap['en-US']},[ordered]@{name='pl-PL';version=$cultureMap['pl-PL']})
        exactNonCultureXmlScaffolding=$prefix + '<TWO_AUTHORED_CULTURE_ELEMENTS>' + $suffix
        exactCultureElementFormatting=$true
    }
}

function Convert-PlatyScenarioObservation {
    param([string] $ScenarioId, $Observation, [datetime] $Started, [datetime] $Completed)
    $previewIds = @('preview-maml','preview-list-conversion','preview-repeat','preview-missing-then-valid')
    $clockIds = @('update-empty','update-missing-command','update-force-missing','update-multiple-command-file','merge-empty')
    if ($ScenarioId -in $previewIds) {
        $Observation.records = @(foreach ($record in $Observation.records) {
            if ('MamlCommandHelpInfo' -in $record.typeNames) { Convert-PlatyPreviewRecord $record } else { $record }
        })
    }
    if ($ScenarioId -in $clockIds) {
        $Observation.records = @(foreach ($record in $Observation.records) {
            if ($record.type -ceq 'System.Management.Automation.WarningRecord') {
                if ($record.message -notmatch '^\[Update-MarkdownHelp\] (?<clock>\d{1,4}[./-]\d{1,2}[./-]\d{1,4} \d{1,2}:\d{2}:\d{2}(?:\s*[AP]M)?) (?<payload>.*)$') { throw 'Expected authored warning clock prefix is absent.' }
                # PowerShell string interpolation of DateTime uses invariant culture here.
                $clock = [datetime]::Parse($Matches.clock, [Globalization.CultureInfo]::InvariantCulture)
                $payload = $Matches.payload
                if ($clock -lt $Started.AddSeconds(-2) -or $clock -gt $Completed.AddSeconds(2)) { throw 'Authored warning clock is outside the observed invocation.' }
                [ordered]@{ type=$record.type; source='Update-MarkdownHelp.log'; authoredClockWithinInvocation=$true; message=$payload }
            } else { $record }
        })
    }
    if ($ScenarioId -ceq 'external-parser-error-log') {
        foreach ($effect in $Observation.fileEffects) {
            if ($effect.path -cne 'parser-errors.json') { continue }
            # Desktop emits a root JSON array as one pipeline object; assignment
            # followed by foreach preserves its authored entries on both hosts.
            $diagnostics = $effect.content.text | ConvertFrom-Json
            $entries = @(foreach ($diagnostic in $diagnostics) {
                $names = @($diagnostic.PSObject.Properties.Name)
                if ($names.Count -ne 3 -or @($names | Where-Object { $_ -notin @('Message','FilePath','Severity') }).Count) { throw 'Parser diagnostic JSON schema changed.' }
                [ordered]@{ message=Normalize-OwnedPaths ([string]$diagnostic.Message); filePath=Normalize-OwnedPaths ([string]$diagnostic.FilePath); severity=[string]$diagnostic.Severity }
            })
            $effect.content = [ordered]@{ kind='platyps-parser-diagnostic-json'; prefixHex=$effect.content.prefixHex; entries=$entries }
        }
    }
    if ($ScenarioId -ceq 'cab-increment-locale') {
        $path = 'cab-increment/PlatyWorkflowFixture_00000000-0000-0000-0000-000000000123_HelpInfo.xml'
        $effects = @($Observation.fileEffects | Where-Object { $_.path -ceq $path })
        if ($effects.Count -ne 1 -or $effects[0].change -cne 'created') { throw 'Expected CAB increment HelpInfo file effect is absent or duplicated.' }
        $effects[0].content = Convert-PlatyCabHelpInfoObservation $effects[0].content
    }
    return $Observation
}
