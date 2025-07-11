﻿# PSPublishModule Changelog

## 2.0.19 - 2025.06.17
### What's Changed
* Improve error handling in release utilities by @PrzemyslawKlys in https://github.com/EvotecIT/PSPublishModule/pull/32
* feat: ✨ Introduce New-DLLHandleRuntime function and integrate runti… by @PrzemyslawKlys in https://github.com/EvotecIT/PSPublishModule/pull/33

**Full Changelog**: https://github.com/EvotecIT/PSPublishModule/compare/v2.0.18...v2.0.19

## 2.0.18 - 2025.06.06
### What's new
- Update to docs and small logic improved for Get-ProjectVersion and Set-ProjectVersion

## 2.0.17 - 2025.05.28
- Make sure to error out on PSM1 file missing
- Added support for .NET 8.0
- Added `Set-ProjectVersion` cmdlet to allow for setting version of the project across (csproj/psd1/ps1) files for consistency in multi-file projects
- Added `Get-ProjectVersion` cmdlet to allow for getting version of the project across (csproj/psd1/ps1) files for consistency in multi-file projects

```powershell
Get-ProjectVersion -Path "C:\Support\GitHub\DnsClientX" -ExcludeFolders @('C:\Support\GitHub\DnsClientX\Module\Artefacts')
Set-ProjectVersion -Path "C:\Support\GitHub\DnsClientX" -NewVersion "0.4.0"
Set-ProjectVersion -Path "C:\Support\GitHub\DnsClientX" -VersionType Build
```

## 2.0.16 - 2024.10.27
- Improve error handling

## 2.0.15 - 2024.10.11
### What's Changed
* Update BuildModule.yml by @SamErde in https://github.com/EvotecIT/PSPublishModule/pull/27
* Improve error handling
* Improve DLL handling
* Add new options for binary cmdlet scanning
* Fixes detecting cmdlets in binary modules for PS 5.1
* Other small improvements

### New Contributors
* @SamErde made their first contribution in https://github.com/EvotecIT/PSPublishModule/pull/27

**Full Changelog**: https://github.com/EvotecIT/PSPublishModule/compare/v2.0.14...v2.0.15

## 2.0.14 - 2024.08.14

### What's Changed
* Adding cmdlet detection by @PrzemyslawKlys in https://github.com/EvotecIT/PSPublishModule/pull/25
* Add custom and builtin place holders replacement functionality by @PrzemyslawKlys in https://github.com/EvotecIT/PSPublishModule/pull/26

**Full Changelog**: https://github.com/EvotecIT/PSPublishModule/compare/v2.0.13...v2.0.14

## 2.0.13 - 2024.07.13
- When using `New-ConfigurtionManifest` and using `CmdletsToExport` it would not export them in PSM1 file. This is now fixed
- Moved function to separate file to improve visibility

