# PSPublishModule Private Gallery Proposal

Last updated: 2026-03-10

## Purpose

This document proposes the user experience, implementation direction, and naming convention for private gallery support in `PSPublishModule`, with Azure DevOps / Azure Artifacts as the primary private-gallery target.

The immediate business goal is simple:

- A server administrator should be able to install one or more internal modules from a private gallery with a very small number of commands.
- The process should not require the user to manually open Azure DevOps settings, create a Personal Access Token (PAT), copy it to a file, register repositories by hand, or remember renewal dates.
- The resulting setup should work with standard PowerShell installation flows such as `Install-PSResource` and, where possible, `Install-Module`.

## Executive Summary

The recommended direction is:

1. Keep private-gallery publishing support in `New-ConfigurationPublish`.
2. Add a simple end-user bootstrap flow that signs the user in interactively, registers the repository locally, and validates access.
3. Prefer Microsoft Entra-based interactive authentication and Azure Artifacts Credential Provider integration over a manual PAT workflow.
4. Treat manual PAT creation as a fallback only, not the main product story.
5. Expose a very small, high-level command surface intended for administrators who are installing internal modules, not building them.
6. Make repository bootstrap and repair first-class operations, and make install/update wrappers the recommended way to consume internal modules.

The proposed end-user experience should look like this:

```powershell
Connect-ModuleRepository -Provider AzureArtifacts `
    -Name 'Company' `
    -Organization 'contoso' `
    -Project 'Platform' `
    -Feed 'Modules' `
    -InstallPrerequisites

Install-PrivateModule -Name 'ModuleA', 'ModuleB' -Repository 'Company'
```

After that initial setup, the expected day-to-day command should be:

```powershell
Update-PrivateModule -Name 'ModuleA', 'ModuleB' -Repository 'Company'
```

If we want to be even more direct, we can support a one-command happy path:

```powershell
Install-PrivateModule -Name 'ModuleA', 'ModuleB' `
    -Provider AzureArtifacts `
    -Organization 'contoso' `
    -Project 'Platform' `
    -Feed 'Modules' `
    -InstallPrerequisites
```

In that flow, the module should:

- install any missing prerequisites,
- trigger browser or device-code sign-in when the ExistingSession path is available, or prompt for credentials as the fallback path,
- register the private repository,
- validate access,
- and then install the requested modules.

## Problem Statement

The current private-feed story is too technical for the target operator profile.

The administrator we need to support is typically able to:

- run PowerShell,
- provide their corporate sign-in,
- follow a browser prompt or device-code prompt,
- answer a small number of questions,
- and rerun one or two commands later if needed.

The same administrator is not a realistic fit for a workflow that says:

- go to Azure DevOps,
- find the PAT screen,
- choose the correct scopes,
- decide on an expiration window,
- save the token somewhere safe,
- create credentials manually,
- register one or more repositories,
- remember when the PAT expires,
- and repeat the whole process later.

That manual PAT-driven approach is operationally fragile, hard to document, and easy to get wrong.

## Why Manual PAT Creation Is The Wrong Primary UX

Historically, Azure Artifacts private PowerShell feeds were often documented around PATs. That still works in many environments, but it is no longer the right primary direction for a polished end-user installation experience.

As of **March 9, 2026**, Microsoft is clearly pushing Azure DevOps customers toward more governed authentication models:

- Azure DevOps announced retirement of **global PATs**.
- On **March 15, 2026**, creation and regeneration of global PATs are blocked.
- On **December 1, 2026**, existing global PATs stop working.
- Azure DevOps also introduced narrower Microsoft Entra OAuth scopes specifically for PAT lifecycle operations.

Relevant Microsoft documentation:

