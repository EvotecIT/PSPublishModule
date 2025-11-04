function Copy-PSPDirectoryTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][string] $Destination,
        [switch] $Overwrite
    )
    if (-not (Test-Path -LiteralPath $Source)) { return }
    if (-not (Test-Path -LiteralPath $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }
    $items = Get-ChildItem -LiteralPath $Source -Force -ErrorAction SilentlyContinue
    foreach ($item in $items) {
        $target = Join-Path $Destination $item.Name
        if ($item.PSIsContainer) {
            Copy-PSPDirectoryTree -Source $item.FullName -Destination $target -Overwrite:$Overwrite.IsPresent
        } else {
            try {
                Copy-Item -LiteralPath $item.FullName -Destination $target -Force:$Overwrite.IsPresent -ErrorAction Stop
            } catch {
                if (-not (Test-Path -LiteralPath $target)) { throw }
                # If not overwriting and file exists, skip silently
            }
        }
    }
}

