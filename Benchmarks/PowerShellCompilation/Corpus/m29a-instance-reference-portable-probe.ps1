param([string]$ModulePath,[string]$FixtureRoot,[string]$OutputPath)
$ErrorActionPreference='Stop'
Import-Module $ModulePath -Force
$null=New-Item -ItemType Directory -Path $FixtureRoot
function Write-OwnedModule($root,$name,$requirements='@()') {
    $folder=Join-Path $root $name
    $null=New-Item -ItemType Directory -Path $folder
    "@{RootModule='$name.psm1';ModuleVersion='1.0.0';GUID='5c53d6f7-f4fb-4c5c-9197-cade20d3dc48';RequiredModules=$requirements;FunctionsToExport=@();CmdletsToExport=@();AliasesToExport=@()}" | Set-Content -LiteralPath (Join-Path $folder "$name.psd1") -Encoding utf8
    "`$global:OwnedPortableTrace.Add('$name')" | Set-Content -LiteralPath (Join-Path $folder "$name.psm1") -Encoding utf8
    return $folder
}
function Normalize($value) {
    if($value -is [Management.Automation.WarningRecord]){return [ordered]@{kind='warning';message=$value.Message.Replace($FixtureRoot,'<owned>')}}
    if($value -is [Management.Automation.ErrorRecord]){return [ordered]@{kind='error';id=$value.FullyQualifiedErrorId;type=$value.Exception.GetType().FullName;message=$value.Exception.Message.Replace($FixtureRoot,'<owned>');line=$value.InvocationInfo.ScriptLineNumber;column=$value.InvocationInfo.OffsetInLine}}
    return [ordered]@{kind='value';type=$value.GetType().FullName;value=[string]$value}
}
$observations=@(foreach($case in 'missing-name','missing-flags','environment-provider','missing-drive','missing-module','valid','version-conflict','cycle','malformed','repeat-alias'){
    $global:OwnedPortableTrace=[Collections.Generic.List[string]]::new()
    $root=Join-Path $FixtureRoot $case
    $null=New-Item -ItemType Directory -Path $root
    $arguments=@{Name='OwnedPortableRoot';Path=$root;Import=$true;WarningAction='Continue'}
    switch($case){
        'missing-name' {$arguments.Name=''}
        'missing-flags' {$arguments.Import=$false}
        'environment-provider' {$arguments.Path='Env:PATH'}
        'missing-drive' {$arguments.Path='MissingOwnedDrive:/path'}
        'valid' {$null=Write-OwnedModule $root 'OwnedPortableDependency';$null=Write-OwnedModule $root 'OwnedPortableRoot' "@('OwnedPortableDependency')"}
        'repeat-alias' {$null=Write-OwnedModule $root 'OwnedPortableDependency';$null=Write-OwnedModule $root 'OwnedPortableRoot' "@('OwnedPortableDependency')";$arguments.Remove('Name');$arguments.ModuleName='OwnedPortableRoot'}
        'version-conflict' {$null=Write-OwnedModule $root 'OwnedPortableDependency';$null=Write-OwnedModule $root 'OwnedPortableRoot' "@(@{ModuleName='OwnedPortableDependency';ModuleVersion='2.0.0'})"}
        'cycle' {$null=Write-OwnedModule $root 'OwnedPortableDependency' "@('OwnedPortableRoot')";$null=Write-OwnedModule $root 'OwnedPortableRoot' "@('OwnedPortableDependency')"}
        'malformed' {$folder=Write-OwnedModule $root 'OwnedPortableRoot';"@{ModuleVersion='not-a-version';GUID='not-a-guid'}"|Set-Content -LiteralPath (Join-Path $folder 'OwnedPortableRoot.psd1') -Encoding utf8}
    }
    $caught=$null;$records=[Collections.Generic.List[object]]::new()
    try {
        Initialize-ModulePortable @arguments 3>&1 2>&1|ForEach-Object {$records.Add((Normalize $_))}
        if($case -eq 'repeat-alias'){Initialize-ModulePortable @arguments 3>&1 2>&1|ForEach-Object {$records.Add((Normalize $_))}}
    } catch {$caught=Normalize $_}
    [ordered]@{id=$case;records=@($records.ToArray());caught=$caught;loaded=@($global:OwnedPortableTrace.ToArray())}
    Get-Module OwnedPortableRoot,OwnedPortableDependency | Remove-Module -Force
})
[ordered]@{host=[string]$PSVersionTable.PSVersion;observations=$observations}|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $OutputPath -Encoding utf8
