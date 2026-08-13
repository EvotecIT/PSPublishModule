function Get-PowerForgeReleaseSourceState {
    <#
    .SYNOPSIS
    Reads tracked and untracked release-input changes from a Git checkout.
    .PARAMETER RepositoryRoot
    Repository checkout to inspect.
    .PARAMETER GeneratedProvenancePath
    The single generated provenance file excluded from release-source state.
    .NOTES
    The exact untracked default public-release receipt is also excluded. Tracked receipt changes remain release inputs.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory)]
        [string] $GeneratedProvenancePath
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $provenance = [IO.Path]::GetFullPath($GeneratedProvenancePath)
    $rootWithSeparator = $root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $rootUri = [Uri] $rootWithSeparator
    $provenanceUri = [Uri] $provenance
    if (-not $rootUri.IsBaseOf($provenanceUri)) {
        throw 'Generated release provenance must stay under the release checkout.'
    }
    $relativeProvenance = [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($provenanceUri).ToString()).Replace('\', '/')
    $relativeDefaultReceipt = 'release-receipts/powerforge-public-release.json'

    $trackedChanges = @(& git -C $root status --porcelain=v1 --untracked-files=no -- . ":(exclude,literal)$relativeProvenance")
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect tracked release inputs.'
    }
    $untrackedInputs = @(& git -C $root ls-files --others --exclude-standard -- . `
        ":(exclude,literal)$relativeProvenance" `
        ":(exclude,top,literal)$relativeDefaultReceipt")
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect untracked release inputs.'
    }
    $changes = @(
        $trackedChanges
        $untrackedInputs | ForEach-Object { "?? $_" }
    )

    [pscustomobject]@{
        SourceDirty = $changes.Count -gt 0
        Changes     = [string[]] $changes
    }
}
