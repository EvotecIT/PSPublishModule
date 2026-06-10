# PSPublishModule.Artefacts

`PSPublishModule.Artefacts` is a companion package-carrier module for locked-down
workstations. It lets administrators mirror one PowerShell module to an internal
gallery and still provide the binary artefacts that PSPublishModule needs for
private-gallery onboarding.

The first artefact set is the Microsoft Azure Artifacts Credential Provider. The
module stores the official Microsoft release ZIPs plus a hash manifest generated
by `Build/Build-Module.ps1`.

Typical managed flow:

```powershell
Install-Module PSPublishModule
Install-Module PSPublishModule.Artefacts

Initialize-ModuleRepository -ProfileName Company -Organization contoso -Project Platform -Feed Modules -InstallPrerequisites
```

PSPublishModule can also auto-install this module from the configured PowerShell
repository when the credential provider is missing and no explicit package path
was supplied.
