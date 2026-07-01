# PSPublishModule Module State Assessment

Date: 2026-06-22
Base: PSPublishModule `v3.0.32`

## Goal

Build a PowerForge-owned module state capability that helps an enterprise
inventory, resolve, install, update, isolate, and clean up PowerShell modules
across machines, PowerShell editions, scopes, repositories, and profiles.

The first implementation should be C# first:

- reusable engine and contracts in `PowerForge`
- PowerShell-hosted adapters in `PowerForge.PowerShell`
- thin PSPublishModule cmdlets that map parameters to engine requests
- later CLI, Studio, website, or WPF surfaces over the same engine

This should not become a GUI-first rewrite or a script-first dependency runner.
Catalog and dependency-runner ideas are useful, but the enterprise value is the
resolver, plan, policy, evidence, and conflict model.

## Product Patterns To Keep

The feature should absorb broad operator needs from module-maintenance work
without documenting, promoting, or tying the design to specific external
projects.

Useful patterns:

- operator-friendly inventory across Windows PowerShell 5.1 and PowerShell 7
- visible status: installed, latest gallery version, update available, missing
- scope awareness: CurrentUser, AllUsers, system folders, PS5, PS7
- repository registration and browsing as first-class workflows
- curated catalog grouping for Graph, Az, Exchange, Teams, Active Directory,
  SQL, security, VMware, utilities, and internal module families
- cleanup actions for old versions, disabled modules, and broken paths
- declarative desired-state files with dependency ordering
- plan/apply workflow with a readable plan before machine mutation
- lockfile or receipt evidence for reproducible CI and workstation rebuilds
- object-first PowerShell workflows, with file artifacts only when persistence,
  approval, CI logs, or support bundles need them
- local-installed-module reuse when it satisfies the requested constraints
- version-range semantics and prerelease handling
- credential indirection by logical repository or feed name
- extension points for enterprise-specific repositories and module families

Boundaries for our design:

- the GUI, if added later, is a surface over the engine, not the engine itself
- the core implementation is C# first and reusable by CLI, Studio, services,
  tests, and cmdlets
- one fast public-gallery path is not enough for enterprise machines, private
  feeds, cross-profile use, and long-lived servers
- a broad workstation bootstrapper is not the same as a module-state engine
- every installer script should not become a first-class core engine path
- testing that a handler ran is not the same as proving module import, assembly,
  profile, repository, or family-version safety

## Current PSPublishModule Starting Point

PSPublishModule already has several strong primitives:

- `RequiredModule`, `ExternalModule`, `ApprovedModule`, and `EmbeddedModule`
  contracts in the module build story
- `Install-PrivateModule` and `Update-PrivateModule` for private repository
  installation/update flows
- repository profiles for Azure Artifacts, JFrog, GitHub Packages, NuGet feeds,
  PSGallery, and Microsoft Artifact Registry
- private gallery bootstrapping, credential-provider support, and access probes
- `PublishRequiredModules` to mirror missing required modules into private feeds
- `Install-ModuleDependency` and `Import-ModuleDependency` for explicit private
  runtime folders and embedded dependency receipts
- curated `Import-IsolatedModule` profiles for `ExchangeOnlineManagement`,
  `MicrosoftTeams`, and `Microsoft.Graph.Authentication`
- out-of-process PSResourceGet and PowerShellGet wrappers
- required-module resolution and embedded transitive dependency receipts

The missing piece is not "install a module". The missing piece is an estate-aware
module state engine that can:

- inventory what exists across editions, scopes, paths, repositories, and
  machines
- understand whether the modules installed on a PC form a coherent runtime set
  for the module families our delivered tools depend on
- compare it to a declared desired state
- resolve conflicts before mutation
- choose install/update/import/isolation actions by policy
- produce a plan, apply it, and write evidence
- expose the same behavior through cmdlets, CLI, Studio, and later UI

