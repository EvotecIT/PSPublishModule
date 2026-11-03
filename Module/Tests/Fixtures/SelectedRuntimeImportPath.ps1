param([Parameter(Mandatory)][string] $ModuleDll, [Parameter(Mandatory)][string] $Root)
$ErrorActionPreference = 'Stop'
Import-Module $ModuleDll -Force -WarningAction SilentlyContinue -ErrorAction Stop
$suffix = [guid]::NewGuid().ToString('N')
$mutator = 'Selected.Mutator.' + $suffix
$follower = 'Selected.Follower.' + $suffix
$otherRoot = Join-Path $Root 'other'
$null = New-Item -ItemType Directory -Path $otherRoot
foreach ($name in @($mutator, $follower)) {
    $moduleRoot = Join-Path $Root $name
    $null = New-Item -ItemType Directory -Path $moduleRoot
    $body = if ($name -eq $mutator) { "`$env:PSModulePath = '$($otherRoot.Replace("'", "''"))'" } else { 'Export-ModuleMember' }
    Set-Content -LiteralPath (Join-Path $moduleRoot ($name + '.psm1')) -Value $body
    Set-Content -LiteralPath (Join-Path $moduleRoot ($name + '.psd1')) -Value "@{ RootModule='$name.psm1'; ModuleVersion='1.0.0'; GUID='$([guid]::NewGuid())'; FunctionsToExport=@(); CmdletsToExport=@() }"
}
$type = [PowerForge.MissingFunctionsOptions].Assembly.GetType('PowerForge.EmbeddedScripts')
$loader = $type.GetMethod('Load', [Reflection.BindingFlags] 'Static,NonPublic')
$scriptPath = Join-Path $Root 'Import-Modules.ps1'
Set-Content -LiteralPath $scriptPath -Value ($loader.Invoke($null, @('Scripts/ModulePipeline/Import-Modules.ps1')))
$json = @(@{ Name = $mutator; RequiredVersion = '1.0.0' }, @{ Name = $follower; RequiredVersion = '1.0.0' }) | ConvertTo-Json -Compress
$modules = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
$searchPath = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Root))
& $scriptPath -ModulesB64 $modules -ImportRequired 1 -ImportSelf 0 -ModuleSearchPathB64 $searchPath
