function Resolve-DocFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('README','CHANGELOG')] [string] $Kind,
        [string] $RootBase,
        [string] $InternalsBase,
        [switch] $PreferInternals
    )
    $root = $null; $intern = $null
    if ($RootBase) { $root = Get-ChildItem -LiteralPath $RootBase -Filter ("$Kind*") -File -ErrorAction SilentlyContinue | Select-Object -First 1 }
    if ($InternalsBase) { $intern = Get-ChildItem -LiteralPath $InternalsBase -Filter ("$Kind*") -File -ErrorAction SilentlyContinue | Select-Object -First 1 }
    if ($PreferInternals) { if ($intern) { return $intern } else { return $root } }
    else { if ($root) { return $root } else { return $intern } }
}

