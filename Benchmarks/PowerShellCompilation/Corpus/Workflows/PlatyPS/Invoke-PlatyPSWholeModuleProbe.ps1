[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ImportPath,
    [Parameter(Mandatory=$true)][ValidateSet('original','built','installed')][string]$Surface,
    [Parameter(Mandatory=$true)][string]$ScenarioRoot,
    [Parameter(Mandatory=$true)][string]$ResultPath
)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$VerbosePreference='SilentlyContinue'
$ModulePath=[IO.Path]::GetFullPath($ImportPath)
$OutputPath=[IO.Path]::GetFullPath($ResultPath)
. (Join-Path $PSScriptRoot 'Workspace.support.ps1')
$trustedInputs=@(foreach($name in @('Invoke-PlatyPSWholeModuleProbe.ps1','SessionBoundary.support.ps1','Workspace.support.ps1','CompleterBoundary.support.ps1','Observation.support.ps1','FixtureHelp.xml')){
    [ordered]@{path=$name;sha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $name) -Algorithm SHA256).Hash}
})
foreach($path in @($ModulePath,$OutputPath,$ScenarioRoot)){Assert-PlatyPlainPath $path}
$moduleRoot=Split-Path $ModulePath
$comparison=if([IO.Path]::DirectorySeparatorChar -eq '\'){[StringComparison]::OrdinalIgnoreCase}else{[StringComparison]::Ordinal}
$expectedWorkRoot=[IO.Path]::GetFullPath((Join-Path $ScenarioRoot 'platyps-observer-owned'))
if($ModulePath.StartsWith($expectedWorkRoot+[IO.Path]::DirectorySeparatorChar,$comparison) -or $OutputPath.StartsWith($expectedWorkRoot+[IO.Path]::DirectorySeparatorChar,$comparison)){throw 'Import and result paths cannot be inside generated observer output.'}
$WorkRoot=Initialize-PlatyObserverWorkspace -ScenarioRoot $ScenarioRoot -TrustedInputs $trustedInputs
$oldLocation=Get-Location; $oldTemp=$env:TEMP; $oldTmp=$env:TMP
$rows=New-Object 'Collections.Generic.List[object]'
$rawFiles=New-Object 'Collections.Generic.List[object]'
$rawObservations=New-Object 'Collections.Generic.List[object]'
$cabEntries=@{}
. (Join-Path $PSScriptRoot 'Observation.support.ps1')
$env:TEMP=Join-Path $WorkRoot 'temp'; $env:TMP=$env:TEMP
New-Item -ItemType Directory -Path $env:TEMP | Out-Null
Set-Location -LiteralPath $WorkRoot
function Normalize-OwnedPaths([string]$Text){
    if($null -eq $Text){return $null}
    $Text=$Text.Replace($WorkRoot,'<WORK>').Replace($moduleRoot,'<MODULE>')
    return $Text
}
function Write-Fixture([string]$Path,[string]$Text){
    $parent=Split-Path $Path
    if(-not(Test-Path -LiteralPath $parent)){New-Item -ItemType Directory -Path $parent | Out-Null}
    [IO.File]::WriteAllText($Path,$Text,(New-Object Text.UTF8Encoding($false)))
}
function Output-Value($Value){
    if($null -eq $Value){return $null}
    if($Value -is [Management.Automation.ErrorRecord]){return [ordered]@{type=$Value.GetType().FullName;id=$Value.FullyQualifiedErrorId;category=$Value.CategoryInfo.Category.ToString();exception=$Value.Exception.GetType().FullName;message=Normalize-OwnedPaths $Value.Exception.Message}}
    if($Value -is [Management.Automation.WarningRecord] -or $Value -is [Management.Automation.VerboseRecord] -or $Value -is [Management.Automation.InformationRecord]){return [ordered]@{type=$Value.GetType().FullName;message=Normalize-OwnedPaths ([string]$Value)}}
    if($Value -is [IO.FileInfo]){return [ordered]@{type=$Value.GetType().FullName;name=$Value.Name;path=Normalize-OwnedPaths $Value.FullName}}
    $json=$Value|ConvertTo-Json -Depth 35
    foreach($entry in @(@($WorkRoot,'<WORK>'),@($moduleRoot,'<MODULE>'))){
        $escaped=ConvertTo-Json -InputObject ([string]$entry[0]) -Compress
        $json=$json.Replace($escaped.Substring(1,$escaped.Length-2),$entry[1])
    }
    return [ordered]@{type=$Value.GetType().FullName;typeNames=@($Value.PSObject.TypeNames);value=($json|ConvertFrom-Json)}
}
function File-State {
    $state=@{}
    foreach($file in @(Get-ChildItem -LiteralPath $WorkRoot -Recurse -File | Sort-Object FullName)){
        $relative=$file.FullName.Substring($WorkRoot.Length+1).Replace('\','/')
        $sha=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if($file.Extension -eq '.zip'){
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zip=[IO.Compression.ZipFile]::OpenRead($file.FullName)
            try{$entries=@(foreach($entry in $zip.Entries){if(-not $entry.FullName.EndsWith('/')){$stream=$entry.Open();$hash=[Security.Cryptography.SHA256]::Create();try{[ordered]@{name=$entry.FullName;sha256=([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-','');bytes=$entry.Length}}finally{$hash.Dispose();$stream.Dispose()}}})}finally{$zip.Dispose()}
            $value=[ordered]@{kind='zip';entries=$entries}
        }elseif($file.Extension -eq '.cab'){
            $value=[ordered]@{kind='cab';entries=@($cabEntries[$file.FullName])}
        }else{
            $bytes=[IO.File]::ReadAllBytes($file.FullName)
            $bom=([BitConverter]::ToString(@($bytes|Select-Object -First 3))).Replace('-','')
            $value=[ordered]@{kind='file';prefixHex=$bom;text=Normalize-OwnedPaths ([IO.File]::ReadAllText($file.FullName))}
        }
        $state[$relative]=[pscustomobject]@{sha256=$sha;bytes=$file.Length;value=$value}
    }
    return $state
}
function Observe([string]$Id,[string]$Export,[ValidateSet('Success','Error','Empty','Warning','Observe')][string]$Expected,[scriptblock]$Action,[string[]]$Owned=@(),[string]$RequiredErrorId){
    $started=[datetime]::Now
    $before=File-State; $caught=$null; $records=@()
    try{$records=@(& $Action *>&1)}catch{$caught=Output-Value $_}
    $after=File-State
    $changes=@(foreach($path in @(@($before.Keys)+@($after.Keys)|Sort-Object -Unique)){
        if(-not $after.ContainsKey($path)){[ordered]@{path=$path;change='removed'}}
        elseif(-not $before.ContainsKey($path) -or $before[$path].sha256 -cne $after[$path].sha256){
            $rawFiles.Add([ordered]@{scenarioId=$Id;path=$path;sha256=$after[$path].sha256;bytes=$after[$path].bytes})
            [ordered]@{path=$path;change=if($before.ContainsKey($path)){'changed'}else{'created'};content=$after[$path].value}
        }
    })
    $errors=@($records|Where-Object{$_ -is [Management.Automation.ErrorRecord]}).Count
    $warnings=@($records|Where-Object{$_ -is [Management.Automation.WarningRecord]}).Count
    $succeeded=switch($Expected){'Error'{$null -ne $caught -or $errors -gt 0};'Empty'{$null -eq $caught -and $errors -eq 0 -and $records.Count -eq 0};'Warning'{$null -eq $caught -and $errors -eq 0 -and $warnings -gt 0};'Observe'{$true};default{$null -eq $caught -and $errors -eq 0}}
    if($RequiredErrorId){$ids=@($records|Where-Object{$_ -is [Management.Automation.ErrorRecord]}|ForEach-Object FullyQualifiedErrorId);if($caught){$ids+=@($caught.id)};if(-not @($ids|Where-Object{$_ -like $RequiredErrorId}).Count){$succeeded=$false}}
    $observation=[ordered]@{expectedOutcome=$Expected;requiredErrorId=$RequiredErrorId;records=@($records|ForEach-Object{Output-Value $_});caught=$caught;fileEffects=$changes}
    $rawObservations.Add([ordered]@{scenarioId=$Id;observation=($observation|ConvertTo-Json -Depth 45|ConvertFrom-Json)})
    $observation=Convert-PlatyScenarioObservation -ScenarioId $Id -Observation $observation -Started $started -Completed ([datetime]::Now)
    $rows.Add([ordered]@{scenarioId=$Id;succeeded=[bool]$succeeded;executedExports=@(if($Export){$Export});executedOwnedFunctions=@($Owned);observation=$observation})
}
function Expand-OwnedCab([string]$Folder){
    $expand=Join-Path $env:SystemRoot 'System32/expand.exe'
    foreach($cab in @(Get-ChildItem -LiteralPath $Folder -Filter '*.cab' -File)){
        $destination=Join-Path $Folder ($cab.BaseName+'-expanded')
        New-Item -ItemType Directory -Path $destination | Out-Null
        & $expand '-F:*' $cab.FullName $destination | Out-Null
        if($LASTEXITCODE -ne 0){throw 'Owned cabinet expansion failed'}
        $cabEntries[$cab.FullName]=@(Get-ChildItem -LiteralPath $destination -File|Sort-Object Name|ForEach-Object{[ordered]@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash;bytes=$_.Length}})
    }
}
try{
    $module=Import-Module -Name $ModulePath -Force -PassThru -ErrorAction Stop
    $actualExports=@($module.ExportedFunctions.Keys|Sort-Object)
    $actualAliases=@(foreach($alias in @($module.ExportedAliases.Keys|Sort-Object)){[ordered]@{name=$alias;definition=$module.ExportedAliases[$alias].Definition}})
    $fixtureRoot=Join-Path $WorkRoot 'fixture'
    $fixtureModule=Join-Path $fixtureRoot 'PlatyWorkflowFixture.psm1'
    Write-Fixture $fixtureModule @'
function Get-PlatyFixture {
<#
.SYNOPSIS
Returns fixture text.
.DESCRIPTION
This local command describes owned compiler qualification data.
.PARAMETER Text
The selected text.
.PARAMETER Count
A count in the alternative parameter set.
.EXAMPLE
Get-PlatyFixture -Text alpha
Returns alpha.
.NOTES
First note paragraph.

Second note paragraph.
.LINK
https://example.invalid/platy-fixture
#>
[CmdletBinding(DefaultParameterSetName='Text')]
param([Parameter(ParameterSetName='Text',ValueFromPipeline=$true)][Alias('t')][ValidateSet('alpha','beta')][string]$Text='alpha',[Parameter(ParameterSetName='Count')][int]$Count=2,[switch]$Enabled,[Parameter(DontShow=$true)][string]$Hidden)
process{if($PSCmdlet.ParameterSetName -eq 'Count'){$Count}else{$Text}}
}
function Get-PlatySecond { <# .SYNOPSIS
Returns a second local value.
#> [CmdletBinding()]param([string]$Value='second') $Value }
Set-Alias pfixture Get-PlatyFixture
Export-ModuleMember -Function Get-PlatyFixture,Get-PlatySecond -Alias pfixture
'@
    Write-Fixture (Join-Path $fixtureRoot 'PlatyWorkflowFixture.psd1') "@{RootModule='PlatyWorkflowFixture.psm1';ModuleVersion='1.0.0';GUID='00000000-0000-0000-0000-000000000123';FunctionsToExport=@('Get-PlatyFixture','Get-PlatySecond');AliasesToExport=@('pfixture')}"
    Import-Module (Join-Path $fixtureRoot 'PlatyWorkflowFixture.psd1') -Force
    $md=Join-Path $WorkRoot 'markdown'; $moduleDocs=Join-Path $WorkRoot 'module-docs'; $empty=Join-Path $WorkRoot 'empty'
    New-Item -ItemType Directory -Path $empty | Out-Null
    $commandDoc=Join-Path $md 'Get-PlatyFixture.md'; $missing=Join-Path $WorkRoot 'missing.md'
    Observe 'markdown-command' 'New-MarkdownHelp' Success {New-MarkdownHelp -Command Get-PlatyFixture,Get-PlatySecond -OutputFolder $md -Metadata @{title='Owned'} -OnlineVersionUrl 'https://example.invalid/platy-fixture' -AlphabeticParamsOrder -UseFullTypeName -ExcludeDontShow}
    $stamp=(Get-Item -LiteralPath $commandDoc).LastWriteTimeUtc
    Observe 'markdown-repeat' 'New-MarkdownHelp' Success {New-MarkdownHelp -Command Get-PlatyFixture -OutputFolder $md -Metadata @{title='Owned'} -OnlineVersionUrl 'https://example.invalid/platy-fixture' -AlphabeticParamsOrder -UseFullTypeName -ExcludeDontShow; if((Get-Item -LiteralPath $commandDoc).LastWriteTimeUtc -ne $stamp){throw 'Unchanged repeat rewrote Markdown'}; [ordered]@{unchangedTimestamp=$true}}
    Add-Content -LiteralPath $commandDoc -Value 'Owned overwrite marker'
    Observe 'markdown-overwrite-refusal' 'New-MarkdownHelp' Error {New-MarkdownHelp -Command Get-PlatyFixture -OutputFolder $md -ErrorAction Continue}
    Observe 'markdown-force' 'New-MarkdownHelp' Success {New-MarkdownHelp -Command Get-PlatyFixture -OutputFolder $md -Force}
    Observe 'markdown-module' 'New-MarkdownHelp' Success {New-MarkdownHelp -Module PlatyWorkflowFixture -OutputFolder $moduleDocs -WithModulePage -Locale en-US -HelpVersion 1.0.0.0 -FwLink 'https://example.invalid/help' -ExcludeDontShow}
    Observe 'markdown-missing-command' 'New-MarkdownHelp' Error {New-MarkdownHelp -Command Get-PlatyMissingCommand -OutputFolder (Join-Path $WorkRoot 'missing-command')}
    Observe 'markdown-metadata-conflict' 'New-MarkdownHelp' Error {New-MarkdownHelp -Command Get-PlatyFixture -OutputFolder (Join-Path $WorkRoot 'metadata-conflict') -Metadata @{title='Owned'} -NoMetadata}
    Observe 'metadata-text' 'Get-MarkdownMetadata' Success {Get-MarkdownMetadata -Markdown "---`ntitle: Owned`nversion: 1.2.3`n---`n# Body"}
    Observe 'metadata-path-pipeline' 'Get-MarkdownMetadata' Success {@($commandDoc,(Join-Path $md 'Get-PlatySecond.md'))|Get-MarkdownMetadata}
    Observe 'metadata-empty' 'Get-MarkdownMetadata' Observe {Get-MarkdownMetadata -Markdown '# No metadata'; Get-MarkdownMetadata -Path $empty}
    Observe 'metadata-invalid-path' 'Get-MarkdownMetadata' Error {Get-MarkdownMetadata -Path $missing -ErrorAction Continue}
    Observe 'metadata-invalid-yaml' 'Get-MarkdownMetadata' Observe {Get-MarkdownMetadata -Markdown "---`nkey: [unterminated`n---`n# Body"}
    $about=Join-Path $WorkRoot 'about'; New-Item -ItemType Directory -Path $about | Out-Null
    Observe 'about-name' 'New-MarkdownAboutHelp' Success {New-MarkdownAboutHelp -OutputFolder $about -AboutName PlatyOwned; if(-not(Test-Path -LiteralPath (Join-Path $about 'about_PlatyOwned.md'))){throw 'About file missing'}}
    Observe 'about-prefixed' 'New-MarkdownAboutHelp' Success {New-MarkdownAboutHelp -OutputFolder $about -AboutName about_PlatyPrefixed}
    Observe 'about-missing-folder' 'New-MarkdownAboutHelp' Error {New-MarkdownAboutHelp -OutputFolder (Join-Path $WorkRoot 'absent-about') -AboutName Missing}
    Observe 'about-duplicate' 'New-MarkdownAboutHelp' Error {New-MarkdownAboutHelp -OutputFolder $about -AboutName PlatyOwned -ErrorAction Continue}
    Observe 'about-empty-name' 'New-MarkdownAboutHelp' Observe {New-MarkdownAboutHelp -OutputFolder $about -AboutName ''}
    $external=Join-Path $WorkRoot 'external'; $xml=Join-Path $WorkRoot 'combined.xml'; $errorLog=Join-Path $WorkRoot 'external-errors.json'
    Observe 'external-one' 'New-ExternalHelp' Success {New-ExternalHelp -Path $commandDoc -OutputPath $external -ErrorLogFile $errorLog}
    Observe 'external-pipeline' 'New-ExternalHelp' Success {@($commandDoc,(Join-Path $md 'Get-PlatySecond.md'))|New-ExternalHelp -OutputPath $xml}
    Observe 'external-explicit-xml' 'New-ExternalHelp' Success {New-ExternalHelp -Path $commandDoc -OutputPath (Join-Path $WorkRoot 'explicit.xml')}
    Observe 'external-repeat' 'New-ExternalHelp' Success {New-ExternalHelp -Path $commandDoc -OutputPath $external}
    $xmlInExternal=@(Get-ChildItem -LiteralPath $external -Filter '*.xml' -File)[0].FullName; Add-Content -LiteralPath $xmlInExternal -Value 'Owned overwrite marker'
    Observe 'external-overwrite-refusal' 'New-ExternalHelp' Error {New-ExternalHelp -Path $commandDoc -OutputPath $external -ErrorAction Continue}
    Observe 'external-force' 'New-ExternalHelp' Success {New-ExternalHelp -Path $commandDoc -OutputPath $external -Force}
    Observe 'external-empty' 'New-ExternalHelp' Empty {New-ExternalHelp -Path $empty -OutputPath (Join-Path $WorkRoot 'external-empty')}
    Observe 'external-invalid-width' 'New-ExternalHelp' Error {New-ExternalHelp -Path $commandDoc -OutputPath (Join-Path $WorkRoot 'invalid-width') -MaxAboutWidth 79}
    $tagged=Join-Path $WorkRoot 'tagged'
    Write-Fixture (Join-Path $tagged 'Get-PlatyFixture.md') ([regex]::Replace([IO.File]::ReadAllText($commandDoc),'\A---',"---`napplicable: Core"))
    Write-Fixture (Join-Path $tagged 'Get-PlatySecond.md') ([regex]::Replace([IO.File]::ReadAllText((Join-Path $md 'Get-PlatySecond.md')),'\A---',"---`napplicable: Desktop"))
    Copy-Item -LiteralPath (Join-Path $moduleDocs 'PlatyWorkflowFixture.md') -Destination (Join-Path $tagged 'PlatyWorkflowFixture.md')
    Observe 'external-about-tags' 'New-ExternalHelp' Success {New-ExternalHelp -Path $tagged,$about -OutputPath (Join-Path $WorkRoot 'tag-about') -ApplicableTag Core -MaxAboutWidth 80}
    $invalidMarkdown=Join-Path $WorkRoot 'invalid/Get-Invalid.md'; Write-Fixture $invalidMarkdown "---`nschema: '999.0.0'`n---`n# Get-Invalid`n## UNKNOWN`nBroken"
    Observe 'external-parser-error-log' 'New-ExternalHelp' Error {New-ExternalHelp -Path $invalidMarkdown -OutputPath (Join-Path $WorkRoot 'invalid-external') -ErrorLogFile (Join-Path $WorkRoot 'parser-errors.json')}
    Observe 'markdown-maml' 'New-MarkdownHelp' Success {New-MarkdownHelp -MamlFile $xml -OutputFolder (Join-Path $WorkRoot 'from-maml') -ConvertNotesToList -ConvertDoubleDashLists -WithModulePage -ModuleName PlatyWorkflowFixture -ModuleGuid '00000000-0000-0000-0000-000000000123'}
    Observe 'markdown-pinned-maml' 'New-MarkdownHelp' Success {New-MarkdownHelp -MamlFile (Join-Path $PSScriptRoot 'FixtureHelp.xml') -OutputFolder (Join-Path $WorkRoot 'from-pinned-maml') -ConvertNotesToList -ConvertDoubleDashLists -ModuleName PlatyWorkflowFixture}
    $yaml=Join-Path $WorkRoot 'yaml'
    Observe 'yaml-files' 'New-YamlHelp' Success {New-YamlHelp -Path $md -OutputFolder $yaml}
    Observe 'yaml-pipeline' 'New-YamlHelp' Success {@($commandDoc,(Join-Path $md 'Get-PlatySecond.md'))|New-YamlHelp -OutputFolder (Join-Path $WorkRoot 'yaml-pipeline')}
    Observe 'yaml-repeat' 'New-YamlHelp' Success {New-YamlHelp -Path $md -OutputFolder $yaml}
    Add-Content -LiteralPath (Join-Path $yaml 'Get-PlatyFixture.yml') -Value 'Owned overwrite marker'
    Observe 'yaml-overwrite-refusal' 'New-YamlHelp' Error {New-YamlHelp -Path $commandDoc -OutputFolder $yaml -ErrorAction Continue}
    Observe 'yaml-force' 'New-YamlHelp' Success {New-YamlHelp -Path $commandDoc -OutputFolder $yaml -Force}
    Observe 'yaml-empty' 'New-YamlHelp' Empty {New-YamlHelp -Path $empty -OutputFolder (Join-Path $WorkRoot 'yaml-empty')}
    $outputFile=Join-Path $WorkRoot 'not-folder.txt'; Write-Fixture $outputFile 'Owned file'
    Observe 'yaml-output-is-file' 'New-YamlHelp' Error {New-YamlHelp -Path $commandDoc -OutputFolder $outputFile}
    $commandBefore=(Get-Command Get-PlatyFixture).ScriptBlock.ToString(); $tempBefore=@(Get-ChildItem -LiteralPath $env:TEMP -Force).Count
    Observe 'preview-maml' 'Get-HelpPreview' Success {Get-HelpPreview -Path $xml}
    Observe 'preview-list-conversion' 'Get-HelpPreview' Success {Get-HelpPreview -Path $xml -ConvertNotesToList -ConvertDoubleDashLists}
    Observe 'preview-repeat' 'Get-HelpPreview' Success {Get-HelpPreview -Path $xml; if((Get-Command Get-PlatyFixture).ScriptBlock.ToString() -cne $commandBefore -or @(Get-ChildItem -LiteralPath $env:TEMP -Force).Count -ne $tempBefore){throw 'Preview changed caller command or leaked temporary files'}; [ordered]@{callerCommandPreserved=$true;tempCountPreserved=$true}}
    Observe 'preview-missing-then-valid' 'Get-HelpPreview' Error {Get-HelpPreview -Path $missing,$xml -ErrorAction Continue}
    $invalidXml=Join-Path $WorkRoot 'invalid.xml'; Write-Fixture $invalidXml '<unclosed'
    Observe 'preview-invalid-xml' 'Get-HelpPreview' Error {Get-HelpPreview -Path $invalidXml -ErrorAction Continue}
    $updateLog=Join-Path $WorkRoot 'update.log'
    Observe 'update-command' 'Update-MarkdownHelp' Success {Update-MarkdownHelp -Path $commandDoc -AlphabeticParamsOrder -UseFullTypeName -UpdateInputOutput -LogPath $updateLog}
    Observe 'update-pipeline' 'Update-MarkdownHelp' Success {@($commandDoc,(Join-Path $md 'Get-PlatySecond.md'))|Update-MarkdownHelp}
    Observe 'update-log-append' 'Update-MarkdownHelp' Success {Update-MarkdownHelp -Path $commandDoc -LogPath $updateLog -LogAppend}
    Observe 'update-empty' 'Update-MarkdownHelp' Warning {Update-MarkdownHelp -Path $empty}
    $unknownDoc=Join-Path $WorkRoot 'unknown/Get-PlatyMissing.md'; Write-Fixture $unknownDoc ([IO.File]::ReadAllText($commandDoc).Replace('Get-PlatyFixture','Get-PlatyMissing'))
    Observe 'update-missing-command' 'Update-MarkdownHelp' Warning {Update-MarkdownHelp -Path $unknownDoc}
    Observe 'update-force-missing' 'Update-MarkdownHelp' Warning {Update-MarkdownHelp -Path $unknownDoc -Force; if(Test-Path -LiteralPath $unknownDoc){throw 'Unknown document was not removed'}}
    $multiDoc=Join-Path $WorkRoot 'multi/Get-Multiple.md'; Write-Fixture $multiDoc ([IO.File]::ReadAllText($commandDoc)+"`n"+([regex]::Replace([IO.File]::ReadAllText((Join-Path $md 'Get-PlatySecond.md')),'(?s)\A---.*?---\r?\n','')))
    Observe 'update-multiple-command-file' 'Update-MarkdownHelp' Warning {Update-MarkdownHelp -Path $multiDoc}
    Remove-Item -LiteralPath (Join-Path $moduleDocs 'Get-PlatySecond.md')
    Observe 'update-module' 'Update-MarkdownHelpModule' Success {Update-MarkdownHelpModule -Path $moduleDocs}
    Observe 'update-module-refresh' 'Update-MarkdownHelpModule' Success {Update-MarkdownHelpModule -Path $moduleDocs -RefreshModulePage}
    Observe 'update-module-empty' 'Update-MarkdownHelpModule' Error {Update-MarkdownHelpModule -Path $empty -ErrorAction Continue}
    $noName=Join-Path $WorkRoot 'no-module-name'; New-Item -ItemType Directory -Path $noName|Out-Null; Write-Fixture (Join-Path $noName 'Get-NoName.md') '# Get-NoName'
    Observe 'update-module-no-name' 'Update-MarkdownHelpModule' Error {Update-MarkdownHelpModule -Path $noName -ErrorAction Continue}
    $missingModule=Join-Path $WorkRoot 'missing-module'; New-Item -ItemType Directory -Path $missingModule|Out-Null; Write-Fixture (Join-Path $missingModule 'MissingModule.md') "---`nModule Name: MissingPlatyModule`nModule Guid: 00000000-0000-0000-0000-000000000124`n---`n# MissingPlatyModule"
    Observe 'update-module-missing-module' 'Update-MarkdownHelpModule' Error {Update-MarkdownHelpModule -Path $missingModule -ErrorAction Continue}
    $mergeRoot=Join-Path $WorkRoot 'merge-input'; $editionA=Join-Path $mergeRoot 'A'; $editionB=Join-Path $mergeRoot 'B'; $mergeOut=Join-Path $WorkRoot 'merged'
    Write-Fixture (Join-Path $editionA 'Get-PlatyFixture.md') ([IO.File]::ReadAllText($commandDoc)); Write-Fixture (Join-Path $editionB 'Get-PlatyFixture.md') ([IO.File]::ReadAllText($commandDoc).Replace('Returns fixture text.','Returns edition B fixture text.'))
    Write-Fixture (Join-Path $editionA 'Get-PlatySecond.md') ([IO.File]::ReadAllText((Join-Path $md 'Get-PlatySecond.md')))
    Observe 'merge-tagged' 'Merge-MarkdownHelp' Success {Merge-MarkdownHelp -Path $editionA,$editionB -OutputPath $mergeOut}
    Observe 'merge-explicit-tags' 'Merge-MarkdownHelp' Success {Merge-MarkdownHelp -Path $editionA,$editionB -OutputPath (Join-Path $WorkRoot 'merged-explicit') -ExplicitApplicableIfAll -MergeMarker 'OWNED: '}
    Observe 'merge-repeat' 'Merge-MarkdownHelp' Success {Merge-MarkdownHelp -Path $editionA,$editionB -OutputPath $mergeOut}
    Add-Content -LiteralPath (Join-Path $mergeOut 'Get-PlatyFixture.md') -Value 'Owned overwrite marker'
    Observe 'merge-overwrite-refusal' 'Merge-MarkdownHelp' Error {Merge-MarkdownHelp -Path $editionA,$editionB -OutputPath $mergeOut -ErrorAction Continue}
    Observe 'merge-force' 'Merge-MarkdownHelp' Success {Merge-MarkdownHelp -Path $editionA,$editionB -OutputPath $mergeOut -Force}
    Observe 'merge-empty' 'Merge-MarkdownHelp' Warning {Merge-MarkdownHelp -Path $empty -OutputPath (Join-Path $WorkRoot 'merged-empty')}
    Observe 'merge-invalid-markdown' 'Merge-MarkdownHelp' Observe {Merge-MarkdownHelp -Path $invalidMarkdown -OutputPath (Join-Path $WorkRoot 'merged-invalid')}
    $cabFiles=Join-Path $WorkRoot 'cab-files'; New-Item -ItemType Directory -Path $cabFiles|Out-Null; Copy-Item -LiteralPath $xml -Destination (Join-Path $cabFiles 'owned-help.xml'); Write-Fixture (Join-Path $cabFiles 'about_owned.help.txt') 'Owned cabinet help'; Write-Fixture (Join-Path $cabFiles 'ignored.bin') 'Owned invalid extension'
    $landing=Join-Path $moduleDocs 'PlatyWorkflowFixture.md'; $cabOut=Join-Path $WorkRoot 'cab-output'; $cabCwd=Join-Path $WorkRoot 'cab-cwd'; New-Item -ItemType Directory -Path $cabCwd|Out-Null
    if((Get-Command MakeCab.exe -ErrorAction Stop).Source -ine (Join-Path $env:SystemRoot 'System32/makecab.exe')){throw 'Unexpected MakeCab identity'}
    Set-Location -LiteralPath $cabCwd
    Observe 'cab-positive' 'New-ExternalHelpCab' Success {New-ExternalHelpCab -CabFilesFolder $cabFiles -LandingPagePath $landing -OutputFolder $cabOut; Expand-OwnedCab $cabOut; if(@(Get-ChildItem -LiteralPath $cabOut -Filter '*.cab').Count -ne 1 -or @(Get-ChildItem -LiteralPath $cabCwd -Force).Count -ne 0){throw 'Expected cabinet missing or temporary cabinet files remain'}}
    $incrementLanding=Join-Path $WorkRoot 'increment-page.md'; Write-Fixture $incrementLanding ([IO.File]::ReadAllText($landing).Replace('Locale: en-US',"Locale: en-US`r`nAdditional Locale: pl-PL`r`npl-PL Version: 2.0.0.0"))
    $incrementOut=Join-Path $WorkRoot 'cab-increment'
    $incrementCwd=Join-Path $WorkRoot 'cab-increment-cwd'; New-Item -ItemType Directory -Path $incrementCwd|Out-Null; Set-Location -LiteralPath $incrementCwd
    Observe 'cab-increment-locale' 'New-ExternalHelpCab' Success {New-ExternalHelpCab -CabFilesFolder $cabFiles -LandingPagePath $incrementLanding -OutputFolder $incrementOut -IncrementHelpVersion; Expand-OwnedCab $incrementOut; if([IO.File]::ReadAllText($incrementLanding) -notmatch 'Help Version: 1\.0\.0\.1'){throw 'Help version did not increment'}}
    Observe 'cab-empty' 'New-ExternalHelpCab' Error {New-ExternalHelpCab -CabFilesFolder $empty -LandingPagePath $landing -OutputFolder (Join-Path $WorkRoot 'cab-empty')}
    Observe 'cab-invalid-folder' 'New-ExternalHelpCab' Error {New-ExternalHelpCab -CabFilesFolder $missing -LandingPagePath $landing -OutputFolder (Join-Path $WorkRoot 'cab-invalid-folder')}
    Observe 'cab-invalid-landing' 'New-ExternalHelpCab' Error {New-ExternalHelpCab -CabFilesFolder $cabFiles -LandingPagePath $missing -OutputFolder (Join-Path $WorkRoot 'cab-invalid-landing')}
    $badCab=Join-Path $WorkRoot 'bad-cab'; New-Item -ItemType Directory -Path $badCab|Out-Null; Write-Fixture (Join-Path $badCab 'only.bin') 'Owned invalid extension'
    Observe 'cab-invalid-help-files' 'New-ExternalHelpCab' Error {New-ExternalHelpCab -CabFilesFolder $badCab -LandingPagePath $landing -OutputFolder (Join-Path $WorkRoot 'cab-invalid-types')}
    Set-Location -LiteralPath $WorkRoot
    Observe 'helper-range' '' Success {foreach($n in @(0,1,3,-1)){try{[ordered]@{n=$n;values=@(& $module {param($value) GetRange -n $value} $n);caught=$null}}catch{[ordered]@{n=$n;caught=Output-Value $_}}}} -Owned GetRange
    Observe 'helper-type-string' '' Success {foreach($type in @([int],[Nullable[int]],[Collections.Generic.List[int]])){[ordered]@{type=$type.FullName;name=& $module {param($value) GetTypeString -TypeObject $value} $type}}} -Owned GetTypeString
    Observe 'helper-provider-fallback' '' Success {try{Set-Location Function:\; $provider=& $module {validateWorkingProvider; [ordered]@{provider=(Get-Location).Drive.Provider.Name}}; if($provider.provider -ne 'FileSystem'){throw 'Provider fallback failed'}; $provider}finally{Set-Location -LiteralPath $WorkRoot}} -Owned validateWorkingProvider
    . (Join-Path $PSScriptRoot 'SessionBoundary.support.ps1')
    Observe 'session-boundary-positive' 'New-MarkdownHelp' Success {Invoke-PlatySessionBoundary -Module $module -OutputFolder (Join-Path $WorkRoot 'session-markdown')} -Owned MyGetCommand,GetRange,GetTypeString
    Observe 'session-boundary-error' 'New-MarkdownHelp' Error {Invoke-PlatySessionBoundary -Module $module -OutputFolder (Join-Path $WorkRoot 'session-error') -FailTransport} -Owned MyGetCommand -RequiredErrorId 'OwnedPlatyTransportFailure*'
    Observe 'module-initialization' '' Success {
        & $module {[ordered]@{localizedKeyCount=$LocalizedData.Count;helpVersion=$LocalizedData.HelpVersion;commandNotFound=$LocalizedData.CommandNotFound;outputFolderNotFound=$LocalizedData.OutputFolderNotFound;encoding=$script:UTF8_NO_BOM.WebName;preambleBytes=$script:UTF8_NO_BOM.GetPreamble().Length;schemaKey=$script:SCHEMA_VERSION_YAML_HEADER;externalHelpKey=$script:EXTERNAL_HELP_FILE_YAML_HEADER}}
        $dependencies=@(foreach($name in @('Markdown.MAML','YamlDotNet')){
            $loaded=@([AppDomain]::CurrentDomain.GetAssemblies()|Where-Object{$_.GetName().Name -eq $name})
            if($loaded.Count -ne 1 -or -not $loaded[0].Location.StartsWith($moduleRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Managed dependency was not loaded from the imported artifact root'}
            [ordered]@{name=$name;identity=$loaded[0].FullName;location=$loaded[0].Location;sha256=(Get-FileHash -LiteralPath $loaded[0].Location).Hash}
        })
        [ordered]@{managedDependencies=$dependencies}
    }
    Observe 'module-reimport' '' Success {Remove-Module $module.Name -ErrorAction Stop; $script:module=Import-Module $ModulePath -Force -PassThru -ErrorAction Stop; [ordered]@{exports=@($module.ExportedFunctions.Keys|Sort-Object);aliases=@($module.ExportedAliases.Keys|Sort-Object);version=$module.Version.ToString()}}
    Observe 'module-completer' '' Success {$inputText='New-MarkdownHelp -Module PlatyWorkflowF'; $completion=TabExpansion2 -inputScript $inputText -cursorColumn $inputText.Length; if(-not @($completion.CompletionMatches|Where-Object CompletionText -eq 'PlatyWorkflowFixture').Count){throw 'Loaded module completion is absent'}; @($completion.CompletionMatches|ForEach-Object{[ordered]@{text=$_.CompletionText;list=$_.ListItemText;type=$_.ResultType.ToString();tooltip=$_.ToolTip}})}
    . (Join-Path $PSScriptRoot 'CompleterBoundary.support.ps1')
    Observe 'module-completer-inbox-direct' '' Success {$values=@(& $module {ModuleNameCompleter 'New-MarkdownHelp' 'Module' 'PlatyWorkflowF' $null @{}}); $empty=@(& $module {ModuleNameCompleter 'New-MarkdownHelp' 'Module' 'AbsentPlatyWorkflow' $null @{}}); if($values.Count -ne 1 -or $values[0] -cne 'PlatyWorkflowFixture' -or $empty.Count -ne 0){throw 'Inbox authored completer returned unexpected values'}; [ordered]@{values=$values;missingPrefix=$empty}}
    Observe 'module-completer-alternative' '' Success {Invoke-PlatyAlternateCompleter -ModulePath $ModulePath -WorkRoot $WorkRoot -WordToComplete 'PlatyWorkflowF'}
    Observe 'module-completer-alternative-empty' '' Success {Invoke-PlatyAlternateCompleter -ModulePath $ModulePath -WorkRoot $WorkRoot -WordToComplete 'AbsentPlatyWorkflow' -ExpectEmpty}
    Seal-PlatyObserverWorkspace -Root $WorkRoot -TrustedInputs $trustedInputs
    $result=[ordered]@{schemaVersion=1;surface=$Surface;importPath=$ModulePath;actualExports=$actualExports;actualAliases=$actualAliases;scenarios=$rows.ToArray();rawFileEvidence=$rawFiles.ToArray();rawScenarioEvidence=$rawObservations.ToArray();interpretation='Ordinary ten-export workflow observer; no production compiler hooks, network or live remoting. Raw archive hashes and raw records remain outside comparable observations. Explicit help-model/parser-diagnostic/log-clock projections preserve semantic fields, array order and message payloads; authored temporary preview identities and clock ranges are validated.'}
    $json=$result|ConvertTo-Json -Depth 45
    [IO.File]::WriteAllText($OutputPath,$json,(New-Object Text.UTF8Encoding($false)))
    if(@($rows|Where-Object{-not $_.succeeded}).Count){exit 1}
}finally{
    Remove-Module TabExpansionPlusPlus -ErrorAction SilentlyContinue
    Remove-Module PlatyWorkflowFixture -ErrorAction SilentlyContinue
    if($module){Remove-Module $module.Name -ErrorAction SilentlyContinue}
    Set-Location $oldLocation; $env:TEMP=$oldTemp; $env:TMP=$oldTmp
}