- [Retirement of Global Personal Access Tokens (PATs) in Azure DevOps](https://learn.microsoft.com/en-us/azure/devops/release-notes/2026/sprint-270-update)
- [Azure DevOps Sprint 257 Update: New Microsoft Entra OAuth scopes for PAT lifecycle APIs](https://learn.microsoft.com/en-us/azure/devops/release-notes/2025/general/sprint-257-update)
- [PAT Lifecycle Management API](https://learn.microsoft.com/en-us/rest/api/azure/devops/tokens/pats?view=azure-devops-rest-7.1)

The practical takeaway from the [Azure DevOps retirement notice for global PATs](https://learn.microsoft.com/en-us/azure/devops/release-notes/2026/sprint-270-update) and the [PAT lifecycle API documentation](https://learn.microsoft.com/en-us/rest/api/azure/devops/tokens/pats?view=azure-devops-rest-7.1) is that PATs are now something to minimize and govern, not the primary UX we should ask every admin to manage manually.

This has two practical consequences for `PSPublishModule`:

1. We should avoid designing our approval proposal around "users create PATs manually forever".
2. We should not promise that a local PowerShell module can simply ask for username/password and mint PATs on behalf of the user in a reliable, future-proof way.

## Important Constraint: Username/Password Is Not A Good Design Target

The requested idea was roughly:

- prompt the user for their login and password,
- create a PAT automatically,
- register the repository,
- refresh the PAT later.

This sounds convenient, but it is not a good foundation for the product.

Reasons:

- Plain username/password authentication is not the modern path for Azure DevOps and Microsoft Entra.
- MFA and Conditional Access policies are common and often mandatory.
- PAT lifecycle APIs are now governed through Microsoft Entra delegated scopes, which means "simple credentials in, PAT out" is not the clean model to build against.
- Even if a workaround exists in one tenant today, it is likely to be brittle across tenants and security policies.

This is consistent with Microsoft's broader guidance to reduce PAT usage across Azure DevOps and move toward Microsoft Entra-backed auth flows:

- [Reduce PAT usage across Azure DevOps](https://devblogs.microsoft.com/devops/reducing-pat-usage-across-azure-devops/)
- [Use the Azure Artifacts Credential Provider with Azure Artifacts feeds](https://learn.microsoft.com/en-us/powershell/gallery/powershellget/how-to/use-credential-provider-with-azure-artifacts?view=powershellget-3.x)

For approval and architecture purposes, the correct statement is:

> We should build an interactive sign-in and repository bootstrap experience, not a username/password-to-PAT automation feature.

## Recommended Authentication Model

The recommended authentication model for private module installation is:

1. Use an interactive Microsoft Entra sign-in experience.
2. Let Azure Artifacts / NuGet credential-provider infrastructure do the hard authentication work.
3. Register the PowerShell repository locally after authentication succeeds.
4. Use standard PowerShell package commands after that.

This closely matches the mental model of:

- GitHub device login,
- Azure CLI browser login,
- or a one-time "sign in to continue" prompt.

Relevant Microsoft documentation:

- [Get started with Azure CLI (`az login --use-device-code`)](https://learn.microsoft.com/cli/azure/get-started-with-azure-cli)
- [Use an Azure Artifacts feed as a private PowerShell repository](https://learn.microsoft.com/en-us/azure/devops/artifacts/tutorials/private-powershell-library?view=azure-devops)
- [Use the Azure Artifacts Credential Provider with Azure Artifacts feeds](https://learn.microsoft.com/en-us/powershell/gallery/powershellget/how-to/use-credential-provider-with-azure-artifacts?view=powershellget-3.x)
- [Consuming packages from authenticated feeds](https://learn.microsoft.com/en-us/nuget/consume-packages/consuming-packages-authenticated-feeds)
- [Supported repository configurations for PSResourceGet](https://learn.microsoft.com/powershell/gallery/powershellget/supported-repositories?view=powershellget-2.x)

Two Microsoft details are especially important here:

- PSResourceGet now recognizes Azure Artifacts feed URLs and can automatically assign the `AzArtifacts` credential provider for `pkgs.dev.azure.com` repositories.
- The Azure Artifacts Credential Provider is the Microsoft-supported way to smooth over authentication for authenticated NuGet-backed feeds.

That is why this proposal recommends an interactive bootstrap and credential-provider model rather than asking users to create, copy, and rotate PATs themselves.

## Recommended User Experience

### One-time repository bootstrap

The user should run one command that:

- checks prerequisites,
- installs the Azure Artifacts Credential Provider if missing,
- optionally verifies `PSResourceGet`,
- triggers interactive authentication,
- registers the repository,
- validates that the repository works.

Proposed experience:

```powershell
Connect-ModuleRepository -Provider AzureArtifacts `
    -Name 'Company' `
    -Organization 'contoso' `
    -Project 'Platform' `
    -Feed 'Modules' `
    -InstallPrerequisites
```

What the user should see conceptually:

- "Checking prerequisites"
- "Installing credential provider"
- "Opening browser sign-in" or "Use this device code"
- "Registering repository Company"
- "Validating repository access"
- "Done. You can now use Install-PSResource or Install-PrivateModule"

`Register-ModuleRepository` should still exist, but as the explicit setup/repair cmdlet. `Connect-ModuleRepository` is the better primary admin-facing verb because it conveys the actual outcome we care about: not just "a repository entry exists", but "the feed is reachable with the chosen auth path".

### Install modules after bootstrap

Once the repository is registered, the user should be able to install modules with either:

```powershell
Install-PSResource ModuleA,ModuleB -Repository Company
```

or the simpler wrapper:

```powershell
Install-PrivateModule -Name ModuleA,ModuleB -Repository Company
```

### Refresh or reconnect flow

If authentication later expires or the environment changes, the user should not have to relearn the full process.

They should have one obvious command:

```powershell
Connect-ModuleRepository -Organization 'contoso' -Project 'Platform' -Feed 'Modules' -Name 'Company' -InstallPrerequisites
```

or:

```powershell
Update-ModuleRepository -Organization 'contoso' -Project 'Platform' -Feed 'Modules' -Name 'Company' -InstallPrerequisites
```

The important part is not the exact command name. The important part is that the user can recover with one familiar, documented action.

## User Workflows

The feature should be documented around a few concrete user workflows rather than one generic story.

### Workflow 1: Preferred first-time onboarding

This is the recommended path for most administrators:

```powershell
Connect-ModuleRepository -Organization 'contoso' -Project 'Platform' -Feed 'Modules' -Name 'Company' -InstallPrerequisites
Install-PrivateModule -Name 'ModuleA', 'ModuleB' -Repository 'Company'
```

Expected behavior:

- install missing prerequisites if needed,
- register the repository,
- validate that authentication actually works,
- and then let the user install modules with either wrapper or native commands.

### Workflow 2: Explicit setup followed by later install

This is useful for runbooks and automation where the repository bootstrap step is kept separate from installation:

```powershell
Register-ModuleRepository -Organization 'contoso' -Project 'Platform' -Feed 'Modules' -Name 'Company' -InstallPrerequisites
Install-PrivateModule -Name 'ModuleA', 'ModuleB' -Repository 'Company'
```

### Workflow 3: One-command install bootstrap

This is the shortest support-desk story:

```powershell
Install-PrivateModule -Name 'ModuleA', 'ModuleB' -Organization 'contoso' -Project 'Platform' -Feed 'Modules' -InstallPrerequisites
```

This should be supported, but it is still useful to document `Connect-ModuleRepository` separately because that gives operators an easy diagnostic and recovery command.

### Workflow 4: Day-2 module maintenance

Once the repository is configured:

```powershell
Update-PrivateModule -Name 'ModuleA', 'ModuleB' -Repository 'Company'
```

### Workflow 5: Repair or reconnect after auth problems

When the feed stops working because prerequisites changed, sign-in expired, or the repository needs refresh:

```powershell
Connect-ModuleRepository -Organization 'contoso' -Project 'Platform' -Feed 'Modules' -Name 'Company' -InstallPrerequisites
```

or, for the more explicit lower-level path:

```powershell
Update-ModuleRepository -Organization 'contoso' -Project 'Platform' -Feed 'Modules' -Name 'Company' -InstallPrerequisites
```

### Workflow 6: Native commands after bootstrap

After successful bootstrap, native commands should work when the chosen path is actually ready:

```powershell
Install-PSResource -Name ModuleA -Repository Company
```

or, when the real working path is PowerShellGet compatibility:

```powershell
Install-Module -Name ModuleA -Repository Company
```

`PSPublishModule` should continue to guide the user toward the wrapper commands first, because they can repair or compensate for broken bootstrap/auth states more gracefully than the raw native commands.

## Recommended Command Surface

There are two audiences in this feature:

- **module authors / release engineers**, who define where packages are published,
- **module consumers / administrators**, who need a dead-simple install experience.

Those two audiences should not share the same cmdlet surface.

### Authoring / publishing surface

Keep the current configuration-oriented naming:

- `New-ConfigurationPublish`

Extend it with Azure Artifacts-friendly parameters so authors can define private publishing targets without hand-crafting URLs.

This fits the current DSL and is consistent with how the module already models build and publish configuration.

### End-user / consumer surface

Recommended public naming:

- `Connect-ModuleRepository`
- `Register-ModuleRepository`
- `Install-PrivateModule`
- `Update-PrivateModule`
- `Update-ModuleRepository`

Why this naming is recommended:

- `Connect-ModuleRepository` is the clearest human-facing onboarding verb when the cmdlet both registers and validates access.
- `Register-ModuleRepository` maps cleanly to the underlying PowerShell concept of a repository.
- It is still understandable by administrators who do not know PowerShell internals.
- It avoids overloading the word "Gallery" too broadly.
- It leaves room for future providers beyond Azure Artifacts.
- It separates module lifecycle actions from repository lifecycle actions.

### About `Register-Gallery`

`Register-Gallery` is short and approachable, and it was the original idea in discussion. It is acceptable as a convenience alias, but it is probably too broad as the primary public name.

Reasons:

- "Gallery" may be interpreted as specifically PowerShell Gallery.
- "Repository" is the actual PowerShell packaging concept.
- We may later support multiple private feed types, and "repository" scales better than "gallery".

Recommendation:

- Use `Connect-ModuleRepository` as the primary end-user onboarding command.
- Use `Register-ModuleRepository` as the explicit repository-management and setup cmdlet.
- Optionally provide `Register-Gallery` as an alias if we want a softer end-user command.

### Why Both Module And Repository Commands Are Needed

We should assume the simplified experience belongs in `PSPublishModule`, not in raw `Install-PSResource` or `Install-Module`.

Native commands are still valuable after setup, but they should not carry the main UX promise because they do not own:

- prerequisite installation,
- credential-provider bootstrap,
- repository registration,
- repository repair,
- tenant-aware guidance,
- or friendly recovery flows.

That means we should support both:

- module lifecycle wrappers:
  - `Install-PrivateModule`
  - `Update-PrivateModule`
- repository lifecycle wrappers:
  - `Connect-ModuleRepository`
  - `Register-ModuleRepository`
  - `Update-ModuleRepository`

This gives administrators a simple mental model:

- first-time setup: register
- first-time validation/login: connect
- first-time consumption: install
- later consumption: update
- if auth breaks: update the repository

## Persistence And Prompting Behavior

One of the most important approval questions is whether the user has to keep re-entering credentials all day long.

The answer is: **the repository registration and prerequisite installation survive reboots; the authentication prompt frequency depends on which bootstrap mode is actually being used**.

### What persists

The following state is expected to persist across PowerShell sessions and machine reboots:

- repository registration created through `Register-PSRepository` / `Register-PSResourceRepository`
- Azure Artifacts Credential Provider installation under the user profile NuGet plugin location
- installed PowerShell modules in the user module path

This means the user should not need to re-register the repository after every reboot.

### ExistingSession path

When `ExistingSession` is available, this is the preferred user experience:

- the repository remains registered,
- the credential provider remains installed,
- and authentication is typically reused through the underlying Azure / credential-provider token cache until that session expires.

In practice, that means the user should **not** be prompted on every install/update command.

However, prompts can legitimately reappear after:

- a reboot or sign-out that invalidates cached session state,
- token expiration,
- MFA revalidation,
- Conditional Access changes,
- or tenant policy requiring a fresh sign-in.

So the correct product promise is:

> The setup survives reboot, but interactive sign-in may still be required again when the underlying Microsoft authentication session expires or policy demands it.

### CredentialPrompt fallback path

When the environment cannot use the ExistingSession path and we fall back to `CredentialPrompt`:

- the repository still remains registered,
- but the credential itself is not treated as a durable long-term single sign-on session by `PSPublishModule`.

In that fallback mode, the user may be prompted again on later install or update operations unless they:

- rerun `Connect-ModuleRepository`,
- provide explicit credential input again,
- or use a credential file / supported automation path.

That is another reason the ExistingSession + credential-provider flow should be the primary supported experience.

## Dependencies And Prerequisites

The private-gallery feature should clearly distinguish between:

- **required dependencies for the smooth supported path**
- **optional dependencies for later enhancements**

### Required for the supported Azure Artifacts path

The recommended baseline is:

- `Microsoft.PowerShell.PSResourceGet`
- Azure Artifacts Credential Provider

Reasoning:

- `PSResourceGet` is the preferred installation engine for authenticated modern repositories.
- Azure Artifacts Credential Provider is the Microsoft-supported way to handle authenticated Azure Artifacts NuGet feeds without forcing users through manual PAT handling.
- This combination aligns with the Microsoft documentation for authenticated PowerShell package consumption from Azure Artifacts.

### Optional, not required

The following should be treated as optional enhancements, not mandatory prerequisites:

- `Microsoft.PowerShell.SecretManagement`
- `Microsoft.PowerShell.SecretStore`

Reasoning:

- They are useful if we later decide to store or resolve credentials through a local vault abstraction.
- They are not required for the core interactive sign-in / credential-provider model.
- Making them mandatory would introduce avoidable friction, especially around locked vaults, password prompts, and non-interactive failures.

This matters because `PSResourceGet` can interact with repository `CredentialInfo` and SecretStore-backed flows, and that can fail when SecretStore is locked. `PSPublishModule` already contains defensive logic around this behavior, which is a good signal that SecretStore should not be part of the minimum supported path.

### Recommended bootstrap behavior

`Connect-ModuleRepository` should:

1. Detect whether `PSResourceGet` is available.
2. Install or update `PSResourceGet` if it is missing or too old.
3. Detect whether Azure Artifacts Credential Provider is available.
4. Install it if missing.
5. Trigger interactive sign-in.
6. Register the repository.
7. Validate access with a repository query.

`Register-ModuleRepository` should provide the same endpoint/prerequisite/registration logic, but it should be positioned as the explicit setup/repair command rather than the main human-facing happy path.

`Install-PrivateModule` and `Update-PrivateModule` should:

- prefer the registered repository path,
- trigger repository bootstrap if required,
- and guide the user back to `Update-ModuleRepository` when repository auth has expired or become invalid.

## Recommended Implementation Phases

### Phase 1: Practical shipping milestone

Deliver a simple, supportable version first.

Scope:

- Azure Artifacts preset support in `New-ConfigurationPublish`
- repository endpoint generation for Azure Artifacts
- `Register-ModuleRepository` for Azure Artifacts
- `Connect-ModuleRepository` for Azure Artifacts
- prerequisite checks for `PSResourceGet` and credential provider
- interactive sign-in bootstrap
- repository validation
- `Install-PrivateModule` wrapper for the two-or-three-command user story
- `Update-PrivateModule` wrapper for the ongoing maintenance story
- `Update-ModuleRepository` for reconnect/repair

Success criteria:

- a user can install internal modules without ever manually creating a PAT,
- the team can document the process in a short runbook,
- and the workflow survives MFA and standard enterprise auth policies better than a username/password script.

### Phase 2: Quality-of-life improvements

After the basic flow is working:

- add better diagnostics and self-healing,
- add provider detection and clearer prerequisite installation,
- add support for additional private gallery providers,
- optionally add secure local credential integration using SecretManagement or another approved secret store.

### Phase 3: Optional advanced automation

If management still wants automated PAT lifecycle support later, treat it as a separate, higher-complexity project.

That future design would likely need:

- a Microsoft Entra app registration,
- delegated OAuth,
- tenant-specific approval,
- policy-aware PAT creation rules,
- and careful handling of organizations that block PAT creation entirely.

This should not block Phase 1.

## Publishing Design Notes

For publishing to Azure Artifacts, the PowerShell ecosystem currently still has some NuGet-specific behavior that matters:

- PowerShellGet-style registration and publish flows often use the **v2** feed URL.
- PSResourceGet repository registration uses the **v3** index URL.
- Publishing to Azure Artifacts with `Publish-PSResource` still requires both:
  - `-Credential`
  - and an `-ApiKey` value, even though the API key value itself can be arbitrary for Azure Artifacts.

That means `PSPublishModule` should abstract this for the author instead of forcing them to memorize feed URL formats and tool-specific quirks.

## Install Design Notes

For installation, the preferred long-term path is:

- `PSResourceGet`
- Azure Artifacts feed registered as a `PSResourceRepository`
- Azure Artifacts Credential Provider handling authentication

This is a better long-term foundation than trying to make `Install-Module` the primary first-class path.

`Install-Module` compatibility can still exist, but it should be treated as:

- a compatibility layer,
- a wrapper scenario,
- or a secondary code path.

## Risks And Tradeoffs

### Risk: Environment prerequisites are missing

The target server may not have:

- `PSResourceGet`,
- the Azure Artifacts Credential Provider,
- or a browser available.

Mitigation:

- bootstrap prerequisites automatically where possible,
- support device-code flow when a browser is unavailable,
- emit clear remediation messages when policy blocks sign-in.

### Risk: Conditional Access blocks some interactive flows

Some tenants may allow browser login but block certain CLI/device-code flows.

Mitigation:

- support browser-first sign-in where possible,
- support device-code as fallback,
- clearly document that tenant policy may affect which interactive flow is allowed.

### Risk: Legacy `Install-Module` expectations

Some teams will expect everything to work through older PowerShellGet-only flows.

Mitigation:

- provide a wrapper command,
- keep PowerShellGet compatibility where feasible,
- but document PSResourceGet as the preferred route.

### Risk: Overpromising PAT automation

If the approval proposal claims "we will generate and rotate PATs automatically from username/password", we create technical and governance risk immediately.

Mitigation:

- explicitly position PAT automation as non-primary and optional,
- ship the interactive sign-in/bootstrap experience first,
- only revisit PAT lifecycle automation if a supported tenant-approved delegated OAuth design is required later.

## Recommended Approval Statement

The approval request can be summarized like this:

> We should implement private gallery support in `PSPublishModule` using an interactive, Microsoft-supported authentication model. The administrator experience should be one-time sign-in plus normal install commands, not manual PAT creation and repository registration. Azure Artifacts is the first provider. The publishing side remains configuration-driven; the consumer side gets a small, task-focused command surface.

## Final Recommendation

Approve the feature in this form:

- **Yes** to private gallery publishing support.
- **Yes** to end-user repository bootstrap and simple install commands.
- **Yes** to Azure Artifacts as the first supported private gallery target.
- **Yes** to interactive sign-in / credential-provider-based UX.
- **No** to making manual PAT creation the primary onboarding flow.
- **No** to promising username/password-driven PAT minting as the initial implementation.

## Proposed Names

Recommended primary names:

- `New-ConfigurationPublish`
- `Connect-ModuleRepository`
- `Register-ModuleRepository`
- `Install-PrivateModule`
- `Update-PrivateModule`
- `Update-ModuleRepository`

Optional convenience alias:

- `Register-Gallery`

If we want the shortest recommendation possible:

- keep `New-ConfigurationPublish` for authors,
- ship `Connect-ModuleRepository` for onboarding,
- keep `Register-ModuleRepository` for explicit setup,
- ship `Install-PrivateModule` and `Update-PrivateModule` for day-to-day use,
- ship `Update-ModuleRepository` for repair and reconnect.

## References

- [Retirement of Global Personal Access Tokens (PATs) in Azure DevOps](https://learn.microsoft.com/en-us/azure/devops/release-notes/2026/sprint-270-update)
- [Azure DevOps Sprint 257 Update: New Microsoft Entra OAuth scopes](https://learn.microsoft.com/en-us/azure/devops/release-notes/2025/general/sprint-257-update)
- [PAT Lifecycle Management API](https://learn.microsoft.com/en-us/rest/api/azure/devops/tokens/pats?view=azure-devops-rest-7.1)
- [Use an Azure Artifacts feed as a private PowerShell repository](https://learn.microsoft.com/en-us/azure/devops/artifacts/tutorials/private-powershell-library?view=azure-devops)
- [Use the Azure Artifacts Credential Provider with Azure Artifacts feeds](https://learn.microsoft.com/en-us/powershell/gallery/powershellget/how-to/use-credential-provider-with-azure-artifacts?view=powershellget-3.x)
- [Supported repository configurations](https://learn.microsoft.com/powershell/gallery/powershellget/supported-repositories?view=powershellget-2.x)
- [Consuming packages from authenticated feeds](https://learn.microsoft.com/en-us/nuget/consume-packages/consuming-packages-authenticated-feeds)
- [Get started with Azure CLI](https://learn.microsoft.com/cli/azure/get-started-with-azure-cli)
