# Reset only output sealed by a previous completed invocation of this exact observer.
# Unknown, changed, linked, or still-open files are preserved by a failed reset.
function Assert-PlatyPlainPath {
    param([string] $Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Observer paths cannot traverse links.' }
        }
        $parent = Split-Path -Parent $cursor
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}

function Get-PlatyWorkspaceInventory {
    param([string] $Root)
    $files = New-Object 'Collections.Generic.List[object]'
    $directories = New-Object 'Collections.Generic.List[string]'
    foreach ($item in @(Get-ChildItem -LiteralPath $Root -Force -Recurse | Sort-Object FullName)) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Observer workspace contains a link; preserve it.' }
        $relative = $item.FullName.Substring($Root.Length + 1).Replace('\', '/')
        if ($relative -eq '.owned-output.json') { continue }
        if ($relative.Split('/') -contains '.git') { throw 'Observer workspace contains Git state; preserve it.' }
        if ($item.PSIsContainer) { $directories.Add($relative) }
        else { $files.Add([ordered]@{ path = $relative; bytes = [long] $item.Length; sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash }) }
    }
    return [ordered]@{ files = @($files.ToArray()); directories = @($directories.ToArray()) }
}

function Initialize-PlatyObserverWorkspace {
    param([string] $ScenarioRoot, [object[]] $TrustedInputs)
    $scenario = [IO.Path]::GetFullPath($ScenarioRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([string]::IsNullOrWhiteSpace($scenario) -or $scenario -eq [IO.Path]::GetPathRoot($ScenarioRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)) {
        throw 'A bounded scenario directory is required.'
    }
    Assert-PlatyPlainPath $scenario
    if (-not (Test-Path -LiteralPath $scenario -PathType Container)) { New-Item -ItemType Directory -Path $scenario | Out-Null }
    $root = [IO.Path]::GetFullPath((Join-Path $scenario 'platyps-observer-owned'))
    $prefix = $scenario + [IO.Path]::DirectorySeparatorChar
    $comparison = if ([IO.Path]::DirectorySeparatorChar -eq '\') { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $root.StartsWith($prefix, $comparison)) { throw 'Observer workspace escaped the declared scenario directory.' }
    if (Test-Path -LiteralPath $root) {
        Assert-PlatyPlainPath $root
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Observer workspace is not a directory; preserve it.' }
        $marker = Join-Path $root '.owned-output.json'
        if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) { throw 'Unsealed existing observer workspace; preserve it.' }
        Assert-PlatyPlainPath $marker
        $receipt = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($receipt.schemaVersion -ne 1 -or $receipt.owner -cne 'PowerForge.Corpus.PlatyPS.Observer.v1' -or
            (ConvertTo-Json -InputObject @($receipt.trustedInputs) -Depth 5 -Compress) -cne
            (ConvertTo-Json -InputObject @($TrustedInputs) -Depth 5 -Compress)) { throw 'Observer ownership or reviewed inputs changed; preserve existing output.' }
        $actual = Get-PlatyWorkspaceInventory $root
        if ((ConvertTo-Json -InputObject $receipt.inventory -Depth 6 -Compress) -cne
            (ConvertTo-Json -InputObject $actual -Depth 6 -Compress)) { throw 'Observer workspace contains unknown or changed output; preserve it.' }
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Force -Recurse -File)) {
            $handle = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
            $handle.Dispose()
        }
        # All descendants match the sealed, task-owned inventory. Native deletion remains
        # fail-closed for directory/CWD holds and read-only files; no Force or fallback.
        Remove-Item -LiteralPath $root -Recurse -ErrorAction Stop
        if (Test-Path -LiteralPath $root) { throw 'Owned workspace reset did not complete.' }
    }
    New-Item -ItemType Directory -Path $root | Out-Null
    return $root
}

function Seal-PlatyObserverWorkspace {
    param([string] $Root, [object[]] $TrustedInputs)
    Assert-PlatyPlainPath $Root
    $receipt = [ordered]@{ schemaVersion = 1; owner = 'PowerForge.Corpus.PlatyPS.Observer.v1'; trustedInputs = @($TrustedInputs); inventory = Get-PlatyWorkspaceInventory $Root }
    [IO.File]::WriteAllText((Join-Path $Root '.owned-output.json'), (ConvertTo-Json -InputObject $receipt -Depth 8), (New-Object Text.UTF8Encoding($false)))
}
