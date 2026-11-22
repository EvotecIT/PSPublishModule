# PowerForge.Modules

Resolve, install and restore PowerShell modules without loading a PowerShell
runtime. Reference this package when your application only needs the module
engine. Applications that already reference PowerForge can continue to use its
existing module APIs.

Use ManagedModuleInstallService for an exact or latest version install. Use
ManagedModuleRestoreService to resolve a dependency lock or to restore from an
existing lock. A frozen restore verifies each package against its SHA-256 pin.

See the [PowerForge repository](https://github.com/EvotecIT/PSPublishModule) for
the native CLI and its managed companion.
