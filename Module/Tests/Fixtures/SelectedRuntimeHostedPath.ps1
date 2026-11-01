param([Parameter(Mandatory)][string] $ModuleDll, [Parameter(Mandatory)][string] $Root)
$ErrorActionPreference = 'Stop'
Import-Module $ModuleDll -Force -WarningAction SilentlyContinue -ErrorAction Stop
$name = 'Selected.Hosted.' + [guid]::NewGuid().ToString('N')
$moduleRoot = Join-Path $Root $name
$null = New-Item -ItemType Directory -Path $moduleRoot
Set-Content -LiteralPath (Join-Path $moduleRoot ($name + '.psd1')) -Value "@{ ModuleVersion='1.0.0'; GUID='$([guid]::NewGuid())'; FunctionsToExport=@(); CmdletsToExport=@() }"
$core = [PowerForge.MissingFunctionsOptions].Assembly
$surface = (Get-Command Get-MissingFunctions).ImplementingType.Assembly
$operationsType = $surface.GetType('PowerForge.PowerShellModulePipelineHostedOperations')
if (-not $operationsType) {
    $surface = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'PowerForge.PowerShell' }
    $operationsType = $surface.GetType('PowerForge.PowerShellModulePipelineHostedOperations')
}
$env:PSModulePath = $Root
$constructor = $operationsType.GetConstructors([Reflection.BindingFlags] 'Instance,NonPublic') | Where-Object { $_.GetParameters().Count -eq 2 }
$operations = $constructor.Invoke(@([PowerForge.PowerShellRunner]::new(), [PowerForge.ConsoleLogger]::new()))
$env:PSModulePath = Join-Path $Root 'unrelated'
$entryType = $core.GetType('PowerForge.ImportModuleEntry')
$entry = [Activator]::CreateInstance($entryType)
$entry.Name = $name
$entry.RequiredVersion = '1.0.0'
$entries = [Array]::CreateInstance($entryType, 1)
$entries.SetValue($entry, 0)
$targetType = $core.GetType('PowerForge.ModuleImportValidationTarget')
$target = [Activator]::CreateInstance($targetType, @('pwsh', 'Core', $true))
$targets = [Array]::CreateInstance($targetType, 1)
$targets.SetValue($target, 0)
$arguments = [object[]]::new(6)
$arguments[0] = ''
$arguments[1] = $entries
$arguments[2] = $true
$arguments[3] = $false
$arguments[4] = $false
$arguments[5] = $targets
$null = $operationsType.GetMethod('ValidateModuleImports').Invoke($operations, $arguments)
'Hosted import passed'
