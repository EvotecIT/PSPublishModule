Clear-Host
Import-Module 'C:\Users\przemyslaw.klys\OneDrive - Evotec\Support\GitHub\PSPublishModule\PSPublishModule.psd1' -Force

$Configuration = @{
    Information = @{
        ModuleName           = 'PSWriteWord'

        DirectoryProjects    = 'C:\Support\GitHub'
        DirectoryModulesCore = "$Env:USERPROFILE\Documents\PowerShell\Modules"
        DirectoryModules     = "$Env:USERPROFILE\Documents\WindowsPowerShell\Modules"

        FunctionsToExport    = 'Public'
        AliasesToExport      = 'Public'

        LibrariesCore        = 'Lib\Core'
        LibrariesDefault     = 'Lib\Default'

        Manifest             = @{
            Path              = "C:\Support\GitHub\PSWriteWord\PSWriteWord.psd1"
            # Minimum version of the Windows PowerShell engine required by this module
            PowerShellVersion = '5.1'
            # ID used to uniquely identify this module
            GUID              = '6314c78a-d011-4489-b462-91b05ec6a5c4'
            # Version number of this module.
            ModuleVersion     = '1.0.3'
            # Author of this module
            Author            = 'Przemyslaw Klys'
            # Company or vendor of this module
            CompanyName       = 'Evotec'
            # Copyright statement for this module
            Copyright         = '(c) 2011-2019 Przemyslaw Klys. All rights reserved.'
            # Description of the functionality provided by this module
            Description       = 'Simple project to create Microsoft Word in PowerShell without having Office installed.'
            # Tags applied to this module. These help with module discovery in online galleries.
            Tags              = @('word', 'docx', 'write', 'PSWord', 'office', 'windows', 'doc')
            # A URL to the main website for this project.
            ProjectUri        = 'https://github.com/EvotecIT/PSWriteWord'

            IconUri           = 'https://evotec.xyz/wp-content/uploads/2018/10/PSWriteWord.png'

            LicenseUri        = 'https://github.com/EvotecIT/PSWriteWord/blob/master/License'

            RequiredModules   = @(
                @{ ModuleName = 'PSSharedGoods'; ModuleVersion = "0.0.107"; Guid = 'ee272aa8-baaa-4edf-9f45-b6d6f7d844fe' }
            )
        }
    }
    Options     = @{
        Merge             = @{
            Sort           = 'None'
            FormatCodePSM1 = @{
                Enabled           = $true
                RemoveComments    = $true
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
        ImportModules     = @{
            Self            = $true
            RequiredModules = $false
            Verbose         = $false
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
    }
    Steps       = @{
        BuildModule        = @{
            Enable       = $true
            DeleteBefore = $true
            Merge        = $true
            MergeMissing = $false
            Releases     = $true
        }
        BuildDocumentation = $false
        PublishModule      = @{
            Enabled      = $false
            Prerelease   = ''
            RequireForce = $false
            GitHub       = $false
        }
    }
}

New-PrepareModule -Configuration $Configuration -Verbose