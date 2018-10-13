function Add-ObjectTo {
    [CmdletBinding()]
    param($Object, $Type)
    #Write-Color 'Adding ', $Object.Name, ' to ', $Type -Color White, Green, White, Yellow
    Write-Verbose "Adding $($Object.Name) to $Type"
    return $Object.Name
}
function Add-FilesWithFolders {
    [CmdletBinding()]
    param ($file, $FullProjectPath, $directory)
    $LinkPrivatePublicFiles = @()
    $path = $file.FullName.Replace("$FullProjectPath\", '')
    foreach ($dir in $directory) {
        if ($path.StartsWith($dir)) {
            $LinkPrivatePublicFiles += $path
            Write-Verbose "Adding file to linking list of files $path"
            # Write-Color 'Adding file to ', 'linking list', ' of files ', $path -Color White, Yellow, White, Yellow

        }
    }
    return $LinkPrivatePublicFiles
}
function Set-LinkedFiles {
    [CmdletBinding()]
    param(
        $LinkFiles,
        $FullModulePath,
        $FullProjectPath,
        [switch] $Delete
    )

    foreach ($file in $LinkFiles) {
        $Path = "$FullModulePath\$file"
        $Path2 = "$FullProjectPath\$file"

        if ($Delete) {
            if (Test-ReparsePoint -path $Path) {
                #  Write-Color 'Removing symlink first ', $path  -Color White, Yellow
                Write-Verbose "Removing symlink first $path"
                Remove-Item $Path -Confirm:$false
            }

        }
        Write-Verbose "Creating symlink from $path2 (source) to $path (target)"
        #Write-Color 'Creating symlink from ', $path2, ' (source) to ', $path, ' (target)' -Color White, Yellow, White, Yellow, White
        $linkingFiles = cmd /c mklink $path $path2
    }
}
function Add-Directory {
    [CmdletBinding()]
    param(
        $dir
    )

    $exists = Test-Path -Path $dir
    if ($exists -eq $false) {
        #Write-Color 'Creating directory ', $dir -Color White, Yellow
        $createdDirectory = mkdir $dir
    } else {
        #Write-Color 'Creating directory ', $dir, ' skipped.' -Color White, Yellow, Red
    }
}
function Remove-Directory {
    [CmdletBinding()]
    param (
        $dir
    )

    $exists = Test-Path $dir
    if ($exists) {
        #Write-Color 'Removing directory ', $dir -Color White, Yellow
        Remove-Item $dir -Confirm:$false -Recurse
    } else {
        #Write-Color 'Removing directory ', $dir, ' skipped.' -Color White, Yellow, Red
    }
}

function Test-ReparsePoint {
    [CmdletBinding()]
    param (
        [string]$path
    )
    $file = Get-Item $path -Force -ea SilentlyContinue
    return [bool]($file.Attributes -band [IO.FileAttributes]::ReparsePoint)
}

function Find-EnumsList {
    [CmdletBinding()]
    param (
        [string] $ProjectPath
    )

    $Enums = @( Get-ChildItem -Path $ProjectPath\Enums\*.ps1 -ErrorAction SilentlyContinue )
    #Write-Verbose "Find-EnumsList - $ProjectPath\Enums"

    $Opening = '@('
    $Closing = ')'
    $Adding = ','

    $EnumsList = New-ArrayList
    Add-ToArray -List $EnumsList -Element $Opening
    Foreach ($import in @($Enums)) {
        $Entry = "'Enums\$($import.Name)'"
        Add-ToArray -List $EnumsList -Element $Entry
        Add-ToArray -List $EnumsList -Element $Adding
    }
    Remove-FromArray -List $EnumsList -LastElement
    Add-ToArray -List $EnumsList -Element $Closing
    return [string] $EnumsList
}
