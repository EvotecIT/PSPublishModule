function New-DLLCodeOutput {
    [CmdletBinding()]
    param(
        [string[]] $File,
        [bool] $DebugDLL,
        [alias('NETHandleAssemblyWithSameName')][bool] $HandleAssemblyWithSameName
    )
    if ($File.Count -gt 1) {
        if ($DebugDLL) {
            @(
                "`$LibrariesToLoad = @("
                foreach ($F in $File) {
                    "   '$F'"
                }
                ")"
                "`$FoundErrors = @("
                "       foreach (`$L in `$LibrariesToLoad) {"
                "           try {"
                "               Add-Type -Path `$PSScriptRoot\`$L -ErrorAction Stop"
                "           } catch [System.Reflection.ReflectionTypeLoadException] {"
                "               Write-Warning `"Processing `$(`$ImportName) Exception: `$(`$_.Exception.Message)`""
                "               `$LoaderExceptions = `$(`$_.Exception.LoaderExceptions) | Sort-Object -Unique",
                "               foreach (`$E in `$LoaderExceptions) {",
                "                   Write-Warning `"Processing `$(`$ImportName) LoaderExceptions: `$(`$E.Message)`""
                "               }"
                "               `$true"
                "           } catch {"
                if ($HandleAssemblyWithSameName) {
                    "               if (`$_.Exception.Message -like '*Assembly with same name is already loaded*') {"
                    "                   Write-Warning -Message `"Assembly with same name is already loaded. Ignoring '`$L'.`""
                    "               } else {"
                    "                   Write-Warning `"Processing `$(`$ImportName) Exception: `$(`$_.Exception.Message)`"",
                    "                   `$LoaderExceptions = `$(`$_.Exception.LoaderExceptions) | Sort-Object -Unique",
                    "                   foreach (`$E in `$LoaderExceptions) {"
                    "                       Write-Warning `"Processing `$(`$ImportName) LoaderExceptions: `$(`$E.Message)`""
                    "                   }"
                    "                   `$true"
                    "               }"
                } else {
                    "               Write-Warning `"Processing `$(`$ImportName) Exception: `$(`$_.Exception.Message)`"",
                    "               `$LoaderExceptions = `$(`$_.Exception.LoaderExceptions) | Sort-Object -Unique",
                    "               foreach (`$E in `$LoaderExceptions) {"
                    "                   Write-Warning `"Processing `$(`$ImportName) LoaderExceptions: `$(`$E.Message)`""
                    "               }"
                    "               `$true"
                }
                "           }"
                "       }"
                ")"
                "if (`$FoundErrors.Count -gt 0) {"
                "    Write-Warning `"Importing module failed. Fix errors before continuing.`""
                "    break"
                "}"
            )
        } else {
            if ($HandleAssemblyWithSameName) {
                $Output = @(
                    "`$LibrariesToLoad = @("
                    foreach ($F in $File) {
                        "    '$F'"
                    }
                    ")"
                    "foreach (`$L in `$LibrariesToLoad) {"
                    '   try {'
                    '       Add-Type -Path $PSScriptRoot\$L -ErrorAction Stop'
                    '   } catch {'
                    "       if (`$_.Exception.Message -like '*Assembly with same name is already loaded*') {"
                    "           Write-Warning -Message `"Assembly with same name is already loaded. Ignoring '`$L'.`""
                    '       } else {'
                    '           throw $_'
                    '       }'
                    '   }'
                    "}"
                )
            } else {
                $Output = @(
                    "`$LibrariesToLoad = @("
                    foreach ($F in $File) {
                        "    '$F'"
                    }
                    ")"
                    "foreach (`$L in `$LibrariesToLoad) {"
                    "    Add-Type -Path `$PSScriptRoot\`$L"
                    "}"
                )
            }
        }
        $Output
    } else {
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
            if ($HandleAssemblyWithSameName) {
                $Output = @"
            try {
                Add-Type -Path `$PSScriptRoot\$File -ErrorAction Stop
            } catch {
                if (`$_.Exception.Message -like '*Assembly with same name is already loaded*') {
                    Write-Warning -Message `"Assembly with same name is already loaded. Ignoring '`$(`$_.InvocationInfo.Statement)'."
                } else {
                    throw `$_
                }
            }
"@
            } else {
                $Output = 'Add-Type -Path $PSScriptRoot\' + $File
            }
        }
        $Output
    }
}