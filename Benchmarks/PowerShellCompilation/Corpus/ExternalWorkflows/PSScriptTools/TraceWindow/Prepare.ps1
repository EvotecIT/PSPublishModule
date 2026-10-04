param([Parameter(Mandatory)][string]$SourceRoot,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$source=Join-Path $SourceRoot 'functions/Trace.ps1'
if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne '90073b0a58a6ca86bc3bbb569f77f3561f98496ade65bc49d47bfb3035bce110'){
 throw 'Pinned Trace source mismatch'
}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new task-owned output directory'}
$tokens=$null;$errors=$null
[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)|Out-Null
if($errors.Count){throw 'Pinned Trace source parse failed'}
$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$folder=(New-Item -ItemType Directory -Path (Join-Path $root 'functions')).FullName
Copy-Item -LiteralPath $source -Destination (Join-Path $folder 'Trace.ps1')
$payload=@'
$script:CapturedCleanup = [Collections.Generic.List[object]]::new()
function Get-Date { [datetime]'2026-09-28T12:00:00' }
function Get-CimInstance {
    [CmdletBinding()]
    param([string]$ClassName,[string[]]$Property)
    if ($ClassName -ne 'Win32_OperatingSystem') { throw 'Only owned OS metadata is accepted' }
    [pscustomobject]@{ Caption='Owned OS'; Version='1'; OSArchitecture='x64' }
}
function New-RunspaceCleanupJob {
    [CmdletBinding()]
    param($Handle,$PowerShell,[int]$Sleep,[switch]$PassThru)
    $script:CapturedCleanup.Add([pscustomobject]@{ Handle=$Handle; PowerShell=$PowerShell; Runspace=$PowerShell.Runspace; Sleep=$Sleep })
    if ($PassThru) { [pscustomobject]@{ Captured=$true } }
}
function Get-CapturedCleanup { $script:CapturedCleanup }
. "$PSScriptRoot/functions/Trace.ps1"
'@
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psm1'),$payload,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $root 'Fixture.psd1'),"@{RootModule='Fixture.psm1';ModuleVersion='1.0.0';FunctionsToExport=@('Trace-Message','Get-CapturedCleanup')}",[Text.UTF8Encoding]::new($false))
Join-Path $root 'Fixture.psd1'
