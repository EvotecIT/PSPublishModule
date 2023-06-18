# PSPublishModule - PowerShell Module

<p align="center">
  <a href="https://dev.azure.com/evotecpl/PSPublishModule/_build/latest?definitionId=3"><img src="https://img.shields.io/azure-devops/build/evotecpl/a5ce5d3e-9ae2-49a0-9905-53eb41205fc9/7?label=Azure%20Pipelines&style=flat-square"></a>
  <a href="https://www.powershellgallery.com/packages/PSPublishModule"><img src="https://img.shields.io/powershellgallery/v/PSPublishModule.svg?style=flat-square"></a>
  <a href="https://www.powershellgallery.com/packages/PSPublishModule"><img src="https://img.shields.io/powershellgallery/vpre/PSPublishModule.svg?label=powershell%20gallery%20preview&colorB=yellow&style=flat-square"></a>
  <a href="https://github.com/EvotecIT/PSPublishModule"><img src="https://img.shields.io/github/license/EvotecIT/PSPublishModule.svg?style=flat-square"></a>

</p>

<p align="center">
  <a href="https://www.powershellgallery.com/packages/PSPublishModule"><img src="https://img.shields.io/powershellgallery/p/PSPublishModule.svg?style=flat-square"></a>
  <a href="https://github.com/EvotecIT/PSPublishModule"><img src="https://img.shields.io/github/languages/top/evotecit/PSPublishModule.svg?style=flat-square"></a>
  <a href="https://github.com/EvotecIT/PSPublishModule"><img src="https://img.shields.io/github/languages/code-size/evotecit/PSPublishModule.svg?style=flat-square"></a>
  <a href="https://www.powershellgallery.com/packages/PSPublishModule"><img src="https://img.shields.io/powershellgallery/dt/PSPublishModule.svg?style=flat-square"></a>
</p>

<p align="center">
  <a href="https://twitter.com/PrzemyslawKlys"><img src="https://img.shields.io/twitter/follow/PrzemyslawKlys.svg?label=Twitter%20%40PrzemyslawKlys&style=flat-square"></a>
  <a href="https://evotec.xyz/hub"><img src="https://img.shields.io/badge/Blog-evotec.xyz-2A6496.svg?style=flat-square"></a>
  <a href="https://www.linkedin.com/in/pklys"><img src="https://img.shields.io/badge/LinkedIn-pklys-0077B5.svg?logo=LinkedIn&style=flat-square"></a>
</p>

## Description

This module is a module builder helper that helps build PowerShell modules "Evotec way".
It allows us to make sure our modules are built the same way every time making the process really easy to build and publish new versions.

Each module that is published to PowerShellGallery has Build or Publish folder next to it and within it there is PS1 script which executes module building that uses this module builder.

For examples and usage you can visit other projects and see how they are built in addition to what is shown in Build folder.

## Support This Project

If you find this project helpful, please consider supporting its development.
Your sponsorship will help the maintainers dedicate more time to maintenance and new feature development for everyone.

It takes a lot of time and effort to create and maintain this project.
By becoming a sponsor, you can help ensure that it stays free and accessible to everyone who needs it.

