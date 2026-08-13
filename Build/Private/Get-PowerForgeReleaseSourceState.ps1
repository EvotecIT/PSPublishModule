function Get-PowerForgeReleaseSourceState {
    <#
    .SYNOPSIS
    Reads tracked and untracked release-input changes from a Git checkout.
    .PARAMETER RepositoryRoot
    Repository checkout to inspect.
    .PARAMETER GeneratedProvenancePath
    The single generated provenance file excluded from release-source state.
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

    $changes = @(& git -C $root status --porcelain=v1 --untracked-files=all -- . ":(exclude,literal)$relativeProvenance")
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the release checkout.'
    }

    [pscustomobject]@{
        SourceDirty = $changes.Count -gt 0
        Changes     = [string[]] $changes
    }
}
