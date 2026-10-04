param([Parameter(Mandatory)][string] $ToolsPath)

$ErrorActionPreference = 'Stop'
if (!$IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'Catalog tools require a Windows x64 PowerShell process.'
}
if (Test-Path -LiteralPath $ToolsPath) { throw 'Catalog tool scratch directory already exists.' }
$pins = Get-Content -LiteralPath "$PSScriptRoot/catalog-tools.json" -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $ToolsPath | Out-Null
try {
    function Save-CatalogTool($Pin, [string] $Path) {
        Invoke-WebRequest -Uri $Pin.Url -OutFile $Path
        if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Pin.Sha256) {
            throw "Catalog tool download failed SHA-256 verification: $([IO.Path]::GetFileName($Path))."
        }
    }
    $ghArchive = Join-Path $ToolsPath 'gh.zip'
    Save-CatalogTool $pins.GitHubCli $ghArchive
    $ghRoot = Join-Path $ToolsPath 'gh'
    [IO.Compression.ZipFile]::ExtractToDirectory($ghArchive, $ghRoot)
    $ghExecutables = @(Get-ChildItem -LiteralPath $ghRoot -Filter gh.exe -Recurse -File)
    if ($ghExecutables.Count -ne 1) { throw 'GitHub CLI archive must contain one gh.exe.' }

    $bundlePath = Join-Path $ToolsPath 'winget.msixbundle'
    Save-CatalogTool $pins.WinGet $bundlePath
    $msixPath = Join-Path $ToolsPath 'winget-x64.msix'
    $bundle = [IO.Compression.ZipFile]::OpenRead($bundlePath)
    try {
        $entry = $bundle.GetEntry('AppInstaller_x64.msix')
        if (!$entry) { throw 'WinGet bundle has no x64 package.' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $msixPath)
    } finally { $bundle.Dispose() }
    $wingetRoot = Join-Path $ToolsPath 'winget'
    [IO.Compression.ZipFile]::ExtractToDirectory($msixPath, $wingetRoot)

    $creatorRoot = Join-Path $ToolsPath 'wingetcreate'
    New-Item -ItemType Directory -Path $creatorRoot | Out-Null
    $creatorExe = Join-Path $creatorRoot 'wingetcreate.exe'
    Save-CatalogTool $pins.WinGetCreate $creatorExe
    $executables = @($ghExecutables[0].FullName, (Join-Path $wingetRoot 'winget.exe'), $creatorExe)
    foreach ($executable in $executables) {
        $versionArgument = if ($executable -eq $creatorExe) { 'info' } else { '--version' }
        & $executable $versionArgument
        if ($LASTEXITCODE -ne 0) { throw "Catalog tool cannot run: $([IO.Path]::GetFileName($executable))." }
    }
    $directories = @($executables | ForEach-Object { [IO.Path]::GetDirectoryName($_) })
    $env:PATH = ($directories -join [IO.Path]::PathSeparator) + [IO.Path]::PathSeparator + $env:PATH
    if ($env:GITHUB_PATH) { $directories | Out-File -LiteralPath $env:GITHUB_PATH -Append -Encoding utf8 }
    Remove-Item -LiteralPath $ghArchive, $bundlePath, $msixPath
} catch {
    # This invocation created the directory from nothing; it contains only verified tool downloads.
    Remove-Item -LiteralPath $ToolsPath -Recurse
    throw
}