To become a sponsor, you can choose from the following options:

 - [Become a sponsor via GitHub Sponsors :heart:](https://github.com/sponsors/PrzemyslawKlys)
 - [Become a sponsor via PayPal :heart:](https://paypal.me/PrzemyslawKlys)

Your sponsorship is completely optional and not required for using this project.
We want this project to remain open-source and available for anyone to use for free,
regardless of whether they choose to sponsor it or not.

If you work for a company that uses our .NET libraries or PowerShell Modules,
please consider asking your manager or marketing team if your company would be interested in supporting this project.
Your company's support can help us continue to maintain and improve this project for the benefit of everyone.

Thank you for considering supporting this project!

### Installing

```powershell
Install-Module -Name PSPublishModule -AllowClobber -Force
```

Force and AllowClobber aren't necessary but they do skip errors in case some appear.

### Updating

```powershell
Update-Module -Name PSPublishModule
```

That's it. Whenever there's a new version you simply run the command and you can enjoy it. Remember, that you may need to close, reopen the PowerShell session if you have already used the module before updating it.

**The important thing** is if something works for you on production, keep using it till you test the new version on a test computer. I do changes that may not be big, but big enough that auto-update will break your code. For example, small rename to a parameter and your code stops working! Be responsible!

### Usage

Introduced in **1.0.0** a new way to build PowerShell module based on DSL language.

```powershell
Build-Module -ModuleName 'PSPublishModule' {
    # Usual defaults as per standard module
    $Manifest = [ordered] @{
        ModuleVersion          = '1.0.X'
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
    New-ConfigurationModule -Type RequiredModule -Name 'platyPS' -Guid 'Auto' -Version 'Latest'
    New-ConfigurationModule -Type RequiredModule -Name 'powershellget' -Guid 'Auto' -Version 'Latest'
    New-ConfigurationModule -Type RequiredModule -Name 'PSScriptAnalyzer' -Guid 'Auto' -Version 'Latest'

    # Add external module dependencies, using loop for simplicity
    foreach ($Module in @('Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Archive', 'Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Security')) {
        New-ConfigurationModule -Type ExternalModule -Name $Module
    }

    # Add approved modules, that can be used as a dependency, but only when specific function from those modules is used
    # And on that time only that function and dependant functions will be copied over
    # Keep in mind it has it's limits when "copying" functions such as it should not depend on DLLs or other external files
    New-ConfigurationModule -Type ApprovedModule -Name 'PSSharedGoods', 'PSWriteColor', 'Connectimo', 'PSUnifi', 'PSWebToolbox', 'PSMyPassword'

    #New-ConfigurationModuleSkip -IgnoreFunctionName 'Invoke-Formatter', 'Find-Module' -IgnoreModuleName 'platyPS'

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
    # enable formatting is not required as Configuration is provided
    New-ConfigurationFormat -ApplyTo 'OnMergePSM1', 'OnMergePSD1' -Sort None @ConfigurationFormat
    # format PSD1 and PSM1 files within the module
    # enable formatting is required to make sure that formatting is applied (with default settings)
    New-ConfigurationFormat -ApplyTo 'DefaultPSD1', 'DefaultPSM1' -EnableFormatting -Sort None
    # when creating PSD1 use special style without comments and with only required parameters
    New-ConfigurationFormat -ApplyTo 'DefaultPSD1', 'OnMergePSD1' -PSD1Style 'Minimal'

    # configuration for documentation, at the same time it enables documentation processing
    New-ConfigurationDocumentation -Enable:$false -StartClean -UpdateWhenNew -PathReadme 'Docs\Readme.md' -Path 'Docs'

    New-ConfigurationImportModule -ImportSelf -ImportRequiredModules

    New-ConfigurationBuild -Enable:$true -SignModule -DeleteTargetModuleBeforeBuild -MergeModuleOnBuild -CertificateThumbprint '36A8A2D0E227D81A2D3B60DCE0CFCF23BEFC343B'

    New-ConfigurationArtefact -Type Unpacked -Enable -Path "$PSScriptRoot\..\Artefacts" -RequiredModulesPath "$PSScriptRoot\..\Artefacts\Modules"
    New-ConfigurationArtefact -Type Packed -Enable -Path "$PSScriptRoot\..\Releases" -IncludeTagName

    # global options for publishing to github/psgallery
    New-ConfigurationPublish -Type PowerShellGallery -FilePath 'C:\Support\Important\PowerShellGalleryAPI.txt' -Enabled:$false
    New-ConfigurationPublish -Type GitHub -FilePath 'C:\Support\Important\GitHubAPI.txt' -UserName 'EvotecIT' -Enabled:$false
}
```

The old way still works, but is less preferred. It's kept for backwards compatibility.
It's much harder to discover what is what and how it impacts things. It's also harder to maintain.

```powershell
$Configuration = @{
    Information = @{
        ModuleName        = 'PSPublishModule'
        #DirectoryProjects = 'C:\Support\GitHub'

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
            ModuleVersion              = '1.0.0'
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
            Description                = 'Simple project allowing preparing, managing, building and publishing modules to PowerShellGallery'
            # Minimum version of the Windows PowerShell engine required by this module
            PowerShellVersion          = '5.1'
            # Functions to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no functions to export.
            Tags                       = @('Windows', 'MacOS', 'Linux', 'Build', 'Module')
            IconUri                    = 'https://evotec.xyz/wp-content/uploads/2019/02/PSPublishModule.png'
            ProjectUri                 = 'https://github.com/EvotecIT/PSPublishModule'

            RequiredModules            = @(
                @{ ModuleName = 'platyps'; ModuleVersion = "Latest"; Guid = 'Auto' }
                @{ ModuleName = 'powershellget'; ModuleVersion = "2.2.5"; Guid = '1d73a601-4a6c-43c5-ba3f-619b18bbb404' }
                @{ ModuleName = 'PSScriptAnalyzer'; ModuleVersion = "Latest"; Guid = 'd6245802-193d-4068-a631-8863a4342a18' }
            )
            ExternalModuleDependencies = @(
                "Microsoft.PowerShell.Utility"
                "Microsoft.PowerShell.Archive"
                "Microsoft.PowerShell.Management"
                "Microsoft.PowerShell.Security"
            )
            DotNetFrameworkVersion     = '4.5.2'
        }
    }
    Options     = @{
        Merge             = @{
            Sort           = 'None'
            FormatCodePSM1 = @{
                Enabled           = $false
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
            # Style          = @{
            #     PSD1 = 'Native'
            # }
        }
        Standard          = @{
            FormatCodePSM1 = @{

            }
            FormatCodePSD1 = @{
                Enabled = $true
                #RemoveComments = $true
            }
            # Style          = @{
            #     PSD1 = 'Native'
            # }
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
        Signing           = @{
            CertificateThumbprint = '36A8A2D0E227D81A2D3B60DCE0CFCF23BEFC343B'
        }
    }
    Steps       = @{
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
            CreateFileCatalog       = $false
            Releases                = $true
            #ReleasesUnpacked        = $false
            ReleasesUnpacked        = @{
                Enabled         = $true
                IncludeTagName  = $false
                Path            = "$PSScriptRoot\..\Artefacts"
                RequiredModules = @{
                    Enabled = $true
                    Path    = "$PSScriptRoot\..\Artefacts\Modules"
                }
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

Build-Module -Configuration $Configuration
```