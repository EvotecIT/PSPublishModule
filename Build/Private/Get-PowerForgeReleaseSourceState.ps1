function Get-PowerForgeReleaseSourceState {
    <#
    .SYNOPSIS
    Reads tracked and untracked release-input changes from a Git checkout.
    .PARAMETER RepositoryRoot
    Repository checkout to inspect.
    .PARAMETER GeneratedProvenancePath
    The single generated provenance file excluded from release-source state.
    .PARAMETER ReceiptPath
    Resolved public-release receipt path. An untracked receipt is excluded only when it is under the dedicated release-receipts directory.
    .PARAMETER GeneratedConfigurationPath
    Deterministic effective-configuration output created by the public-release wrapper after this preflight. Prior untracked outputs in the same reserved namespace are also excluded.
    .NOTES
    The exact untracked public-release receipt and deterministic authorized wrapper configurations are excluded. Tracked changes remain release inputs.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory)]
        [string] $GeneratedProvenancePath,

        [Parameter(Mandatory)]
        [string] $ReceiptPath,

        [string] $GeneratedConfigurationPath
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
    $receipt = [IO.Path]::GetFullPath($ReceiptPath)
    $receiptUri = [Uri] $receipt
    $relativeReceipt = $null
    if ($rootUri.IsBaseOf($receiptUri)) {
        $receiptRoot = [IO.Path]::GetFullPath((Join-Path $root 'release-receipts'))
        $receiptRootUri = [Uri] ($receiptRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar)
        if (-not $receiptRootUri.IsBaseOf($receiptUri)) {
            throw 'An in-checkout release receipt must stay under the dedicated release-receipts directory.'
        }
        $relativeReceipt = [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($receiptUri).ToString()).Replace('\', '/')
    }

    $relativeGeneratedConfiguration = $null
    $relativeGeneratedConfigurationDirectory = $null
    if (-not [string]::IsNullOrWhiteSpace($GeneratedConfigurationPath)) {
        $generatedConfiguration = [IO.Path]::GetFullPath($GeneratedConfigurationPath)
        $generatedConfigurationUri = [Uri] $generatedConfiguration
        if (-not $rootUri.IsBaseOf($generatedConfigurationUri)) {
            throw 'Generated authorized release configuration must stay under the release checkout.'
        }
        if ([IO.Path]::GetFileName($generatedConfiguration) -notmatch '^\.release\.authorized\.\d+\.\d+\.\d+\.[0-9a-fA-F]{40}\.json$') {
            throw 'Generated authorized release configuration must use the deterministic .release.authorized.<version>.<commit>.json name.'
        }
        $relativeGeneratedConfiguration = [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($generatedConfigurationUri).ToString()).Replace('\', '/')
        $relativeGeneratedConfigurationDirectory = [IO.Path]::GetDirectoryName($relativeGeneratedConfiguration).Replace('\', '/')
    }

    $trackedChanges = @(& git -C $root status --porcelain=v1 --untracked-files=no -- . ":(exclude,literal)$relativeProvenance")
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect tracked release inputs.'
    }
    $untrackedPathspecs = [Collections.Generic.List[string]]::new()
    $untrackedPathspecs.Add('.')
    $untrackedPathspecs.Add(":(exclude,literal)$relativeProvenance")
    if (-not [string]::IsNullOrWhiteSpace($relativeReceipt)) {
        $untrackedPathspecs.Add(":(exclude,top,literal)$relativeReceipt")
    }
    $untrackedInputs = @(& git -C $root ls-files --others --exclude-standard -- @untrackedPathspecs)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect untracked release inputs.'
    }
    if (-not [string]::IsNullOrWhiteSpace($relativeGeneratedConfiguration)) {
        $untrackedInputs = @($untrackedInputs | Where-Object {
            $candidate = ([string] $_).Replace('\', '/')
            $candidateDirectory = [IO.Path]::GetDirectoryName($candidate).Replace('\', '/')
            $candidateName = [IO.Path]::GetFileName($candidate)
            -not ($candidateDirectory -ieq $relativeGeneratedConfigurationDirectory -and
                $candidateName -match '^\.release\.authorized\.\d+\.\d+\.\d+\.[0-9a-fA-F]{40}\.json$')
        })
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