This is also not primarily an embedded-module story. Embedded modules remain a
valid packaging/runtime option for very specific products, but the broader
enterprise maintenance problem is different: a PC has normal installed modules,
our own delivered modules depend on some of them, and PowerForge should keep
that installed estate controlled enough that those delivered modules continue to
work.

## Proposed Mental Model

Use five durable concepts.

### Module State

The observed state of one host or a collected fleet:

- machine identity and OS
- PowerShell editions and executable paths
- `PSModulePath` entries per edition
- installed module versions per scope/path
- installed module source and management tool when known
- side-by-side versions of the same module and their effective precedence
- module family membership, such as Microsoft Graph, Az, Exchange Online, Teams,
  VMware PowerCLI, and RSAT/ActiveDirectory
- loaded modules in the current process when available
- loaded assemblies and their versions when available
- registered repositories per tool
- repository profile readiness
- installed package manager/tool versions
- PowerForge-maintained receipts that record modules installed or reconciled by
  our tooling

### Module Intent

The desired state:

- module families and individual modules
- version policy: exact, minimum, range, latest-approved, latest-safe
- family coherence policy: same-version, compatible-minor, tested-bundle,
  independent, or custom
- allowed source repositories
- scope or destination root
- install mode: normal module store, explicit maintained root, or private runtime
- import mode: default, explicit-path, isolated-profile, or fresh-process
- cleanup policy: keep all, keep latest, keep approved versions, quarantine
- conflict policy: fail, warn, isolate, side-by-side, replace, pin
- machine/profile applicability
- ownership policy: PowerForge-maintained, externally maintained, or observe-only

### Module Plan

An immutable preview:

- installs, updates, saves, imports, removes, disables, profile changes
- dependency closure and selected versions
- family bundle decisions, including whether all relevant Graph/Az/Teams/EXO
  modules must move together
- repository and credential assumptions
- conflicts and their chosen resolution
- risk/warning list
- expected file paths and scopes
- reason for every action

### Module Apply

The execution of a plan:

- runs only from a previously resolved plan or in one-shot mode that internally
  creates a plan first
- can consume a typed plan object from the pipeline, or an approved plan
  artifact with `Invoke-ModuleStatePlan -PlanPath`
- honors `ShouldProcess`
- writes structured results per action
- does not hide partial failures
- writes an optional receipt for repeatability and support
- can render a Spectre.Console summary for operators without replacing the
  pipeline object contract

### Module Policy

Reusable enterprise defaults:

- approved feeds and mirrors
- approved Microsoft module families
- preferred profile per machine role
- allowed prerelease policy
- package manager preference and fallbacks
- approved tested bundles for high-risk module families
- Graph/Az/Exchange/Teams isolation decisions
- cleanup guardrails
- server-safe and desktop-safe modes

### Maintained Module State

This is the key product shape for delivered Evotec modules. We should be able to
say:

- this PC has the module families our delivered module needs
- the installed versions are coherent for that family
- the effective import winner is the one we expect
- the repository/profile that owns updates is known
- older or conflicting versions are either harmless, quarantined, or flagged
- an operator can repair drift without understanding Graph/Az internals

That suggests extending the existing private-module workflow so it can be
receipt-backed while still using normal module installation, not embedded
payloads by default. For example, `Install-PrivateModule` could:

1. install our delivered module from the enterprise feed
2. read its declared module-state dependencies or family policy
3. install or reconcile those dependencies in the chosen scope/root
4. write a PowerForge maintenance receipt
5. let `Test-ModuleState` and `Get-ModuleStatePlan -Repair` keep the
   machine aligned

The receipt should not be a second module manifest. It should be an operational
evidence file: what PowerForge installed, from which repository, with what
policy, for which delivered module or profile, and what conflicts were accepted
or repaired.

A maintenance receipt can be checked by the same plan/test workflow without
creating another cmdlet family:

