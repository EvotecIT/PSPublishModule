function Get-NullSeededReferenceState {
    param([bool] $Create)

    $stream = $null
    if ($Create) {
        $stream = [System.IO.MemoryStream]::new()
    }
    if ($null -eq $stream) {
        return 0
    }
    try {
        return 1
    } finally {
        $stream.Dispose()
    }
}

"$(Get-NullSeededReferenceState -Create $false)|$(Get-NullSeededReferenceState -Create $true)"
