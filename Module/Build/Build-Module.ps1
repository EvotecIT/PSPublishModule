# This version is for local building
# We need to remove library before we start, as it may contain old files, which will be in use once PSD1 loads
# This is only required for PSPublisModule, as it's the only module that is being built by itself

[CmdletBinding()] param(
    [switch] $JsonOnly,
    [string] $JsonPath = (Join-Path $PSScriptRoot '..\..\powerforge.json'),
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release',
    [switch] $NoDotnetBuild
)

if (-not $JsonOnly) {
    Remove-Item -Path (Join-Path $PSScriptRoot '..\Lib') -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $JsonOnly -and -not $NoDotnetBuild) {
    $csproj = Join-Path -Path $PSScriptRoot -ChildPath '..\..\PSPublishModule\PSPublishModule.csproj'
    if (Test-Path -LiteralPath $csproj) {
        Write-Host "ℹ️ Building PSPublishModule ($Configuration)" -ForegroundColor DarkGray
        $buildOutput = & dotnet build $csproj -c $Configuration --nologo --verbosity quiet 2>&1
        if ($LASTEXITCODE -ne 0) {
            $buildOutput | Out-Host
            Write-Host "❌ dotnet build failed (exit $LASTEXITCODE). Stopping." -ForegroundColor Red
            return
        }
    }
}

Import-Module "$PSScriptRoot\..\PSPublishModule.psd1" -Force

$buildParams = @{
    ModuleName = 'PSPublishModule'
    ExitCode   = $true
}
if ($JsonOnly) {
    $buildParams.JsonOnly = $true
    $buildParams.JsonPath = $JsonPath
}

