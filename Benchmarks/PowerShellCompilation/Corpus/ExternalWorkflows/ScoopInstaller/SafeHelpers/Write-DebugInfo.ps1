function Write-DebugInfo {
    param($BoundArgs)

    Write-Verbose '-------- PSBoundParameters --------'
    $BoundArgs.GetEnumerator() | ForEach-Object { Write-Verbose $_ }
    Write-Verbose '-------- Environment Variables --------'
    Write-Verbose "`$env:USERPROFILE: $env:USERPROFILE"
    Write-Verbose "`$env:ProgramData: $env:ProgramData"
    Write-Verbose "`$env:SCOOP: $env:SCOOP"
    Write-Verbose "`$env:SCOOP_CACHE: $SCOOP_CACHE"
    Write-Verbose "`$env:SCOOP_GLOBAL: $env:SCOOP_GLOBAL"
    Write-Verbose '-------- Selected Variables --------'
    Write-Verbose "SCOOP_DIR: $SCOOP_DIR"
    Write-Verbose "SCOOP_CACHE_DIR: $SCOOP_CACHE_DIR"
    Write-Verbose "SCOOP_GLOBAL_DIR: $SCOOP_GLOBAL_DIR"
    Write-Verbose "SCOOP_CONFIG_HOME: $SCOOP_CONFIG_HOME"
}