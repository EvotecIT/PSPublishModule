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
        # Documentation     = @{
        #     Path       = 'Docs'
        #     PathReadme = 'Docs\Readme.md'
        # }
        Style             = @{
            PSD1 = 'Minimal' # Native
        }
        Signing           = @{
            CertificateThumbprint = '36A8A2D0E227D81A2D3B60DCE0CFCF23BEFC343B'
        }
    }
    Steps   = @{
        BuildLibraries = @{
            Enable        = $false # build once every time nuget gets updated
            Configuration = 'Release'
            Framework     = 'netstandard2.0', 'net472'
            #ProjectName   = 'ImagePlayground.PowerShell'
        }
        BuildModule    = @{  # requires Enable to be on to process all of that
            Enable                  = $true
            DeleteBefore            = $false
            Merge                   = $true
            MergeMissing            = $true
            SignMerged              = $true
            #CreateFileCatalog       = $false
            Releases                = $true
            #ReleasesUnpacked        = $false
            ReleasesUnpacked        = @{
                Enabled         = $false
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
        # BuildDocumentation = @{
        #     Enable        = $true # enables documentation processing
        #     StartClean    = $true # always starts clean
        #     UpdateWhenNew = $true # always updates right after update
        # }
        ImportModules  = @{
            Self            = $true
            RequiredModules = $false
            Verbose         = $false
        }
        PublishModule  = @{  # requires Enable to be on to process all of that
            Enabled      = $false
            Prerelease   = ''
            RequireForce = $false
            GitHub       = $false
        }
    }
}

New-PrepareModule -ModuleName 'PSPublishModule' -Configuration $Configuration {
    # Usual defaults as per standard module
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

    # Add approved modules, that can be used as a dependency, but only when specific function from those modules is used
    # And on that time only that function and dependant functions will be copied over
    # Keep in mind it has it's limits when "copying" functions such as it should not depend on DLLs or other external files
    New-ConfigurationModules -Type ApprovedModule -Name 'PSSharedGoods', 'PSWriteColor', 'Connectimo', 'PSUnifi', 'PSWebToolbox', 'PSMyPassword'

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
    # format PSD1 and PSM1 files when merging into a single file
    New-ConfigurationFormat -ApplyTo 'OnMergePSM1', 'OnMergePSD1' -Sort None @ConfigurationFormat
    # format PSD1 and PSM1 files within the module
    New-ConfigurationFormat -ApplyTo 'DefaultPSD1', 'DefaultPSM1'

    New-ConfigurationDocumentation -Enable:$false -StartClean -UpdateWhenNew -PathReadme 'Docs\Readme.md' -Path 'Docs'
}