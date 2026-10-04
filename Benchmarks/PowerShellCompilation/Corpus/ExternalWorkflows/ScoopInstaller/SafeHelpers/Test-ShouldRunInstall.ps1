function Test-ShouldRunInstall {
    param(
        [String] $InvocationName
    )

    # not dot-sourced, run the install flow
    if ($InvocationName -ne '.') {
        return $true
    }

    # dot-sourced, but not in CI, don't run the install flow
    if (-not $env:CI) {
        return $false
    }

    # dot-sourced, in CI, determined by env SCOOP_NOINSTALL
    $should = $true
    if ($null -ne $env:SCOOP_NOINSTALL) {
        $value = $env:SCOOP_NOINSTALL.ToString().Trim().ToLowerInvariant()
        if ($value -in @('1', 'true', 'yes', 'on')) {
            $should = $false
        }
    }

    return $should
}