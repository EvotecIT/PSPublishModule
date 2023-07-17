### PSPublishModule Changelog

#### 1.7.0
- Improve `New-ConfigurationBuild` by adding CertificatePFXPath, CertificatePFXBase64, CertificatePFXPassword to support GitHub Actions/AzureDevOps
- Fixes copy of files during build process

#### 1.6.0 - 2023.07.11
- Improve logic for replacing expandable variables and allow disabling this feature
- Impovements overall

#### 1.5.1 - 2023.07.06
- Improve logic for detection of used functions
- Update default configuration for new modules

#### 1.5.0 - 2023.07.04
- Fixes detection of unknown functions in module

#### 1.4.0 - 2023.07.03
- Fixes minor issue with reading required modules

#### 1.3.0 - 2023.07.03
- Improve termination when things go wrong
- Fix replacement of GUID in module manifest when creating new module
- Fix for duplicate detection of used functions

#### 1.2.0 - 2023.06.23
- Improve termination when things go wrong
- Small fixes here and there
- Added ability to test after merge before publishing

#### 1.1.0 - 2023.06.19
- Improve initially provided files when creating new module for easy use
- Autogenerate the example Build-Module.ps1 file for new projects
- Update GUID/ModuleName based on data provided by user
- Other small improvements

#### 1.0.0 - 2023.06.18
- Big rewrite of PSPublishModule allowing more configuration options
- New configuration options
- New safety checks in place

#### 0.9.53 - 2022.12.31
- Adds local versioning via BuildModule\LocalVersion property (bumps version on every build)
- No longer delete older tagged unpacked releases (only current) when tagging is enabled
- Allows to specify Releases property in BuildModule\Releases. Both Enabled/Path need to be specified. If not specified it will use default values.

#### 0.9.52 - 2022.12.30
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

#### 0.9.51 - 2022.09.26
- Fixed path behavior for ReleasedUnpacked (hopefully)

#### 0.9.43 - 2022.04.14
- Small fixes for publishing modules with Standard Libraries only
- Improved building of Artefacts