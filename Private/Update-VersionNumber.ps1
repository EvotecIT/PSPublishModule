function Update-VersionNumber {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$Version,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Major', 'Minor', 'Build', 'Revision')]
        [string]$Type
    )

    $versionParts = $Version -split '\.'

    while ($versionParts.Count -lt 3) {
        $versionParts += "0"
    }

    if ($Type -eq 'Revision' -and $versionParts.Count -lt 4) {
        $versionParts += "0"
    }

    switch ($Type) {
        'Major' {
            $versionParts[0] = [string]([int]$versionParts[0] + 1)
            $versionParts[1] = "0"
            $versionParts[2] = "0"
            if ($versionParts.Count -gt 3) {
                $versionParts[3] = "0"
            }
        }
        'Minor' {
            $versionParts[1] = [string]([int]$versionParts[1] + 1)
            $versionParts[2] = "0"
            if ($versionParts.Count -gt 3) {
                $versionParts[3] = "0"
            }
        }
        'Build' {
            $versionParts[2] = [string]([int]$versionParts[2] + 1)
            if ($versionParts.Count -gt 3) {
                $versionParts[3] = "0"
            }
        }
        'Revision' {
            if ($versionParts.Count -lt 4) {
                $versionParts += "1"
            } else {
                $versionParts[3] = [string]([int]$versionParts[3] + 1)
            }
        }
    }

    $newVersion = $versionParts -join '.'
    $versionPartCount = ($Version -split '\.' | Measure-Object).Count
    if ($versionPartCount -eq 3 -and $Type -ne 'Revision') {
        $newVersion = ($versionParts | Select-Object -First 3) -join '.'
    }

    return $newVersion
}