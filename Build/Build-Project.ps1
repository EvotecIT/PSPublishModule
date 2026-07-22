param(
    [string] $ConfigPath,
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [switch] $ModuleNoDotnetBuild,
    [string] $ModuleVersion,
    [string] $ModulePreReleaseTag,
    [switch] $ModuleNoSign,
    [switch] $ModuleSignModule,
    [string] $CertificateThumbprint,
    [switch] $SignIncludeBinaries,
    [switch] $SignIncludeInternals,
    [switch] $SignIncludeExe,
    [string] $DiagnosticsBaselinePath,
    [switch] $GenerateDiagnosticsBaseline,
    [switch] $UpdateDiagnosticsBaseline,
    [switch] $FailOnNewDiagnostics,
    [ValidateSet('Warning', 'Error')]
    [string] $FailOnDiagnosticsSeverity,
    [switch] $Plan,
    [switch] $Validate,
    [switch] $Json,
    [switch] $PackagesOnly,
    [switch] $ModuleOnly,
    [switch] $ToolsOnly,
    [switch] $PublishNuget,
    [switch] $PublishGitHub,
    [switch] $PublishToolGitHub,
    [ValidateSet('auto', 'net10.0', 'net8.0')]
    [string] $ModuleFramework,
    [ValidateSet('Manifest', 'Documentation', 'Build', 'Publish')]
    [string] $ModuleRunMode,
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
$cmdletFramework = if ($PSEdition -eq 'Desktop') { 'net472' } else { 'net8.0' }
$moduleBinary = Join-Path $repoRoot ("PSPublishModule\bin\{0}\{1}\PSPublishModule.dll" -f $Configuration, $cmdletFramework)

Write-Verbose "Building PSPublishModule cmdlets ($cmdletFramework, $Configuration)."
$buildOutput = & dotnet build $moduleProject -c $Configuration -f $cmdletFramework --nologo --verbosity quiet 2>&1
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
if ($Json) {
    $invokeParams.NoInteractive = $true
    $invokeParams.WarningAction = 'SilentlyContinue'
}
if ($Plan) { $invokeParams.Plan = $true }
if ($Validate) { $invokeParams.Validate = $true }
if ($ModuleNoDotnetBuild) { $invokeParams.ModuleNoDotnetBuild = $true }
if ($PSBoundParameters.ContainsKey('ModuleVersion')) { $invokeParams.ModuleVersion = $ModuleVersion }
if ($PSBoundParameters.ContainsKey('ModulePreReleaseTag')) { $invokeParams.ModulePreReleaseTag = $ModulePreReleaseTag }
if ($PSBoundParameters.ContainsKey('ModuleNoSign')) { $invokeParams.ModuleNoSign = $ModuleNoSign.IsPresent }
if ($PSBoundParameters.ContainsKey('ModuleSignModule')) { $invokeParams.ModuleSignModule = $ModuleSignModule.IsPresent }
if ($PSBoundParameters.ContainsKey('CertificateThumbprint')) { $invokeParams.ModuleCertificateThumbprint = $CertificateThumbprint }
if ($PSBoundParameters.ContainsKey('SignIncludeBinaries')) { $invokeParams.ModuleSignIncludeBinaries = $SignIncludeBinaries.IsPresent }
if ($PSBoundParameters.ContainsKey('SignIncludeInternals')) { $invokeParams.ModuleSignIncludeInternals = $SignIncludeInternals.IsPresent }
if ($PSBoundParameters.ContainsKey('SignIncludeExe')) { $invokeParams.ModuleSignIncludeExe = $SignIncludeExe.IsPresent }
if ($PSBoundParameters.ContainsKey('DiagnosticsBaselinePath')) { $invokeParams.ModuleDiagnosticsBaselinePath = $DiagnosticsBaselinePath }
if ($PSBoundParameters.ContainsKey('GenerateDiagnosticsBaseline')) { $invokeParams.ModuleGenerateDiagnosticsBaseline = $GenerateDiagnosticsBaseline.IsPresent }
if ($PSBoundParameters.ContainsKey('UpdateDiagnosticsBaseline')) { $invokeParams.ModuleUpdateDiagnosticsBaseline = $UpdateDiagnosticsBaseline.IsPresent }
if ($PSBoundParameters.ContainsKey('FailOnNewDiagnostics')) { $invokeParams.ModuleFailOnNewDiagnostics = $FailOnNewDiagnostics.IsPresent }
if ($PSBoundParameters.ContainsKey('FailOnDiagnosticsSeverity')) { $invokeParams.ModuleFailOnDiagnosticsSeverity = $FailOnDiagnosticsSeverity }
if ($PackagesOnly) { $invokeParams.PackagesOnly = $true }
if ($ModuleOnly) { $invokeParams.ModuleOnly = $true }
if ($ToolsOnly) { $invokeParams.ToolsOnly = $true }
if ($PSBoundParameters.ContainsKey('PublishNuget')) { $invokeParams.PublishNuget = $PublishNuget.IsPresent }
if ($PSBoundParameters.ContainsKey('PublishGitHub')) { $invokeParams.PublishProjectGitHub = $PublishGitHub.IsPresent }
if ($PSBoundParameters.ContainsKey('PublishToolGitHub')) { $invokeParams.PublishToolGitHub = $PublishToolGitHub.IsPresent }
if ($PSBoundParameters.ContainsKey('ModuleFramework')) { $invokeParams.ModuleFramework = $ModuleFramework }
if ($PSBoundParameters.ContainsKey('ModuleRunMode')) { $invokeParams.ModuleRunMode = $ModuleRunMode }
if ($Target) { $invokeParams.Target = $Target }
if ($Runtime) { $invokeParams.Runtimes = $Runtime }
if ($Framework) { $invokeParams.Frameworks = $Framework }
if ($Flavor) { $invokeParams.Flavors = $Flavor }
if ($VerbosePreference -ne 'SilentlyContinue') { $invokeParams.Verbose = $true }

try {
    $result = Invoke-PowerForgeRelease @invokeParams
    if ($Json) {
        $result | ConvertTo-Json -Depth 20
    } else {
        $result
    }
} catch {
    if ($Json) {
        [ordered]@{
            Success = $false
            ErrorMessage = $_.Exception.Message
        } | ConvertTo-Json -Depth 5
    } else {
        Write-Error $_
    }
    exit 1
}
