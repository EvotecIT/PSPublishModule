function Write-PowerShellHashtable {
    [cmdletbinding()]
    <#
    .Synopsis
        Takes an creates a script to recreate a hashtable
    .Description
        Allows you to take a hashtable and create a hashtable you would embed into a script.

        Handles nested hashtables and indents nested hashtables automatically.
    .Parameter inputObject
        The hashtable to turn into a script
    .Parameter scriptBlock
        Determines if a string or a scriptblock is returned
    .Example
        # Corrects the presentation of a PowerShell hashtable
        @{Foo='Bar';Baz='Bing';Boo=@{Bam='Blang'}} | Write-PowerShellHashtable
    .Outputs
        [string]
    .Outputs
        [ScriptBlock]
    .Link
        https://github.com/StartAutomating/Pipeworks
        about_hash_tables
    #>
    [OutputType([string], [ScriptBlock])]
    param(
        [Parameter(Position = 0, ValueFromPipeline = $true, ValueFromPipelineByPropertyName = $true)][PSObject] $InputObject,
        # Returns the content as a script block, rather than a string
        [Alias('ScriptBlock')][switch]$AsScriptBlock,
        # If set, items in the hashtable will be sorted alphabetically
        [Switch]$Sort
    )
    process {
        $callstack = @(foreach ($_ in (Get-PSCallStack)) {
                if ($_.Command -eq "Write-PowerShellHashtable") {
                    $_
                }
            })
        $depth = $callStack.Count
        if ($inputObject -isnot [System.Collections.IDictionary]) {

            $newInputObject = @{
                PSTypeName = @($inputobject.pstypenames)[-1]
            }
            foreach ($prop in $inputObject.psobject.properties) {
                $newInputObject[$prop.Name] = $prop.Value
            }
            $inputObject = $newInputObject
        }

        if ($inputObject -is [System.Collections.IDictionary]) {
            #region Indent
            $scriptString = ""
            $indent = $depth * 4
            $scriptString += "@{
"
            #endregion Indent
            #region Include
            $items = $inputObject.GetEnumerator()

            if ($Sort) {
                $items = $items | Sort-Object Key
            }


            foreach ($kv in $items) {
                $scriptString += " " * $indent

                $keyString = "$($kv.Key)"
                if ($keyString.IndexOfAny(" _.#-+:;()'!?^@#$%&".ToCharArray()) -ne -1) {
                    if ($keyString.IndexOf("'") -ne -1) {
                        $scriptString += "'$($keyString.Replace("'","''"))'="
                    } else {
                        $scriptString += "'$keyString'="
                    }
                } elseif ($keyString) {
                    $scriptString += "$keyString="
                }



                $value = $kv.Value
                # Write-Verbose "$value"
                if ($value -is [string]) {
                    $value = "'" + $value.Replace("'", "''").Replace("’", "’’").Replace("‘", "‘‘") + "'"
                } elseif ($value -is [ScriptBlock]) {
                    $value = "{$value}"
                } elseif ($value -is [switch]) {
                    $value = if ($value) { '$true' } else { '$false' }
                } elseif ($value -is [DateTime]) {
                    $value = if ($value) { "[DateTime]'$($value.ToString("o"))'" }
                } elseif ($value -is [bool]) {
                    $value = if ($value) { '$true' } else { '$false' }
                } elseif ($value -is [System.Collections.IList] -and $value.Count -eq 0) {
                    $value = '@()'
                } elseif ($value -is [System.Collections.IList] -and $value.Count -gt 0) {
                #} elseif ($value -and $value.GetType -and ($value.GetType().IsArray -or $value -is [Collections.IList])) {
                    $value = foreach ($v in $value) {
                        if ($v -is [System.Collections.IDictionary]) {
                            Write-PowerShellHashtable $v
                        } elseif ($v -is [Object] -and $v -isnot [string]) {
                            Write-PowerShellHashtable $v
                        } else {
                            ("'" + "$v".Replace("'", "''").Replace("’", "’’").Replace("‘", "‘‘") + "'")
                        }
                    }
                    $oldOfs = $ofs
                    $ofs = ",$(' ' * ($indent + 4))"
                    $value = "@($value)"
                    $ofs = $oldOfs
                } elseif ($value -as [System.Collections.IDictionary[]]) {
                    $value = foreach ($v in $value) {
                        Write-PowerShellHashtable $v
                    }
                    $value = $value -join ","
                } elseif ($value -is [System.Collections.IDictionary]) {
                    $value = "$(Write-PowerShellHashtable $value)"
                } elseif ($value -as [Double]) {
                    $value = "$value"
                } else {
                    $valueString = "'$value'"
                    if ($valueString[0] -eq "'" -and
                        $valueString[1] -eq "@" -and
                        $valueString[2] -eq "{") {
                        $value = Write-PowerShellHashtable -InputObject $value
                    } else {
                        $value = $valueString
                    }

                }
                $scriptString += "$value
"
            }
            $scriptString += " " * ($depth - 1) * 4
            $scriptString += "}"
            if ($AsScriptBlock) {
                [ScriptBlock]::Create($scriptString)
            } else {
                $scriptString
            }
            #endregion Include
        }
    }
}