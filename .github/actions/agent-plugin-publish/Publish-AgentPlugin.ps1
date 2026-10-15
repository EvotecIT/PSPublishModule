<#
.SYNOPSIS
Publishes a qualified plugin archive to its generated Git distribution branch.
.DESCRIPTION
Checks the archive checksum and PowerForge package contract before publishing.
Preserves branch history, rejects older or changed same-version payloads, and
uses an ordinary fast-forward push so concurrent updates fail safely.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArchivePath,
    [string] $ChecksumPath,
    [Parameter(Mandatory)] [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')] [string] $Repository,
    [ValidatePattern('^agent-plugin(?:/[a-z0-9][a-z0-9.-]*)?$')] [string] $Branch = 'agent-plugin',
    [Parameter(Mandatory)] [ValidatePattern('^[a-fA-F0-9]{40}$')] [string] $SourceRevision,
    [Parameter(Mandatory)] [string] $PowerForgePath,
    [Parameter(Mandatory)] [string] $ScratchRoot
)
$ErrorActionPreference = 'Stop'
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
if ([IO.Directory]::Exists($archive)) {
    $archives = @([IO.Directory]::GetFiles($archive, '*.zip'))
    if ($archives.Count -ne 1) { throw 'Expected exactly one plugin ZIP in the archive directory.' }
    $archive = $archives[0]
}
if ([string]::IsNullOrWhiteSpace($ChecksumPath)) { $ChecksumPath = $archive + '.sha256' }
$checksum = (Get-Content -LiteralPath $ChecksumPath -Raw).Trim()
$expected = '^([a-fA-F0-9]{64})  ' + [regex]::Escape([IO.Path]::GetFileName($archive)) + '$'
if ($checksum -notmatch $expected -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $Matches[1]) {
    throw 'Plugin archive does not match its SHA-256 sidecar.'
}
$packer = (Resolve-Path -LiteralPath $PowerForgePath).Path
$scratch = (Resolve-Path -LiteralPath $ScratchRoot).Path
$temporary = Join-Path $scratch ('agent-plugin-publish-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    # Extraction precedes Git initialization; validation rejects repository entries and links.
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $temporary)
    & dotnet $packer agent-plugin validate --source $temporary
    if ($LASTEXITCODE -ne 0) { throw 'Plugin package validation failed.' }
    $executablePaths = @()
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            $mode = ($entry.ExternalAttributes -shr 16) -band 511
            if (-not [OperatingSystem]::IsWindows() -and $mode -ne 0) {
                [IO.File]::SetUnixFileMode((Join-Path $temporary $entry.FullName), [IO.UnixFileMode]$mode)
            }
            if (($mode -band 73) -ne 0) { $executablePaths += $entry.FullName }
        }
    } finally { $zip.Dispose() }
    $plugin = Get-Content -LiteralPath (Join-Path $temporary 'plugin.json') -Raw | ConvertFrom-Json
    if ($plugin.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Distribution branches require a stable three-part plugin version.' }
    function Invoke-PublicationGit {
        param([string[]] $Arguments)
        $output = & git -C $temporary @Arguments
        if ($LASTEXITCODE -ne 0) { throw "Git publication failed: $($Arguments[0])" }
        $output
    }
    Invoke-PublicationGit -Arguments @('init', '--quiet') | Out-Null
    Invoke-PublicationGit -Arguments @('remote', 'add', 'origin', "https://github.com/$Repository.git")
    Invoke-PublicationGit -Arguments @('config', 'credential.helper', '!gh auth git-credential')
    Invoke-PublicationGit -Arguments @('config', 'user.name', 'github-actions[bot]')
    Invoke-PublicationGit -Arguments @('config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com')
    $remote = & git -C $temporary ls-remote --exit-code --heads origin "refs/heads/$Branch"
    $remoteExit = $LASTEXITCODE
    if ($remoteExit -notin @(0, 2)) { throw 'Cannot inspect the publication branch.' }
    $parent = $null
    if ($remoteExit -eq 0) {
        $parent = ($remote -split '\s+')[0]
        Invoke-PublicationGit -Arguments @('fetch', '--quiet', '--no-tags', 'origin', "refs/heads/$Branch")
        $previous = (Invoke-PublicationGit -Arguments @('show', "${parent}:plugin.json") | Out-String) | ConvertFrom-Json
        if ($previous.name -ne $plugin.name) { throw 'The distribution branch belongs to a different plugin.' }
        if ([version]$previous.version -gt [version]$plugin.version) { throw 'Cannot replace a newer published plugin version.' }
    }
    Invoke-PublicationGit -Arguments @('-c', 'core.autocrlf=false', 'add', '--force', '--all')
    foreach ($path in $executablePaths) {
        Invoke-PublicationGit -Arguments @('add', '--force', '--chmod=+x', '--', $path) | Out-Null
    }
    $tree = Invoke-PublicationGit -Arguments @('write-tree')
    if ($parent -and $previous.version -eq $plugin.version) {
        $previousTree = Invoke-PublicationGit -Arguments @('rev-parse', "${parent}^{tree}")
        if ($tree -ne $previousTree) { throw 'A published plugin version cannot be replaced with different files.' }
        "Distribution branch already contains $($plugin.name)@$($plugin.version)."
        return
    }
    $commitArguments = @('commit-tree', $tree, '-m', "Publish $($plugin.name) $($plugin.version)", '-m', "Source-Commit: $SourceRevision")
    if ($parent) { $commitArguments += @('-p', $parent) }
    $commit = Invoke-PublicationGit $commitArguments
    Invoke-PublicationGit -Arguments @('push', 'origin', "${commit}:refs/heads/$Branch")
    "Published https://github.com/$Repository/tree/$Branch at $commit."
} finally {
    try {
        $entries = @(Get-ChildItem -LiteralPath $temporary -Recurse -Force)
        if (((Get-Item -LiteralPath $temporary -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            @($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0) {
            throw 'Publication scratch contains a link; preserve it for inspection.'
        }
        # Git makes its own metadata hidden/read-only. Clear those flags only in this owned staging directory.
        foreach ($entry in $entries) {
            $entry.Attributes = $entry.Attributes -band (-bnot ([IO.FileAttributes]::ReadOnly -bor [IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System))
        }
        # Directory.Delete also handles Git's dot-prefixed metadata on Unix.
        [IO.Directory]::Delete($temporary, $true)
    }
    catch { Write-Warning "Retained publication scratch after cleanup failure: $temporary" }
}
