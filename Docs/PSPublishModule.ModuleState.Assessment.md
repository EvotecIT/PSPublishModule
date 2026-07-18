# PSPublishModule Module State and Repair Contract

Last reviewed: 2026-07-17

## Current Status

The PowerForge-owned module-state engine and `Repair-ManagedModule` are complete
for local managed-module estate repair. This is a managed extension, not a
PSResourceGet parity gap: PSResourceGet provides individual lifecycle commands
but has no equivalent inventory, drift, family-coherence, cleanup, and
post-apply convergence workflow.

Stable PSResourceGet module-lifecycle parity is tracked separately in
[PSResourceGet Parity](PSPublishModule.PSResourceGetParity.md). The public
compatibility contract is in
[Managed Module Compatibility](PSPublishModule.ManagedModules.Compatibility.md).

## Ownership

The implementation keeps the public cmdlet thin:

- `PowerForge` owns inventory, physical-estate identity, desired-state
  adaptation, planning, drift analysis, cleanup planning, receipts, and
  convergence models.
- `PowerForge.PowerShell` owns reusable behavior that needs PowerShell runtime
  concepts.
- `PSPublishModule` owns parameter binding, repository-profile resolution,
  `ShouldProcess`, PowerShell streams, and result mapping.
- Install and update execution reuse the same managed repository, dependency,
  integrity, license, cache, and transactional-promotion services as the
  lifecycle cmdlets.
- Cleanup reuses the managed uninstall engine instead of deleting directories
  directly.

There are no public `*-ModuleState` cmdlets. ModuleState is the reusable engine
and typed result model behind `Get-ManagedModule -AsInventory` and
`Repair-ManagedModule`.

## Physical Estate Identity

Repair decisions are made per physical module estate. An installed copy is
identified by:

- module name;
- physical module root;
- PowerShell edition when known;
- installation scope when known;
- local profile identity when known.

Copies in Windows PowerShell and PowerShell 7, CurrentUser and AllUsers, separate
user profiles, or custom module roots are not collapsed into one logical
installation. Family coherence and old-version cleanup are evaluated within
each physical estate. Filesystem path comparison is case-insensitive on Windows
and case-sensitive on POSIX systems.

The inventory also records the exact installed location for every version.
Cleanup actions therefore carry both the physical module root and exact
version directory.

## Inventory Sources

`Repair-ManagedModule` accepts four inventory forms:

- an existing typed inventory through `-Inventory`;
- a persisted inventory through `-InventoryPath`;
- explicit module roots through `-ModulePath`;
- the current process `PSModulePath` when no explicit inventory is supplied.

Additional local profiles can be included with:

- `-UserProfilePath` for explicit profile home directories;
- `-IncludeAllUserProfiles` for standard profile directories below the current
  local profile container.

Profile discovery recognizes the standard Windows PowerShell, PowerShell 7 on
Windows, and PowerShell 7 on POSIX user module roots. Redirected profiles,
service-account layouts, mounted volumes, and nonstandard server roots should
be supplied explicitly through `-UserProfilePath` or `-ModulePath`.

Explicit `-ModulePath` entries are required inventory roots. A missing or
inaccessible required root produces a structured error diagnostic and blocks
apply. Missing default `PSModulePath` entries are ignored because normal
PowerShell installations frequently contain optional paths. Failures to
enumerate an existing root are always reported. `-IncludeLoaded` adds current
runspace evidence for loaded-module safety.

## Desired State and Repair Inputs

Repair can derive desired state from:

- selected installed modules;
- literal `-Name` values with `-InstallMissing`;
- PSResourceGet-style `-RequiredResource` maps;
- `-RequiredResourceFile` data, JSON, or PowerShell data files;
- maintenance receipts;
- exact, minimum, range, or latest version policy;
- built-in family policies;
- explicit source, repository profile, scope, and module-root intent.

Required-resource options remain per module, including repository, scope,
prerelease, reinstall, clobber, license, and dependency policy.

## Destination Rules

Existing modules keep their physical root, edition, scope, and profile placement
unless the operator explicitly narrows the operation.

`-ModuleRoot` is an explicit target and estate selector. It is added to the
inventory set, and existing baseline selection is limited to that physical
root. A missing target directory may be created by managed delivery.

A missing module can be applied only when its destination is unambiguous:

