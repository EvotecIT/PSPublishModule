# PSPublishModule.Artefacts

`PSPublishModule.Artefacts` is a companion package-carrier module for locked-down
workstations. It lets administrators mirror one PowerShell module to an internal
gallery and still provide the binary artefacts that PSPublishModule needs for
private-gallery onboarding.

The first artefact set is the Microsoft Azure Artifacts Credential Provider. The
module stores the official Microsoft release ZIPs plus a hash manifest generated
by `Build/Build-Module.ps1`. Windows netcore delivery uses Microsoft's
self-contained `Microsoft.win-x64`, `Microsoft.win-x86`, and
`Microsoft.win-arm64` packages so workstations do not need a separately
installed .NET runtime just to launch the credential provider. PSPublishModule
selects the package that matches the current Windows process architecture.

The module intentionally does not install the credential provider itself.
PSPublishModule/PowerForge owns installation and uses this module as a trusted
package source. `Get-PSPublishModuleArtefact` is provided only for operator
inventory and hash inspection.

Typical managed flow:

```powershell
Install-Module PSPublishModule
Install-Module PSPublishModule.Artefacts

Initialize-ModuleRepository -ProfileName Company -Organization contoso -Project Platform -Feed Modules -InstallPrerequisites
```

PSPublishModule can also auto-install this module from the configured PowerShell
repository when the credential provider is missing and no explicit package path
was supplied.