```json
{
  "source": "ServerAutomation baseline",
  "maintainedModules": [
    {
      "name": "Company.Tools",
      "version": "1.2.0",
      "sourceRepository": "CompanyModules",
      "scope": "AllUsers"
    }
  ]
}
```

`Get-ModuleStatePlan`, `Test-ModuleState`, and `Invoke-ModuleStatePlan` accept
`-MaintenanceReceiptPath` so a support bundle can combine current inventory,
desired state, and prior PowerForge maintenance evidence. Initial drift findings
cover missing receipt modules, version drift, source drift, and scope drift.
Receipt source drift is repaired from the receipt source when it is known; the
plan action records the target repository so a reviewed plan artifact can still
prepare the expected private-module command later.
Receipt scope drift is treated as enforceable drift, because the wrong scope can
change which module version normal imports see. `Get-ModuleStatePlan -Repair`
turns scope drift into an exact scoped install intent that flows through
`Install-PrivateModule -RequiredVersion -Scope`.
`-Cleanup OldVersions` adds conservative removal intents for old, unloaded
versions of modules already covered by desired state or maintenance receipts.
Loaded old versions become conflict findings instead of removal intents. The same
cleanup mode is available in `Test-ModuleState`, so compliance checks can fail
when old managed versions still need cleanup.
`Invoke-ModuleStatePlan -MaintenanceReceiptOutputPath` writes a drift-checkable
maintenance receipt for modules whose maintained version is known from exact
policy or satisfied inventory. Maintenance receipt output is refused when the
plan is blocked by conflicts, cleanup-only actions, or missing delivery target,
because that artifact represents controlled managed state rather than a failed
attempt.
`Get-ModuleState -IncludeLoaded` enriches local inventory with modules already
loaded in the current runspace so plan/test conflict checks can reason about
in-use versions instead of only list-available module files.
Inventory captured from module paths marks the effective import candidate for
each module name using module-path precedence and the highest version within the
first matching root. Plan and source-policy checks prefer that effective copy
when no explicit desired scope is supplied, then fall back to highest installed
version for older or hand-authored inventories that do not include winner
evidence.
`Invoke-ModuleState` is the day-to-day one-stop command: it inventories, plans,
tests, prepares delivery, optionally executes install/update actions, writes
requested artifacts/receipts, and returns a single workflow result with all
evidence attached. The lower-level cmdlets remain available for operators who
want to inspect or persist each stage separately.

`Get-ModuleState` returns a typed inventory object by default. That object can
be piped directly to `Get-ModuleStatePlan`, `Test-ModuleState`, or
`Invoke-ModuleStatePlan` together with a normal PowerShell desired-state object.
`-OutputPath` and `-AsJson` exist for support evidence, CI artifacts, and
reviewable approval files; they are not the primary operator workflow.
`Get-ModuleStatePlan` returns a typed plan object by default. That object can be
piped directly to `Test-ModuleState` or `Invoke-ModuleStatePlan`. A plan artifact
can still be written when an operator wants to apply the exact approved plan
later instead of rebuilding it from source inputs.

## Conflict Model

Conflicts need to be explicit rather than incidental. Treat these as first-class
findings in the plan:

| Conflict | Example | Preferred handling |
| --- | --- | --- |
| Version collision | Graph auth 2.36 and 2.38 both visible | select by intent, warn about extra versions, optionally cleanup old |
| Family coherence conflict | Graph Authentication 2.36, Graph Groups 2.36, and Graph Users 2.38 are installed together | plan a coherent tested set; update/downgrade together or fail before import |
| Assembly collision | Graph, Teams, EXO, Az load incompatible shared assemblies | choose isolated profile or fresh process before import |
| Command collision | two modules export the same command | report command and source modules; allow prefix/import strategy |
| Repository ambiguity | same module/version in PSGallery and private feed | prefer policy source; record chosen source |
| Scope ambiguity | CurrentUser older than AllUsers newer, or reverse | pick effective import winner per edition and policy |
| Profile drift | user profile says one repository URI, machine profile another | report both and resolve by precedence |
| Loaded-module lock | old version loaded in current process | require fresh process, isolated import, or explicit `-Force` where safe |
| Maintenance receipt drift | delivered module says Graph bundle 2.36 was reconciled, but the PC now imports 2.38 first | repair to the receipt policy or update the whole receipt intentionally |
| Server constraint | GUI/profile mutation attempted on server automation | fail or plan-only depending on mode |

