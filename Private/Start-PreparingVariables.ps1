function Start-PreparingVariables {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $FullProjectPath
    )
    Write-TextWithTime -Text "Preparing files and folders variables" -PreAppend Plus {
        $LinkDirectories = @()
        $LinkPrivatePublicFiles = @()

        if ($null -ne $Configuration.Information.Exclude) {
            $Exclude = $Configuration.Information.Exclude
        } else {
            $Exclude = '.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'
        }
        if ($null -ne $Configuration.Information.IncludeRoot) {
            $IncludeFilesRoot = $Configuration.Information.IncludeRoot
        } else {
            $IncludeFilesRoot = '*.psm1', '*.psd1', 'License*'
        }
        if ($null -ne $Configuration.Information.IncludePS1) {
            $DirectoriesWithPS1 = $Configuration.Information.IncludePS1
        } else {
            $DirectoriesWithPS1 = 'Classes', 'Private', 'Public', 'Enums'
        }
        # This is basically converting given folder into array of variables
        # mostly done for internal project and testimo
        $DirectoriesWithArrays = $Configuration.Information.IncludeAsArray.Values

        if ($null -ne $Configuration.Information.IncludeClasses) {
            $DirectoriesWithClasses = $Configuration.Information.IncludeClasses
        } else {
            $DirectoriesWithClasses = 'Classes'
        }
        if ($null -ne $Configuration.Information.IncludeAll) {
            $DirectoriesWithAll = $Configuration.Information.IncludeAll | ForEach-Object {
                if ($null -eq $IsWindows -or $IsWindows -eq $true) {
                    if ($_.EndsWith('\')) {
                        $_
                    } else {
                        "$_\"
                    }
                } else {
                    if ($_.EndsWith('/')) {
                        $_
                    } else {
                        "$_/"
                    }

                }
            }
        } else {
            if ($null -eq $IsWindows -or $IsWindows -eq $true) {
                $DirectoriesWithAll = 'Images\', 'Resources\', 'Templates\', 'Bin\', 'Lib\', 'Data\'
            } else {
                $DirectoriesWithAll = 'Images/', 'Resources/', 'Templates/', 'Bin/', 'Lib/', 'Data/'
            }
        }

        $Path = [io.path]::Combine($FullProjectPath, '*')
        if ($PSEdition -eq 'core') {
            $Directories = @(
                $TempDirectories = Get-ChildItem -Path $FullProjectPath -Directory -Exclude $Exclude -FollowSymlink
                @(
                    $TempDirectories
                    $TempDirectories | Get-ChildItem -Directory -Recurse -FollowSymlink
                )
            )
            $Files = Get-ChildItem -Path $FullProjectPath -Exclude $Exclude -FollowSymlink | Get-ChildItem -File -Recurse -FollowSymlink
            $FilesRoot = Get-ChildItem -Path $Path -Include $IncludeFilesRoot -File -FollowSymlink
        } else {
            $Directories = @(
                $TempDirectories = Get-ChildItem -Path $FullProjectPath -Directory -Exclude $Exclude
                @(
                    $TempDirectories
                    $TempDirectories | Get-ChildItem -Directory -Recurse
                )
            )
            $Files = Get-ChildItem -Path $FullProjectPath -Exclude $Exclude | Get-ChildItem -File -Recurse
            $FilesRoot = Get-ChildItem -Path $Path -Include $IncludeFilesRoot -File
        }
        $LinkDirectories = @(
            foreach ($Directory in $Directories) {
                if ($null -eq $IsWindows -or $IsWindows -eq $true) {
                    $RelativeDirectoryPath = (Resolve-Path -LiteralPath $Directory.FullName -Relative).Replace('.\', '')
                    $RelativeDirectoryPath = "$RelativeDirectoryPath\"
                } else {
                    $RelativeDirectoryPath = (Resolve-Path -LiteralPath $Directory.FullName -Relative).Replace('./', '')
                    $RelativeDirectoryPath = "$RelativeDirectoryPath/"
                }
                $RelativeDirectoryPath
            }
        )
        $AllFiles = foreach ($File in $Files) {
            if ($null -eq $IsWindows -or $IsWindows -eq $true) {
                $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
            } else {
                $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('./', '')
            }
            $RelativeFilePath
        }
        $RootFiles = foreach ($File in $FilesRoot) {
            if ($null -eq $IsWindows -or $IsWindows -eq $true) {
                $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
            } else {
                $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('./', '')
            }
            $RelativeFilePath
        }
        # Link only files in Root Directory
        $LinkFilesRoot = @(
            foreach ($File in $RootFiles | Sort-Object -Unique) {
                switch -Wildcard ($file) {
                    '*.psd1' {
                        $File
                    }
                    '*.psm1' {
                        $File
                    }
                    'License*' {
                        $File
                    }
                }
            }
        )
        # Link only files from subfolers
        $LinkPrivatePublicFiles = @(
            foreach ($file in $AllFiles | Sort-Object -Unique) {
                switch -Wildcard ($file) {
                    '*.ps1' {
                        foreach ($dir in $DirectoriesWithPS1) {
                            if ($file -like "$dir*") {
                                $file
                            }
                        }
                        foreach ($dir in $DirectoriesWithArrays) {
                            if ($file -like "$dir*") {
                                $file
                            }
                        }
                        continue
                    }
                    '*.*' {
                        foreach ($dir in $DirectoriesWithAll) {
                            if ($file -like "$dir*") {
                                $file
                            }
                        }
                        continue
                    }
                }
            }
        )
        $LinkPrivatePublicFiles = $LinkPrivatePublicFiles | Select-Object -Unique

        [ordered] @{
            LinkDirectories        = $LinkDirectories
            LinkFilesRoot          = $LinkFilesRoot
            LinkPrivatePublicFiles = $LinkPrivatePublicFiles
            DirectoriesWithClasses = $DirectoriesWithClasses
            Files                  = $Files
            DirectoriesWithPS1     = $DirectoriesWithPS1
        }
    }
}