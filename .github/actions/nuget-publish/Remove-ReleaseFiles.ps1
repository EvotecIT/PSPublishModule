$ErrorActionPreference = 'Stop'
foreach ($prefix in @('nuget-release', 'nuget-publisher')) {
    $path = Join-Path $env:RUNNER_TEMP "$prefix-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT"
    if (Test-Path -LiteralPath $path) {
        # The tool store contains hidden and read-only package metadata on Linux.
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
    }
}
