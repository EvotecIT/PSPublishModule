function New-LibraryContent {
    [CmdletBinding()]
    param(
        [string[]] $LibrariesStandard,
        [string[]] $LibrariesCore,
        [string[]] $LibrariesDefault,
        [System.Collections.IDictionary] $Configuration,
        [switch] $OptimizedLoading
    )
    if ($Configuration.Steps.BuildLibraries.HandleAssemblyWithSameName) {
        $Handle = $Configuration.Steps.BuildLibraries.HandleAssemblyWithSameName
    } else {
        $Handle = $false
    }

    if ($OptimizedLoading) {
        $LibraryContent = @(
            if ($LibrariesStandard.Count -gt 0) {
                $Files = :nextFile foreach ($File in $LibrariesStandard) {
                    $Extension = $File.Substring($File.Length - 4, 4)
                    if ($Extension -eq '.dll') {
                        foreach ($IgnoredFile in $Configuration.Steps.BuildLibraries.IgnoreLibraryOnLoad) {
                            if ($File -like "*\$IgnoredFile") {
                                continue nextFile
                            }
                        }
                        $File
                    }
                }
                $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $Files -HandleAssemblyWithSameName $Handle
                $Output
            } elseif ($LibrariesCore.Count -gt 0 -and $LibrariesDefault.Count -gt 0) {
                'if ($PSEdition -eq ''Core'') {'
                if ($LibrariesCore.Count -gt 0) {
                    $Files = :nextFile foreach ($File in $LibrariesCore) {
                        $Extension = $File.Substring($File.Length - 4, 4)
                        if ($Extension -eq '.dll') {
                            foreach ($IgnoredFile in $Configuration.Steps.BuildLibraries.IgnoreLibraryOnLoad) {
                                if ($File -like "*\$IgnoredFile") {
                                    continue nextFile
                                }
                            }
                            $File
                        }
                    }
                    $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $Files -HandleAssemblyWithSameName $Handle
                    $Output
                }
                '} else {'
                if ($LibrariesDefault.Count -gt 0) {
                    $Files = :nextFile foreach ($File in $LibrariesDefault) {
                        $Extension = $File.Substring($File.Length - 4, 4)
                        if ($Extension -eq '.dll') {
                            foreach ($IgnoredFile in $Configuration.Steps.BuildLibraries.IgnoreLibraryOnLoad) {
                                if ($File -like "*\$IgnoredFile") {
                                    continue nextFile
                                }
                            }
                            $File
                        }
                    }
                    $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $Files -HandleAssemblyWithSameName $Handle
                    $Output
                }
                '}'
            } else {
                if ($LibrariesCore.Count -gt 0) {
                    $Files = :nextFile foreach ($File in $LibrariesCore) {
                        $Extension = $File.Substring($File.Length - 4, 4)
                        if ($Extension -eq '.dll') {
                            foreach ($IgnoredFile in $Configuration.Steps.BuildLibraries.IgnoreLibraryOnLoad) {
                                if ($File -like "*\$IgnoredFile") {
                                    continue nextFile
                                }
                            }
                            $File
                        }
                    }
                    $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $Files -HandleAssemblyWithSameName $Handle
                    $Output
                }
                if ($LibrariesDefault.Count -gt 0) {
                    $Files = :nextFile foreach ($File in $LibrariesDefault) {
                        $Extension = $File.Substring($File.Length - 4, 4)
                        if ($Extension -eq '.dll') {
                            foreach ($IgnoredFile in $Configuration.Steps.BuildLibraries.IgnoreLibraryOnLoad) {
                                if ($File -like "*\$IgnoredFile") {
                                    continue nextFile
                                }
                            }
                            $File
                        }
                    }
                    $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $Files -HandleAssemblyWithSameName $Handle
                    $Output
                }
            }
        )
    } else {

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
                        $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File -HandleAssemblyWithSameName $Handle
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
                            $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File -HandleAssemblyWithSameName $Handle
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
                            $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File -HandleAssemblyWithSameName $Handle
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
                            $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File -HandleAssemblyWithSameName $Handle
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
                            $Output = New-DLLCodeOutput -DebugDLL $Configuration.Steps.BuildModule.DebugDLL -File $File -HandleAssemblyWithSameName $Handle
                            $Output
                        }
                    }
                }
            }
        )
    }
    $LibraryContent
}