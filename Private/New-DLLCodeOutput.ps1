function New-DLLCodeOutput {
    [CmdletBinding()]
    param(
        [string] $File,
        [bool] $DebugDLL
    )
    if ($DebugDLL) {
        $Output = @"
    `$FoundErrors = @(
        try {
            `$ImportName = "`$PSScriptRoot\$File"
            Add-Type -Path `$ImportName -ErrorAction Stop
        } catch [System.Reflection.ReflectionTypeLoadException] {
            Write-Warning "Processing `$(`$ImportName) Exception: `$(`$_.Exception.Message)"
            `$LoaderExceptions = `$(`$_.Exception.LoaderExceptions) | Sort-Object -Unique
            foreach (`$E in `$LoaderExceptions) {
                Write-Warning "Processing `$(`$ImportName) LoaderExceptions: `$(`$E.Message)"
            }
           `$true
        } catch {
            Write-Warning "Processing `$(`$ImportName) Exception: `$(`$_.Exception.Message)"
            `$LoaderExceptions = `$(`$_.Exception.LoaderExceptions) | Sort-Object -Unique
            foreach (`$E in `$LoaderExceptions) {
                Write-Warning "Processing `$(`$ImportName) LoaderExceptions: `$(`$E.Message)"
            }
            `$true
        }
    )
    if (`$FoundErrors.Count -gt 0) {
        Write-Warning "Importing module failed. Fix errors before continuing."
        break
    }
"@
    } else {
        $Output = 'Add-Type -Path $PSScriptRoot\' + $File
    }
    $Output
}