For Graph, Az, ExchangeOnlineManagement, and MicrosoftTeams, default to a
family-aware profile. These modules are not just packages; they bring command
families, authentication state, nested modules, native/managed assemblies, and
rapid version churn.

The Graph example is the important warning sign: having
`Microsoft.Graph.Authentication` 2.36 and 2.38 installed is not automatically
bad, but mixing imported Graph modules across those versions is a practical
failure mode. The planner must reason at the family level, not just per module
name. If our delivered module needs Graph Users and Groups, the plan should
select one coherent bundle and treat cross-version import winners as a conflict
even when every individual module technically satisfies its own version range.
Inventory that includes loaded modules is part of that safety model because a
loaded incompatible version can block a repair or require a fresh process before
the install/update work is meaningful.

## Engine Design

Add a new core area in `PowerForge`, likely:

```text
PowerForge/
  Models/ModuleState/
  Services/ModuleState/
  Abstractions/ModuleState/
```

Core contracts should include:

- `ModuleStateInventoryRequest`
- `ModuleStateInventory`
- `PowerShellEngineInventory`
- `ModulePathInventory`
- `InstalledModuleInventory`
- `RepositoryInventory`
- `ModuleStateDesiredState`
- `ModuleStateFamilyPolicy`
- `ModuleStateFamilyCoherenceRule`
- `ModuleStateVersionPolicy`
- `ModuleStateMaintenanceReceipt`
- `ModuleStateConflictFinding`
- `ModuleStatePlan`
- `ModuleStateAction`
- `ModuleStateApplyRequest`
- `ModuleStateApplyResult`
- `ModuleStateReceipt`

Core services should include:

- `ModuleStateInventoryService`
- `ModuleStateDesiredStateReader`
- `ModuleStateCatalogService`
- `ModuleStateVersionResolver`
- `ModuleStateFamilyCoherenceAnalyzer`
- `ModuleStateConflictAnalyzer`
- `ModuleStatePlanner`
- `ModuleStateReceiptWriter`

`PowerForge.PowerShell` should own adapters that need PowerShell runtime/tooling:

- PS5/PS7 executable discovery
- `Get-Module -ListAvailable`
- `Get-InstalledModule`
- `Find-PSResource` / `Install-PSResource`
- `Find-Module` / `Install-Module`
- repository registration probes
- current-runspace loaded module/assembly inspection
- isolated import execution
- module-state receipt discovery and repair execution
- profile store read/write

The existing `PSResourceGetClient`, `ModuleDependencyInstaller`,
`PrivateModuleWorkflowService`, `EmbeddedModuleDependencyService`, and
`ModuleIsolationProfileRegistry` should be reused rather than replaced.

## Desired State File

Introduce a portable file format for persistence, for example
`powerforge.modules.json`:

```json
{
  "schema": "https://schemas.evotec.xyz/powerforge/modules/v1.json",
  "profiles": [
    {
      "name": "ServerAutomation",
      "repositories": ["CompanyModules", "MicrosoftArtifactRegistry"],
      "defaultScope": "AllUsers",
      "cleanup": { "keepLatest": true, "keepPinned": true },
      "families": {
        "MicrosoftGraph": {
          "modules": [
            "Microsoft.Graph.Authentication",
            "Microsoft.Graph.Users",
            "Microsoft.Graph.Groups"
          ],
          "version": "2.36.0",
          "coherence": "SameVersion",
          "importMode": "IsolatedProfile",
          "isolationProfile": "MicrosoftGraphAuthentication",
          "requiredBy": ["Company.GraphTools", "Company.UserLifecycle"]
        },
        "ExchangeOnline": {
          "modules": ["ExchangeOnlineManagement"],
          "version": ">=3.9.0",
          "importMode": "IsolatedProfile",
          "isolationProfile": "ExchangeOnlineManagement"
        }
      }
    }
  ]
}
```