1. an explicit `-ModuleRoot` wins;
2. otherwise, exactly one eligible scanned root is inferred;
3. otherwise, the plan contains
   `ModuleState.AmbiguousRepairTarget` and apply is blocked.

Use `-ModuleRoot`, a narrower `-ModulePath`, `-Scope`, or explicit profile
selection to resolve ambiguity.

## Plan and Apply Sequence

Every invocation produces an inspectable workflow object containing inventory,
plan, compliance test, and apply preparation. `-Plan` stops before mutation.

A live apply uses this sequence:

1. inventory every selected root and preserve diagnostics;
2. build placement-aware desired state;
3. analyze version, source, scope, receipt, manifest dependency, command
   conflict, family, and cleanup findings;
4. resolve repository and license metadata;
5. prepare managed delivery and honor `ShouldProcess`;
6. execute install or update operations;
7. execute exact-path old-version cleanup only after delivery succeeds;
8. inventory the same roots again;
9. rebuild and test the plan against convergence-safe desired state;
10. return execution and post-apply evidence.

One-shot reinstall, force, and package-hash intent is removed from the
post-apply convergence policy. This prevents a successful forced repair from
appearing permanently noncompliant merely because the original request was
intentionally imperative.

## Cleanup Safety

`-Cleanup OldVersions` is executable through `Repair-ManagedModule`; it is no
longer plan-only. Cleanup:

- keeps the highest policy-satisfying or receipt-managed version per physical
  estate;
- never treats a copy in another edition, scope, profile, or root as the winner
  for the current estate;
- excludes loaded versions during planning;
- revalidates the exact installed path and module root immediately before
  removal;
- runs the managed uninstall dependency and loaded-module checks;
- honors `WhatIf`, `Confirm`, and `ShouldProcess`;
- stops cleanup after the first operational failure;
- does not continue deleting after delivery or estate revalidation fails.

`-SkipDependencyCheck` remains an explicit operator override. It is never
enabled implicitly by repair.

## Results and Failure Semantics

The workflow result exposes:

- scanned root metadata and inventory diagnostics;
- placement-aware actions and findings;
- exact cleanup target paths;
- per-operation success, error, target, transport, and dependency results;
- post-apply inventory, plan, and compliance test;
- `ExecutionSucceeded` and `Converged`.

Operational failures are returned in the typed workflow and also written as
nonterminating PowerShell errors. A partial or skipped operation cannot be
reported as converged. Successful execution is not sufficient by itself:
`Converged` is true only when the post-apply plan is compliant.

## Examples

Preview every copy found in explicit Windows PowerShell and PowerShell 7 roots:

```powershell
Repair-ManagedModule -ModulePath $ps5Root,$ps7Root -Latest -Repository PSGallery -Plan -ShowSummary
```

Repair a repeatable baseline into one explicit server root:

```powershell
Repair-ManagedModule -RequiredResourceFile .\required-resources.psd1 `
    -ModuleRoot 'D:\PowerShell\Modules' -Repository CompanyModules -ShowSummary
```

Inspect standard module roots for selected local profiles:

```powershell
Repair-ManagedModule -UserProfilePath 'C:\Users\Alice','C:\Users\Service.PowerShell' `
    -Name Company.* -Latest -Repository CompanyModules -Plan -ShowSummary
```

Safely update and remove old versions across discovered local profiles:

```powershell
Repair-ManagedModule -IncludeAllUserProfiles -Latest -Cleanup OldVersions `
    -Repository CompanyModules -Confirm:$false -ShowSummary
```

Seed a missing module only when the target root is explicit:

```powershell
Repair-ManagedModule -Name Company.Tools -InstallMissing -ModuleRoot $moduleRoot `
    -Repository CompanyModules
```

## Deliberate Limits

- Repair operates on the local filesystem available to the current process. A
  remote fleet transport is not part of this cmdlet.
- `-IncludeAllUserProfiles` discovers standard local profile layouts; it does
  not query every possible redirected-profile registry, directory service, or
  network home source.
- Cleanup currently supports exact old-version removal. Disabled-module cleanup,
  quarantine, and arbitrary broken-directory deletion are not implied.
- Provider-specific repository bootstrap gaps remain documented in the managed
  compatibility contract; repair does not hide them with compatibility
  subprocesses.

These limits are explicit product boundaries, not unfinished PSResourceGet
module-lifecycle parity.
