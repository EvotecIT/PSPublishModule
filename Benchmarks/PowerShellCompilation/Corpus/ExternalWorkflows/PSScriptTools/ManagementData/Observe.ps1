param([string]$ModulePath,[string]$OutputPath)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$module=Import-Module $ModulePath -Force -PassThru
$rows=[Collections.Generic.List[object]]::new()
$sessions=[Collections.Generic.List[Microsoft.Management.Infrastructure.CimSession]]::new()
$instances=[Collections.Generic.List[Microsoft.Management.Infrastructure.CimInstance]]::new()
try {
 foreach($name in 'first','second') {$sessions.Add([Microsoft.Management.Infrastructure.CimSession]::Create("$name.compiler-offline.invalid"))}
 foreach($name in 'OfflineAlpha','OfflineBeta','OtherClass') {$instances.Add((New-CimInstance -ClassName $name -Namespace root/offline -Property @{Value=7} -ClientOnly))}
 $class=$instances[0].CimClass
 $map=[Collections.Generic.Dictionary[string,Microsoft.Management.Infrastructure.CimSession]]::new();$map.Add('one',$sessions[0])
 foreach($case in 'single','array','pipeline','map') {
  $result=switch($case){single {@(Read-BorrowedCimSession $sessions[0])};array {@(Read-BorrowedCimSessions -Sessions $sessions.ToArray())};pipeline {@($sessions.ToArray()|Read-BorrowedCimSessions)};map {@(Read-BorrowedCimSessionMap $map)}}
  $rows.Add([ordered]@{case=$case;records=@($result|ForEach-Object {[ordered]@{computer=$_.ComputerName;type=$_.GetType().FullName;borrowed=([object]::ReferenceEquals($_,$sessions[0]) -or [object]::ReferenceEquals($_,$sessions[1]))}});callerName=$sessions[0].ComputerName})
 }
 $result=@(Read-BorrowedCimClass $class)
 $rows.Add([ordered]@{case='class';identity=[object]::ReferenceEquals($result[0],$class);className=$result[0].CimClassName;callerClass=$class.CimClassName})
 foreach($name in 'Read-BorrowedCimSession','Read-BorrowedCimClass') {
  $caught=$null;try{& $name 'invalid'|Out-Null}catch{$caught=[ordered]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
  $rows.Add([ordered]@{case='invalid';command=$name;caught=$caught})
 }
 $rows.Add([ordered]@{case='common-parameters';values=@(Read-HostCommonParameters);sdkValues=@([System.Management.Automation.Cmdlet]::CommonParameters)})
 $owned=New-OfflineOwnedCimSession;$sessions.Add($owned)
 $rows.Add([ordered]@{case='owned-factory';computer=$owned.ComputerName;type=$owned.GetType().FullName})
 $rows.Add([ordered]@{case='class-type';name=(Read-OfflineCimType).FullName})
 $session=$sessions[0]
 $global:ManagementOfflineTrace=[Collections.Generic.List[string]]::new()
 $global:ManagementOfflineRows=@{'root/one'=@($instances[0].CimClass,$instances[2].CimClass);'root/two'=@($instances[1].CimClass)}
 $session|Add-Member ScriptMethod TestConnection {param([ref]$instance,[ref]$exception) $global:ManagementOfflineTrace.Add('test');$instance.Value=$null;$exception.Value=$null;$global:ManagementOfflineConnect} -Force
 $session|Add-Member ScriptMethod EnumerateClasses {param($namespace,$filter) $global:ManagementOfflineTrace.Add("enumerate:$namespace|$filter");if($namespace -eq 'root/failure'){throw 'offline enumeration failure'};$global:ManagementOfflineRows[$namespace]} -Force
 foreach($method in 'TestConnection','EnumerateClasses') {if($session.PSObject.Members[$method].MemberType -ne 'ScriptMethod'){throw "Offline method missing: $method"}}
 foreach($case in 'platform-refusal','connection-refusal','match','exclude','empty','partial-failure','pipeline') {
  $global:ManagementOfflineTrace.Clear();$global:ManagementOfflineConnect=$case -ne 'connection-refusal'
  & $module {param($windows,$trace,$namespaces) $script:OfflineWindows=$windows;$script:ProviderTrace=$trace;$script:OfflineNamespaces=$namespaces} ($case -ne 'platform-refusal') $global:ManagementOfflineTrace $(if($case -eq 'partial-failure'){@('root/one','root/failure','root/two')}else{@('root/one','root/two')})
  $arguments=@{ClassName=if($case -eq 'empty'){'Missing*'}else{'Offline*'}}
  if($case -eq 'exclude'){$arguments.Exclude='Beta'}
  $warnings=@();$caught=$null
  try {$result=@(if($case -eq 'pipeline'){$session|Find-CimClass @arguments -WarningVariable warnings -WarningAction SilentlyContinue}else{Find-CimClass @arguments -CimSession $session -WarningVariable warnings -WarningAction SilentlyContinue})} catch {$result=@();$caught=[ordered]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
  $rows.Add([ordered]@{case=$case;records=@($result|ForEach-Object {[ordered]@{type=$_.GetType().FullName;name=$_.CimClassName}});warnings=@($warnings|ForEach-Object {$_.Message});trace=@($global:ManagementOfflineTrace.ToArray());caught=$caught;callerSession=$session.ComputerName;callerClass=$class.CimClassName})
 }
 foreach($value in @($null,'')) {
  $caught=$null;try{Find-CimClass -ClassName $value -CimSession $session|Out-Null}catch{$caught=[ordered]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
  $rows.Add([ordered]@{case='invalid-class-name';input=$value;caught=$caught})
 }
 $command=Get-Command Find-CimClass
 $rows.Add([ordered]@{case='metadata';sessionType=$command.Parameters['CimSession'].ParameterType.FullName;aliases=@($command.Parameters['CimSession'].Aliases);output=@($command.OutputType|ForEach-Object {$_.Name});hostVersion=$PSVersionTable.PSVersion.ToString();sdkIdentities=@([System.Management.Automation.Cmdlet],[Microsoft.Management.Infrastructure.CimSession],[Microsoft.Management.Infrastructure.CimClass]|ForEach-Object {[ordered]@{type=$_.FullName;assembly=$_.Assembly.FullName}})})
} finally {
 foreach($instance in $instances){$instance.Dispose()}
 foreach($session in $sessions){$session.Dispose()}
 Remove-Variable ManagementOfflineTrace,ManagementOfflineRows,ManagementOfflineConnect -Scope Global -ErrorAction SilentlyContinue
}
$rows|ConvertTo-Json -Depth 14|Set-Content -LiteralPath $OutputPath -Encoding UTF8