Keep the schema engine-owned so cmdlets, CLI, Studio, and docs can all consume
the same contract.

The PowerShell-facing cmdlets should not require JSON for normal use. The
implemented lightweight desired-state adapter accepts ordinary PowerShell
objects:

```powershell
$desired = @{
    Modules = @(
        @{
            Name       = 'Company.Tools'
            Version    = '=1.2.0'
            Repository = 'CompanyModules'
            Scope      = 'AllUsers'
        }
    )
}

$inventory = Get-ModuleState -IncludeLoaded -ShowSummary
$plan = $inventory | Get-ModuleStatePlan -DesiredState $desired -Repair -ShowSummary
$plan | Test-ModuleState -PassThru -ShowSummary
$plan | Invoke-ModuleStatePlan -Execute -ShowSummary

Invoke-ModuleState -DesiredState $desired -Repository CompanyModules -Repair -Execute -ShowSummary
Invoke-ModuleState -ModuleName Company.Tools -RequiredVersion 1.2.0 -Repository CompanyModules -Scope AllUsers -ShowSummary
Invoke-ModuleState -Installed -Latest -Repository PSGallery -ShowSummary
Invoke-ModuleState -Installed -Latest -Repository PSGallery -Cleanup OldVersions -ShowSummary
```

Artifact readers also accept smaller JSON shapes for early adoption:

```json
{
  "modules": [
    {
      "name": "Company.Tools",
      "versionPolicy": ">=1.2.0",
      "allowedSources": ["CompanyModules"],
      "scope": "AllUsers"
    }
  ]
}
```

Inventory JSON can include `isEffectiveImportCandidate` per installed module.
`Get-ModuleState -AsJson` writes that flag for local scans, and
`Get-ModuleStatePlan` uses it to model the module copy that normal import would
see first.

The optional module `scope` is planning evidence, not a new install cmdlet. When
it is present, `Get-ModuleStatePlan` selects the installed version from that
scope, emits install/update intent targeted at that scope, and reports a
`ModuleState.ScopeMismatch` finding when installed copies exist only elsewhere.
Maintenance receipts preserve target scope for satisfied state and prefer
observed post-apply scope when evidence is available.
`Invoke-ModuleStatePlan` carries install/update target scopes into the existing
private delivery flow, so reviewed plans use `Install-PrivateModule -Scope` or
`Update-PrivateModule -Scope` instead of a separate managed-module command.
When desired state specifies exactly one repository, the plan records that
repository as the action target so object-first plans can still prepare the
correct `Install-PrivateModule -Repository` or `Update-PrivateModule
-Repository` command without requiring a separate command-line repository
override.

The file should support both target-state profiles and delivered-module
requirements. A delivered module can say "I require the Microsoft Graph family
as a coherent 2.36.0 bundle from the enterprise feed" without embedding Graph
inside its own package.

## Cmdlet Surface

Do not create a sprawling command catalog. Twenty-plus new cmdlets would make
the feature harder to discover, document, test, and support. Start with a small
workflow surface and let typed result objects carry the detail.

Recommended initial surface:

