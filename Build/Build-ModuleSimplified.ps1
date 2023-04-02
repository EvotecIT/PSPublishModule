Clear-Host
Import-Module "$PSScriptRoot\..\PSPublishModule.psd1" -Force

$Configuration = @{
    # Information = @{
    # Those options below are not nessecary but can be used to configure other options. Those are "defaults"
    # Exclude           = '.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'
    # IncludeRoot       = '*.psm1', '*.psd1', 'License*'
    # IncludePS1        = 'Private', 'Public', 'Enums', 'Classes'
    # IncludeAll        = 'Images\', 'Resources\', 'Templates\', 'Bin\', 'Lib\', 'Data\'

    # IncludeCustomCode = {

    # }
    # IncludeToArray    = @{
    #     'Rules' = 'Examples'
    # }

    # LibrariesCore     = 'Lib\Core'
    # LibrariesDefault  = 'Lib\Default'
    # LibrariesStandard = 'Lib\Standard'
    # }
    Options = @{
        # Merge             = @{
        #     Sort           = 'None'
        #     FormatCodePSM1 = @{
        #         Enabled           = $true
        #         RemoveComments    = $true
        #         FormatterSettings = @{
        #             IncludeRules = @(
        #                 'PSPlaceOpenBrace',
        #                 'PSPlaceCloseBrace',
        #                 'PSUseConsistentWhitespace',
        #                 'PSUseConsistentIndentation',
        #                 'PSAlignAssignmentStatement',
        #                 'PSUseCorrectCasing'
        #             )

        #             Rules        = @{
        #                 PSPlaceOpenBrace           = @{
        #                     Enable             = $true
        #                     OnSameLine         = $true
        #                     NewLineAfter       = $true
        #                     IgnoreOneLineBlock = $true
        #                 }

        #                 PSPlaceCloseBrace          = @{
        #                     Enable             = $true
        #                     NewLineAfter       = $false
        #                     IgnoreOneLineBlock = $true
        #                     NoEmptyLineBefore  = $false
        #                 }

        #                 PSUseConsistentIndentation = @{
        #                     Enable              = $true
        #                     Kind                = 'space'
        #                     PipelineIndentation = 'IncreaseIndentationAfterEveryPipeline'
        #                     IndentationSize     = 4
        #                 }

        #                 PSUseConsistentWhitespace  = @{
        #                     Enable          = $true
        #                     CheckInnerBrace = $true
        #                     CheckOpenBrace  = $true
        #                     CheckOpenParen  = $true
        #                     CheckOperator   = $true
        #                     CheckPipe       = $true
        #                     CheckSeparator  = $true
        #                 }

        #                 PSAlignAssignmentStatement = @{
        #                     Enable         = $true
        #                     CheckHashtable = $true
        #                 }

        #                 PSUseCorrectCasing         = @{
        #                     Enable = $true
        #                 }
        #             }
        #         }
        #     }
        #     FormatCodePSD1 = @{
        #         Enabled        = $true
        #         RemoveComments = $false
        #     }
        #     Integrate      = @{
        #         ApprovedModules = 'PSSharedGoods', 'PSWriteColor', 'Connectimo', 'PSUnifi', 'PSWebToolbox', 'PSMyPassword'
        #     }
        # }
        # Standard          = @{
        #     FormatCodePSM1 = @{

        #     }
        #     FormatCodePSD1 = @{
        #         Enabled = $true
        #         #RemoveComments = $true
        #     }
        # }
        PowerShellGallery = @{
            ApiKey   = 'C:\Support\Important\PowerShellGalleryAPI.txt'
            FromFile = $true
        }
        GitHub            = @{
            ApiKey   = 'C:\Support\Important\GithubAPI.txt'
            FromFile = $true
            UserName = 'EvotecIT'
            #RepositoryName = 'PSPublishModule' # not required, uses project name
        }
        Documentation     = @{
            Path       = 'Docs'
            PathReadme = 'Docs\Readme.md'
        }
        Style             = @{
            PSD1 = 'Minimal' # Native
        }
    }
    Steps   = @{
        BuildLibraries     = @{
            Enable        = $false # build once every time nuget gets updated
            Configuration = 'Release'
            Framework     = 'netstandard2.0', 'net472'
            #ProjectName   = 'ImagePlayground.PowerShell'
        }
        BuildModule        = @{  # requires Enable to be on to process all of that
            Enable                  = $true
            DeleteBefore            = $false
            Merge                   = $true
            MergeMissing            = $true
            SignMerged              = $true
            #CreateFileCatalog       = $false
            Releases                = $true
            #ReleasesUnpacked        = $false
            ReleasesUnpacked        = @{
                Enabled         = $true
                IncludeTagName  = $false
                Path            = "$PSScriptRoot\..\Artefacts"
                RequiredModules = $false
                DirectoryOutput = @{

                }
                FilesOutput     = @{

                }
            }
            RefreshPSD1Only         = $false
            # only when there are classes
            ClassesDotSource        = $false
            LibrarySeparateFile     = $false
            LibraryDotSource        = $false
            # Applicable only for non-merge/publish situation
            # It's simply to make life easier during debugging
            # It makes all functions/aliases exportable
            UseWildcardForFunctions = $false

            # special features for binary modules
            DebugDLL                = $false
            ResolveBinaryConflicts  = $false # mostly for memory and other libraries
            # ResolveBinaryConflicts  = @{
            #     ProjectName = 'ImagePlayground.PowerShell'
            # }
            LocalVersion            = $false # bumps version in PSD1 on every build
        }
        BuildDocumentation = @{
            Enable        = $true # enables documentation processing
            StartClean    = $true # always starts clean
            UpdateWhenNew = $true # always updates right after update
        }
        ImportModules      = @{
            Self            = $true
            RequiredModules = $false
            Verbose         = $false
        }
        PublishModule      = @{  # requires Enable to be on to process all of that
            Enabled      = $false
            Prerelease   = ''
            RequireForce = $false
            GitHub       = $false
        }
    }
}

