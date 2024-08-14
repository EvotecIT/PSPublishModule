function New-ConfigurationBuild {
    <#
    .SYNOPSIS
    Allows to configure build process for the module

    .DESCRIPTION
    Allows to configure build process for the module

    .PARAMETER Enable
    Enable build process

    .PARAMETER DeleteTargetModuleBeforeBuild
    Delete target module before build

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

    .PARAMETER NETProjectPath
    Path to the project that you want to build. This is useful if it's not in Sources folder directly within module directory

    .PARAMETER NETProjectName
    By default it will assume same name as project name, but you can provide different name if needed.
    It's required if NETProjectPath is provided

    .PARAMETER NETExcludeMainLibrary
    Exclude main library from build, this is useful if you have C# project that you want to build
    that is used mostly for generating libraries that are used in PowerShell module
    It won't include main library in the build, but it will include all other libraries

    .PARAMETER NETExcludeLibraryFilter
    Provide list of filters for libraries that you want to exclude from build, this is useful if you have C# project that you want to build, but don't want to include all libraries for some reason

    .PARAMETER NETIgnoreLibraryOnLoad
    This is to exclude libraries from being loaded in PowerShell by PSM1/Librarties.ps1 files.
    This is useful if you have a library that is not supposed to be loaded in PowerShell, but you still need it
    For example library that's not NET based and is as dependency for other libraries

    .PARAMETER NETBinaryModule
    Provide list of binary modules that you want to import-module in the module.
    This is useful if you're building a module that has binary modules and you want to import them in the module.
    In here you provide one or more binrary module names that you want to import in the module.
    Just the DLL name with extension without path. Path is assumed to be $PSScriptRoot\Lib\Standard or $PSScriptRoot\Lib\Default or $PSScriptRoot\Lib\Core

    .PARAMETER NETBinaryModuleCmdletScanDisabled
    This is to disable scanning for cmdlets in binary modules, this is useful if you have a lot of binary modules and you don't want to scan them for cmdlets.
    By default it will scan for cmdlets/aliases in binary modules and add them to the module PSD1/PSM1 files.

    .PARAMETER NETHandleAssemblyWithSameName
    Adds try/catch block to handle assembly with same name is already loaded exception and ignore it.
    It's useful in PowerShell 7, as it's more strict about this than Windows PowerShell, and usually everything should work as expected.

    .PARAMETER NETLineByLineAddType
    Adds Add-Type line by line, this is useful if you have a lot of libraries and you want to see which one is causing the issue.

    .PARAMETER NETMergeLibraryDebugging
    Add special logic to simplify debugging of merged libraries, this is useful if you have a lot of libraries and you want to see which one is causing the issue.

    .PARAMETER NETResolveBinaryConflicts
    Add special logic to resolve binary conflicts. It uses by defalt the project name. If you want to use different name use NETResolveBinaryConflictsName

    .PARAMETER NETResolveBinaryConflictsName
    Add special logic to resolve binary conflicts for specific project name.

    .PARAMETER NETSearchClass
    Provide a name for class when using NETResolveBinaryConflicts or NETResolveBinaryConflictsName. By default it uses `$LibraryName.Initialize` however that may not be always the case

    .EXAMPLE
    $newConfigurationBuildSplat = @{
        Enable                            = $true
        SignModule                        = $true
        MergeModuleOnBuild                = $true
        MergeFunctionsFromApprovedModules = $true
        CertificateThumbprint             = '483292C9E317AA1'
        NETResolveBinaryConflicts            = $true
        NETResolveBinaryConflictsName        = 'Transferetto'
        NETProjectName                    = 'Transferetto'
        NETConfiguration                  = 'Release'
        NETFramework                      = 'netstandard2.0'
        DotSourceLibraries                = $true
        DotSourceClasses                  = $true
        DeleteTargetModuleBeforeBuild     = $true
    }

    New-ConfigurationBuild @newConfigurationBuildSplat

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

        [string] $CertificateThumbprint,
        [string] $CertificatePFXPath,
        [string] $CertificatePFXBase64,
        [string] $CertificatePFXPassword,

        [string] $NETProjectPath,
        [ValidateSet('Release', 'Debug')][string] $NETConfiguration, # may need to allow user choice
        [string[]] $NETFramework,
        [string] $NETProjectName,
        [switch] $NETExcludeMainLibrary,
        [string[]] $NETExcludeLibraryFilter,
        [string[]] $NETIgnoreLibraryOnLoad,
        [string[]] $NETBinaryModule,
        [alias('HandleAssemblyWithSameName')][switch] $NETHandleAssemblyWithSameName,
        [switch] $NETLineByLineAddType,
        [switch] $NETBinaryModuleCmdletScanDisabled,
        [alias("MergeLibraryDebugging")][switch] $NETMergeLibraryDebugging,
        [alias("ResolveBinaryConflicts")][switch] $NETResolveBinaryConflicts,
        [alias("ResolveBinaryConflictsName")][string] $NETResolveBinaryConflictsName,
        [string] $NETSearchClass
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

    if ($PSBoundParameters.ContainsKey('NETMergeLibraryDebugging')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                DebugDLL = $NETMergeLibraryDebugging.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('NETResolveBinaryConflictsName')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                ResolveBinaryConflicts = @{
                    ProjectName = $NETResolveBinaryConflictsName
                }
            }
        }
    } elseif ($PSBoundParameters.ContainsKey('NETResolveBinaryConflicts')) {
        [ordered] @{
            Type        = 'Build'
            BuildModule = [ordered] @{
                ResolveBinaryConflicts = $NETResolveBinaryConflicts.IsPresent
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

    if ($PSBoundParameters.ContainsKey('NETExcludeMainLibrary')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                ExcludeMainLibrary = $NETExcludeMainLibrary.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('NETExcludeLibraryFilter')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                ExcludeLibraryFilter = $NETExcludeLibraryFilter
            }
        }
    }
    # This is to exclude libraries from being loaded in PowerShell by PSM1/Librarties.ps1 files.
    # This is useful if you have a library that is not supposed to be loaded in PowerShell, but you still need it
    # For example library that's not NET based and is as dependency for other libraries
    if ($PSBoundParameters.ContainsKey('NETIgnoreLibraryOnLoad')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                IgnoreLibraryOnLoad = $NETIgnoreLibraryOnLoad
            }
        }
    }
    # this is to add binary modules that you want to import-module in the module
    # it accepts one or more binrary module names that you want to import in the module
    # just the DLL name with extension without path
    if ($PSBoundParameters.ContainsKey('NETBinaryModule')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                BinaryModule = $NETBinaryModule
            }
        }
    }
    # this is to add try/catch block to handle assembly with same name is already loaded exception and ignore it
    if ($PSBoundParameters.ContainsKey('NETHandleAssemblyWithSameName')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                HandleAssemblyWithSameName = $NETHandleAssemblyWithSameName.IsPresent
            }
        }
    }
    # this is to add Add-Type line by line, this is useful if you have a lot of libraries and you want to see which one is causing the issue
    # this is basically legacy setting that may come useful, and it was by default in the past
    if ($PSBoundParameters.ContainsKey('NETLineByLineAddType')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                NETLineByLineAddType = $NETLineByLineAddType.IsPresent
            }
        }
    }

    # this is to add NET project path, this is useful if it's not in Sources folder directly within module directory
    if ($PSBoundParameters.ContainsKey('NETProjectPath')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                NETProjectPath = $NETProjectPath
            }
        }
    }

    if ($PSBoundParameters.ContainsKey('NETBinaryModuleCmdletScanDisabled')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                BinaryModuleCmdletScanDisabled = $NETBinaryModuleCmdletScanDisabled.IsPresent
            }
        }
    }

    if ($PSBoundParameters.ContainsKey('NETSearchClass')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                SearchClass = $NETSearchClass
            }
        }
    }
}