| Cmdlet | Purpose |
| --- | --- |
| `Invoke-ModuleState` | One-stop inventory, plan, test, prepare, optional execute, receipts, artifacts, and Spectre summary for day-to-day module maintenance. Supports `-ModuleName` convenience input or richer `-DesiredState` objects. |
| `Get-ModuleState` | Inventory the local PC or supplied inventory artifacts. Returns objects by default. Supports `-Path`, `-ModulePath`, `-IncludeLoaded`, `-OutputPath`, `-AsJson`, and `-ShowSummary`. |
| `Test-ModuleState` | Evaluate a plan object, or inventory plus desired state, against policy, delivered-module requirements, optional maintenance receipts, family presets, and optional cleanup policy. Emits findings and can fail CI/support checks with `-FailOnConflict`. |
| `Get-ModuleStatePlan` | Build a typed plan from an inventory object plus desired-state object, or from artifacts. `-Repair` turns receipt drift into conservative install/update intents, and `-Cleanup OldVersions` adds cleanup intents under the same result model. Supports `-OutputPath`, `-AsJson`, and `-ShowSummary`. |
| `Invoke-ModuleStatePlan` | Prepare or execute an approved install/update plan with `ShouldProcess`, receipts, and per-action results. It accepts a typed plan object from the pipeline, `-PlanPath` for a reviewed plan artifact, or inventory/desired-state inputs before private-module delivery is prepared. Cleanup intents are reported but not executed by private-module delivery. |

That is five new cmdlets. Do not create a second install/update/repair noun
family when the repo already has `Install-PrivateModule` and
`Update-PrivateModule`. Delivered-module installation should stay there, with
optional policy/receipt behavior. Repair should be a plan/apply workflow, not a
second install verb.

With the one-stop entrypoint included, the public surface is still intentionally
small: one daily operator command plus focused inspect/plan/test/apply stages.
All remain in the `ModuleState` noun family.

Do not create module-state profile cmdlets in the first slice. Reuse
existing repository-profile commands where the data is repository-shaped, and
use `powerforge.modules.json` where the data is policy-shaped.

Avoid separate commands for every noun such as family, receipt, compare, export,
update, remove, and repair unless usage proves they are truly independent
workflows.

Prefer parameter sets and output shaping over command explosion:

- `Get-ModuleState -Path inventory.json` instead of `Import-ModuleState`
- `Get-ModuleState -OutputPath inventory.json` instead of `Export-ModuleState`
- `$inventory | Get-ModuleStatePlan -DesiredState $desired` instead of forcing
  JSON through the pipeline
- `$plan | Invoke-ModuleStatePlan` instead of requiring `-PlanPath`
- `Invoke-ModuleState -ModuleName Company.Tools -RequiredVersion 1.2.0` instead
  of asking operators to hand-wire inventory, plan, test, and apply every time
- `Invoke-ModuleState -Installed -Latest -Repository PSGallery` instead of
  hand-authoring a desired-state object for every currently installed module
- `Test-ModuleState -ModuleName Company.Tools` instead of a separate test command
- `Get-ModuleStatePlan -Repair -Cleanup OldVersions` instead of a separate
  repair command
- `Get-ModuleStatePlan -Cleanup OldVersions -WhatIf` instead of a separate
  cleanup command
- `Get-ModuleStatePlan -Family MicrosoftGraph` instead of a separate family
  command
- `Invoke-ModuleStatePlan -UpdateOnly` instead of `Update-ModuleState`
- `Install-PrivateModule -ProfileName Company -PolicyPath .\powerforge.modules.json`
  instead of a second install command

Current repair intent is deliberately conservative. `Get-ModuleStatePlan -Repair`
pins the planned action's `VersionPolicy` to the receipt-managed version and
marks the action as repair evidence. `Invoke-ModuleStatePlan -Repair` prepares
delivery through the existing `Install-PrivateModule` and `Update-PrivateModule`
workflow with `-RequiredVersion` when the action has an exact version policy.
That keeps repair execution in the existing delivery family while preserving
receipt-backed version intent. Receipt source and scope drift are repaired with
the same path: source drift adds the receipt repository as the action target,
and scope drift adds `-Scope` to the prepared private-module command.

