param([string]$ModulePath,[string]$DataRoot,[string]$OutputPath)
$ErrorActionPreference='Stop'
Import-Module $ModulePath -Force
$m=Get-Module|Where-Object {$_.Path -eq $ModulePath -or $_.Name -eq [IO.Path]::GetFileNameWithoutExtension($ModulePath)}|Select-Object -First 1
function Normalize($v){
 if($null -eq $v){return $null}
 if($v -is [Management.Automation.ErrorRecord]){return [ordered]@{error=$v.FullyQualifiedErrorId;exception=$v.Exception.GetType().FullName;category=[string]$v.CategoryInfo.Category;message=$v.Exception.Message.Replace($DataRoot,'<data>')}}
 if($v -is [Collections.IDictionary]){return [ordered]@{type=$v.GetType().FullName;entries=@($v.Keys|Sort-Object|ForEach-Object {[ordered]@{key=[string]$_;value=(Normalize $v[$_])}})}}
 if($v -is [Collections.IList]){return [ordered]@{type=$v.GetType().FullName;values=@($v|ForEach-Object {Normalize $_})}}
 if($v -is [datetime]){return [ordered]@{type=$v.GetType().FullName;value=$v.ToString('o',[Globalization.CultureInfo]::InvariantCulture)}}
 if($v -is [string]){return $v}
 return [ordered]@{type=$v.GetType().FullName;value=[Convert]::ToString($v,[Globalization.CultureInfo]::InvariantCulture)}
}
$cases=@(
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
$observations=@(foreach($case in $cases){$caught=$null;try{$records=@(& $case.run 2>&1|ForEach-Object {Normalize $_})}catch{$records=@();$caught=Normalize $_};[ordered]@{id=$case.id;records=$records;caught=$caught}})
$locations=@($m.Invoke({$yamlDotNetAssembly.Location;$stringQuotedAssembly.Location}))
$moduleRoot=[IO.Path]::GetDirectoryName($m.Path)+[IO.Path]::DirectorySeparatorChar
$dependencies=@($locations|ForEach-Object {[ordered]@{name=[IO.Path]::GetFileName($_);identity=[Reflection.AssemblyName]::GetAssemblyName($_).FullName;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant();underModuleRoot=([IO.Path]::GetFullPath($_).StartsWith($moduleRoot,[StringComparison]::OrdinalIgnoreCase))}})
$enumType=(Get-Command ConvertTo-Yaml).Parameters['Options'].ParameterType
[ordered]@{host=[string]$PSVersionTable.PSVersion;observations=$observations;dependencies=$dependencies;options=[ordered]@{type=$enumType.FullName;names=[Enum]::GetNames($enumType);values=@([Enum]::GetValues($enumType)|ForEach-Object {[int]$_});flags=$enumType.IsDefined([FlagsAttribute],$false)}}|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $OutputPath -Encoding utf8
