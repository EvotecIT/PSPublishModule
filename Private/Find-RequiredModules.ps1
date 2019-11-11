function Find-RequiredModules {
    param(
        [string] $Name
    )
    $Module = Get-Module -ListAvailable $Name -ErrorAction SilentlyContinue -Verbose:$false
    $AllModules = if ($Module) {
        [Array] $RequiredModules = $Module.RequiredModules.Name
        if ($null -ne $RequiredModules) {
            $null
        }
        $RequiredModules
        foreach ($_ in $RequiredModules) {
            Find-RequiredModules -Path $Path -Name $_
        }
    }

    [Array] $ListModules = $AllModules | Where-Object { $null -ne $_ }
    if ($null -ne $ListModules) {
        [array]::Reverse($ListModules)
    }
    $CleanedModules = [System.Collections.Generic.List[string]]::new()
    foreach ($_ in $ListModules) {
        if ($CleanedModules -notcontains $_) {
            $CleanedModules.Add($_)
        }
    }
    # $CleanedModules.Add($Name)
    $CleanedModules
}