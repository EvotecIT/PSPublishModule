Clear-Host
Import-Module "$PSScriptRoot\..\PSPublishModule.psd1" -Force

$Configuration = @{
    Information = @{
        ModuleName        = 'PSPublishModule'
        DirectoryProjects = 'C:\Support\GitHub'

        # Where from to export aliases / functions
        FunctionsToExport = 'Public'
        AliasesToExport   = 'Public'

        # Those options below are not nessecary but can be used to configure other options. Those are "defaults"
        Exclude           = '.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'
        IncludeRoot       = '*.psm1', '*.psd1', 'License*'
        IncludePS1        = 'Private', 'Public', 'Enums', 'Classes'
        IncludeAll        = 'Images\', 'Resources\', 'Templates\', 'Bin\', 'Lib\', 'Data\'

        IncludeCustomCode = {

        }
        IncludeToArray    = @{
            'Rules' = 'Examples'
        }

        LibrariesCore     = 'Lib\Core'
        LibrariesDefault  = 'Lib\Default'
        LibrariesStandard = 'Lib\Standard'

        # manifest information
        Manifest          = @{
            # Version number of this module.
            ModuleVersion              = '0.9.X'
            # Supported PSEditions
            CompatiblePSEditions       = @('Desktop', 'Core')
            # ID used to uniquely identify this module
            GUID                       = 'eb76426a-1992-40a5-82cd-6480f883ef4d'
            # Author of this module
            Author                     = 'Przemyslaw Klys'
            # Company or vendor of this module
            CompanyName                = 'Evotec'
            # Copyright statement for this module
            Copyright                  = "(c) 2011 - $((Get-Date).Year) Przemyslaw Klys @ Evotec. All rights reserved."
            # Description of the functionality provided by this module
            Description                = 'Simple project allowing preparing, managing and publishing modules to PowerShellGallery'
            # Minimum version of the Windows PowerShell engine required by this module
            PowerShellVersion          = '5.1'
            # Functions to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no functions to export.
            Tags                       = @('Windows', 'MacOS', 'Linux', 'Build', 'Module')
            IconUri                    = 'https://evotec.xyz/wp-content/uploads/2019/02/PSPublishModule.png'
            ProjectUri                 = 'https://github.com/EvotecIT/PSPublishModule'

            RequiredModules            = @(
                @{ ModuleName = 'platyps'; ModuleVersion = "Latest"; Guid = '0bdcabef-a4b7-4a6d-bf7e-d879817ebbff' }
                @{ ModuleName = 'powershellget'; ModuleVersion = "2.2.5"; Guid = '1d73a601-4a6c-43c5-ba3f-619b18bbb404' }
                @{ ModuleName = 'PSScriptAnalyzer'; ModuleVersion = "Latest"; Guid = 'd6245802-193d-4068-a631-8863a4342a18' }
            )
            ExternalModuleDependencies = @(
                "Microsoft.PowerShell.Utility"
                "Microsoft.PowerShell.Archive"
                "Microsoft.PowerShell.Management"
                "Microsoft.PowerShell.Security"
            )
            #DotNetFrameworkVersion     = '4.5.2'
        }
    }
    Options     = @{
        Merge             = @{
            Sort           = 'None'
            FormatCodePSM1 = @{
                Enabled           = $true
                RemoveComments    = $false
                FormatterSettings = @{
                    IncludeRules = @(
                        'PSPlaceOpenBrace',
                        'PSPlaceCloseBrace',
                        'PSUseConsistentWhitespace',
                        'PSUseConsistentIndentation',
                        'PSAlignAssignmentStatement',
                        'PSUseCorrectCasing'
                    )

                    Rules        = @{
                        PSPlaceOpenBrace           = @{
                            Enable             = $true
                            OnSameLine         = $true
                            NewLineAfter       = $true
                            IgnoreOneLineBlock = $true
                        }

                        PSPlaceCloseBrace          = @{
                            Enable             = $true
                            NewLineAfter       = $false
                            IgnoreOneLineBlock = $true
                            NoEmptyLineBefore  = $false
                        }

                        PSUseConsistentIndentation = @{
                            Enable              = $true
                            Kind                = 'space'
                            PipelineIndentation = 'IncreaseIndentationAfterEveryPipeline'
                            IndentationSize     = 4
                        }

                        PSUseConsistentWhitespace  = @{
                            Enable          = $true
                            CheckInnerBrace = $true
                            CheckOpenBrace  = $true
                            CheckOpenParen  = $true
                            CheckOperator   = $true
                            CheckPipe       = $true
                            CheckSeparator  = $true
                        }

                        PSAlignAssignmentStatement = @{
                            Enable         = $true
                            CheckHashtable = $true
                        }

                        PSUseCorrectCasing         = @{
                            Enable = $true
                        }
                    }
                }
            }
            FormatCodePSD1 = @{
                Enabled        = $true
                RemoveComments = $false
            }
            Integrate      = @{
                ApprovedModules = 'PSSharedGoods', 'PSWriteColor', 'Connectimo', 'PSUnifi', 'PSWebToolbox', 'PSMyPassword'
            }
        }
        Standard          = @{
            FormatCodePSM1 = @{

            }
            FormatCodePSD1 = @{
                Enabled = $true
                #RemoveComments = $true
            }
        }
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
    Steps       = @{
        BuildModule        = @{  # requires Enable to be on to process all of that
            Enable                  = $true
            DeleteBefore            = $false
            Merge                   = $true
            MergeMissing            = $true
            SignMerged              = $true
            CreateFileCatalog       = $false
            Releases                = $true
            #ReleasesUnpacked        = $false
            ReleasesUnpacked        = @{
                Enabled         = $true
                IncludeTagName  = $false
                Path            = "$PSScriptRoot\..\Artefacts"
                RequiredModules = $true
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

New-PrepareModule -Configuration $Configuration