## 2.0.12 - 2024.05.06
- Always publish to Github with PreReleaseTag if it's set
- Added support for HelpOut module to be used for help generation (platyPS will stay default for now)
- Added support for `NETIgnoreLibraryOnLoad` in `New-ConfigurationBuild` to allow for ignoring libraries on load (for example C/C++ dlls)
- Added support for `NETBinaryModule` in `New-ConfigurationBuild` to allow for building binary modules (C#) and doing Import-Module in PSM1 for them
- Improves detection of used functions/aliases in module (now it's more accurate)
- When using `New-ConfigurationArtefact` with Script artefact type and removal of comments it would break the whole script. This is now fixed
- Fixes case where during binary module building the DLL would not be used at all
- Improvement to `TargetFramework` and `TargetFrameworks` support when using C# projects. `TargetFrameworkVersion` is not supported.
- Added support for `NETHandleAssemblyWithSameName` in `New-ConfigurationBuild` to allow for handling of assemblies with same name (for example Newtonsoft.Json)
- Improved loading of libraries to use foreach instead of loading one by one
- Removed dependency on `PlatyPS`/`HelpOut`. Now it's optional and can be used if needed. HelpOut does have some issues that I hope will be solved sooner or later
- Improved creating empty PSM1 file when Public/Private folders have no files or are not present
- Added `NETProjectPath` to `New-ConfigurationBuild` to allow for specifying path to project file (csproj) when building binary modules
- Improve `New-ConfigurationManifest` allowing  to define `FunctionsToExport` and `CmdletsToExport` and `AliasesToExport` to allow for more control over exported functions/cmdlets/aliases (by default Aliaeses/Functions are exported but you can overwrite them)

## 2.0.11
- Fix for broken script generation if `New-ConfigurationCommand` is used

## 2.0.10
- Fix for paths that I've broken in 2.0.9

## 2.0.9
- Added usage of OpenAuthenticode from @jborean93 to allow for signing of modules on Linux/MacOS
  - The module is not automatically installed, you need to install it yourself
  - May need to be updated, as haven't really tested it

## 2.0.8
- Improve error handling
- Added `NETExcludeMainLibrary` to not include Initialize library if no logic is required
- Added `NETExcludeLibraryFilter` to allow for excluding libraries from being copied on build
- Added `PreScriptMergePath` and `PostScriptMergePath` to allow for files implementation instead of direct code in `New-ConfigurationArtefact`
  - This supplements using `PreScriptMerge` and `PostScriptMerge` (which are still supported, but overwritten when using `PreScriptMergePath` and `PostScriptMergePath`)

## 2.0.7
- Added to `New-ConfigurationArtefact` replacement of variables in paths (for example `<TagModuleVersionWithPreRelease>`)
- Added to `New-ConfigurationArtefact` - `DoNotClear` parameter to allow for not clearing of artefact folder, by default it will delete it's content with excluded .zip files (if any)

## 2.0.6
- Fixes problem with defaults such as IncludeAll, IncludePS1 and similar always being set preventing user from overwriting values on demand

## 2.0.5

### What's new
- Fixes remove comments functionality
- Added remove empty lines functionality
- Small fixes on PSD1 generation (sorting fixed)

**Full Changelog**: https://github.com/EvotecIT/PSPublishModule/compare/v2.0.4...v2.0.5

## 2.0.4
### What's Changed
* Adding support for Manual version bumping. by @cocazoulou in https://github.com/EvotecIT/PSPublishModule/pull/21

### New Contributors
* @cocazoulou made their first contribution in https://github.com/EvotecIT/PSPublishModule/pull/21

**Full Changelog**: https://github.com/EvotecIT/PSPublishModule/compare/v2.0.3...v2.0.4

## 2.0.3
- Fix for non-terminating error when can't delete folders

## 2.0.2 - 2023.08.06
- Fail build script if copying of module fails

## 2.0.1 - 2023.08.04
- Improve `New-ConfigurationArtefact` to allow for more flexibility for "scripts"
  - Added `PreScriptMerge` and `PostScriptMerge`
  - Remove `ScriptMerge` (renamed to `PostScriptMerge`)
  - Added `ScriptName` to allow for custom name of script (renames the file from usual module name)

## 2.0.0 - 2023.08.03
- Fixes to Import-Module functionality

## 1.8.0-Preview5
- Fixes wrong path for packed zip

## 1.8.0-Preview4
- Added handling of wrong certificate thumbrint
- Prevent failure on required modules
- Stop on missing requirede module

## 1.8.0-Preview3
- Fixes `ModuleVersionWithPreRelease` -and `TagModuleVersionWithPreRelease` tags

## 1.8.0-Preview2
- Fix for old configuration supporting releases and releases unpacked
- Fix for ModuleName in old configuration
- Allow to publish module Artefacts to GitHub using single `New-ConfigurationPublish` by marking multiple artefacts with same ID
- Small visual improvements

## 1.8.0-Preview1
- Added `RequiredVersion` to `New-ConfigurationModule` to allow specifying exact version instead the minimum version
- Minor bugfix for potential issue with CommandModuleDependencies
- Added ability to create multiple artefacts (not just 2) by using `New-ConfigurationArtefact`
- Added ability to pick from 4 types of artefacts (Unpacked, Packed, Script, PackedScript)
  - Unpacked - unpacked module
  - Packed - packed module (as zip)
  - Script - script that is module in form of PS1 without PSD1 - only applicable to very simple modules
  - PackedScript - packed module (as zip) that is script that is module in form of PS1 without PSD1 - only applicable to very simple modules
- Added ability to publish multiple artefacts (not just 2) by using `New-ConfigurationPublish`
- Removed `PreReleaseTag` from `New-ConfigurationPublish`
- Added `PreReleaseTag` to `New-ConfigurationManifest` to allow specifying pre-release tag
- `PreReleaseTag` is autoamtically used for GitHub publishing when Manifest is set to prerelease

## 1.7.0
- Improve `New-ConfigurationBuild` by adding CertificatePFXPath, CertificatePFXBase64, CertificatePFXPassword to support GitHub Actions/AzureDevOps
- Fixes copy of files during build process
- Fix exit code when build fails

## 1.6.0 - 2023.07.11
- Improve logic for replacing expandable variables and allow disabling this feature
- Impovements overall

## 1.5.1 - 2023.07.06
- Improve logic for detection of used functions
- Update default configuration for new modules

## 1.5.0 - 2023.07.04
- Fixes detection of unknown functions in module

## 1.4.0 - 2023.07.03
- Fixes minor issue with reading required modules

## 1.3.0 - 2023.07.03
- Improve termination when things go wrong
- Fix replacement of GUID in module manifest when creating new module
- Fix for duplicate detection of used functions

## 1.2.0 - 2023.06.23
- Improve termination when things go wrong
- Small fixes here and there
- Added ability to test after merge before publishing

## 1.1.0 - 2023.06.19
- Improve initially provided files when creating new module for easy use
- Autogenerate the example Build-Module.ps1 file for new projects
- Update GUID/ModuleName based on data provided by user
- Other small improvements

## 1.0.0 - 2023.06.18
- Big rewrite of PSPublishModule allowing more configuration options
- New configuration options
- New safety checks in place

## 0.9.53 - 2022.12.31
- Adds local versioning via BuildModule\LocalVersion property (bumps version on every build)
- No longer delete older tagged unpacked releases (only current) when tagging is enabled
- Allows to specify Releases property in BuildModule\Releases. Both Enabled/Path need to be specified. If not specified it will use default values.

## 0.9.52 - 2022.12.30
- Changed loading of libraries so it's always done in following way:
 - If Standard, Core and Default are present -> load Default when PS 5, load Standard when PS 6+
 - If Standard, Default are present -> load Default when PS 5, load Standard when PS 6+
 - If Standard and Core are present -> load Standard only
 - If Core and Default are present -> load Default when PS 5, load Core when PS 6+
 - If Standard is present -> load Standard only for all PowerShell

I've removed the logic where Standard would always get loaded even if Default/Core was also provided which could lead to unexpected behavior.
- Added ability to build libraries on demand (when using -BuildLibraries). Requires more testing.
- Cleaned code a bit, moved things around
- Added ability to ResolveBinaryConflicts (when using -ResolveBinaryConflicts). Requires more testing. Visible in project [ImagePlayground](https://github.com/EvotecIT/ImagePlayground)
- Removed need for `DirectoryProjects` in `$Configuration.Information` - if not present it will get the value itself

## 0.9.51 - 2022.09.26
- Fixed path behavior for ReleasedUnpacked (hopefully)

## 0.9.43 - 2022.04.14
- Small fixes for publishing modules with Standard Libraries only
- Improved building of Artefacts