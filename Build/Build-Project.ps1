param(
    [string] $ConfigPath,
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [switch] $ModuleNoDotnetBuild,
    [string] $ModuleVersion,
    [string] $ModulePreReleaseTag,
    [switch] $ModuleNoSign,
    [switch] $ModuleSignModule,
    [switch] $Plan,
    [switch] $Validate,
    [switch] $PackagesOnly,
    [switch] $ToolsOnly,
    [switch] $PublishNuget,
    [switch] $PublishGitHub,
    [switch] $PublishToolGitHub,
    [string[]] $Target,
    [string[]] $Runtime,
    [string[]] $Framework,
    [ValidateSet('SingleContained', 'SingleFx', 'Portable', 'Fx')]
    [string[]] $Flavor
)

if (-not $PSBoundParameters.ContainsKey('ConfigPath') -or [string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $PSScriptRoot 'release.json'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$moduleProject = Join-Path $repoRoot 'PSPublishModule\PSPublishModule.csproj'
$moduleFramework = if ($PSEdition -eq 'Desktop') { 'net472' } else { 'net8.0' }
$moduleBinary = Join-Path $repoRoot ("PSPublishModule\bin\{0}\{1}\PSPublishModule.dll" -f $Configuration, $moduleFramework)

Write-Verbose "Building PSPublishModule cmdlets ($moduleFramework, $Configuration)."
$buildOutput = & dotnet build $moduleProject -c $Configuration -f $moduleFramework --nologo --verbosity quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    $buildOutput | Out-Host
    exit $LASTEXITCODE
}

if (-not (Test-Path -LiteralPath $moduleBinary)) {
    throw "Local PSPublishModule build output not found: $moduleBinary"
}

Get-Module -Name 'PSPublishModule' -All -ErrorAction SilentlyContinue | Remove-Module -Force -ErrorAction SilentlyContinue
Import-Module $moduleBinary -Force -ErrorAction Stop -Verbose:$false

$invokeParams = @{
    ConfigPath = $ConfigPath
    Configuration = $Configuration
    ErrorAction = 'Stop'
}
if ($Plan) { $invokeParams.Plan = $true }
if ($Validate) { $invokeParams.Validate = $true }
if ($ModuleNoDotnetBuild) { $invokeParams.ModuleNoDotnetBuild = $true }
if ($PSBoundParameters.ContainsKey('ModuleVersion')) { $invokeParams.ModuleVersion = $ModuleVersion }
if ($PSBoundParameters.ContainsKey('ModulePreReleaseTag')) { $invokeParams.ModulePreReleaseTag = $ModulePreReleaseTag }
if ($PSBoundParameters.ContainsKey('ModuleNoSign')) { $invokeParams.ModuleNoSign = $ModuleNoSign.IsPresent }
if ($PSBoundParameters.ContainsKey('ModuleSignModule')) { $invokeParams.ModuleSignModule = $ModuleSignModule.IsPresent }
if ($PackagesOnly) { $invokeParams.PackagesOnly = $true }
if ($ToolsOnly) { $invokeParams.ToolsOnly = $true }
if ($PublishNuget) { $invokeParams.PublishNuget = $true }
if ($PublishGitHub) { $invokeParams.PublishProjectGitHub = $true }
if ($PublishToolGitHub) { $invokeParams.PublishToolGitHub = $true }
if ($Target) { $invokeParams.Target = $Target }
if ($Runtime) { $invokeParams.Runtimes = $Runtime }
if ($Framework) { $invokeParams.Frameworks = $Framework }
if ($Flavor) { $invokeParams.Flavors = $Flavor }
if ($VerbosePreference -ne 'SilentlyContinue') { $invokeParams.Verbose = $true }

try {
    Invoke-PowerForgeRelease @invokeParams
} catch {
    Write-Error $_
    exit 1
}
