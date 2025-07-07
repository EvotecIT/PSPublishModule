function Get-RelativePath {
    <#
    .SYNOPSIS
        Gets the relative path from one path to another, compatible with PowerShell 5.1.

    .DESCRIPTION
        Provides PowerShell 5.1 compatible relative path calculation that works like
        [System.IO.Path]::GetRelativePath() which is only available in .NET Core 2.0+.

    .PARAMETER From
        The base path to calculate the relative path from.

    .PARAMETER To
        The target path to calculate the relative path to.

    .EXAMPLE
        Get-RelativePath -From 'C:\Projects' -To 'C:\Projects\MyProject\file.txt'
        Returns: MyProject\file.txt
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $From,

        [Parameter(Mandatory)]
        [string] $To
    )

    # Use .NET Core method if available (PowerShell 7+)
    if ([System.IO.Path].GetMethods() | Where-Object { $_.Name -eq 'GetRelativePath' -and $_.IsStatic }) {
        return [System.IO.Path]::GetRelativePath($From, $To)
    }

    # PowerShell 5.1 compatible implementation
    try {
        # Use New-Object for PS 5.1 compatibility instead of ::new()
        $fromPath = [System.IO.Path]::GetFullPath($From)
        if (-not $fromPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
            $fromPath += [System.IO.Path]::DirectorySeparatorChar
        }
        $fromUri = New-Object System.Uri $fromPath
        $toUri = New-Object System.Uri ([System.IO.Path]::GetFullPath($To))

        $relativeUri = $fromUri.MakeRelativeUri($toUri)
        $relativePath = [System.Uri]::UnescapeDataString($relativeUri.ToString())

        # Convert forward slashes to backslashes on Windows
        if ([System.IO.Path]::DirectorySeparatorChar -eq '\') {
            $relativePath = $relativePath.Replace('/', '\')
        }

        return $relativePath
    } catch {
        # Fallback: just return the filename if relative path calculation fails
        return [System.IO.Path]::GetFileName($To)
    }
}
