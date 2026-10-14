[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ImportPath,
    [Parameter(Mandatory=$true)][ValidateSet('Original','Built','Installed')][string]$Surface,
    [Parameter(Mandatory=$true)][string]$ScenarioRoot,
    [Parameter(Mandatory=$true)][string]$ResultPath,
    [string]$PreloadYamlAssemblyPath
)
$ErrorActionPreference = 'Stop'
$ModulePath=$ImportPath
$DataRoot=$ScenarioRoot
$OutputPath=$ResultPath
if (-not (Test-Path -LiteralPath $DataRoot -PathType Container)) { throw 'Caller must provide an existing owned DataRoot.' }
$DataRoot = [IO.Path]::GetFullPath($DataRoot)
if ((Get-Item -LiteralPath $DataRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Owned scenario root must not be a link.' }
# The shared harness reuses the same scenario path so error/output paths compare
# exactly. Reset only this observer's previous file fixtures before each surface;
# its preceding JSON report already retains those bytes and their hashes.
$knownFixtureNames = @('output.yaml','getter-error.yaml','empty-input.yaml')
$existingFixtures = @(Get-ChildItem -LiteralPath $DataRoot -Force)
foreach ($fixture in $existingFixtures) {
    if ($fixture.PSIsContainer -or $fixture.Name -notin $knownFixtureNames -or
        ($fixture.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        -not $fixture.DirectoryName.Equals($DataRoot,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Scenario root contains an unexpected file, directory or link; preserve it for inspection.'
    }
}
foreach ($fixture in $existingFixtures) { Remove-Item -LiteralPath $fixture.FullName -ErrorAction Stop }
$moduleRoot = Split-Path -Parent ([IO.Path]::GetFullPath($ModulePath))
function Normalize-YamlObservation {
    param($Value,[switch]$PreserveUnorderedEnumeration)
    if ($null -eq $Value) { return $null }
    if ($Value -is [Management.Automation.ErrorRecord]) {
        return [ordered]@{ kind='ErrorRecord'; errorId=$Value.FullyQualifiedErrorId; exception=$Value.Exception.GetType().FullName; category=[string]$Value.CategoryInfo.Category; message=$Value.Exception.Message.Replace($DataRoot,'<data>').Replace($moduleRoot,'<module>') }
    }
    if ($Value -is [Collections.IDictionary]) {
        $keys=@($Value.Keys)
        # Fresh Original/Original proof established random Hashtable enumeration.
        # Canonicalize only its string-key map semantics; raw order is retained below.
        # OrderedDictionary and custom-object/property/sequence order remain strict.
        if($Value -is [hashtable] -and -not $PreserveUnorderedEnumeration -and @($keys|Where-Object {$_ -isnot [string]}).Count -eq 0){
            [string[]]$canonicalKeys=$keys
            [Array]::Sort($canonicalKeys,[StringComparer]::Ordinal)
            $keys=$canonicalKeys
        }
        return [ordered]@{ type=$Value.GetType().FullName; ordered=($Value -is [Collections.Specialized.OrderedDictionary]); entries=@(foreach($key in $keys) { [ordered]@{key=(Normalize-YamlObservation $key -PreserveUnorderedEnumeration:$PreserveUnorderedEnumeration);value=(Normalize-YamlObservation $Value[$key] -PreserveUnorderedEnumeration:$PreserveUnorderedEnumeration)} }) }
    }
    if ($Value -is [Collections.IList]) { return [ordered]@{type=$Value.GetType().FullName;values=@(foreach($item in $Value){Normalize-YamlObservation $item -PreserveUnorderedEnumeration:$PreserveUnorderedEnumeration})} }
    if ($Value -is [Reflection.Assembly]) { return [ordered]@{type='Assembly';identity=$Value.FullName} }
    if ($Value -is [datetime]) { return [ordered]@{type=$Value.GetType().FullName;kind=[string]$Value.Kind;value=$Value.ToString('o',[Globalization.CultureInfo]::InvariantCulture)} }
    if ($Value -is [string]) { return $Value }
    if ($Value -is [Management.Automation.PSCustomObject]) { return [ordered]@{type=$Value.GetType().FullName;properties=@(foreach($property in $Value.PSObject.Properties){[ordered]@{name=$property.Name;value=(Normalize-YamlObservation $property.Value -PreserveUnorderedEnumeration:$PreserveUnorderedEnumeration)}})} }
    return [ordered]@{type=$Value.GetType().FullName;value=[Convert]::ToString($Value,[Globalization.CultureInfo]::InvariantCulture)}
}
$requiredHelpers = @('Invoke-LoadFile','Invoke-LoadAssembly','Get-YamlDocuments','Convert-ValueToProperType','Convert-YamlMappingToHashtable','Convert-YamlSequenceToArray','Convert-YamlDocumentToPSObject','Convert-HashtableToDictionary','Convert-OrderedHashtableToDictionary','Convert-ListToGenericList','Convert-PSObjectToGenericObject','ConvertFrom-Yaml','Get-Serializer','ConvertTo-Yaml')
# The common qualification harness owns dependency locks, artifacts, deployment,
# and complete-method ledger checks. This file only observes one module surface.

# Existing forty original/generated cases are inserted from the maintained probe AST.
$cases = @(
 @{id='scalar-int32';run={ConvertFrom-Yaml '42'}},
 @{id='scalar-int32-min';run={ConvertFrom-Yaml '-2147483648'}},
 @{id='scalar-int64';run={ConvertFrom-Yaml '2147483648'}},
 @{id='scalar-int64-max';run={ConvertFrom-Yaml '9223372036854775807'}},
 @{id='scalar-bigint';run={ConvertFrom-Yaml '9223372036854775808'}},
 @{id='scalar-negative-bigint';run={ConvertFrom-Yaml '-9223372036854775809'}},
 @{id='scalar-decimal';run={ConvertFrom-Yaml '123.45'}},
 @{id='scalar-scientific';run={ConvertFrom-Yaml '1.25e3'}},
 @{id='scalar-bool';run={ConvertFrom-Yaml 'true'}},
 @{id='scalar-null';run={ConvertFrom-Yaml 'null'}},
 @{id='scalar-quoted';run={ConvertFrom-Yaml '"true"'}},
 @{id='tagged-string';run={ConvertFrom-Yaml '!!str 42'}},
 @{id='tagged-boolean';run={ConvertFrom-Yaml '!!bool FALSE'}},
 @{id='tagged-int-hex';run={ConvertFrom-Yaml '!!int 0x2a'}},
 @{id='tagged-int-octal';run={ConvertFrom-Yaml '!!int 0o52'}},
 @{id='tagged-bigint';run={ConvertFrom-Yaml '!!int 9223372036854775808'}},
 @{id='tagged-infinity';run={ConvertFrom-Yaml '!!float -.inf'}},
 @{id='tagged-timestamp';run={ConvertFrom-Yaml '!!timestamp 2020-01-02T03:04:05Z'}},
 @{id='invalid-tagged-bool';run={ConvertFrom-Yaml '!!bool not-bool'}},
 @{id='invalid-tagged-int';run={ConvertFrom-Yaml '!!int not-int'}},
 @{id='invalid-tagged-float';run={ConvertFrom-Yaml '!!float not-float'}},
 @{id='invalid-tagged-timestamp';run={ConvertFrom-Yaml '!!timestamp not-date'}},
 @{id='all-documents';run={ConvertFrom-Yaml "---`na: 1`n---`nb: true`n" -AllDocuments}},
 @{id='pipeline-yaml';run={@('name: pipeline','count: 42')|ConvertFrom-Yaml}},

 @{id='parse-map';run={ConvertFrom-Yaml "name: example`ncount: 3`nactive: true`nempty: null`n"}},
 @{id='parse-ordered';run={ConvertFrom-Yaml "z: last`na: first`n" -Ordered}},
 @{id='parse-sequence';run={ConvertFrom-Yaml "- one`n- two`n- three`n"}},
 @{id='parse-nested';run={ConvertFrom-Yaml "outer:`n  children:`n    - name: alpha`n      value: 7`n    - name: beta`n      value: 8`n"}},
 @{id='parse-alias';run={ConvertFrom-Yaml "first: &v [one, two]`nsecond: *v`n"}},
 @{id='parse-multiple';run={ConvertFrom-Yaml "---`na: one`n---`nb: two`n"}},
 @{id='parse-invalid';run={ConvertFrom-Yaml 'bad: [unfinished'}},
 @{id='serialize-map';run={ConvertTo-Yaml ([ordered]@{name='example';count=3;active=$true;empty=$null})}},
 @{id='serialize-json';run={ConvertTo-Yaml ([ordered]@{name='example';count=3}) -JsonCompatible}},
 @{id='serialize-flags';run={ConvertTo-Yaml ([ordered]@{name='example';empty=$null;values=@('one','two')}) -Options 'DisableAliases, OmitNullValues, WithIndentedSequences'}},
 @{id='serialize-flow';run={ConvertTo-Yaml ([ordered]@{name='example';values=@('one','two')}) -UseFlowStyle}},
 @{id='serialize-pipeline';run={@([ordered]@{id=1},[ordered]@{id=2})|ConvertTo-Yaml -KeepArray}},
 @{id='invalid-option';run={ConvertTo-Yaml 'example' -Options 'Missing'}},
 @{id='outfile';run={ConvertTo-Yaml ([ordered]@{name='owned'}) -OutFile (Join-Path $DataRoot 'output.yaml'); [IO.File]::ReadAllText((Join-Path $DataRoot 'output.yaml'))}},
 @{id='outfile-exists';run={ConvertTo-Yaml 'second' -OutFile (Join-Path $DataRoot 'output.yaml')}},
 @{id='outfile-force';run={ConvertTo-Yaml ([ordered]@{name='replacement'}) -OutFile (Join-Path $DataRoot 'output.yaml') -Force; [IO.File]::ReadAllText((Join-Path $DataRoot 'output.yaml'))}}
)
$cases += @(
 @{id='public-contract';run={$script:YamlPublicContract}},
 @{id='alias-cfy';run={cfy 'answer: 42'}},
 @{id='alias-cty';run={cty ([ordered]@{answer=42})}},
 @{id='parse-no-input';run={ConvertFrom-Yaml}},
 @{id='parse-empty';run={ConvertFrom-Yaml ''}},
 @{id='parse-comments';run={ConvertFrom-Yaml '# comment only'}},
 @{id='parse-empty-sequence';run={ConvertFrom-Yaml '[]'}},
 @{id='parse-empty-map';run={ConvertFrom-Yaml '{}'}},
 @{id='parse-ordered-nested';run={$value=ConvertFrom-Yaml "z: first`na:`n  y: 2`n  b: 1`n" -Ordered;[ordered]@{value=$value;outerKeys=@($value.Keys);innerKeys=@($value.a.Keys)}}},
 @{id='parse-merge-enabled';run={ConvertFrom-Yaml "default: &base`n  a: 1`nitem:`n  <<: *base`n  b: 2`n" -UseMergingParser}},
 @{id='parse-merge-disabled';run={ConvertFrom-Yaml "default: &base`n  a: 1`nitem:`n  <<: *base`n  b: 2`n"}},
 @{id='parse-merge-duplicate';run={ConvertFrom-Yaml "default: &base`n  a: 1`nitem:`n  <<: *base`n  a: 2`n" -UseMergingParser}},
 @{id='parse-ordered-all-documents';run={ConvertFrom-Yaml "---`nz: 1`na: 2`n---`ny: 3`nb: 4`n" -Ordered -AllDocuments}},
 @{id='scalar-zero';run={ConvertFrom-Yaml '0'}},
 @{id='scalar-int64-min';run={ConvertFrom-Yaml '-9223372036854775808'}},
 @{id='scalar-double';run={ConvertFrom-Yaml '1e100'}},
 @{id='scalar-null-spellings';run={foreach($text in @('~','Null','NULL')){[ordered]@{text=$text;value=(ConvertFrom-Yaml $text)}}}},
 @{id='scalar-single-quoted';run={ConvertFrom-Yaml "'001'"}},
 @{id='tagged-null';run={ConvertFrom-Yaml '!!null null'}},
 @{id='tagged-int-short';run={ConvertFrom-Yaml '!!int 42'}},
 @{id='tagged-float-decimal';run={ConvertFrom-Yaml '!!float 12.50'}},
 @{id='tagged-positive-infinity';run={ConvertFrom-Yaml '!!float +.Inf'}},
 @{id='serialize-no-input';run={ConvertTo-Yaml}},
 @{id='serialize-null';run={ConvertTo-Yaml -Data $null}},
 @{id='serialize-empty-array';run={ConvertTo-Yaml -Data @()}},
 @{id='serialize-array-argument';run={ConvertTo-Yaml -Data @(1,'two',$null)}},
 @{id='serialize-single-pipeline';run={@([ordered]@{id=1})|ConvertTo-Yaml}},
 @{id='serialize-single-keep-array';run={@([ordered]@{id=1})|ConvertTo-Yaml -KeepArray}},
 @{id='serialize-hashtable';run={$value=@{alpha=1;beta=@('two','three')};$inputKeys=@($value.Keys);$text=ConvertTo-Yaml $value;$ordered=ConvertFrom-Yaml $text -Ordered;$semantic=ConvertFrom-Yaml $text;if($semantic.alpha -isnot [int] -or $semantic.alpha -ne 1 -or $semantic.beta -isnot [Collections.IList] -or $semantic.beta.Count -ne 2 -or $semantic.beta[0] -cne 'two' -or $semantic.beta[1] -cne 'three'){throw 'Hashtable YAML semantic contract failed.'};$orderMatches=[string]::Join('|',@($value.Keys)) -ceq [string]::Join('|',@($ordered.Keys));$global:YamlRawCaseDiagnostics['serialize-hashtable']=[ordered]@{rawYaml=$text;inputKeys=$inputKeys;normalizedInputKeys=@($value.Keys);outputKeys=@($ordered.Keys)};if(-not $orderMatches){throw 'Serialized Hashtable order differs from its own normalized input enumeration.'};[ordered]@{inputType=$value.GetType().FullName;yamlType=$text.GetType().FullName;semanticMapping=$semantic;serializedOrderFollowsNormalizedInput=$orderMatches}}},
 @{id='serialize-generic-dictionary';run={$value=New-Object 'Collections.Generic.Dictionary[string,object]';$value.Add('alpha',1);$value.Add('beta',[pscustomobject]@{child=2});$inputKeys=@($value.Keys);$normalized=& $script:ObservedModule {param($data) Convert-PSObjectToGenericObject $data} $value;$text=ConvertTo-Yaml $value;$ordered=ConvertFrom-Yaml $text -Ordered;$semantic=ConvertFrom-Yaml $text;if($semantic.alpha -isnot [int] -or $semantic.alpha -ne 1 -or $semantic.beta.child -isnot [int] -or $semantic.beta.child -ne 2){throw 'Generic dictionary YAML semantic contract failed.'};$orderMatches=[string]::Join('|',@($normalized.Keys)) -ceq [string]::Join('|',@($ordered.Keys));$global:YamlRawCaseDiagnostics['serialize-generic-dictionary']=[ordered]@{rawYaml=$text;inputKeys=$inputKeys;normalizedInputKeys=@($normalized.Keys);outputKeys=@($ordered.Keys)};if(-not $orderMatches){throw 'Serialized dictionary order differs from its own normalized input enumeration.'};[ordered]@{inputType=$value.GetType().FullName;normalizedType=$normalized.GetType().FullName;yamlType=$text.GetType().FullName;semanticMapping=$semantic;serializedOrderFollowsNormalizedInput=$orderMatches}}},
 @{id='serialize-nested-ordered';run={ConvertTo-Yaml ([ordered]@{z=[ordered]@{y=2;b=1};a=3})}},
 @{id='serialize-nested-pscustomobject';run={ConvertTo-Yaml ([pscustomobject][ordered]@{z=[pscustomobject]@{child=2};a=@([pscustomobject]@{leaf=3})})}},
 @{id='serialize-quoted-primitives';run={ConvertTo-Yaml ([ordered]@{a='001';b='12.50';c='true';d='false';e='null';f='~';g=''})}},
 @{id='serialize-input-mutation';run={$value=[ordered]@{list=@([pscustomobject]@{leaf=2});hash=@{nested=@(1,2)}};$before=[ordered]@{listType=$value.list.GetType().FullName;hashChildType=$value.hash.nested.GetType().FullName};$text=ConvertTo-Yaml $value;[ordered]@{before=$before;text=$text;afterListType=$value.list.GetType().FullName;afterHashChildType=$value.hash.nested.GetType().FullName;after=$value}}},
 @{id='serialize-conflicting-parameter-set';run={ConvertTo-Yaml 'value' -Options Roundtrip -JsonCompatible}},
 @{id='outfile-missing-parent';run={ConvertTo-Yaml 'value' -OutFile (Join-Path $DataRoot 'missing-parent/output.yaml')}},
 @{id='outfile-empty-input';run={$path=Join-Path $DataRoot 'empty-input.yaml';ConvertTo-Yaml -Data $null -OutFile $path;[ordered]@{exists=(Test-Path -LiteralPath $path)}}},
 @{id='serialize-error-recovery';run={ConvertTo-Yaml ([ordered]@{good='after-error'})}},
 @{id='serialize-getter-error';run={$value=[pscustomobject]@{ok=1};$value|Add-Member ScriptProperty Broken {throw 'owned getter failure'};ConvertTo-Yaml $value}},
 @{id='outfile-getter-error-close';run={$path=Join-Path $DataRoot 'getter-error.yaml';$value=[pscustomobject]@{ok=1};$value|Add-Member ScriptProperty Broken {throw 'owned getter failure'};ConvertTo-Yaml $value -OutFile $path;$stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);try{[ordered]@{writerReleased=$true;bytes=$stream.Length}}finally{$stream.Dispose()}}},
 @{id='private-load-file';run={& $script:ObservedModule {$subdir=if($PSVersionTable.PSEdition -eq 'Core'){'netstandard2.1'}else{'net47'};Invoke-LoadFile -assemblyPath (Join-Path (Join-Path $here 'lib') $subdir)}}},
 @{id='private-load-assembly';run={& $script:ObservedModule {Invoke-LoadAssembly}}},
 @{id='private-documents';run={& $script:ObservedModule {$docs=@(Get-YamlDocuments 'a: 1');[ordered]@{count=$docs.Count;nodeType=$docs[0].RootNode.GetType().FullName}}}},
 @{id='private-scalar';run={& $script:ObservedModule {$docs=@(Get-YamlDocuments '42');Convert-ValueToProperType $docs[0].RootNode}}},
 @{id='private-mapping';run={& $script:ObservedModule {$docs=@(Get-YamlDocuments "z: 1`na: 2");Convert-YamlMappingToHashtable $docs[0].RootNode -Ordered}}},
 @{id='private-sequence';run={& $script:ObservedModule {$docs=@(Get-YamlDocuments '[1,2]');Convert-YamlSequenceToArray $docs[0].RootNode}}},
 @{id='private-document-object';run={& $script:ObservedModule {$docs=@(Get-YamlDocuments 'a: 1');Convert-YamlDocumentToPSObject $docs[0].RootNode}}},
 @{id='private-hashtable';run={& $script:ObservedModule {Convert-HashtableToDictionary @{items=@(1,2)}}}},
 @{id='private-ordered';run={& $script:ObservedModule {Convert-OrderedHashtableToDictionary ([ordered]@{z=@(1,2);a=3})}}},
 @{id='private-list';run={& $script:ObservedModule {Convert-ListToGenericList @(@{a=1},[ordered]@{z=2})}}},
 @{id='private-object';run={& $script:ObservedModule {Convert-PSObjectToGenericObject ([ordered]@{nested=@(1,2)})}}},
 @{id='private-serializer';run={& $script:ObservedModule {$serializer=Get-Serializer -Options 'None';[ordered]@{type=$serializer.GetType().FullName;value=$serializer.Serialize([ordered]@{a=1})}}}},
 @{id='module-reimport';run={Remove-Module $script:ObservedModule.Name -Force;$script:ObservedModule=Import-Module $YamlModulePath -Force -PassThru -ErrorAction Stop;[ordered]@{commands=@($script:ObservedModule.ExportedCommands.Keys|Sort-Object);aliases=@($script:ObservedModule.ExportedAliases.Keys|Sort-Object);value=(ConvertFrom-Yaml 'after: reimport');serialized=(ConvertTo-Yaml ([ordered]@{after='reimport'}))}}},
 @{id='module-dependencies';run={$root=$script:ObservedModule.ModuleBase;@($script:ObservedModule.Invoke({$yamlDotNetAssembly;$stringQuotedAssembly})|ForEach-Object {$path=$_.Location;$underRoot=[IO.Path]::GetFullPath($path).StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase);if(-not $underRoot){throw 'YAML dependency loaded outside observed module root.'};[ordered]@{name=[IO.Path]::GetFileName($path);identity=$_.FullName;sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant();underModuleRoot=$underRoot}})}},
 @{id='file-effects';run={@(Get-ChildItem -LiteralPath $DataRoot -File -Recurse|Sort-Object FullName|ForEach-Object {[ordered]@{relativePath=$_.FullName.Substring($DataRoot.TrimEnd('\','/').Length).TrimStart('\','/').Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant();contentBase64=[Convert]::ToBase64String([IO.File]::ReadAllBytes($_.FullName))}})}}
)
foreach($option in @('None','Roundtrip','DisableAliases','EmitDefaults','JsonCompatible','DefaultToStaticType','WithIndentedSequences','OmitNullValues','UseFlowStyle','UseSequenceFlowStyle')) {
    $cases += @{id=('serialize-option-'+$option);run=([scriptblock]::Create("ConvertTo-Yaml ([ordered]@{z='value';a=`$null;items=@('one','two')}) -Options '$option'"))}
}

$initialState = [Management.Automation.Runspaces.InitialSessionState]::CreateDefault()
$windowsValidation = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
if ($windowsValidation) {
    # Unsigned task artifacts run only inside this owned validation runspace.
    # No CurrentUser/LocalMachine policy or certificate state is changed.
    $initialState.ExecutionPolicy = [Microsoft.PowerShell.ExecutionPolicy]::Bypass
}
$runspace = [Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace($initialState)
$runspace.Open()
$ps = [Management.Automation.PowerShell]::Create()
$ps.Runspace = $runspace
try {
    $runspace.SessionStateProxy.SetVariable('YamlModulePath',$ModulePath)
    $runspace.SessionStateProxy.SetVariable('DataRoot',$DataRoot)
    if($PreloadYamlAssemblyPath) {
        $runspace.SessionStateProxy.SetVariable('YamlPreloadPath',$PreloadYamlAssemblyPath)
        $null=$ps.AddScript('Add-Type -Path $YamlPreloadPath -ErrorAction Stop').Invoke()
        if($ps.HadErrors){throw 'Requested preloaded YAML fixture failed.'}
        $ps.Commands.Clear();$ps.Streams.Error.Clear()
    }
    $null=$ps.AddScript('$ErrorActionPreference="Stop";$global:YamlRawCaseDiagnostics=@{};$script:ObservedModule=Import-Module $YamlModulePath -Force -PassThru -ErrorAction Stop').Invoke()
    if($ps.HadErrors){throw 'Whole original/generated module import failed.'}
    $ps.Commands.Clear()
    $metadataScript=@'
$enumType=(Get-Command ConvertTo-Yaml).Parameters['Options'].ParameterType
$script:YamlPublicContract=[ordered]@{
 name=$script:ObservedModule.Name
 version=$script:ObservedModule.Version.ToString()
 exports=@($script:ObservedModule.ExportedCommands.Keys|Sort-Object)
 aliases=@($script:ObservedModule.ExportedAliases.Keys|Sort-Object|ForEach-Object {[ordered]@{name=$_;target=$script:ObservedModule.ExportedAliases[$_].Definition}})
 options=[ordered]@{type=$enumType.FullName;names=[Enum]::GetNames($enumType);values=@([Enum]::GetValues($enumType)|ForEach-Object {[int]$_});flags=$enumType.IsDefined([FlagsAttribute],$false)}
 commands=@(foreach($name in @('ConvertFrom-Yaml','ConvertTo-Yaml')){
  $command=Get-Command $name
  [ordered]@{
   name=$name
   parameterSets=@($command.ParameterSets|ForEach-Object {[ordered]@{name=$_.Name;isDefault=$_.IsDefault;parameters=@($_.Parameters|Sort-Object Name|Select-Object Name,IsMandatory,Position,ValueFromPipeline,ValueFromPipelineByPropertyName)}})
   parameters=@($command.Parameters.Keys|Sort-Object|ForEach-Object {[ordered]@{name=$_;type=$command.Parameters[$_].ParameterType.FullName}})
  }
 })
}
$script:YamlPublicContract
'@
    $contract=@($ps.AddScript($metadataScript).Invoke())
    if($ps.HadErrors -or $contract.Count -ne 1){throw 'Public module metadata observation failed.'}
    $ps.Commands.Clear()
    $rawDiagnostics=New-Object 'Collections.Generic.List[object]'
    $observations=@(foreach($case in $cases){
        foreach($stream in @($ps.Streams.Error,$ps.Streams.Warning,$ps.Streams.Verbose,$ps.Streams.Debug,$ps.Streams.Information,$ps.Streams.Progress)){$stream.Clear()}
        $caught=$null
        try{$raw=@($ps.AddScript($case.run.ToString()).Invoke())}catch{$raw=@();$caught=Normalize-YamlObservation $_}
        $records=@(foreach($record in $raw){Normalize-YamlObservation $record.PSObject.BaseObject})
        $rawDiagnostics.Add([ordered]@{scenarioId=$case.id;successRecords=@(foreach($record in $raw){Normalize-YamlObservation $record.PSObject.BaseObject -PreserveUnorderedEnumeration})})
        $callNames=@($case.run.Ast.FindAll({param($node)$node -is [Management.Automation.Language.CommandAst]},$true)|ForEach-Object {$_.GetCommandName()}|Where-Object {$_}|Sort-Object -Unique)
        $exports=@(@(foreach($call in $callNames){if($call -eq 'cfy'){'ConvertFrom-Yaml'}elseif($call -eq 'cty'){'ConvertTo-Yaml'}elseif($call -in @('ConvertFrom-Yaml','ConvertTo-Yaml')){$call}})|Sort-Object -Unique)
        $mustSucceed=$case.id.StartsWith('private-') -or $case.id -in @('module-reimport','module-dependencies','file-effects','public-contract','serialize-hashtable','serialize-generic-dictionary')
        $observed= -not $mustSucceed -or ($null -eq $caught -and $ps.Streams.Error.Count -eq 0 -and $ps.InvocationStateInfo.State -eq 'Completed')
        [ordered]@{scenarioId=$case.id;succeeded=$observed;executedExports=$exports;executedOwnedFunctions=@($callNames|Where-Object {$_ -in $requiredHelpers}|ForEach-Object {[ordered]@{relativePath='powershell-yaml.psm1';name=$_}});observation=[ordered]@{successRecords=$records;errorRecords=@(foreach($errorRecord in $ps.Streams.Error){Normalize-YamlObservation $errorRecord});warnings=@($ps.Streams.Warning|ForEach-Object Message);verbose=@($ps.Streams.Verbose|ForEach-Object Message);debug=@($ps.Streams.Debug|ForEach-Object Message);information=@($ps.Streams.Information|ForEach-Object {Normalize-YamlObservation $_.MessageData});caught=$caught;hadErrors=$ps.HadErrors;invocationState=[string]$ps.InvocationStateInfo.State}}
        $ps.Commands.Clear()
    })
    $dependencies=@($ps.AddScript('$script:ObservedModule.Invoke({$yamlDotNetAssembly.Location;$stringQuotedAssembly.Location})').Invoke()|ForEach-Object {$path=[string]$_.PSObject.BaseObject;[ordered]@{name=[IO.Path]::GetFileName($path);identity=[Reflection.AssemblyName]::GetAssemblyName($path).FullName;sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant();underModuleRoot=([IO.Path]::GetFullPath($path).StartsWith($moduleRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))}})
    $files=@(Get-ChildItem -LiteralPath $DataRoot -File -Recurse|ForEach-Object {[ordered]@{relativePath=$_.FullName.Substring($DataRoot.TrimEnd('\','/').Length).TrimStart('\','/').Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant();contentBase64=[Convert]::ToBase64String([IO.File]::ReadAllBytes($_.FullName))}})
    $publicContract=$contract[0].PSObject.BaseObject
    $runtime=if($PSVersionTable.PSEdition -eq 'Core'){[Runtime.InteropServices.RuntimeInformation]::FrameworkDescription}else{'CLR '+[string]$PSVersionTable.CLRVersion}
    [ordered]@{schemaVersion=1;surface=$Surface;importPath=[IO.Path]::GetFullPath($ImportPath);actualExports=@($publicContract.exports|Where-Object {$_ -notin @($publicContract.aliases|ForEach-Object name)});actualAliases=@($publicContract.aliases|ForEach-Object {[ordered]@{name=$_.name;definition=$_.target}});scenarios=$observations;rawDiagnostics=@($rawDiagnostics.ToArray());rawSerializerDiagnostics=$runspace.SessionStateProxy.GetVariable('YamlRawCaseDiagnostics');comparisonPolicy='Only actual unorderedHashtable/string-key contents are canonical maps after reproduced Original/Original variation. Two unordered serialization scenarios compare typed semantic YAML and normalized-input/output enumeration relationship; raw YAML/orders remain diagnostics. OrderedDictionary, custom-object property order, sequences, explicit ordered serialization and file bytes remain exact.';host=[string]$PSVersionTable.PSVersion;runtime=$runtime;processBits=([IntPtr]::Size*8);moduleContract=@($contract|ForEach-Object {Normalize-YamlObservation $_.PSObject.BaseObject});caseCount=$cases.Count;dependencies=$dependencies;fileEffects=$files;preloadFixture=$(if($PreloadYamlAssemblyPath){[ordered]@{sha256=(Get-FileHash -LiteralPath $PreloadYamlAssemblyPath).Hash.ToLowerInvariant();identity=[Reflection.AssemblyName]::GetAssemblyName($PreloadYamlAssemblyPath).FullName}}else{$null});limits='succeeded means observation completed, including expected native errors. Fixed unconditional call sites record public/direct-private invocations. Common harness verifies complete14-method ledger and existing callback-shim guard; counts alone do not prove execution. No branch-count, timing, runtime-free or universal loader-fault claim.'}|ConvertTo-Json -Depth 60|Set-Content -LiteralPath $OutputPath -Encoding utf8
} finally {$ps.Dispose();$runspace.Close();$runspace.Dispose()}
