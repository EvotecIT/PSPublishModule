# PSMaintenance

Rich documentation viewer/installer for PowerShell modules.

- Render README, CHANGELOG, License, Intro/Upgrade text, Internals\Docs as tabs
- Parse Get-Help to pretty HTML with syntax, parameter sets, examples
- Show dependencies (table + diagram) and commands
- Fetch docs directly from repository (GitHub/Azure DevOps), including Docs folders

## Install

```powershell
Install-Module PSMaintenance -Scope CurrentUser
```

## Quick start

```powershell
# View local docs
Show-ModuleDocumentation -Name 'EFAdminManager' -Readme -Changelog

# Prefer docs from repository when available
Show-ModuleDocumentation -Name 'EFAdminManager' -PreferRepository

# Copy bundled docs (Internals + README/CHANGELOG/LICENSE) to a folder
Install-ModuleDocumentation -Name 'EFAdminManager' -Path 'C:\Docs' -Layout ModuleAndVersion
```

## Configure repository access (private repos)

PSMaintenance can fetch documentation directly from your repository defined in the module manifest (`PrivateData.PSData.ProjectUri`). For private repositories, store a token once:

- GitHub
  - Token scope: `repo` (read access)
  - Windows: `setx PG_GITHUB_TOKEN "ghp_xxx"`
  - Or persist securely: `Set-ModuleDocumentation -GitHubToken 'ghp_xxx'`

- Azure DevOps
  - URL: `https://dev.azure.com/{organization}/{project}/_git/{repository}`
  - PAT scope: `Code (Read)`
  - Windows: `setx PG_AZDO_PAT "your_pat"`
  - Or persist securely: `Set-ModuleDocumentation -AzureDevOpsPat 'your_pat'`

You can also pass a token per-call: `-RepositoryToken '...'`. Stored tokens are used when env vars and explicit tokens are not provided.

## Tell PSMaintenance where your Docs live in the repo

In the module’s build script (PSPublishModule DSL):

```powershell
New-ConfigurationDelivery -Enable -RepositoryPaths 'docs' -RepositoryBranch main   # GitHub
# or
New-ConfigurationDelivery -Enable -RepositoryPaths 'Docs/en-US' -RepositoryBranch main  # Azure DevOps
```

Then:

```powershell
Show-ModuleDocumentation -Name 'EFAdminManager' -PreferRepository
Show-ModuleDocumentation -Name 'EFAdminManager' -PreferRepository -RepositoryPaths 'Docs/en-US'
```

## Commands

- `Show-ModuleDocumentation` [Alias: `Show-Documentation`]
  - Displays README, CHANGELOG, LICENSE, Intro/Upgrade, Docs; local or repository.
- `Install-ModuleDocumentation` [Alias: `Install-Documentation`]
  - Copies Internals and selected root docs to a destination path.
- `Set-ModuleDocumentation` [Alias: `Set-Documentation`]
  - Stores or clears tokens used to access private GitHub/Azure DevOps repos.

## Security notes

- On Windows, persisted tokens are protected with DPAPI (CurrentUser scope).
- On non-Windows, tokens are stored as Base64 (best effort). Prefer environment variables if you require stronger guarantees.
