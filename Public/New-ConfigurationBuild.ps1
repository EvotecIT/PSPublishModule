function New-ConfigurationBuild {
    [CmdletBinding()]
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

        [switch] $MergeLibraryDebugging,
        [switch] $ResolveBinaryConflicts,
        [string] $ResolveBinaryConflictsName,

        [string] $CertificateThumbprint,

        [switch] $NETBuild,
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

        # $Options = @{
        #     Signing = @{
        #         CertificateThumbprint = '36A8A2D0E227D81A2D3B60DCE0CFCF23BEFC343B'
        #     }
        # }
        [ordered] @{
            Type    = 'Options'
            Options = [ordered] @{
                Signing = [ordered] @{
                    CertificateThumbprint = $CertificateThumbprint
                }
            }
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

    if ($PSBoundParameters.ContainsKey('NETBuild')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                Enable = $NETBuild.IsPresent
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('NETConfiguration')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
                Configuration = $NETConfiguration
            }
        }
    }
    if ($PSBoundParameters.ContainsKey('NETFramework')) {
        [ordered] @{
            Type           = 'BuildLibraries'
            BuildLibraries = [ordered] @{
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