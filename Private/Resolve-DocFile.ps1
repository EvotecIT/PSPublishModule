function Resolve-DocFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('README','CHANGELOG','LICENSE','UPGRADE')] [string] $Kind,
        [string] $RootBase,
        [string] $InternalsBase,
        [switch] $PreferInternals
    )
    $root = $null; $intern = $null
    $patterns = @()
    switch ($Kind) {
        'README'    { $patterns = @('README*') }
        'CHANGELOG' { $patterns = @('CHANGELOG*') }
        'LICENSE'   { $patterns = @('LICENSE*','license.txt') }
        'UPGRADE'   { $patterns = @('UPGRADE*','UPGRADING*','MIGRATION*') }
    }
    if ($RootBase) {
        foreach ($p in $patterns) {
            if (-not $root) { $root = Get-ChildItem -LiteralPath $RootBase -Filter $p -File -ErrorAction SilentlyContinue | Select-Object -First 1 }
        }
    }
    if ($InternalsBase) {
        foreach ($p in $patterns) {
            if (-not $intern) { $intern = Get-ChildItem -LiteralPath $InternalsBase -Filter $p -File -ErrorAction SilentlyContinue | Select-Object -First 1 }
        }
    }
    if ($PreferInternals) { if ($intern) { return $intern } else { return $root } }
    else { if ($root) { return $root } else { return $intern } }
}
