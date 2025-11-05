function Step-Version {
    <#
    .SYNOPSIS
    Short description

    .DESCRIPTION
    Long description

    .PARAMETER Module
    Parameter description

    .PARAMETER ExpectedVersion
    Parameter description

    .PARAMETER Advanced
    Parameter description

    .EXAMPLE
    Step-Version -Module Testimo12 -ExpectedVersion '0.1.X'
    Step-Version -ExpectedVersion '0.1.X'
    Step-Version -ExpectedVersion '0.1.5.X'
    Step-Version -ExpectedVersion '1.2.X'
    Step-Version -Module PSWriteHTML -ExpectedVersion '0.0.X'
    Step-Version -Module PSWriteHTML1 -ExpectedVersion '0.1.X'
    Step-Version -Module PSPublishModule -ExpectedVersion '0.9.X' -Advanced -LocalPSD1 "C:\Support\GitHub\PSPublishModule\PSPublishModule.psd1"

    .NOTES
    General notes
    #>
    [cmdletBinding()]
    param(
        [string] $Module,
        [Parameter(Mandatory)][string] $ExpectedVersion,
        [switch] $Advanced,
        [string] $LocalPSD1
    )
    $Version = $null
    $VersionCheck = [version]::TryParse($ExpectedVersion, [ref] $Version)
    if ($VersionCheck) {
        # Don't do anything, return what user wanted to get anyways
        @{
            Version        = $ExpectedVersion
            CurrentVersion = 'Not aquired, no auto versioning.'
        }
    } else {
        if ($Module) {
            if (-not $LocalPSD1) {
                try {
                    $ModuleGallery = Find-Module -Name $Module -ErrorAction Stop -Verbose:$false -WarningAction SilentlyContinue
                    $CurrentVersion = [version] $ModuleGallery.Version
                } catch {
                    #throw "Couldn't find module $Module to asses version information. Terminating."
                    $CurrentVersion = [version] $null
                }
            } else {
                if (Test-Path -LiteralPath $LocalPSD1) {
                    $PSD1Data = Import-PowerShellDataFile -Path $LocalPSD1
                    if ($PSD1Data.ModuleVersion) {
                        try {
                            $CurrentVersion = [version] $PSD1Data.ModuleVersion
                        } catch {
                            Write-Warning -Message "Couldn't parse version $($PSD1Data.ModuleVersion) from PSD1 file $LocalPSD1"
                            $CurrentVersion = $null
                        }
                    }
                } else {
                    Write-Warning -Message "Couldn't find local PSD1 file $LocalPSD1"
                    $CurrentVersion = $null
                }
            }
        } else {
            $CurrentVersion = $null
        }
        $Splitted = $ExpectedVersion.Split('.')
        $PreparedVersion = [ordered] @{
            Major    = $Splitted[0]
            Minor    = $Splitted[1]
            Build    = $Splitted[2]
            Revision = $Splitted[3]
        }
        [string] $StepType = foreach ($Key in $PreparedVersion.Keys) {
            if ($PreparedVersion[$Key] -eq 'X') {
                $Key
                break
            }
        }

        if ($null -eq $CurrentVersion) {
            $PreparedVersion.$StepType = 1
        } else {
            $PreparedVersion.$StepType = $CurrentVersion.$StepType
        }

        if([version] (($PreparedVersion.Values | Where-Object {$null -ne $_ }) -join '.') -gt $CurrentVersion) {
            $PreparedVersion.$StepType = 0
        }

        while ([version] (($PreparedVersion.Values | Where-Object {$null -ne $_ }) -join '.') -le $CurrentVersion) {
            $PreparedVersion.$StepType = $PreparedVersion.$StepType + 1
        }

        $ProposedVersion = ([version] (($PreparedVersion.Values | Where-Object {$null -ne $_ }) -join '.')).ToString()

        $FinalVersion = $null
        $VersionCheck = [version]::TryParse($ProposedVersion, [ref] $FinalVersion)
        if ($VersionCheck) {
            if ($Advanced) {
                [ordered] @{
                    Version        = $ProposedVersion
                    CurrentVersion = $CurrentVersion
                }
            } else {
                $ProposedVersion
            }
        } else {
            throw "Couldn't properly verify version is version. Terminating."
        }
    }
}