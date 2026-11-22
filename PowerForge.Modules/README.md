# PowerForge.Modules

Manage PowerShell modules without loading a PowerShell runtime. Reference this
package when your application only needs the module engine. Existing
`PowerForge` references still resolve the moved public types.

- `ManagedModuleRepositoryClient` searches feeds. `ManagedModuleRepositoryProfiles`
  manages the user and machine profiles shared with the native CLI and cmdlets.
- `ManagedModuleInstallService` installs an exact or latest version. Set
  `SaveAsNupkg` to save the original package archive.
- `ManagedModuleInventory` lists installed versions and paths.
  `ManagedModuleUpdateService` and `ManagedModuleUninstallService` plan and
  apply changes to an installed module root.
- `ManagedModulePackService` creates a local NuGet package.
- `ManagedModuleRestoreService` resolves a dependency lock or restores from
  one. A frozen restore verifies each package against its SHA-256 pin.

See the [PowerForge repository](https://github.com/EvotecIT/PSPublishModule) for
the native CLI and its managed companion.
