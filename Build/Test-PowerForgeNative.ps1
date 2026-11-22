[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,
    [string] $ExpectedInformationalVersion
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$extension = if ($IsWindows) { '.exe' } else { '' }
$sourceLauncher = Join-Path $packageRoot "powerforge$extension"
$managed = Join-Path $packageRoot "full/powerforge-full$extension"
$work = [IO.Directory]::CreateTempSubdirectory('powerforge-smoke-').FullName
$previousPath = $env:PATH

function Read-CommandResult {
    param(
        [Parameter(Mandatory)] [string] $Executable,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [int] $ExpectedExitCode = 0
    )

    $output = & $Executable @Arguments
    if ($LASTEXITCODE -ne $ExpectedExitCode) {
        throw "Expected exit $ExpectedExitCode from $Executable, received $LASTEXITCODE. $output"
    }
    $json = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    if ($json.schemaVersion -ne 1 -or $json.exitCode -ne $ExpectedExitCode -or
        $json.success -ne ($ExpectedExitCode -eq 0)) {
        throw "Unexpected command envelope from $Executable. $output"
    }
    return $json
}

try {
    $feed = New-Item -ItemType Directory -Path (Join-Path $work 'local feed') -Force
    $nativeOnly = New-Item -ItemType Directory -Path (Join-Path $work 'native only')
    $launcher = Join-Path $nativeOnly.FullName "powerforge$extension"
    Copy-Item -LiteralPath $sourceLauncher -Destination $launcher
    if (-not $IsWindows) {
        [IO.File]::SetUnixFileMode($launcher, [IO.File]::GetUnixFileMode($sourceLauncher))
    }

    $nupkg = Join-Path $feed.FullName 'PowerForgeNativeSmoke.1.0.0.nupkg'
    $manifestName = 'PowerForgeNativeSmoke.psd1'
    $manifest = "@{ ModuleVersion = '1.0.0'; GUID = 'd6379ba8-c225-438a-a07e-a92a7cc56bd0' }"
    $nuspec = '<package><metadata><id>PowerForgeNativeSmoke</id><version>1.0.0</version><authors>PowerForge</authors><description>Offline native package smoke fixture</description></metadata></package>'
    $archive = [IO.Compression.ZipFile]::Open($nupkg, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in @{ $manifestName = $manifest; 'PowerForgeNativeSmoke.nuspec' = $nuspec }.GetEnumerator()) {
            $writer = [IO.StreamWriter]::new($archive.CreateEntry($entry.Key).Open())
            try { $writer.Write($entry.Value) } finally { $writer.Dispose() }
        }
    } finally {
        $archive.Dispose()
    }

    $env:PATH = Join-Path $work 'empty path'
    $found = Read-CommandResult -Executable $launcher -Arguments @(
        'module', 'find', '--name', 'PowerForgeNative*', '--source', $feed.FullName, '--output', 'json'
    )
    if ($found.command -ne 'module.find' -or $found.result.Count -ne 1 -or
        $found.result[0].name -ne 'PowerForgeNativeSmoke' -or $found.result[0].version -ne '1.0.0') {
        throw 'Native feed search did not find the expected package.'
    }

    $exactFind = @('module', 'find', '--name', 'PowerForgeNativeSmoke', '--source', $feed.FullName, '--output', 'json')
    $matching = Read-CommandResult -Executable $launcher -Arguments ($exactFind + @('--version', '1.0.0.0'))
    $different = Read-CommandResult -Executable $launcher -Arguments ($exactFind + @('--version', '1.0.1'))
    if ($matching.result.Count -ne 1 -or $matching.result[0].version -ne '1.0.0' -or $different.result.Count -ne 0) {
        throw 'Native exact-version search did not match equivalent package versions.'
    }

    $projectRoot = New-Item -ItemType Directory -Path (Join-Path $work 'version project')
    $project = Join-Path $projectRoot.FullName 'Example.csproj'
    $projectText = '<Project><PropertyGroup><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>'
    [IO.File]::WriteAllText($project, $projectText)
    [IO.File]::WriteAllText((Join-Path $projectRoot.FullName 'Example.psd1'), "@{ ModuleVersion = '2.3.4' }")
    $versions = Read-CommandResult -Executable $launcher -Arguments @(
        'project', 'versions', '--project-root', $projectRoot.FullName, '--output', 'json'
    )
    if ($versions.command -ne 'project.versions' -or $versions.result.Count -ne 2 -or
        '1.2.3' -notin $versions.result.version -or '2.3.4' -notin $versions.result.version -or
        [IO.File]::ReadAllText($project) -cne $projectText) {
        throw 'Native version discovery did not report the two declared versions or modified the project.'
    }

    $commit = '0000000000000000000000000000000000000001'
    $fingerprint = 'A' * 64
    $releaseManifest = Join-Path $feed.FullName 'nuget-release-manifest.json'
    @{
        schemaVersion = 1
        sourceCommitSha = $commit
        assets = @(@{
            fileName = [IO.Path]::GetFileName($nupkg)
            id = 'PowerForgeNativeSmoke'
            version = '1.0.0'
            sha256 = 'B' * 64
            authorCertificateFingerprint = $fingerprint
        })
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $releaseManifest
    $verification = & $launcher nuget verify --manifest $releaseManifest --source-commit $commit --author-fingerprints $fingerprint 2>&1
    if ($LASTEXITCODE -ne 1 -or ($verification -join "`n") -notmatch 'SHA256 mismatch') {
        throw "Native NuGet verification accepted a mismatched package or missed the hash check. $verification"
    }

    $manifestDocument = Get-Content -LiteralPath $releaseManifest -Raw | ConvertFrom-Json
    $manifestDocument.assets[0].sha256 = (Get-FileHash -LiteralPath $nupkg -Algorithm SHA256).Hash
    $manifestDocument | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $releaseManifest
    $workerlessPath = $env:PATH
    try {
        # Signature validation uses the installed SDK. The copied launcher still has no managed companion.
        $env:PATH = $previousPath
        $verification = & $launcher nuget verify --manifest $releaseManifest --source-commit $commit --author-fingerprints $fingerprint 2>&1
        if ($LASTEXITCODE -ne 1 -or ($verification -join "`n") -notmatch 'NU3004') {
            throw "The SDK did not reject the unsigned package. $verification"
        }
    } finally {
        $env:PATH = $workerlessPath
    }

    $install = Join-Path $work 'installed modules'
    $installed = Read-CommandResult -Executable $launcher -Arguments @(
        '--quiet', 'module', 'install', 'PowerForgeNativeSmoke', '--version', '1.0.0',
        '--source', $feed.FullName, '--destination', $install, '--output', 'json'
    )
    $installedManifest = Join-Path $install "PowerForgeNativeSmoke/1.0.0/$manifestName"
    if ($installed.command -ne 'module.install' -or [IO.File]::ReadAllText($installedManifest) -ne $manifest) {
        throw 'Native install did not install the expected manifest.'
    }

    $requirements = Join-Path $work 'modules.json'
    $lock = Join-Path $work 'modules.lock.json'
    $restored = Join-Path $work 'restored modules'
    [IO.File]::WriteAllText($requirements, '{"PowerForgeNativeSmoke":"1.0.0"}')
    $restoreArgs = @(
        'module', 'restore', '--requirements', $requirements, '--lock-file', $lock,
        '--source', $feed.FullName, '--output', 'json'
    )
    $locked = Read-CommandResult -Executable $launcher -Arguments ($restoreArgs + @('--destination', $restored, '--write-lock'))
    $expectedHash = (Get-FileHash -LiteralPath $nupkg -Algorithm SHA256).Hash
    $actualLock = Get-Content -LiteralPath $lock -Raw | ConvertFrom-Json
    if ($locked.command -ne 'module.restore' -or -not $locked.result.lockWritten -or
        $actualLock.packages.Count -ne 1 -or $actualLock.packages[0].sha256 -ne $expectedHash) {
        throw 'Native restore did not write the expected package pin.'
    }

    $verified = Join-Path $work 'verified modules'
    $frozen = Read-CommandResult -Executable $launcher -Arguments ($restoreArgs + @('--destination', $verified))
    $verifiedManifest = Join-Path $verified "PowerForgeNativeSmoke/1.0.0/$manifestName"
    if ($frozen.result.lockWritten -or [IO.File]::ReadAllText($verifiedManifest) -ne $manifest) {
        throw 'Native frozen restore did not apply the verified package.'
    }

    [IO.File]::AppendAllText($nupkg, 'altered')
    $rejectedDir = Join-Path $work 'rejected modules'
    $rejected = Read-CommandResult -Executable $launcher -Arguments ($restoreArgs + @('--destination', $rejectedDir)) -ExpectedExitCode 1
    if ($rejected.error -notmatch 'SHA-?256' -or (Test-Path -LiteralPath $rejectedDir)) {
        throw "Native frozen restore did not reject changed package bytes before writing the destination. $($rejected.error)"
    }

    $version = Read-CommandResult -Executable $launcher -Arguments @('--version', '--output', 'json')
    $direct = Read-CommandResult -Executable $managed -Arguments @('--version', '--output', 'json')
    if ($version.command -ne 'version' -or -not $version.result.version -or
        $version.result.version -ne $direct.result.version) {
        throw 'The standalone native launcher did not report the same version as the managed CLI.'
    }
    if ($ExpectedInformationalVersion -and $version.result.version -cne $ExpectedInformationalVersion) {
        throw "The native bundle reports '$($version.result.version)' instead of '$ExpectedInformationalVersion'."
    }

    $inspectPath = Join-Path $work 'Sample.psd1'
    [IO.File]::WriteAllText($inspectPath, "@{ RootModule = 'Implementation.psm1'; ModuleVersion = '2.3.4'; PrivateData = @{ PSData = @{ Prerelease = 'rc.1' } } }")
    $inspectArgs = @('module', 'manifest', '--path', $inspectPath, '--output', 'json')
    $inspected = Read-CommandResult -Executable $launcher -Arguments $inspectArgs
    if ($inspected.command -ne 'module.manifest' -or $inspected.result.moduleName -ne 'Sample' -or
        $inspected.result.moduleVersion -ne '2.3.4' -or $inspected.result.preRelease -ne 'rc.1') {
        throw 'Standalone native manifest inspection did not report the expected static metadata.'
    }

    [IO.File]::WriteAllText($inspectPath, "@{ RootModule = 'Sample.psm1' }")
    $invalid = Read-CommandResult -Executable $launcher -Arguments $inspectArgs -ExpectedExitCode 2
    if ($invalid.error -notmatch 'ModuleVersion') {
        throw 'Native manifest inspection did not explain the missing ModuleVersion.'
    }
    $routedHelp = (& $sourceLauncher --help) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or $routedHelp -notmatch 'powerforge build') {
        throw 'The bundled managed CLI did not expose the full command help.'
    }

    Write-Output 'Native module search, install, frozen restore, version discovery, manifest inspection, NuGet verification and managed forwarding passed.'
} finally {
    $env:PATH = $previousPath
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
