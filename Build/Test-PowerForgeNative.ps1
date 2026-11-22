[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory
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

    $version = Read-CommandResult -Executable $sourceLauncher -Arguments @('--version', '--output', 'json')
    $direct = Read-CommandResult -Executable $managed -Arguments @('--version', '--output', 'json')
    if ($version.command -ne 'version' -or $version.result.version -ne $direct.result.version) {
        throw 'The native launcher did not forward the version command to the bundled managed CLI.'
    }
    $routedHelp = (& $sourceLauncher --help) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or $routedHelp -notmatch 'powerforge build') {
        throw 'The bundled managed CLI did not expose the full command help.'
    }

    Write-Output 'Native install, frozen restore, package integrity and managed forwarding passed.'
} finally {
    $env:PATH = $previousPath
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
