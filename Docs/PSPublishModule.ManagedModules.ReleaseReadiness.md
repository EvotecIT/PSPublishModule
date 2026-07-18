# Managed Module Release Readiness

The managed module lifecycle is feature-complete against the stable
`Microsoft.PowerShell.PSResourceGet` 1.2.0 module workflows. The authoritative
behavior and intentional differences are documented in:

- [Managed Module Compatibility](PSPublishModule.ManagedModules.Compatibility.md)
- [PSResourceGet Parity](PSPublishModule.PSResourceGetParity.md)
- [Managed Module Benchmarks](../Benchmarks/ManagedModules/README.md)

This page tracks only the evidence needed for a release claim. It is not a
feature roadmap.

## Completed Product Scope

- [x] Managed find, inventory, save, install, update, uninstall, publish,
  compression, and estate repair use shared C# engines behind thin PowerShell
  cmdlets.
- [x] Estate repair keeps physical roots, PowerShell editions, scopes, and local
  profiles independent; supplied inventories and maintenance receipts retain
  explicit destinations;
  cleanup replans after delivery, requires an error-free refreshed plan,
  batch-preflights exact-path uninstall safety, protects current-runspace
  loaded modules, validates global/profile/custom cross-root dependencies with
  per-profile visibility for global cleanup targets,
  treats unknown-edition roots conservatively, fails closed when a previously
  available inventory/dependency root cannot be inspected, preserves
  profile/edition visibility instead of pooling unrelated alternatives, orders selected dependents
  before dependencies, and returns post-apply convergence evidence without
  treating declined actions as success.
- [x] Common PowerShellGet and stable PSResourceGet module workflows have
  documented managed equivalents, including typed pipelines and version-range
  selection.
- [x] Supported module operations do not invoke PowerShellGet, PSResourceGet,
  PackageManagement, external executables, or embedded PowerShell scripts.
- [x] Windows PowerShell 5.1 and PowerShell 7 validation covers the supported
  lifecycle, compatibility contracts, generated help, and package artifacts.

## Release Closure

- [x] Stable PSResourceGet 1.2.0 `-AuthenticodeCheck` behavior is covered by
  unsigned-package rejection tests and an exact-candidate install of the
  upstream `PackageManagement` 1.4.3 fixture. WinTrust accepted 26 signed files,
  including `PackageManagement.cat`, before promotion. The managed command
  fails closed on unsupported non-Windows hosts instead of continuing unchecked.
- [x] Repeated exact-candidate benchmark runs completed on PowerShell 7.6.3 and
  Windows PowerShell 5.1.26100.8875 with one warmup, three measured iterations,
  grouped rotation, correctness validation, and zero failures. Runs
  `20260718-102954-b3d1926c` and `20260718-103142-3dc02445` pinned runtime
  candidate `23c41a4ec29c0c345f19a5cffd7e8669cd839b5d`; native provider install lanes were
  skipped because the current-profile benchmark intentionally does not mutate
  the maintainer's module roots.
- [x] The public README keeps the representative multi-scenario matrix rather
  than replacing it with a focused release spot check. Both use Managed as the
  `1.00x` baseline, show raw times, and label non-equivalent or skipped lanes.
- [x] Generated and PSGallery 3.0.68 packages were imported under PowerShell 7
  and Windows PowerShell 5.1 and exercised through documented managed-module
  repair, provenance, dependency-safety, and convergence workflows.

### Exact-Candidate Performance Spot Check

The focused release run used `PSScriptAnalyzer` 1.25.0 against the normal
PowerShell Gallery paths. Times are medians of three measured iterations after
one warmup; install rows are Managed-only because native install benchmarking
requires the isolated `TemporaryLocalUser` profile.

| Host | Operation | Managed | PSResourceGet | PowerShellGet |
| --- | --- | ---: | ---: | ---: |
| PowerShell 7.6.3 | Find | 390 ms | 389 ms | 1.12 s |
| PowerShell 7.6.3 | Install | 614 ms | Skipped | Skipped |
| PowerShell 7.6.3 | Save | 774 ms | 1.65 s | 3.82 s |
| Windows PowerShell 5.1 | Find | 339 ms | Unsupported | 661 ms |
| Windows PowerShell 5.1 | Install | 968 ms | Unsupported | Skipped |
| Windows PowerShell 5.1 | Save | 965 ms | Unsupported | 2.88 s |

The pinned net10.0 assembly SHA-256 was
`A8A95D233EA6B71BAE656FE7F8AB9E1287F5481764EA9B972DCF6541511A7F1A`; the
net472 assembly SHA-256 was
`B1022F2C405A94C7EA74D0474E83A6FFD27D9BA073496F036D33FE66F04EA5EB`.
The final documentation-only commit does not change this measured runtime
source or either pinned assembly.

## Explicit Non-Goals

These do not block the managed module lifecycle release:

- remaining script find, update, uninstall, publish, and compression commands
- DSC-resource, command-name, and role-capability package-content search
- automatic repository-priority fanout
- automatic credential-provider installation or bootstrap
- Microsoft Artifact Registry `ModulePrefix` transport
- direct discovery and conversion from the PowerShellGet v2 repository store

Add any future capability only through a reusable resource or repository model
that preserves the current module-path behavior and performance.
