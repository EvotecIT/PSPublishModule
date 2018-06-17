function Add-ObjectTo($Object, $Type) {
    Write-Color 'Adding ', $Object.Name, ' to ', $Type -Color White, Green, White, Yellow
    return $Object.Name
}
function Add-FilesWithFolders($file, $FullProjectPath, $directory) {
    $LinkPrivatePublicFiles = @()
    $path = $file.FullName.Replace("$FullProjectPath\", '')
    foreach ($dir in $directory) {
        if ($path.StartsWith($dir)) {
            $LinkPrivatePublicFiles += $path
            Write-Color 'Adding file to ', 'linking list', ' of files ', $path -Color White, Yellow, White, Yellow

        }
    }
    return $LinkPrivatePublicFiles
}
function Set-LinkedFiles($LinkFiles, $FullModulePath, $FullProjectPath) {

    foreach ($file in $LinkFiles) {
        $Path = "$FullModulePath\$file"
        $Path2 = "$FullProjectPath\$file"
        Write-Color 'Creating symlink from ', $path2, ' (source) to ', $path, ' (target)' -Color White, Yellow, White, Yellow, White
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
        Write-Color 'Creating directory ', $dir -Color White, Yellow
        $createdDirectory = mkdir $dir
    } else {
        Write-Color 'Creating directory ', $dir, ' skipped.' -Color White, Yellow, Red
    }
}
function Remove-Directory {
    [CmdletBinding()]
    param (
        $dir
    )

    $exists = Test-Path $dir
    if ($exists) {
        Write-Color 'Removing directory ', $dir -Color White, Yellow
        Remove-Item $dir -Confirm:$false -Recurse
    } else {
        Write-Color 'Removing directory ', $dir, ' skipped.' -Color White, Yellow, Red
    }
}


function New-ArrayList {
    [CmdletBinding()]
    param(

    )
    $List = New-Object System.Collections.ArrayList
    return $List
}

function Add-ToArray {
    [CmdletBinding()]
    param(
        $List,
        [ValidateNotNullOrEmpty()][Object] $Element
    )
    Write-Verbose $Element
    $List.Add($Element) > $null
}
function Remove-FromArray {
    param(
        [System.Collections.ArrayList] $List,
        [Object] $Element,
        [switch] $LastElement
    )
    if ($LastElement) {
        $LastID = $List.Count - 1
        $List.RemoveAt($LastID) > $null
    } else {
        $List.Remove($Element) > $null
    }
}