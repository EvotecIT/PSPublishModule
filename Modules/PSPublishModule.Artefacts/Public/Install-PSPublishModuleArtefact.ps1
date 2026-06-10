function Install-PSPublishModuleArtefact {
    <#
    .SYNOPSIS
    Installs a bundled PSPublishModule artefact for the current user.

    .DESCRIPTION
    Installs the Azure Artifacts Credential Provider ZIP payloads shipped with
    PSPublishModule.Artefacts into the current user's NuGet plugin directory.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [string] $Name = 'AzureArtifactsCredentialProvider',

        [ValidateSet('All', 'NetCore', 'NetFx')]
        [string] $Runtime = 'All',

        [switch] $Force
    )

    $selected = Get-PSPublishModuleArtefact -Name $Name | Where-Object {
        $Runtime -eq 'All' -or
        ($Runtime -eq 'NetCore' -and $_.Runtime -eq 'netcore') -or
        ($Runtime -eq 'NetFx' -and $_.Runtime -eq 'netfx')
    }

    foreach ($artefact in @($selected)) {
        if (-not (Test-Path -LiteralPath $artefact.Path -PathType Leaf)) {
            Write-Error -Message "Artefact package '$($artefact.Path)' was not found." -Category ObjectNotFound -ErrorId 'PSPublishModuleArtefactPackageMissing'
            continue
        }

        $actualHash = (Get-FileHash -LiteralPath $artefact.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($artefact.Sha256 -and $actualHash -ne ([string] $artefact.Sha256).ToLowerInvariant()) {
            Write-Error -Message "SHA256 mismatch for '$($artefact.Path)'. Expected $($artefact.Sha256), actual $actualHash." -Category InvalidData -ErrorId 'PSPublishModuleArtefactHashMismatch'
            continue
        }

        $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        if ([string]::IsNullOrWhiteSpace($userProfile)) {
            $userProfile = $env:USERPROFILE
        }
        if ([string]::IsNullOrWhiteSpace($userProfile)) {
            Write-Error -Message 'Unable to resolve the current user profile path.' -Category InvalidOperation -ErrorId 'PSPublishModuleArtefactUserProfileMissing'
            return
        }

        $runtimeFolder = if ($artefact.Runtime -eq 'netfx') { 'netfx' } else { 'netcore' }
        $target = Join-Path $userProfile ".nuget\plugins\$runtimeFolder\CredentialProvider.Microsoft"
        if ((Test-Path -LiteralPath $target) -and -not $Force.IsPresent) {
            Write-Verbose "Skipping existing artefact target '$target'. Use -Force to overwrite."
            continue
        }

        $extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PSPublishModule.Artefacts." + [guid]::NewGuid().ToString('N'))
        try {
            Expand-Archive -LiteralPath $artefact.Path -DestinationPath $extractRoot -Force
            $source = Join-Path $extractRoot "plugins\$runtimeFolder\CredentialProvider.Microsoft"
            if (-not (Test-Path -LiteralPath $source -PathType Container)) {
                Write-Error -Message "Package '$($artefact.Path)' does not contain '$source'." -Category InvalidData -ErrorId 'PSPublishModuleArtefactLayoutInvalid'
                continue
            }

            if ($PSCmdlet.ShouldProcess($target, "Install $Name $($artefact.Runtime)")) {
                if (Test-Path -LiteralPath $target) {
                    Remove-Item -LiteralPath $target -Recurse -Force
                }

                New-Item -ItemType Directory -Path (Split-Path -LiteralPath $target -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $source -Destination $target -Recurse -Force
                Get-Item -LiteralPath $target
            }
        } finally {
            Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
