function Exit-Install {
    param(
        [Int] $ErrorCode = 1
    )

    if ((-not $env:CI) -and $IS_EXECUTED_FROM_IEX) {
        # Don't abort with `exit` that would close the interactive PS session
        # if invoked with iex, yet set `LASTEXITCODE` for the caller to check
        $Global:LASTEXITCODE = $ErrorCode
        break
    } else {
        exit $ErrorCode
    }
}