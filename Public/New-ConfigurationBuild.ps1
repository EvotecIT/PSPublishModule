function New-ConfigurationBuild {
    <#
    .SYNOPSIS
    Short description

    .DESCRIPTION
    Long description

    .PARAMETER Enable
    Parameter description

    .PARAMETER DeleteTargetModuleBeforeBuild
    Parameter description

    .PARAMETER MergeModuleOnBuild
    Parameter description

    .PARAMETER MergeFunctionsFromApprovedModules
    Parameter description

    .PARAMETER SignModule
    Parameter description

    .PARAMETER DotSourceClasses
    Parameter description

    .PARAMETER DotSourceLibraries
    Parameter description

    .PARAMETER SeparateFileLibraries
    Parameter description

    .PARAMETER RefreshPSD1Only
    Parameter description

    .PARAMETER UseWildcardForFunctions
    Parameter description

    .PARAMETER LocalVersioning
    Parameter description

    .PARAMETER DoNotAttemptToFixRelativePaths
    Configures module builder to not replace $PSScriptRoot\..\ with $PSScriptRoot\
    This is useful if you have a module that has a lot of relative paths that are required when using Private/Public folders,
    but for merge process those are not supposed to be there as the paths change.
    By default module builder will attempt to fix it. This option disables this functionality.
    Best practice is to use $MyInvocation.MyCommand.Module.ModuleBase or similar instead of relative paths.

    .PARAMETER MergeLibraryDebugging
    Parameter description

    .PARAMETER ResolveBinaryConflicts
    Parameter description

    .PARAMETER ResolveBinaryConflictsName
    Parameter description

    .PARAMETER CertificateThumbprint
    Parameter description

    .PARAMETER CertificatePFXPath
    Parameter description

    .PARAMETER CertificatePFXBase64
    Parameter description

    .PARAMETER CertificatePFXPassword
    Parameter description

    .PARAMETER NETConfiguration
    Parameter description

    .PARAMETER NETFramework
    Parameter description

    .PARAMETER NETProjectName
    Parameter description

    .EXAMPLE
    An example

    .NOTES
    General notes
    #>
    [CmdletBinding()]
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute("PSAvoidUsingPlainTextForPassword", "")]
    param(
        [switch] $Enable,
        [switch] $DeleteTargetModuleBeforeBuild,
        [switch] $MergeModuleOnBuild,
        [switch] $MergeFunctionsFromApprovedModules,
        [switch] $SignModule,
        [switch] $DotSourceClasses,
        [switch] $DotSourceLibraries,
        [switch] $SeparateFileLibraries,
        [switch] $RefreshPSD1Only,
        [switch] $UseWildcardForFunctions,
        [switch] $LocalVersioning,

        [switch] $DoNotAttemptToFixRelativePaths,

        [switch] $MergeLibraryDebugging,
        [switch] $ResolveBinaryConflicts,
        [string] $ResolveBinaryConflictsName,

        [string] $CertificateThumbprint,
        [string] $CertificatePFXPath,
        [string] $CertificatePFXBase64,
        [string] $CertificatePFXPassword,

        #[switch] $NETBuild,
        [ValidateSet('Release', 'Debug')][string] $NETConfiguration, # may need to allow user choice
        [string[]] $NETFramework,
        [string] $NETProjectName
    )

    if ($PSBoundParameters.ContainsKey('Enable')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                Enable = $Enable.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('DeleteTargetModuleBeforeBuild')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                DeleteBefore = $DeleteTargetModuleBeforeBuild.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('MergeModuleOnBuild')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                Merge = $MergeModuleOnBuild.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('MergeFunctionsFromApprovedModules')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                MergeMissing = $MergeFunctionsFromApprovedModules.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('SignModule')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                SignMerged = $SignModule.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('DotSourceClasses')) {
        # only when there are classes
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                ClassesDotSource = $DotSourceClasses.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('DotSourceLibraries')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                LibraryDotSource = $DotSourceLibraries.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('SeparateFileLibraries')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                LibrarySeparateFile = $SeparateFileLibraries.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('RefreshPSD1Only')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                RefreshPSD1Only = $RefreshPSD1Only.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('UseWildcardForFunctions')) {
        # Applicable only for non-merge/publish situation
        # It's simply to make life easier during debugging
        # It makes all functions/aliases exportable
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                UseWildcardForFunctions = $UseWildcardForFunctions.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('LocalVersioning')) {
        # bumps version in PSD1 on every build
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                LocalVersion = $LocalVersioning.IsPresent
            }
        }
    }

    if ($PSBoundParameters.ContainsKey('DoNotAttemptToFixRelativePaths')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                DoNotAttemptToFixRelativePaths = $DoNotAttemptToFixRelativePaths.IsPresent
            }
        }
    }

    if ($PSBoundParameters.ContainsKey('MergeLibraryDebugging')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                DebugDLL = $MergeLibraryDebugging.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('ResolveBinaryConflictsName')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                ResolveBinaryConflicts = @{
                    ProjectName = $ResolveBinaryConflictsName
                }
            }
        }
    } elseif ($PSBoundParameters.ContainsKey('ResolveBinaryConflicts')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                ResolveBinaryConflicts = $ResolveBinaryConflicts.IsPresent
            }
        }
    }

    if ($PSBoundParameters.ContainsKey('CertificateThumbprint')) {
        [ordered] @{
            Type    = 'Options'
            Options = [ordered] @{
                Signing = [ordered] @{
                    CertificateThumbprint = $CertificateThumbprint
                }
            }
        }
    } elseif ($PSBoundParameters.ContainsKey('CertificatePFXPath')) {
        if ($PSBoundParameters.ContainsKey('CertificatePFXPassword')) {
            # this is added to support users direct PFX
            [ordered] @{
                Type    = 'Options'
                Options = [ordered] @{
                    Signing = [ordered] @{
                        CertificatePFXPath     = $CertificatePFXPath
                        CertificatePFXPassword = $CertificatePFXPassword
                    }
                }
            }
        } else {
            throw "CertificatePFXPassword is required when using CertificatePFXPath"
        }
    } elseif ($PSBoundParameters.ContainsKey('CertificatePFXBase64')) {
        if ($PSBoundParameters.ContainsKey('CertificatePFXPassword')) {
            # this is added to support GitHub/Azure DevOps Secrets
            [ordered] @{
                Type    = 'Options'
                Options = [ordered] @{
                    Signing = [ordered] @{
                        CertificatePFXBase64   = $CertificatePFXBase64
                        CertificatePFXPassword = $CertificatePFXPassword
                    }
                }
            }
        } else {
            throw "CertificatePFXPassword is required when using CertificatePFXBase64"
        }
    }

    # Build libraries configuration, this is useful if you have C# project that you want to build
    # so libraries are autogenerated and you can use them in your PowerShell module

    # $BuildLibraries = @{
    #     Enable        = $false # build once every time nuget gets updated
    #     Configuration = 'Release'
    #     Framework     = 'netstandard2.0', 'net472'
    #     ProjectName   = 'ImagePlayground.PowerShell'
    # }

    if ($PSBoundParameters.ContainsKey('NETConfiguration')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                Enable        = $true
                Configuration = $NETConfiguration
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('NETFramework')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                Enable    = $true
                Framework = $NETFramework
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('NETProjectName')) {
        # this is optional as normaly it will assume same name as project name
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                ProjectName = $NETProjectName
            }
        }
    }
}