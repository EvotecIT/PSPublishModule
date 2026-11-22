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
$dependencyRoot = Join-Path $work 'dependency modules'
$previousPath = $env:PATH
$previousProfilePath = $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH
$previousMachineProfilePath = $env:POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH

function Read-CommandResult {
    param(
        [Parameter(Mandatory)] [string] $Executable,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [int] $ExpectedExitCode = 0
    )

    $previousModulePath = $env:PSModulePath
    try {
        $env:PSModulePath = $dependencyRoot
        $output = & $Executable @Arguments
        $actualExitCode = $LASTEXITCODE
    } finally {
        $env:PSModulePath = $previousModulePath
    }
    if ($actualExitCode -ne $ExpectedExitCode) {
        throw "Expected exit $ExpectedExitCode from $Executable, received $actualExitCode. $output"
    }
    $json = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    if ($json.schemaVersion -ne 1 -or $json.exitCode -ne $ExpectedExitCode -or
        $json.success -ne ($ExpectedExitCode -eq 0)) {
        throw "Unexpected command envelope from $Executable. $output"
    }
    return $json
}

try {
    $null = [IO.Directory]::CreateDirectory($dependencyRoot)
    $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = Join-Path $work 'user-profiles.json'
    $env:POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH = Join-Path $work 'machine-profiles.json'
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

    $moduleSource = New-Item -ItemType Directory -Path (Join-Path $work 'PowerForgeNativeSmoke')
    $nextManifest = "@{`nModuleVersion = '1.1.0'`nGUID = 'd6379ba8-c225-438a-a07e-a92a7cc56bd0'`nAuthor = 'PowerForge'`nDescription = 'Lifecycle smoke test'`n}"
    [IO.File]::WriteAllText((Join-Path $moduleSource.FullName $manifestName), $nextManifest)
    $packed = Read-CommandResult -Executable $launcher -Arguments @(
        'module', 'pack', '--path', $moduleSource.FullName, '--destination', $feed.FullName, '--output', 'json'
    )
    $nextPackage = Join-Path $feed.FullName 'PowerForgeNativeSmoke.1.1.0.nupkg'
    if ($packed.command -ne 'module.pack' -or $packed.result.version -ne '1.1.0' -or
        -not (Test-Path -LiteralPath $nextPackage -PathType Leaf)) {
        throw 'Native pack did not produce the requested version.'
    }

    $null = Read-CommandResult -Executable $launcher -Arguments @(
        'module', 'repository', 'add', '--name', 'NativeSmoke', '--source', $feed.FullName, '--output', 'json'
    )
    $null = Read-CommandResult -Executable $launcher -Arguments @(
        'module', 'repository', 'set', '--name', 'NativeSmoke', '--trusted', '--output', 'json'
    )
    $profiles = Read-CommandResult -Executable $launcher -Arguments @('module', 'repository', 'list', '--output', 'json')
    if ($profiles.result.Count -ne 1 -or $profiles.result[0].name -ne 'NativeSmoke' -or -not $profiles.result[0].trusted) {
        throw 'Native repository CRUD did not persist the profile and its trust setting.'
    }

    $saveRoot = Join-Path $work 'saved packages'
    $denied = Read-CommandResult -Executable $launcher -ExpectedExitCode 1 -Arguments @(
        'module', 'save', 'PowerForgeNativeSmoke', '--version', '1.1.0', '--source', $feed.FullName,
        '--destination', $saveRoot, '--as-nupkg', '--require-trusted-repository', '--output', 'json'
    )
    if ($denied.error -notmatch 'trust' -or (Test-Path -LiteralPath $saveRoot)) {
        throw 'Native save did not enforce explicit repository trust before writing.'
    }
    $saved = Read-CommandResult -Executable $launcher -Arguments @(
        'module', 'save', 'PowerForgeNativeSmoke', '--version', '1.1.0', '--profile', 'NativeSmoke',
        '--destination', $saveRoot, '--as-nupkg', '--require-trusted-repository', '--output', 'json'
    )
    $savedPackage = @(Get-ChildItem -LiteralPath $saveRoot -Filter '*.nupkg' -Recurse -File)
    if ($saved.command -ne 'module.save' -or $savedPackage.Count -ne 1 -or
        (Get-FileHash -LiteralPath $savedPackage[0].FullName).Hash -ne (Get-FileHash -LiteralPath $nextPackage).Hash) {
        throw 'Native save did not write an archive identical to the source.'
    }

    $updateArgs = @(
        'module', 'update', 'PowerForgeNativeSmoke', '--version', '1.1.0', '--profile', 'NativeSmoke',
        '--destination', $install, '--require-trusted-repository', '--output', 'json'
    )
    $updatePlan = Read-CommandResult -Executable $launcher -Arguments ($updateArgs + '--plan')
    $newManifestPath = Join-Path $install "PowerForgeNativeSmoke/1.1.0/$manifestName"
    if ($updatePlan.result.targetVersion -ne '1.1.0' -or (Test-Path -LiteralPath $newManifestPath)) {
        throw 'Native update plan did not remain read-only or selected the wrong version.'
    }
    $update = Read-CommandResult -Executable $launcher -Arguments $updateArgs
    $listArgs = @('module', 'list', '--destination', $install, '--name', 'PowerForgeNative*', '--output', 'json')
    $listed = Read-CommandResult -Executable $launcher -Arguments $listArgs
    if ($update.result.status -ne 'Updated' -or [IO.File]::ReadAllText($newManifestPath) -ne $nextManifest -or
        $listed.result.Count -ne 2 -or '1.1.0' -notin $listed.result.version -or '1.0.0' -notin $listed.result.version) {
        throw 'Native update and list did not report the two installed versions.'
    }

    $uninstallArgs = @(
        'module', 'uninstall', 'PowerForgeNativeSmoke', '--version', '1.0.0', '--destination', $install, '--output', 'json'
    )
    $linkedRoot = New-Item -ItemType Directory -Path (Join-Path $work 'linked module root')
    $linkPath = Join-Path $linkedRoot.FullName 'PowerForgeNativeSmoke'
    $linkTarget = Join-Path $install 'PowerForgeNativeSmoke'
    if ($IsWindows) {
        $null = New-Item -ItemType Junction -Path $linkPath -Target $linkTarget
    } else {
        $null = [IO.Directory]::CreateSymbolicLink($linkPath, $linkTarget)
    }
    try {
        $linkedArgs = $uninstallArgs.Clone()
        $linkedArgs[[Array]::IndexOf($linkedArgs, '--destination') + 1] = $linkedRoot.FullName
        $linked = Read-CommandResult -Executable $launcher -Arguments $linkedArgs -ExpectedExitCode 1
        if ($linked.error -notmatch 'symbolic link or junction') {
            throw "Native uninstall did not reject the linked directory with the expected diagnostic. $($linked.error)"
        }
        if (-not (Test-Path -LiteralPath $installedManifest) -or
            [IO.File]::ReadAllText($installedManifest) -cne $manifest) {
            throw 'Native uninstall touched files outside the selected root through a linked directory.'
        }
    } finally {
        [IO.Directory]::Delete($linkPath)
    }
    $removalPlan = Read-CommandResult -Executable $launcher -Arguments ($uninstallArgs + '--plan')
    if ($removalPlan.result.targets.Count -ne 1 -or -not (Test-Path -LiteralPath $installedManifest)) {
        throw 'Native uninstall plan did not remain read-only or selected the wrong version.'
    }
    $uninstalled = Read-CommandResult -Executable $launcher -Arguments $uninstallArgs
    $remaining = Read-CommandResult -Executable $launcher -Arguments $listArgs
    if ($uninstalled.result[0].status -ne 'Uninstalled' -or (Test-Path -LiteralPath $installedManifest) -or
        $remaining.result.Count -ne 1 -or $remaining.result[0].version -ne '1.1.0') {
        throw 'Native uninstall removed an unexpected version or left the target installed.'
    }

    $null = Read-CommandResult -Executable $launcher -Arguments @(
        'module', 'repository', 'remove', '--name', 'NativeSmoke', '--output', 'json'
    )
    $profiles = Read-CommandResult -Executable $launcher -Arguments @('module', 'repository', 'list', '--output', 'json')
    if ($profiles.result.Count -ne 0) { throw 'Native repository remove did not remove the saved profile.' }

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

    Write-Output 'Native module lifecycle, repository profiles, frozen restore, version discovery, manifest inspection, NuGet verification and managed forwarding passed.'
} finally {
    $env:PATH = $previousPath
    $env:POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH = $previousProfilePath
    $env:POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH = $previousMachineProfilePath
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