Build-Module @buildParams -Settings {
    # Usual defaults as per standard module
    $Manifest = [ordered] @{
        ModuleVersion          = '3.0.X'
        #PreReleaseTag          = 'Preview5'
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
    New-ConfigurationModule -Type RequiredModule -Name 'powershellget' -Guid 'Auto' -Version 'Latest'
    New-ConfigurationModule -Type RequiredModule -Name 'PSScriptAnalyzer' -Guid 'Auto' -Version 'Latest'
    New-ConfigurationModule -Type RequiredModule -Name 'Pester' -Version Auto -Guid Auto
    New-ConfigurationModule -Type RequiredModule -Name 'Microsoft.PowerShell.PSResourceGet' -Guid 'Auto' -Version 'Latest'

    # Add external module dependencies, using loop for simplicity
    New-ConfigurationModule -Type ExternalModule -Name @(
        'Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Archive', 'Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Security'
    )

    # Add approved modules, that can be used as a dependency, but only when specific function from those modules is used
    # And on that time only that function and dependant functions will be copied over
    # Keep in mind it has it's limits when "copying" functions such as it should not depend on DLLs or other external files
    New-ConfigurationModule -Type ApprovedModule -Name 'PSSharedGoods', 'PSWriteColor', 'Connectimo', 'PSUnifi', 'PSWebToolbox', 'PSMyPassword'

    New-ConfigurationModuleSkip -IgnoreModuleName 'PKI', 'OpenAuthenticode' -IgnoreFunctionName @(
        # ignore functions from OpenAuthenticode module when used during linux/macos build
        'Set-OpenAuthenticodeSignature'
        'Get-OpenAuthenticodeSignature'
        # ignore functions from Microsoft.PowerShell.Security, as those are not on linux/macos
        'Get-AuthenticodeSignature'
        'Set-AuthenticodeSignature'
        # ignore functions from PKI module when used during linux/macos build
        #'Import-PfxCertificate'
        # seems to be windows only
        'New-FileCatalog'
    )

    $ConfigurationFormat = [ordered] @{
        RemoveComments                              = $true
        RemoveEmptyLines                            = $true

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
    New-ConfigurationDocumentation -Enable:$true -StartClean -UpdateWhenNew -PathReadme 'Docs\Readme.md' -Path 'Docs'

    # quality checks (non-blocking by default; add -FailOn* switches to hard-fail)
    $newConfigurationValidationSplat = @{
        Enable                               = $true
        StructureSeverity                    = 'Warning'
        DocumentationSeverity                = 'Warning'
        EnableScriptAnalyzer                 = $true
        ScriptAnalyzerSeverity               = 'Warning'
        FileIntegritySeverity                = 'Warning'
        FileIntegrityCheckTrailingWhitespace = $true
        FileIntegrityCheckSyntax             = $true
    }

    New-ConfigurationValidation @newConfigurationValidationSplat

    $newConfigurationFileConsistencySplat = @{
        Enable                   = $true
        RequiredEncoding         = 'UTF8BOM'
        RequiredLineEnding       = 'CRLF'
        ExcludeDirectories       = 'Build', 'Docs', 'Documentation', 'Examples', 'Tests'
        ExportReport             = $true
        CheckMixedLineEndings    = $true
        CheckMissingFinalNewline = $true
        Scope                    = 'StagingAndProject'
        EncodingOverrides        = @{ '*.xml' = 'UTF8' }
    }

    New-ConfigurationFileConsistency @newConfigurationFileConsistencySplat

    $newConfigurationCompatibilitySplat = @{
        Enable                         = $true
        RequireCrossCompatibility      = $true
        MinimumCompatibilityPercentage = 95
        ExportReport                   = $true
    }

    New-ConfigurationCompatibility @newConfigurationCompatibilitySplat

    New-ConfigurationImportModule -ImportSelf

    $newConfigurationBuildSplat = @{
        Enable                            = $true
        SignModule                        = if ($Env:COMPUTERNAME -eq 'EVOMONSTER') { $true } else { $false }
        # DeleteTargetModuleBeforeBuild     = $true
        MergeModuleOnBuild                = $true
        CertificateThumbprint             = '483292C9E317AA13B07BB7A96AE9D1A5ED9E7703'
        #CertificatePFXBase64           = $BasePfx
        #CertificatePFXPassword         = "zGT"
        DoNotAttemptToFixRelativePaths    = $false
        SkipBuiltinReplacements           = $true

        # required for Cmdlet/Alias functionality
        NETProjectPath                    = "$PSScriptRoot\..\..\PSPublishModule"
        ResolveBinaryConflicts            = $true
        ResolveBinaryConflictsName        = 'PSPublishModule'
        NETProjectName                    = 'PSPublishModule'
        NETConfiguration                  = 'Release'
        NETFramework                      = 'net8.0', 'net472'
        NETHandleAssemblyWithSameName     = $true
        #NETDocumentation                  = $true
        DotSourceLibraries                = $true
        DotSourceClasses                  = $true

        # This has to be disabled as it will not have DLLs required to do this
        NETBinaryModuleCmdletScanDisabled = $true

        VersionedInstallStrategy          = 'AutoRevision'   # or 'Exact'
        VersionedInstallKeep              = 3                # how many versions to retain
        KillLockersBeforeInstall          = $true
        KillLockersForce                  = $true
    }

    New-ConfigurationBuild @newConfigurationBuildSplat

    New-ConfigurationArtefact -Type Unpacked -Enable -Path "$PSScriptRoot\..\Artefacts\Unpacked\<TagModuleVersionWithPreRelease>" -RequiredModulesPath "$PSScriptRoot\..\Artefacts\Unpacked\<TagModuleVersionWithPreRelease>\Modules" -AddRequiredModules -CopyFiles @{
        "Examples\Step01.CreateModuleProject.ps1" = "Examples\Step01.CreateModuleProject.ps1"
        "Examples\Step02.BuildModuleOver.ps1"     = "Examples\Step02.BuildModuleOver.ps1"
    } -CopyFilesRelative

    New-ConfigurationArtefact -Type Packed -Enable -Path "$PSScriptRoot\..\Artefacts\PackedWithModules" -IncludeTagName -ID 'ToGitHub' -AddRequiredModules -CopyFiles @{
        "Examples\Step01.CreateModuleProject.ps1" = "Examples\Step01.CreateModuleProject.ps1"
        "Examples\Step02.BuildModuleOver.ps1"     = "Examples\Step02.BuildModuleOver.ps1"
    } -CopyFilesRelative -ArtefactName "PSPublishModule.<TagModuleVersionWithPreRelease>-FullPackage.zip"

    New-ConfigurationArtefact -Type Packed -Enable -Path "$PSScriptRoot\..\Artefacts\Packed" -IncludeTagName -ID 'ToGitHub' -ArtefactName "PSPublishModule.<TagModuleVersionWithPreRelease>.zip"

    # Disabled because PSPublishModule testing itself after build causes multiple module instances
    # which breaks InModuleScope tests. The module is tested separately via PSPublishModule.Tests.ps1
    #New-ConfigurationTest -TestsPath "$PSScriptRoot\..\Tests" -Enable

    # global options for publishing to github/psgallery
    # you can use FilePath where APIKey are saved in clear text or use APIKey directly
    #New-ConfigurationPublish -Type PowerShellGallery -FilePath 'C:\Support\Important\PowerShellGalleryAPI.txt' -Enabled:$true
    #New-ConfigurationPublish -Type GitHub -FilePath 'C:\Support\Important\GitHubAPI.txt' -UserName 'EvotecIT' -Enabled:$true -ID 'ToGitHub' -OverwriteTagName '<TagModuleVersionWithPreRelease>'


    ### FOR TESTING PURPOSES ONLY ###
    ### SHOWING HOW THINGS WORK HERE ###

    #New-ConfigurationArtefact -Type Packed -Enable -Path "$PSScriptRoot\..\Artefacts\Packed2" -IncludeTagName -ID 'Packed2'
    #New-ConfigurationArtefact -Type Packed -Enable -Path "$PSScriptRoot\..\Artefacts\Packed1" -IncludeTagName

    # those 2 are only useful for testing purposes
    # New-ConfigurationArtefact -Type Script -Enable -Path "$PSScriptRoot\..\Artefacts\Script" -IncludeTagName {
    #     # Lets test this, this will be added in the bottom of the script
    #     Invoke-ModuleBuilder
    # } -ID 'ToGitHubAsScript'
    # New-ConfigurationArtefact -Type ScriptPacked -Enable -Path "$PSScriptRoot\..\Artefacts\ScriptPacked" -ArtefactName "Script-<ModuleName>-$((Get-Date).ToString('yyyy-MM-dd')).zip" {
    #     Invoke-ModuleBuilder
    # } -PreScriptMerge {
    #     # Lets test this
    #     param (
    #         [int]$Mode
    #     )
    # } -ScriptName 'Invoke-ModuleBuilder.ps1'
    # New-ConfigurationArtefact -Type Script -Enable -Path "$PSScriptRoot\..\Artefacts\Script" {
    #     Invoke-ModuleBuilder
    # } -PreScriptMerge {
    #     # Lets test this
    #     param (
    #         [int]$Mode
    #     )
    # } -ScriptName 'Invoke-ModuleBuilder.ps1'

    #New-ConfigurationPublish -Type GitHub -FilePath 'C:\Support\Important\GitHubAPI.txt' -UserName 'EvotecIT' -Enabled:$true -ID 'ToGitHubWithoutModules' -OverwriteTagName 'v1.8.0-Preview1'
    #New-ConfigurationPublish -Type GitHub -FilePath 'C:\Support\Important\GitHubAPI.txt' -UserName 'EvotecIT' -Enabled:$true -ID 'ToGitHubAsScript'
}
