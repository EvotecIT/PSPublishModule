Clear-Host
Import-Module "C:\Support\GitHub\PSPublishModule\PSPublishModule.psm1" -Force

$Configuration = @{
    Information = @{
        ModuleName        = 'Emailimo'
        DirectoryProjects = 'C:\Support\GitHub'
        ScriptsToProcess  = 'Enums'

        Manifest          = @{
            Path              = "C:\Support\GitHub\Emailimo\Emailimo.psd1"
            # Script module or binary module file associated with this manifest.
            RootModule        = 'Emailimo.psm1'
            # Version number of this module.
            ModuleVersion     = '0.0.11'
            # ID used to uniquely identify this module
            GUID              = '3e94ee8d-4851-467e-8f84-17e518f8f865'
            # Author of this module
            Author            = 'Przemyslaw Klys'
            # Company or vendor of this module
            CompanyName       = 'Evotec'
            # Copyright statement for this module
            Copyright         = 'Evotec (c) 2011-2019. All rights reserved.'
            # Description of the functionality provided by this module
            Description       = 'Easy way to send emails in PowerShell'
            # Minimum version of the Windows PowerShell engine required by this module
            PowerShellVersion = '5.1'
            # Tags applied to this module. These help with module discovery in online galleries.
            Tags              = @('Windows', 'MacOs', 'Linux', 'Email')
            # A URL to the main website for this project.
            ProjectUri        = 'https://github.com/EvotecIT/Emailimo'
            # A URL to an icon representing this module.
            IconUri           = 'https://evotec.xyz/wp-content/uploads/2019/04/Emailimo.png'
            # Modules that must be imported into the global environment prior to importing this module
            RequiredModules = @(
                #@{ ModuleName = 'PSSharedGoods'; ModuleVersion = "0.0.105"; Guid = 'ee272aa8-baaa-4edf-9f45-b6d6f7d844fe' }
                @{ ModuleName = 'PSWriteHTML'; ModuleVersion = '0.0.61'; Guid = 'a7bdf640-f5cb-4acf-9de0-365b322d245c' }
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
            Enable       = $true
            Merge        = $true
            MergeMissing = $true
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

New-PrepareModule -Configuration $Configuration