Current install/update scope intent is carried through the same delivery family.
`Install-PrivateModule` and `Update-PrivateModule` accept `-Scope`, and
ModuleState target scopes map into that parameter during `Invoke-ModuleStatePlan`.
The default remains `CurrentUser` when no desired scope is declared.

Current cleanup intent is also deliberately conservative. `Get-ModuleStatePlan
-Cleanup OldVersions` can emit `Remove` actions for old, unloaded managed module
versions and includes target scope/path evidence when the inventory contains it.
`Invoke-ModuleStatePlan -Cleanup OldVersions` does not translate those actions
into private-module commands; it blocks execution so removal or quarantine can be
reviewed and implemented as a separate guarded executor later.
The one-stop `Invoke-ModuleState -Installed -Latest -Repository PSGallery`
convenience mode creates update intents for currently installed modules so
PowerShellGet/PSResourceGet can resolve the latest repository version. Adding
`-Cleanup OldVersions` also reports old-version removal intents, but destructive
cleanup remains blocked until a dedicated safe cleanup/quarantine executor exists.

Maintenance receipt output is intentionally narrow. It records exact repair
targets and already-satisfied installed versions, but it does not record a
module from an unresolved range-only install/update action until a known version
exists. `PostApplyModulePath` or private-delivery execution evidence can supply
that known version, so a range-based install can still produce a useful receipt
after the maintained version is observed. Observed scope wins over planned scope
when both are available, which keeps future drift checks honest.

Built-in family presets should also stay under this surface. `-Family
MicrosoftGraph` adds the same-version coherence policy for the core Graph
modules that commonly fail when mixed across versions. Other high-risk families
can be accepted as observed families first, then promoted to stricter rules only
after we model their real compatibility behavior instead of guessing.
When `-Repair` is combined with a same-version family mismatch, the planner
creates exact repair intents that align installed family members to the highest
version already observed in that family. It does not install every module from a
broad preset and it does not query for a guessed latest version.

If a future Studio or CLI needs more buttons, it should call the same engine
operations behind these few cmdlets rather than forcing the PowerShell surface
to mirror every internal service.

Naming rule:

- New public cmdlets use the `ModuleState` noun family only.
- Existing delivery cmdlets stay as `Install-PrivateModule` and
  `Update-PrivateModule`.
- Graph, Az, Exchange, Teams, receipts, repair, cleanup, and compare are
  parameters, filters, modes, or result sections under `ModuleState`, not new
  command families.
- New core types should start with `ModuleState` unless they are reusing an
  existing PowerForge type.

## Enterprise Workflows

### Workstation Onboarding

1. Import a repository/profile bundle.
2. Register approved repositories.
3. Install prerequisites such as PSResourceGet and credential providers.
4. Inventory PS5 and PS7.
5. Keep inventory as an object for plan/test input; optionally write inventory
   with `Get-ModuleState -OutputPath` for support evidence.
6. Plan approved baseline modules.
7. Apply installs/updates.
8. Write an evidence receipt.

### Server Baseline

1. Read machine-role desired state.
2. Inventory without mutating profiles by default.
3. Prefer private feeds and pinned/approved versions.
4. Avoid GUI assumptions and user-profile edits.
5. Fail on unresolved conflicts.
6. Apply only exact planned changes.

### Graph/Az/Exchange/Teams Maintenance

1. Resolve module family policy.
2. Inventory installed versions and effective import winners per edition/scope.
3. Detect family coherence conflicts before import, such as Graph Auth 2.36 with
   Graph Users 2.38.
4. Avoid full meta-module installs unless policy requests them.
5. Prefer leaf modules where possible.
6. Install shared authentication/accounts modules at compatible versions.
7. Use isolated imports or fresh process only when the workflow actually needs
   import-time protection.
8. Warn when an incompatible version is already loaded in the current process.
9. Recommend repair/update of the machine state when import isolation would only
   hide a broken installed estate.

### Delivered Module Maintenance

