function Start-PreparingVariables {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $FullProjectPath
    )
    Write-TextWithTime -Text "Preparing files and folders variables" -PreAppend Plus {
        $LinkDirectories = @()
        $LinkPrivatePublicFiles = @()

        if ($Configuration.Information.Exclude) {
            $Exclude = $Configuration.Information.Exclude
        } else {
            $Exclude = '.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'
        }
        if ($Configuration.Information.IncludeRoot) {
            $IncludeFilesRoot = $Configuration.Information.IncludeRoot
        } else {
            $IncludeFilesRoot = '*.psm1', '*.psd1', 'License*'
        }
        if ($Configuration.Information.IncludePS1) {
            $DirectoriesWithPS1 = $Configuration.Information.IncludePS1
        } else {
            $DirectoriesWithPS1 = 'Classes', 'Private', 'Public', 'Enums'
        }
        # This is basically converting given folder into array of variables
        # mostly done for internal project and testimo
        $DirectoriesWithArrays = $Configuration.Information.IncludeAsArray.Values

        if ($Configuration.Information.IncludeClasses) {
            $DirectoriesWithClasses = $Configuration.Information.IncludeClasses
        } else {
            $DirectoriesWithClasses = 'Classes'
        }
        if ($Configuration.Information.IncludeAll) {
            $DirectoriesWithAll = $Configuration.Information.IncludeAll | ForEach-Object {
                if ($_.EndsWith('\')) {
                    $_
                } else {
                    "$_\"
                }
            }
        } else {
            $DirectoriesWithAll = 'Images\', 'Resources\', 'Templates\', 'Bin\', 'Lib\', 'Data\'
        }

        if ($PSEdition -eq 'core') {
            $Directories = @(
                $TempDirectories = Get-ChildItem -Path $FullProjectPath -Directory -Exclude $Exclude -FollowSymlink
                @(
                    $TempDirectories
                    $TempDirectories | Get-ChildItem -Directory -Recurse -FollowSymlink
                )
            )
            $Files = Get-ChildItem -Path $FullProjectPath -Exclude $Exclude -FollowSymlink | Get-ChildItem -File -Recurse -FollowSymlink
            $FilesRoot = Get-ChildItem -Path "$FullProjectPath\*" -Include $IncludeFilesRoot -File -FollowSymlink
        } else {
            $Directories = @(
                $TempDirectories = Get-ChildItem -Path $FullProjectPath -Directory -Exclude $Exclude
                @(
                    $TempDirectories
                    $TempDirectories | Get-ChildItem -Directory -Recurse
                )
            )
            $Files = Get-ChildItem -Path $FullProjectPath -Exclude $Exclude | Get-ChildItem -File -Recurse
            $FilesRoot = Get-ChildItem -Path "$FullProjectPath\*" -Include $IncludeFilesRoot -File
        }
        $LinkDirectories = @(
            foreach ($directory in $Directories) {
                $RelativeDirectoryPath = (Resolve-Path -LiteralPath $directory.FullName -Relative).Replace('.\', '')
                $RelativeDirectoryPath = "$RelativeDirectoryPath\"
                $RelativeDirectoryPath
            }
        )
        $AllFiles = foreach ($File in $Files) {
            $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
            $RelativeFilePath
        }
        $RootFiles = foreach ($File in $FilesRoot) {
            $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
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
                        # Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory $DirectoriesWithPS1
                        continue
                    }
                    '*.*' {
                        #Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory $DirectoriesWithAll
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
            #RootFiles              = $RootFiles
            #AllFiles               = $AllFiles
            #Directories            = $Directories
            Files                  = $Files
            #Exclude                = $Exclude
            #IncludeFilesRoot       = $IncludeFilesRoot
            DirectoriesWithPS1     = $DirectoriesWithPS1
            #DirectoriesWithArrays  = $DirectoriesWithArrays
            #DirectoriesWithAll     = $DirectoriesWithAll
        }
    }
}