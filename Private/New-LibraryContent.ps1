function New-LibraryContent {
    [CmdletBinding()]
    param(
        [string[]] $LibrariesStandard,
        [string[]] $LibrariesCore,
        [string[]] $LibrariesDefault,
        [System.Collections.IDictionary] $Configuration
    )

    $LibraryContent = @(
        if ($LibrariesStandard.Count -gt 0) {
            :nextFile foreach ($File in $LibrariesStandard) {
                $Extension = $File.Substring($File.Length - 4, 4)
                if ($Extension -eq '.dll') {
                    foreach ($IgnoredFile in $Configuration.Steps.BuildLibraries.IgnoreLibraryOnLoad) {
                        if ($File -like "*\$IgnoredFile") {
                            continue nextFile
                        }
                    }
                    $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File
                    $Output
                }
            }
        } elseif ($LibrariesCore.Count -gt 0 -and $LibrariesDefault.Count -gt 0) {
            'if ($PSEdition -eq ''Core'') {'
            if ($LibrariesCore.Count -gt 0) {
                :nextFile foreach ($File in $LibrariesCore) {
                    $Extension = $File.Substring($File.Length - 4, 4)
                    if ($Extension -eq '.dll') {
                        foreach ($IgnoredFile in $Configuration.Steps.BuildLibraries.IgnoreLibraryOnLoad) {
                            if ($File -like "*\$IgnoredFile") {
                                continue nextFile
                            }
                        }
                        $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File
                        $Output
                    }
                }
            }
            '} else {'
            if ($LibrariesDefault.Count -gt 0) {
                :nextFile foreach ($File in $LibrariesDefault) {
                    $Extension = $File.Substring($File.Length - 4, 4)
                    if ($Extension -eq '.dll') {
                        foreach ($IgnoredFile in $Configuration.Steps.BuildLibraries.IgnoreLibraryOnLoad) {
                            if ($File -like "*\$IgnoredFile") {
                                continue nextFile
                            }
                        }
                        $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File
                        $Output
                    }
                }
            }
            '}'
        } else {
            if ($LibrariesCore.Count -gt 0) {
                :nextFile foreach ($File in $LibrariesCore) {
                    $Extension = $File.Substring($File.Length - 4, 4)
                    if ($Extension -eq '.dll') {
                        foreach ($IgnoredFile in $Configuration.Steps.BuildLibraries.IgnoreLibraryOnLoad) {
                            if ($File -like "*\$IgnoredFile") {
                                continue nextFile
                            }
                        }
                        $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File
                        $Output
                    }
                }
            }
            if ($LibrariesDefault.Count -gt 0) {
                :nextFile foreach ($File in $LibrariesDefault) {
                    $Extension = $File.Substring($File.Length - 4, 4)
                    if ($Extension -eq '.dll') {
                        foreach ($IgnoredFile in $Configuration.Steps.BuildLibraries.IgnoreLibraryOnLoad) {
                            if ($File -like "*\$IgnoredFile") {
                                continue nextFile
                            }
                        }
                        $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File
                        $Output
                    }
                }
            }
        }
    )
    $LibraryContent
}