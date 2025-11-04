Clear-Host
Import-Module "C:\Support\GitHub\PSPublishModule\PSPublishModule.psm1" -Force

$Configuration = @{
    Information = @{
        ModuleName        = 'EFUserManager'
        DirectoryProjects = 'C:\Support\GitHub'
        LibrariesCore     = 'Lib\Standard'
        LibrariesDefault  = 'Lib\Standard'

        Manifest          = @{
            # Version number of this module.
            ModuleVersion              = '0.0.41'
            # Supported PSEditions
            CompatiblePSEditions       = @('Desktop', 'Core')
            # ID used to uniquely identify this module
            GUID                       = '49e0bffa-2cc6-4cba-9d7d-0ef0a82bd741'
            # Author of this module
            Author                     = 'Przemyslaw Klys'
            # Company or vendor of this module
            CompanyName                = 'Eurofins'
            # Copyright statement for this module
            Copyright                  = "(c) 2020 - $((Get-Date).Year) Przemyslaw Klys @ Eurofins. All rights reserved."
            # Description of the functionality provided by this module
            Description                = 'User Synchronization'
            # Minimum version of the Windows PowerShell engine required by this module
            PowerShellVersion          = '5.1'
            # Functions to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no functions to export.
            # Private data to pass to the module specified in RootModule/ModuleToProcess. This may also contain a PSData hashtable with additional module metadata used by PowerShell.
            Tags                       = @('Windows')
            #IconUri              = 'https://evotec.xyz/wp-content/uploads/2018/10/PSSharedGoods-Alternative.png'
            #ProjectUri                 = 'https://github.com/EvotecIT/Transferetto'
            RequiredModules            = @(
                @{ ModuleName = 'PSSharedGoods'; ModuleVersion = "Latest"; Guid = 'ee272aa8-baaa-4edf-9f45-b6d6f7d844fe' }
                @{ ModuleName = 'Mailozaurr'; ModuleVersion = "Latest"; Guid = '2b0ea9f1-3ff1-4300-b939-106d5da608fa' }
                @{ ModuleName = 'PSWriteHTML'; ModuleVersion = "Latest"; Guid = 'a7bdf640-f5cb-4acf-9de0-365b322d245c' }
                @{ ModuleName = 'Transferetto'; ModuleVersion = 'Latest'; Guid = '7d61db15-9efe-41d1-a1c0-81d738975dec' }
                @{ ModuleName = 'O365EmailAddressPolicy'; ModuleVersion = 'Latest'; Guid = '303e4cc7-2b05-469a-b700-ab84569a5464' }
            )
            ExternalModuleDependencies = @(
                # 'ActiveDirectory'
                #"DnsServer"
                #"DnsClient"
                #"CimCmdlets"
                #"NetTCPIP"
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
                ApprovedModules = @('Graphimo', 'PSSharedGoods', 'PSWriteColor', 'Connectimo', 'PSUnifi', 'PSWebToolbox', 'PSMyPassword', 'O365EmailAddressPolicy')
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
            ReleasesUnpacked = $true
            RefreshPSD1Only  = $false
        }
        BuildDocumentation = $false
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