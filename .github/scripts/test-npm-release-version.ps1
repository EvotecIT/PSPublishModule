param([string] $OutputPath)
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../actions/npm-release-version/Resolve-NpmReleaseVersion.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('powerforge-npm-version-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$results = [Collections.Generic.List[object]]::new()
$savedEnvironment = @{}
$names = @('GITHUB_REPOSITORY','GITHUB_SHA','GITHUB_REF','GITHUB_EVENT_NAME','DEFAULT_BRANCH','QUALITY_RUN',
    'QUALITY_WORKFLOW','RELEASE_TAG_PREFIX','RESERVE_VERSION','PACKAGE_MANIFEST','REQUESTED_VERSION','GITHUB_OUTPUT','GITHUB_EVENT_PATH')
foreach ($name in $names) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }

# Only external boundaries are replaced; every case runs the shipped action script.
function global:git { $global:LASTEXITCODE = 0; return $global:npmVersionFixture.Checkout }
function global:gh {
    $global:LASTEXITCODE = 0
    if ($args[1] -like '*/actions/runs/*') { return $global:npmVersionFixture.Quality | ConvertTo-Json -Compress }
    if ($args[1] -like '*/compare/*') { return @{status=$global:npmVersionFixture.Comparison} | ConvertTo-Json -Compress }
    if ($args[1] -like '*/git/matching-refs/*') {
        if ('--paginate' -notin $args -or '--slurp' -notin $args) { throw 'Tag discovery must include every page.' }
        return ConvertTo-Json -InputObject $global:npmVersionFixture.Pages -Depth 10 -Compress
    }
    if ($args[1] -like '*/git/refs') {
        $global:npmVersionFixture.Creates++
        if ($global:npmVersionFixture.Collision) { $global:LASTEXITCODE = 1; return '{}' }
        $name = [string]($args | Where-Object { $_ -like 'ref=*' } | Select-Object -First 1)
        $global:npmVersionFixture.CreatedRef = $name.Substring(4)
        return @{object=@{type='commit';sha=$global:npmVersionFixture.Checkout}} | ConvertTo-Json -Compress
    }
    throw 'Unexpected GitHub request.'
}
function global:Invoke-WebRequest {
    param($Uri, [switch]$SkipHttpErrorCheck)
    if ($Uri -cne 'https://registry.npmjs.org/%40example%2Fwidget') { throw 'Unexpected registry request.' }
    return @{StatusCode=$global:npmVersionFixture.Status; Content=$global:npmVersionFixture.Registry}
}
function New-Reservation([string] $Version, [string] $Sha) {
    return @{ref="refs/tags/npm-v$Version";object=@{type='commit';sha=$Sha}}
}
function Reset-Fixture {
    $env:GITHUB_REPOSITORY = 'example/widget'
    $env:GITHUB_SHA = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
    $env:GITHUB_REF = 'refs/heads/main'
    $env:GITHUB_EVENT_NAME = 'workflow_run'
    $env:DEFAULT_BRANCH = 'main'
    $env:QUALITY_RUN = '100'
    $env:QUALITY_WORKFLOW = 'quality.yml'
    $env:RELEASE_TAG_PREFIX = 'npm-v'
    $env:RESERVE_VERSION = 'true'
    $env:REQUESTED_VERSION = ''
    $env:PACKAGE_MANIFEST = Join-Path $fixture 'package.json'
    $env:GITHUB_OUTPUT = Join-Path $fixture 'outputs'
    $env:GITHUB_EVENT_PATH = Join-Path $fixture 'event.json'
    @{action='completed';workflow_run=@{id=$env:QUALITY_RUN;head_sha=$env:GITHUB_SHA;head_repository=@{full_name=$env:GITHUB_REPOSITORY}}} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $env:GITHUB_EVENT_PATH
    [IO.File]::WriteAllText($env:GITHUB_OUTPUT, '')
    @{name='@example/widget';version='0.1.1';repository=@{url='git+https://github.com/example/widget.git'}} |
        ConvertTo-Json | Set-Content -LiteralPath $env:PACKAGE_MANIFEST
    $global:npmVersionFixture = @{
        Quality=@{head_sha=$env:GITHUB_SHA;head_branch='main';event='push';status='completed';conclusion='success';path='.github/workflows/quality.yml'}
        Pages=@(@()); Creates=0; Collision=$false; CreatedRef=''; Status=200; Comparison='behind'; Checkout=$env:GITHUB_SHA
        Registry='{"name":"@example/widget","versions":{"0.1.0":{}}}'
    }
}
function Assert-Case([string] $Name, [scriptblock] $Arrange, [string] $Version, [int] $Creates = 1, [switch] $Reject, [switch] $Included) {
    Reset-Fixture
    & $Arrange
    $failure = $null
    try { & $scriptPath } catch { $failure = $_ }
    $output = Get-Content -LiteralPath $env:GITHUB_OUTPUT -Raw
    if ($Reject) {
        if (-not $failure -or $output) { throw "$Name did not reject before emitting a release version." }
    } else {
        if ($failure) { throw "$Name failed: $failure" }
        if ($output -notmatch "(?m)^version=$([regex]::Escape($Version))\r?$" -or
            $output -notmatch "(?m)^tag=npm-v$([regex]::Escape($Version))\r?$") { throw "$Name selected the wrong version." }
        if ($global:npmVersionFixture.Creates -ne $Creates) { throw "$Name used an unexpected number of tag mutations." }
        if ($Creates -eq 1 -and $global:npmVersionFixture.CreatedRef -cne "refs/tags/npm-v$Version") { throw "$Name reserved the wrong tag." }
        $expected = if ($Included) { 'false' } else { 'true' }
        if ($output -notmatch "(?m)^should-release=$expected\r?$") { throw "$Name selected the wrong publication state." }
    }
    $results.Add(@{case=$Name;passed=$true})
}
try {
    Assert-Case 'Initial release uses the manifest minimum' {} '0.1.1'
    Assert-Case 'Published minimum advances the patch' { $global:npmVersionFixture.Registry='{"name":"@example/widget","versions":{"0.1.1":{}}}' } '0.1.2'
    Assert-Case 'A higher manifest starts a new release line' {
        $m=Get-Content $env:PACKAGE_MANIFEST -Raw | ConvertFrom-Json; $m.version='0.2.0'; $m | ConvertTo-Json | Set-Content $env:PACKAGE_MANIFEST
    } '0.2.0'
    Assert-Case 'All reservation pages participate' {
        $global:npmVersionFixture.Pages=@(@((New-Reservation '0.1.2' 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb')),@((New-Reservation '0.1.9' 'cccccccccccccccccccccccccccccccccccccccc')))
    } '0.1.10'
    Assert-Case 'Retry keeps the same commit reservation' {
        $global:npmVersionFixture.Pages=@(@((New-Reservation '0.1.2' $env:GITHUB_SHA)))
    } '0.1.2' 0
    Assert-Case 'Dry run never reserves a tag' { $env:RESERVE_VERSION='false' } '0.1.1' 0
    Assert-Case 'Explicit release selects a higher version' { $env:REQUESTED_VERSION='1.0.0' } '1.0.0'
    Assert-Case 'Missing public package starts at the manifest minimum' { $global:npmVersionFixture.Status=404 } '0.1.1'
    Assert-Case 'Network failure is not an empty registry' { $global:npmVersionFixture.Status=503 } -Reject
    Assert-Case 'Explicit occupied version is rejected' { $env:REQUESTED_VERSION='0.1.0' } -Reject
    Assert-Case 'Reservation collision stops before publication' { $global:npmVersionFixture.Collision=$true } -Reject
    Assert-Case 'Changed retry version is rejected' { $global:npmVersionFixture.Pages=@(@((New-Reservation '0.1.2' $env:GITHUB_SHA))); $env:REQUESTED_VERSION='0.1.3' } -Reject
    Assert-Case 'Recovered source reuses its highest reservation' { $global:npmVersionFixture.Pages=@(@((New-Reservation '0.1.2' $env:GITHUB_SHA),(New-Reservation '0.1.3' $env:GITHUB_SHA))) } '0.1.3' 0
    Assert-Case 'Failed quality cannot reserve a version' { $global:npmVersionFixture.Quality.conclusion='failure' } -Reject
    Assert-Case 'Another source commit cannot qualify the release' { $global:npmVersionFixture.Quality.head_sha='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' } -Reject
    Assert-Case 'Another workflow cannot qualify the release' { $global:npmVersionFixture.Quality.path='.github/workflows/other.yml' } -Reject
    Assert-Case 'Branch dispatch cannot reserve a version' { $env:GITHUB_EVENT_NAME='workflow_dispatch'; $env:GITHUB_REF='refs/heads/feature' } -Reject
    Assert-Case 'Another repository cannot supply registry metadata' { $global:npmVersionFixture.Registry='{"name":"@other/widget","versions":{"0.1.0":{}}}' } -Reject
    Assert-Case 'Non-three-part requested version is rejected' { $env:REQUESTED_VERSION='1.0.0.1' } -Reject
    Assert-Case 'Explicit version cannot fall below manifest minimum' { $env:REQUESTED_VERSION='0.1.0'; $global:npmVersionFixture.Status=404 } -Reject
    Assert-Case 'Quality source remains valid after a later merge' { $env:GITHUB_SHA='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' } '0.1.1'
    Assert-Case 'Wrong checkout cannot reserve a source version' { $global:npmVersionFixture.Checkout='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' } -Reject
    Assert-Case 'Already included source does not release older code' {
        $global:npmVersionFixture.Pages=@(@((New-Reservation '0.1.2' 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb')))
        $global:npmVersionFixture.Registry='{"name":"@example/widget","versions":{"0.1.2":{}}}'
        $global:npmVersionFixture.Comparison='ahead'
    } '0.1.2' 0 -Included
    Assert-Case 'Newer source recovers an overtaken unpublished reservation' {
        $global:npmVersionFixture.Pages=@(@((New-Reservation '0.1.1' $env:GITHUB_SHA),(New-Reservation '0.1.2' 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb')))
        $global:npmVersionFixture.Registry='{"name":"@example/widget","versions":{"0.1.2":{}}}'
    } '0.1.3'
    Assert-Case 'Diverged public source history stops publication' {
        $global:npmVersionFixture.Pages=@(@((New-Reservation '0.1.2' 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb')))
        $global:npmVersionFixture.Registry='{"name":"@example/widget","versions":{"0.1.2":{}}}'
        $global:npmVersionFixture.Comparison='diverged'
    } -Reject
    if ($OutputPath) { $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath }
    Write-Host "npm release version contracts passed: $($results.Count)."
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Remove-Item Function:/git,Function:/gh,Function:/Invoke-WebRequest
    Remove-Variable npmVersionFixture -Scope Global -ErrorAction SilentlyContinue
    # This unique fixture contains only files created above, never a shared cache.
    Remove-Item -LiteralPath $fixture -Recurse -ErrorAction Stop
}
