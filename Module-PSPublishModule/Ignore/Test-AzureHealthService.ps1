Clear-Host
Import-Module "C:\Users\przemyslaw.klys\OneDrive - Evotec\Support\GitHub\PSPublishModule\PSPublishModule.psd1" -Force

$Configuration = @{
    Information = @{
        ModuleName        = 'PSWinDocumentation.AzureHealthService'
        DirectoryProjects = 'C:\Support\GitHub'

        Manifest          = @{
            # Supported PSEditions
            CompatiblePSEditions = @('Desktop', 'Core')
            # Version number of this module.
            ModuleVersion        = '0.0.1'
            # ID used to uniquely identify this module
            GUID                 = '5a1dfb46-012e-4800-a556-93e1d770fbfb'
            # Author of this module
            Author               = 'Przemyslaw Klys'
            # Company or vendor of this module
            CompanyName          = 'Evotec'
            # Copyright statement for this module
            Copyright            = 'Evotec (c) 2011-2019. All rights reserved.'
            # Description of the functionality provided by this module
            Description          = 'Module that helps providing Azure Health as PowerShell data.'
            # Minimum version of the Windows PowerShell engine required by this module
            PowerShellVersion    = '5.1'
            # Tags applied to this module. These help with module discovery in online galleries.
            Tags                 = @('Windows', 'MacOS', 'Linux', 'Azure')
            # A URL to the main website for this project.
            ProjectUri           = 'https://github.com/EvotecIT/PSWinDocumentation.AzureHealthService'
            # A URL to an icon representing this module.
            IconUri              = 'https://evotec.xyz/wp-content/uploads/2018/10/PSWinDocumentation.png'
            # Modules that must be imported into the global environment prior to importing this module
            RequiredModules      = @(
                @{ ModuleName = 'PSParseHTML'; ModuleVersion = "0.0.10"; Guid = 'f0387960-7034-4918-a1e1-d5847cbf90df' }
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
            Enable              = $true
            DeleteBefore        = $true
            LibrarySeparateFile = $false
            Merge               = $true
            MergeMissing        = $true
            Releases            = $true
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