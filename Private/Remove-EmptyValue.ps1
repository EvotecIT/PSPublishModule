﻿function Remove-EmptyValue {
    [alias('Remove-EmptyValues')]
    [CmdletBinding()]
    param(
        [alias('Splat', 'IDictionary')][Parameter(Mandatory)][System.Collections.IDictionary] $Hashtable,
        [string[]] $ExcludeParameter,
        [switch] $Recursive,
        [int] $Rerun,
        [switch] $DoNotRemoveNull,
        [switch] $DoNotRemoveEmpty,
        [switch] $DoNotRemoveEmptyArray,
        [switch] $DoNotRemoveEmptyDictionary
    )
    foreach ($Key in [string[]] $Hashtable.Keys) {
        if ($Key -notin $ExcludeParameter) {
            if ($Recursive) {
                if ($Hashtable[$Key] -is [System.Collections.IDictionary]) {
                    if ($Hashtable[$Key].Count -eq 0) {
                        if (-not $DoNotRemoveEmptyDictionary) {
                            $Hashtable.Remove($Key)
                        }
                    } else {
                        Remove-EmptyValue -Hashtable $Hashtable[$Key] -Recursive:$Recursive
                    }
                } else {
                    if (-not $DoNotRemoveNull -and $null -eq $Hashtable[$Key]) {
                        $Hashtable.Remove($Key)
                    } elseif (-not $DoNotRemoveEmpty -and $Hashtable[$Key] -is [string] -and $Hashtable[$Key] -eq '') {
                        $Hashtable.Remove($Key)
                    } elseif (-not $DoNotRemoveEmptyArray -and $Hashtable[$Key] -is [System.Collections.IList] -and $Hashtable[$Key].Count -eq 0) {
                        $Hashtable.Remove($Key)
                    }
                }
            } else {
                if (-not $DoNotRemoveNull -and $null -eq $Hashtable[$Key]) {
                    $Hashtable.Remove($Key)
                } elseif (-not $DoNotRemoveEmpty -and $Hashtable[$Key] -is [string] -and $Hashtable[$Key] -eq '') {
                    $Hashtable.Remove($Key)
                } elseif (-not $DoNotRemoveEmptyArray -and $Hashtable[$Key] -is [System.Collections.IList] -and $Hashtable[$Key].Count -eq 0) {
                    $Hashtable.Remove($Key)
                }
            }
        }
    }
    if ($Rerun) {
        for ($i = 0; $i -lt $Rerun; $i++) {
            Remove-EmptyValue -Hashtable $Hashtable -Recursive:$Recursive
        }
    }
}