1. Install the delivered module from a private or approved feed.
2. Read its module-state dependency policy or associated enterprise profile.
3. Reconcile normal installed modules, not embedded payloads by default.
4. Write a receipt with `Invoke-ModuleStatePlan -MaintenanceReceiptOutputPath`
   that records the chosen dependency family versions once they are known.
5. Periodically run `Test-ModuleState` against the delivered module policy or
   generated plan object.
6. Use `Get-ModuleStatePlan -Repair` and `Invoke-ModuleStatePlan` when
   a later manual install, PSGallery update, or profile change makes the
   delivered module unsafe to run.

### Cleanup

1. Inventory all versions by path/scope/edition.
2. Mark effective winners and pinned versions.
3. Mark removable old versions.
4. Detect loaded/locked versions.
5. Produce a cleanup plan.
6. Report target path and scope for each cleanup intent.
7. Apply removal/quarantine only after a dedicated guarded executor exists and
   honors `ShouldProcess`.

## Roadmap

### Phase 0: Assessment and Contracts

- Add this assessment.
- Add schema and model skeletons for inventory, intent, plan, conflict, result,
  and receipt.
- Add tests around pure version policy and conflict decisions.

### Phase 1: Local Inventory

- Implement PS5/PS7 engine discovery.
- Inventory `PSModulePath`, installed modules, available module manifests, and
  registered repositories.
- Detect side-by-side versions, effective import winners, and module family
  membership.
- Add `Get-ModuleState`.

### Phase 2: Plan-Only Desired State

- Read `powerforge.modules.json`.
- Resolve local satisfaction versus missing/update/cleanup actions.
- Add conflict findings, including family coherence conflicts.
- Add `Get-ModuleStatePlan`.

### Phase 3: Install/Update Apply

- Reuse `ModuleDependencyInstaller` and private gallery workflows.
- Extend `Install-PrivateModule` / `Update-PrivateModule` only where needed to
  accept policy/receipt-backed reconciliation.
- Add `Invoke-ModuleStatePlan`.
- Write module-state maintenance receipts for normal installed module state.

### Phase 4: Family Policies

- Add built-in policies for Graph, Az, ExchangeOnlineManagement, MicrosoftTeams,
  ActiveDirectory/RSAT, and VMware.PowerCLI.
- Connect family policy to existing isolation profiles.
- Expose family checks through `Test-ModuleState` and
  `Get-ModuleStatePlan -Family`, not separate family cmdlets.

### Phase 5: Cleanup and Repair

- Add old-version cleanup planning.
- Keep cleanup planning separate from private-module install/update execution
  until a removal/quarantine executor is designed.
- Add repair planning for missing manifest/files, broken repository
  registration, incoherent module families, and mismatched maintenance receipts.
- Extend cleanup/quarantine actions after receipt repair, including old-version
  pruning and safe quarantine before destructive removal.
- Add conservative quarantine mode before destructive removal.

### Phase 6: Fleet and Studio

- Add evidence aggregation for typed objects and JSON artifacts.
- Add Studio views over the same engine contracts.
- Add optional remote collection/apply adapters later, not in the first slice.

## First Concrete Slice

The first implementation PR should be small and contract-heavy:

1. Add module-state models in `PowerForge`.
2. Add `ModuleStateVersionPolicy` parsing and comparison.
3. Add `ModuleStateFamilyPolicy` and same-version coherence checks for synthetic
   Graph-family inventory data.
4. Add `Get-ModuleStatePlan` as a plan-only cmdlet that accepts an
   inventory object and desired-state object, with artifact paths available for
   persistence and support bundles.
5. Add focused tests for version selection, source preference, scope ambiguity,
   family coherence, and loaded-module conflict findings.

That gives us a reusable center of gravity before we mutate real machines.

## Repository Context Reviewed

- PSPublishModule docs for module dependencies, isolation, private galleries,
  module lifecycle actions, and module layering cleanup
