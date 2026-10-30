# Offline composition is separate from the Apple operator boundary: feature
# branches and local specs are valid inputs, while Apple credentials are not.
function Invoke-PinnedStoreArt {
    Assert-UnlinkedDirectory -Path $consumer -Name 'Store art consumer'
    Assert-GitHubOrigin -Root $consumer -ExpectedRepository $ExpectedConsumerRepository
    Assert-NoReplaceRefs -Root $toolRoot -Name 'PSPublishModule source'
    Assert-NoReplaceRefs -Root $consumer -Name 'Store art consumer'
    if ($ArgumentList.Count -lt 2 -or $ArgumentList[0] -cne 'apple-store-art' -or $ArgumentList[1] -cne 'compose') {
        throw 'The offline Web tool only accepts apple-store-art compose.'
    }
    $options = @{}
    for ($i = 2; $i -lt $ArgumentList.Count; $i++) {
        $name = $ArgumentList[$i]
        if ($options.ContainsKey($name)) { throw "Duplicate store art option: $name" }
        if ($name -ceq '--verify-existing') { $options[$name] = $true; continue }
        if ($name -cnotin @('--config', '--out', '--locales', '--devices', '--browser-executable', '--output') -or
            $i + 1 -ge $ArgumentList.Count -or $ArgumentList[$i + 1].StartsWith('--')) {
            throw "Unsupported store art option: $name"
        }
        $options[$name] = $ArgumentList[++$i]
    }
    if (-not $options.ContainsKey('--config') -or -not $options.ContainsKey('--out')) {
        throw 'Store art requires --config and --out.'
    }
    $dotnet = Resolve-FixedTool -Name dotnet
    $snapshot = New-TrackedToolSnapshot -TarPath (Resolve-FixedTool -Name tar)
    $project = Join-Path $snapshot 'PowerForge.Web.Cli/PowerForge.Web.Cli.csproj'
    $artifacts = Join-Path $temporaryRoot 'artifacts'
    $output = Join-Path $temporaryRoot 'cli'
    # Ordinary offline rendering shares the normal NuGet cache and locks. The
    # exact source and its build output remain private to this invocation.
    $packages = $env:NUGET_PACKAGES
    if ([string]::IsNullOrWhiteSpace($packages)) { $packages = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
    $restore = Invoke-RedactedProcess -FilePath $dotnet -WorkingDirectory $snapshot `
        -Arguments @('restore', $project, '--packages', $packages, '--artifacts-path', $artifacts, '--verbosity', 'minimal') `
        -NuGetPackagesPath $packages
    if ($restore -ne 0) { $global:LASTEXITCODE = $restore; return }
    $build = Invoke-RedactedProcess -FilePath $dotnet -WorkingDirectory $snapshot `
        -Arguments @('build', $project, '--configuration', 'Release', '--framework', 'net10.0', '--no-restore', '--artifacts-path', $artifacts,
            '--output', $output, "--property:RestorePackagesPath=$packages", '--verbosity', 'minimal') -NuGetPackagesPath $packages
    if ($build -ne 0) { $global:LASTEXITCODE = $build; return }
    $assembly = Join-Path $output 'PowerForge.Web.Cli.dll'
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'Pinned Web CLI build output is missing.' }
    Write-Host "PowerForge source: $toolHead"
    $global:LASTEXITCODE = Invoke-RedactedProcess -FilePath $dotnet -WorkingDirectory $consumer `
        -Arguments (@($assembly) + $ArgumentList) -NuGetPackagesPath $packages
}
