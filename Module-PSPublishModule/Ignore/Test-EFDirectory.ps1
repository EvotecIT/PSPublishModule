Clear-Host

Import-Module 'C:\Support\GitHub\PSPublishModule\PSPublishModule.psd1' -Force

$Configuration = @{
    Information = @{
        ModuleName        = 'EFILMDirectorySynchronization'
        DirectoryProjects = 'C:\Support\GitHub'

        Manifest          = @{
            # Version number of this module.
            ModuleVersion              = '0.0.1'
            # Supported PSEditions
            CompatiblePSEditions       = @('Desktop', 'Core')
            # ID used to uniquely identify this module
            GUID                       = 'd1b334d3-e2a7-431a-922a-96a327f4c187'
            # Author of this module
            Author                     = 'Przemyslaw Klys'
            # Company or vendor of this module
            CompanyName                = 'Eurofins'
            # Copyright statement for this module
            Copyright                  = "(c) 2020 - $((Get-Date).Year) Przemyslaw Klys @ Eurofins. All rights reserved."
            # Description of the functionality provided by this module
            Description                = 'PowerShell module to syncchonize ILM with Federated Directory'
            # Minimum version of the Windows PowerShell engine required by this module
            PowerShellVersion          = '5.1'
            # Private data to pass to the module specified in RootModule/ModuleToProcess. This may also contain a PSData hashtable with additional module metadata used by PowerShell.
            Tags                       = @('Windows', 'macOS', "Linux", 'Federated', 'Directory', 'FederatedDirectory')

            IconUri                    = 'https://www.federated.directory/assets/icons/icon-144x144.png'

            RequiredModules            = @(
                @{ ModuleName = 'PSSharedGoods'; ModuleVersion = "Latest"; Guid = 'ee272aa8-baaa-4edf-9f45-b6d6f7d844fe' }
                @{ ModuleName = 'PowerFederatedDirectory'; ModuleVersion = "Latest"; Guid = 'b73875b9-d87b-4a10-8cb5-0980d180c05b' }
                @{ ModuleName = 'EFILM'; ModuleVersion = "Latest"; Guid = '49e0bffa-2cc6-4cba-9d7d-0ef0a82bd741' }
            )
            ExternalModuleDependencies = @(
                #"Microsoft.PowerShell.Management"
                #"Microsoft.PowerShell.Security"
            )
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
                ApprovedModules = @('PSSharedGoods', 'PSWriteColor', 'Connectimo', 'PSUnifi', 'PSWebToolbox', 'PSMyPassword')
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
            #RepositoryName = 'PSWriteHTML'
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
            Enable           = $true
            DeleteBefore     = $false
            Merge            = $true
            MergeMissing     = $true
            SignMerged       = $true
            Releases         = $true
            ReleasesUnpacked = @{
                Enabled         = $true
                IncludeTagName  = $false
                Relative        = $true # default is $true
                Path            = "$PSScriptRoot\..\Artefacts"
                RequiredModules = @{
                    Enabled = $true
                    Path    = "$PSScriptRoot\..\Artefacts\Modules"
                }
                FilesOutput     = [ordered] @{
                    #"Examples\PackageExamples\GetSpecificUsers.ps1"     = "GetSpecificUsers.ps1"
                    #"Examples\PackageExamples\GetSpecificUsersList.ps1" = "GetSpecificUsersList.ps1"
                    #"Examples\PackageExamples\GetUsers.ps1"             = "GetUsers.ps1"
                }
            }
            RefreshPSD1Only  = $false
        }
        BuildDocumentation = @{
            Enable        = $false # enables documentation processing
            StartClean    = $true # always starts clean
            UpdateWhenNew = $true # always updates right after new
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