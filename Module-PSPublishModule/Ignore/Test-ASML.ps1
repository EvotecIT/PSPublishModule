Clear-Host
Import-Module "C:\Support\GitHub\PSPublishModule\PSPublishModule.psd1" -Force

$Configuration = @{
    Information = @{
        ModuleName        = 'ASMLBenchmark'
        DirectoryProjects = 'C:\Support\GitHub'

        IncludePS1        = 'Private', 'Public'

        IncludeAsArray    = @{
            '$Script:BuiltinRules' = "Rules"
        }

        Manifest          = @{
            # Version number of this module.
            ModuleVersion              = '1.0.4'
            # Supported PSEditions
            CompatiblePSEditions       = @('Desktop', 'Core')
            # ID used to uniquely identify this module
            GUID                       = '481f442b-5183-4fb0-895c-cb293e9ff488'
            # Author of this module
            Author                     = 'Przemyslaw Klys'
            # Company or vendor of this module
            CompanyName                = 'ASML'
            # Copyright statement for this module
            Copyright                  = "(c) 2011 - $((Get-Date).Year) Przemyslaw Klys @ ASML. All rights reserved."
            # Description of the functionality provided by this module
            Description                = 'ASML CIS Benchmarks'
            # Minimum version of the Windows PowerShell engine required by this module
            PowerShellVersion          = '5.1'
            # Private data to pass to the module specified in RootModule/ModuleToProcess. This may also contain a PSData hashtable with additional module metadata used by PowerShell.
            Tags                       = @('Windows', 'CIS', 'Benchmarks')

            RequiredModules            = @(
                @{ ModuleName = 'PSSharedGoods'; ModuleVersion = "Latest"; Guid = 'ee272aa8-baaa-4edf-9f45-b6d6f7d844fe' }
                @{ ModuleName = 'PSWriteHTML'; ModuleVersion = "Latest"; Guid = 'a7bdf640-f5cb-4acf-9de0-365b322d245c' }
                @{ ModuleName = 'Mailozaurr'; ModuleVersion = "0.0.24"; Guid = '2b0ea9f1-3ff1-4300-b939-106d5da608fa' }
                @{ ModuleName = 'PSWriteExcel'; ModuleVersion = 'Latest'; Guid = '82232c6a-27f1-435d-a496-929f7221334b' }
                @{ ModuleName = 'AuditPolicy'; ModuleVersion = 'Latest'; Guid = '14651643-e1f8-4123-9250-9ed210b86963' }
                @{ ModuleName = 'Indented.SecurityPolicy'; ModuleVersion = 'Latest'; Guid = 'e1d90894-388a-42a1-aa09-b7bb0bbdfae0' }
                @{ ModuleName = 'SecurityPolicy'; ModuleVersion = 'Latest'; Guid = '0e3eaa53-5e0b-4f10-9375-d6a0a9a1eb45' }
                @{ ModuleName = 'PSEventViewer'; ModuleVersion = 'Latest'; Guid = '5df72a79-cdf6-4add-b38d-bcacf26fb7bc' }
            )
            ExternalModuleDependencies = @(
                #"ActiveDirectory"
                #"GroupPolicy"
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
                ApprovedModules = @('PSSharedGoods', 'PSWriteColor', 'Connectimo', 'PSUnifi', 'PSWebToolbox', 'PSMyPassword', 'AuditPolicy')
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
            Releases         = $false
            ReleasesUnpacked = @{
                Enabled         = $true
                IncludeTagName  = $false
                Relative        = $true # default is $true
                Path            = "Artefacts"
                RequiredModules = $true
                FilesOutput     = [ordered] @{
                    "Examples\Benchmark.ps1"              = "Benchmark.ps1"
                    "Examples\Servers.xlsx"               = "Servers.xlsx"
                    "Examples\Servers\ExampleServer.xlsx" = "ExampleServer.xlsx"
                }
            }
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