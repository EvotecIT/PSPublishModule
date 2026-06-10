function Get-PSPublishModuleArtefact {
    <#
    .SYNOPSIS
    Lists artefacts bundled with PSPublishModule.Artefacts.

    .DESCRIPTION
    Reads generated artefact manifests shipped with PSPublishModule.Artefacts and
    returns package paths, runtime labels, versions, source URLs, and hashes.
    #>
    [CmdletBinding()]
    param(
        [string] $Name = 'AzureArtifactsCredentialProvider'
    )

    $manifestPath = Join-Path $script:ModuleRoot "Artefacts\$Name\manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        Write-Error -Message "Artefact manifest '$manifestPath' was not found. Rebuild PSPublishModule.Artefacts with its Build-Module.ps1 script." -Category ObjectNotFound -ErrorId 'PSPublishModuleArtefactManifestMissing'
        return
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($file in @($manifest.files)) {
        [pscustomobject]@{
            Name         = $manifest.name
            Version      = $manifest.version
            Runtime      = $file.runtime
            Path         = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -LiteralPath $manifestPath -Parent) $file.path))
            Sha256       = $file.sha256
            Source       = $manifest.source
            License      = $manifest.license
            ManifestPath = $manifestPath
        }
    }
}
