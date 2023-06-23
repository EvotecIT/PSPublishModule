function Initialize-InternalTests {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $Type
    )

    if ($Configuration.Options.$Type.TestsPath -and (Test-Path -LiteralPath $Configuration.Options.$Type.TestsPath)) {
        Write-TextWithTime -PreAppend Plus -Text "Running tests ($Type)" {
            $TestsResult = Invoke-Pester -Script $Configuration.Options.$Type.TestsPath -Verbose -PassThru
            Write-Host
            if (-not $TestsResult) {
                if ($Configuration.Options.$Type.Force) {
                    Write-Text "[e] Tests ($Type) failed, but Force was used to skip failed tests. Continuing" -Color Red
                } else {
                    Write-Text "[e] Tests ($Type) failed. Terminating." -Color Red
                    return $false
                }
            } elseif ($TestsResult.FailedCount -gt 0) {
                if ($Configuration.Options.$Type.Force) {
                    Write-Text "[e] Tests ($Type) failed, but Force was used to skip failed tests. Continuing" -Color Red
                } else {
                    Write-Text "[e] Tests ($Type) failed (failedCount $($TestsResult.FailedCount)). Terminating." -Color Red
                    return $false
                }
            }
        } -Color Blue
    } else {
        Write-Text "[e] Tests ($Type) are enabled, but the path to tests doesn't exits. Terminating." -Color Red
        return $false
    }
}