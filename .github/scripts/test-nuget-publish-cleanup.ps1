$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('powerforge-nuget-cleanup-' + [Guid]::NewGuid().ToString('N'))
$saved = @{}
foreach ($name in @('RUNNER_TEMP', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    $env:RUNNER_TEMP = $fixture
    $env:GITHUB_RUN_ID = '1234'
    $env:GITHUB_RUN_ATTEMPT = '1'
    $downloads = Join-Path $fixture 'nuget-release-1234-1'
    $tool = Join-Path $fixture 'nuget-publisher-1234-1'
    $store = Join-Path $tool '.store/package/1.0.0'
    New-Item -ItemType Directory -Path $downloads, $store | Out-Null
    Set-Content -LiteralPath (Join-Path $downloads 'package.nupkg') -Value 'signed package fixture'
    $metadata = Join-Path $store '.nupkg.metadata'
    Set-Content -LiteralPath $metadata -Value 'read-only tool metadata'
    [IO.File]::SetAttributes($metadata, [IO.FileAttributes]::ReadOnly)
    $neighbor = Join-Path $fixture 'another-invocation.txt'
    Set-Content -LiteralPath $neighbor -Value 'keep'

    & (Join-Path $PSScriptRoot '../actions/nuget-publish/Remove-ReleaseFiles.ps1')

    if ((Test-Path -LiteralPath $downloads) -or (Test-Path -LiteralPath $tool)) {
        throw 'Invocation-owned release and tool directories were not removed.'
    }
    if ((Get-Content -LiteralPath $neighbor -Raw).Trim() -cne 'keep') {
        throw 'Cleanup changed unrelated runner files.'
    }
    'NuGet publisher cleanup removes hidden/read-only tool files and preserves neighboring data.'
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