New-PrepareModule -ModuleName 'PSPublishModule' -Configuration $Configuration {
    $Manifest = [ordered] @{
        ModuleVersion          = '0.9.X'
        CompatiblePSEditions   = @('Desktop', 'Core')
        GUID                   = 'eb76426a-1992-40a5-82cd-6480f883ef4d'
        Author                 = 'Przemyslaw Klys'
        CompanyName            = 'Evotec'
        Copyright              = "(c) 2011 - $((Get-Date).Year) Przemyslaw Klys @ Evotec. All rights reserved."
        Description            = 'Simple project allowing preparing, managing, building and publishing modules to PowerShellGallery'
        PowerShellVersion      = '5.1'
        Tags                   = @('Windows', 'MacOS', 'Linux', 'Build', 'Module')
        IconUri                = 'https://evotec.xyz/wp-content/uploads/2019/02/PSPublishModule.png'
        ProjectUri             = 'https://github.com/EvotecIT/PSPublishModule'
        DotNetFrameworkVersion = '4.5.2'
    }
    New-ConfigurationManifest @Manifest

    # Add standard module dependencies (directly, but can be used with loop as well)
    New-ConfigurationModules -Type RequiredModule -Name 'platyPS' -Guid 'Auto' -Version 'Latest'
    New-ConfigurationModules -Type RequiredModule -Name 'powershellget' -Guid 'Auto' -Version 'Latest'
    New-ConfigurationModules -Type RequiredModule -Name 'PSScriptAnalyzer' -Guid 'Auto' -Version 'Latest'

    # Add external module dependencies, using loop for simplicity
    foreach ($Module in @('Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Archive', 'Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Security')) {
        New-ConfigurationModules -Type ExternalModule -Name $Module
    }

    $ConfigurationFormat = [ordered] @{
        RemoveComments                              = $false

        PlaceOpenBraceEnable                        = $true
        PlaceOpenBraceOnSameLine                    = $true
        PlaceOpenBraceNewLineAfter                  = $true
        PlaceOpenBraceIgnoreOneLineBlock            = $false

        PlaceCloseBraceEnable                       = $true
        PlaceCloseBraceNewLineAfter                 = $true
        PlaceCloseBraceIgnoreOneLineBlock           = $false
        PlaceCloseBraceNoEmptyLineBefore            = $true

        UseConsistentIndentationEnable              = $true
        UseConsistentIndentationKind                = 'space'
        UseConsistentIndentationPipelineIndentation = 'IncreaseIndentationAfterEveryPipeline'
        UseConsistentIndentationIndentationSize     = 4

        UseConsistentWhitespaceEnable               = $true
        UseConsistentWhitespaceCheckInnerBrace      = $true
        UseConsistentWhitespaceCheckOpenBrace       = $true
        UseConsistentWhitespaceCheckOpenParen       = $true
        UseConsistentWhitespaceCheckOperator        = $true
        UseConsistentWhitespaceCheckPipe            = $true
        UseConsistentWhitespaceCheckSeparator       = $true

        AlignAssignmentStatementEnable              = $true
        AlignAssignmentStatementCheckHashtable      = $true

        UseCorrectCasingEnable                      = $true
    }
    New-ConfigurationFormat -ApplyTo 'OnMergePSM1', 'OnMergePSD1' -Sort None @ConfigurationFormat
    New-ConfigurationFormat -ApplyTo 'DefaultPSD1', 'DefaultPSM1'
}