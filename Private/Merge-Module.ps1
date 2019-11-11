function Merge-Module {
    [CmdletBinding()]
    param (
        [string] $ModuleName,
        [string] $ModulePathSource,
        [string] $ModulePathTarget,
        [Parameter(Mandatory = $false, ValueFromPipeline = $false)]
        [ValidateSet("ASC", "DESC", "NONE", '')]
        [string] $Sort = 'NONE',
        [string[]] $FunctionsToExport,
        [string[]] $AliasesToExport,
        [Array] $LibrariesCore,
        [Array] $LibrariesDefault,
        [System.Collections.IDictionary] $FormatCodePSM1,
        [System.Collections.IDictionary] $FormatCodePSD1,
        [System.Collections.IDictionary] $Configuration
    )
    $TimeToExecute = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Text "[+] 1st stage merging" -Color Blue

    $PSM1FilePath = "$ModulePathTarget\$ModuleName.psm1"
    $PSD1FilePath = "$ModulePathTarget\$ModuleName.psd1"

    if ($PSEdition -eq 'Core') {
        $ScriptFunctions = Get-ChildItem -Path $ModulePathSource\*.ps1 -ErrorAction SilentlyContinue -Recurse -FollowSymlink
    } else {
        $ScriptFunctions = Get-ChildItem -Path $ModulePathSource\*.ps1 -ErrorAction SilentlyContinue -Recurse
    }
    if ($Sort -eq 'ASC') {
        $ScriptFunctions = $ScriptFunctions | Sort-Object -Property Name
    } elseif ($Sort -eq 'DESC') {
        $ScriptFunctions = $ScriptFunctions | Sort-Object -Descending -Property Name
    }

    foreach ($FilePath in $ScriptFunctions) {
        $Content = Get-Content -Path $FilePath -Raw
        $Content = $Content.Replace('$PSScriptRoot\..\..\', '$PSScriptRoot\')
        $Content = $Content.Replace('$PSScriptRoot\..\', '$PSScriptRoot\')

        try {
            $Content | Out-File -Append -LiteralPath $PSM1FilePath -Encoding utf8
        } catch {
            $ErrorMessage = $_.Exception.Message
            #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
            Write-Error "Merge-Module - Merge on file $FilePath failed. Error: $ErrorMessage"
            Exit
        }
    }


    # Using file is needed if there are 'using namespaces' - this is a workaround provided by seeminglyscience
    $FilePathUsing = "$ModulePathTarget\$ModuleName.Usings.ps1"

    $UsingInPlace = Format-UsingNamespace -FilePath $PSM1FilePath -FilePathUsing $FilePathUsing
    if ($UsingInPlace) {
        Format-Code -FilePath $FilePathUsing -FormatCode $FormatCodePSM1
        $Configuration.UsingInPlace = "$ModuleName.Usings.ps1"
    }

    $TimeToExecute.Stop()
    Write-Text "[+] 1st stage merging [Time: $($($TimeToExecute.Elapsed).Tostring())]" -Color Blue

    $TimeToExecute = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Text "[+] 3rd stage required modules" -Color Blue

    $RequiredModules = @(
        if ($Configuration.Information.Manifest.RequiredModules[0] -is [System.Collections.IDictionary]) {
            $Configuration.Information.Manifest.RequiredModules.ModuleName
        } else {
            $Configuration.Information.Manifest.RequiredModules
        }
    )
    $DependantRequiredModules = foreach ($_ in $RequiredModules) {
        Find-RequiredModules -Name $_
    }
    $DependantRequiredModules = $DependantRequiredModules | Sort-Object -Unique

    $TimeToExecute.Stop()
    Write-Text "[+] 2nd stage required modules [Time: $($($TimeToExecute.Elapsed).Tostring())]" -Color Blue


    $TimeToExecute = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Text "[+] 3rd stage missing functions" -Color Blue

    [Array] $ApprovedModules = $Configuration.Options.Merge.Integrate.ApprovedModules

    $MissingFunctions = Get-MissingFunctions -FilePath $PSM1FilePath -SummaryWithCommands -ApprovedModules $ApprovedModules

    $TimeToExecute.Stop()
    Write-Text "[+] 3rd stage missing functions [Time: $($($TimeToExecute.Elapsed).Tostring())]" -Color Blue

    $TimeToExecute = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Text "[+] 4th stage commands used" -Color Blue

    foreach ($Module in $MissingFunctions.Summary.Source | Sort-Object -Unique) {
        if ($Module -in $RequiredModules -and $Module -in $ApprovedModules) {
            Write-Text "[+] Module $Module is in required modules with ability to merge." -Color Green
            $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module }).Name #-join ','
            foreach ($F in $MyFunctions) {
                Write-Text "   [>] Command used $F" -Color Yellow
            }
        } elseif ($Module -in $DependantRequiredModules -and $Module -in $ApprovedModules) {
            Write-Text "[+] Module $Module is in dependant required module within required modules with ability to merge." -Color Green
            $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module }).Name #-join ','
            foreach ($F in $MyFunctions) {
                Write-Text "   [>] Command used $F" -Color Yellow
            }
        } elseif ($Module -in $DependantRequiredModules) {
            Write-Text "[+] Module $Module is in dependant required module within required modules." -Color Green
            $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module }).Name #-join ','
            foreach ($F in $MyFunctions) {
                Write-Text "   [>] Command used $F" -Color Green
            }
        } elseif ($Module -in $RequiredModules) {
            Write-Text "[+] Module $Module is in required modules." -Color Green
            $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module }).Name #-join ','
            foreach ($F in $MyFunctions) {
                Write-Text "   [>] Command used $F" -Color Green
            }
        } else {
            Write-Text "[-] Module $Module is missing in required modules. Potential issue." -Color Red
            $MyFunctions = ($MissingFunctions.Summary | Where-Object { $_.Source -eq $Module }).Name #-join ','
            foreach ($F in $MyFunctions) {
                Write-Text "   [>] Command affected $F" -Color Red
            }
        }
    }

    $TimeToExecute.Stop()
    Write-Text "[+] 4th stage commands used [Time: $($($TimeToExecute.Elapsed).Tostring())]" -Color Blue


    if ($Configuration.Steps.BuildModule.MergeMissing -eq $true) {

        $TimeToExecute = [System.Diagnostics.Stopwatch]::StartNew()
        Write-Text "[+] 5th stage merge mergable commands" -Color Blue

        $PSM1Content = Get-Content -LiteralPath $PSM1FilePath -Raw
        $IntegrateContent = @(
            $MissingFunctions.Functions
            $PSM1Content
        )
        $IntegrateContent | Set-Content -LiteralPath $PSM1FilePath -Encoding UTF8

        # Overwrite Required Modules
        $NewRequiredModules = foreach ($_ in $Configuration.Information.Manifest.RequiredModules) {
            if ($_ -is [System.Collections.IDictionary]) {
                if ($_.ModuleName -notin $ApprovedModules) {
                    $_
                }
            } else {
                if ($_ -notin $ApprovedModules) {
                    $_
                }
            }
        }
        $Configuration.Information.Manifest.RequiredModules = $NewRequiredModules


        #$MissingFunctions.Functions
        #$MissingFunctions.Summary | Format-Table -AutoSize
        <#
        Name                      Source                       CommandType Error ScriptBlock
        ----                      ------                       ----------- ----- -----------
        cmd.exe                   C:\Windows\system32\cmd.exe  Application
        Import-PowerShellDataFile Microsoft.PowerShell.Utility    Function       ...
        New-MarkdownHelp          platyPS                         Function       ...
        Publish-Module            PowerShellGet                   Function       ...
        Update-MarkdownHelpModule platyPS                         Function       ...
        Find-Module               PowerShellGet                   Function       ...
        Find-Script               PowerShellGet                   Function       ...
        Get-MarkdownMetadata      platyPS                         Function       ...
        Get-PSRepository          PowerShellGet                   Function       ...
        Update-MarkdownHelp       platyPS                         Function       ...
        #>

        $TimeToExecute.Stop()
        Write-Text "[+] 5th stage merge mergable commands [Time: $($($TimeToExecute.Elapsed).Tostring())]" -Color Blue
    }

    New-PSMFile -Path $PSM1FilePath `
        -FunctionNames $FunctionsToExport `
        -FunctionAliaes $AliasesToExport `
        -LibrariesCore $LibrariesCore `
        -LibrariesDefault $LibrariesDefault `
        -ModuleName $ModuleName `
        -UsingNamespaces:$UsingInPlace


    Format-Code -FilePath $PSM1FilePath -FormatCode $FormatCodePSM1
    New-PersonalManifest -Configuration $Configuration -ManifestPath $PSD1FilePath -AddUsingsToProcess
    Format-Code -FilePath $PSD1FilePath -FormatCode $FormatCodePSD1

    # cleans up empty directories
    Get-ChildItem $ModulePathTarget -Recurse -Force -Directory | Sort-Object -Property FullName -Descending | `
        Where-Object { $($_ | Get-ChildItem -Force | Select-Object -First 1).Count -eq 0 } | `
        Remove-Item #-Verbose
}