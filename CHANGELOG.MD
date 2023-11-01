# PSPublishModule Changelog

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