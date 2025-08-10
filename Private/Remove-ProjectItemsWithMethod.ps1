function Remove-ProjectItemsWithMethod {
    <#
    .SYNOPSIS
    Removes project items using the specified deletion method.

    .DESCRIPTION
    Processes an array of items for deletion using Remove-FileItem with the specified
    deletion method and retry logic. Provides detailed results for each item.

    .PARAMETER ItemsToProcess
    Array of items to remove.

    .PARAMETER DeleteMethod
    Deletion method to use (RemoveItem, DotNetDelete, RecycleBin).

    .PARAMETER Retries
    Number of retry attempts.

    .PARAMETER ShowProgress
    Whether to show progress information.

    .PARAMETER Internal
    Whether to use internal (verbose) messaging.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [PSObject[]] $ItemsToProcess,

        [Parameter(Mandatory)]
        [ValidateSet('RemoveItem', 'DotNetDelete', 'RecycleBin')]
        [string] $DeleteMethod,

        [int] $Retries = 3,

        [bool] $ShowProgress = $false,

        [bool] $Internal = $false
    )

    $resultsList = [System.Collections.Generic.List[PSObject]]::new()
    $totalItems = $ItemsToProcess.Count
    $currentItem = 0

    foreach ($item in $ItemsToProcess) {
        $currentItem++

        $result = [PSCustomObject]@{
            RelativePath = $item.RelativePath
            FullPath = $item.FullPath
            Type = $item.Type
            Pattern = $item.Pattern
            Status = 'Unknown'
            Size = $item.Size
            Error = $null
        }

        try {
            if ($WhatIfPreference) {
                $result.Status = 'WhatIf'
                if ($ShowProgress -and -not $Internal) {
                    Write-Host "  [WOULD REMOVE] $($item.RelativePath)" -ForegroundColor Yellow
                } elseif ($Internal) {
                    Write-Verbose "Would remove: $($item.RelativePath)"
                }
            } else {
                # Use Remove-FileItem to delete the item
                $removeParams = @{
                    Paths = $item.FullPath
                    DeleteMethod = $DeleteMethod
                    Retries = $Retries
                    Recursive = ($item.Type -eq 'Folder')
                    SimpleReturn = $true
                }

                $deleteResult = Remove-FileItem @removeParams

                if ($deleteResult) {
                    $result.Status = 'Removed'

                    if ($ShowProgress -and -not $Internal) {
                        Write-Host "  [$currentItem/$totalItems] [REMOVED] $($item.RelativePath)" -ForegroundColor Red
                    } elseif ($Internal) {
                        Write-Verbose "Removed: $($item.RelativePath)"
                    }
                } else {
                    $result.Status = 'Failed'
                    $result.Error = 'Remove-FileItem returned false'

                    if ($ShowProgress -and -not $Internal) {
                        Write-Host "  [$currentItem/$totalItems] [FAILED] $($item.RelativePath)" -ForegroundColor Red
                    } elseif ($Internal) {
                        Write-Warning "Failed to remove: $($item.RelativePath)"
                    }
                }
            }
        } catch {
            $result.Status = 'Error'
            $result.Error = $_.Exception.Message

            if ($ShowProgress -and -not $Internal) {
                Write-Host "  [$currentItem/$totalItems] [ERROR] $($item.RelativePath): $($_.Exception.Message)" -ForegroundColor Red
            } elseif ($Internal) {
                Write-Warning "Error removing $($item.RelativePath): $($_.Exception.Message)"
            } else {
                Write-Warning "Failed to remove '$($item.RelativePath)': $($_.Exception.Message)"
            }
        }

        $resultsList.Add($result)
    }

    return $resultsList.ToArray()
}
