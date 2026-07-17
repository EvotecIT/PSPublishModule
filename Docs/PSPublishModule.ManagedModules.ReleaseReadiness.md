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

- [ ] Prove complete `-AuthenticodeCheck` catalog behavior with signed fixtures,
  including timestamped signatures and short-lived certificate chains. Until
  then, describe ordinary signable-file validation as supported and full
  catalog parity as an explicit gap.
- [ ] Run the repeated benchmark suite from the exact release-candidate build
  on Windows PowerShell 5.1 and PowerShell 7, retaining the commands, suite
  metadata, gate results, and compact evidence paths.
- [ ] Regenerate the public README benchmark tables from those retained results.
  Use Managed as the `1.00x` baseline, include raw times, identify
  non-equivalent operations, and state the host, module family, cache mode,
  repeat count, engine order, cleanup mode, and correctness gate.
- [ ] Validate the final packaged module by importing it and exercising the
  documented managed-module parameters and pipelines before publication.

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
