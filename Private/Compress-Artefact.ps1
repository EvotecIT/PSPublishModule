function Compress-Artefact {
    [CmdletBinding()]
    param(
        [string] $Destination,
        [string] $FileName
    )

    $ZipPath = [System.IO.Path]::Combine($Destination, $FileName)

    Write-TextWithTime -Text "Compressing final merged release $ZipPath" {
        $null = New-Item -ItemType Directory -Path $Destination -Force
        if ($DestinationPaths.Desktop) {
            $CompressPath = [System.IO.Path]::Combine($DestinationPaths.Desktop, '*')
            Compress-Archive -Path $CompressPath -DestinationPath $ZipPath -Force
        } elseif ($DestinationPaths.Core) {
            $CompressPath = [System.IO.Path]::Combine($DestinationPaths.Core, '*')
            Compress-Archive -Path $CompressPath -DestinationPath $ZipPath -Force
        }
    } -PreAppend 'Plus'
}