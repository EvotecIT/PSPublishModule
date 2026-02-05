# PSPublishModule parity matrix (2.0.26 PowerShell DSL vs 3.x C#)

Scope:
- DSL configuration segments (`New-Configuration*`) and pipeline behavior.
- Hashtable-based configuration is explicitly out of scope.
- Removed public helper functions are out of scope (see TODO for exclusions).

Legend:
- [OK] parity or improved
- [PARTIAL] implemented but behavior differs or incomplete
- [MISSING] not wired in C# pipeline

| Segment / Area | 2.0.26 PowerShell behavior | 3.x C# behavior | Parity / gaps |
|---|---|---|---|
| Build (BuildModule) | Merge/sign/version/install options; merge missing approved modules; certificate selection; dot-source options | `ConfigurationBuildSegment` + `ModulePipelineRunner` + `ModuleBuilder` (C#). Same core behavior plus new options | [OK] plus additions |
| Build (BuildLibraries) | .NET build options (NET*), binary module import, runtime handling | `ConfigurationBuildLibrariesSegment` + C# .NET pipeline | [OK] |
| Auto-install missing dependencies | Not present | `InstallMissingModules*` options in build; `ModulePipelineRunner.EnsureBuildDependenciesInstalled` | [OK] new |
| Artefact | Copies files/folders; required modules export; optional script conversion with pre/post merge | `ConfigurationArtefactSegment` + `ArtefactBuilder` | [OK] plus repository/credential support |
| Manifest | Emits manifest fields + prerelease | `ConfigurationManifestSegment` merged into `ModulePipelinePlan` | [OK] |
| Information | Include/exclude paths and library folders | `ConfigurationInformationSegment` | [OK] |
| ImportModules | Imports self/required modules with verbose control before build steps | Wired into pipeline (import + test integration) | [OK] |
| Module dependencies | Required/External/Approved modules; version/Guid | `ConfigurationModuleSegment` with `ModuleDependencyKind` | [OK] (adds `MinimumVersion`) |
| ModuleSkip | Ignore missing modules/functions; force continue | Used in `ModulePipelineRunner` for missing command handling and dependency install | [OK] |
| Command module dependencies | Writes `CommandModuleDependencies` into manifest | Manifest patch in C# pipeline | [OK] |
| PlaceHolder | Custom placeholders used during merge/build | Applied to merged PSM1 | [OK] |
| PlaceHolderOption | Skip built-in replacements | Applied during placeholder replacement | [OK] |
| Documentation (paths) | Emits `Documentation` segment (Path/Readme) | `ConfigurationDocumentationSegment` | [OK] |
| Documentation (build) | `BuildDocumentation` segment, PlatyPS/HelpOut checks | `ConfigurationBuildDocumentationSegment` + C# docs engine; no module availability check | [PARTIAL] (no pre-check) |
| Delivery metadata | Options.Delivery with internals bundle + repo paths | `ConfigurationOptionsSegment` + delivery metadata written in pipeline | [OK] plus Install/Update command generation |
| Publish | Gallery/GitHub publish settings | `ConfigurationPublishSegment` + repository registration options | [OK] plus repo/tool controls |
| Formatting | `Invoke-Formatter` options applied to merge/standard | `ConfigurationFormattingSegment` + formatter settings | [OK] |
| FileConsistency | Encoding/line-ending checks + reports | `ConfigurationFileConsistencySegment` | [OK] plus scoped checks and overrides |
| Compatibility | Compatibility checks + report export | `ConfigurationCompatibilitySegment` | [OK] plus severity option |
| Validation | Not present | `ConfigurationValidationSegment` (new) | [OK] new |
| TestsAfterMerge | Pester tests after merge | Wired into pipeline via test runner | [OK] |
| Execute | Placeholder (no-op) | Placeholder (no-op) | [OK] |

Known gaps to close (C# pipeline): none currently identified for the DSL segments listed above.
