function Get-RequiredModule {
    [cmdletbinding()]
    param(
        [string] $Path,
        [string] $Name
    )
    $PrimaryModule = Get-ChildItem -LiteralPath "$Path\$Name" -Filter '*.psd1' -Recurse -ErrorAction SilentlyContinue -Depth 1
    if ($PrimaryModule) {
        $Module = Get-Module -ListAvailable $PrimaryModule.FullName -ErrorAction SilentlyContinue -Verbose:$false
        if ($Module) {
            [Array] $RequiredModules = $Module.RequiredModules.Name
            if ($null -ne $RequiredModules) {
                $null
            }
            $RequiredModules
            foreach ($_ in $RequiredModules) {
                Get-RequiredModule -Path $Path -Name $_
            }
        }
    } else {
        Write-Warning "Initialize-ModulePortable - Modules to load not found in $Path"
    }
}