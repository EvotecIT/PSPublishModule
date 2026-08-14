function Test-PowerForgeTrackedReleaseReceipt {
    <#
    .SYNOPSIS
    Tests whether a release receipt is tracked by the repository.

    .DESCRIPTION
    Converts an in-checkout receipt to a repository-relative literal Git pathspec before querying the index.
    The implementation uses APIs shared by Windows PowerShell 5.1 and PowerShell 7.

    .PARAMETER RepositoryRoot
    Root directory of the Git checkout.

    .PARAMETER ReceiptPath
    Full or relative path to the candidate release receipt.

    .EXAMPLE
    Test-PowerForgeTrackedReleaseReceipt -RepositoryRoot C:\Source\Project -ReceiptPath C:\Source\Project\release-receipts\release.json
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory)]
        [string] $ReceiptPath
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $receipt = [IO.Path]::GetFullPath($ReceiptPath)
    $rootUri = [Uri] ($root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar)
    $receiptUri = [Uri] $receipt
    if (-not $rootUri.IsBaseOf($receiptUri)) {
        return $false
    }

    $relativeReceipt = [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($receiptUri).ToString()).Replace('\', '/')
    & git -C $root ls-files --error-unmatch -- ":(literal)$relativeReceipt" *> $null
    return $LASTEXITCODE -eq 0
}
