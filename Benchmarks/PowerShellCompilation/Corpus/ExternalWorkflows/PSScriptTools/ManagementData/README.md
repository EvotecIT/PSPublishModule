# Offline SDK management data and class search

This packet qualifies unchanged `Find-CimClass` from PSScriptTools at commit
`549fa3e3d769532320054fc0b3c8f42df0452361`. The pinned source file is
`functions/find-cimclass.ps1`, SHA-256
`6a13743f5de27e510a9cce5f90ea6a1a98859280132e8307ef5c9c0e0faac1f2`.
Upstream source stays external; the existing portfolio records MIT metadata and
its archive hash. Only the observation/preparation drivers and authored helpers
are stored here.

Run `Prepare.ps1 -SourcePath <pinned-function-file> -OutputDirectory <new-owned-folder>`.
It checks the exact hash and assembles its UTF-8 payload with `Functions.psm1`,
excluding only a leading BOM. Analyze the resulting `Fixture.psd1` as a Hybrid
binary module, inspect and save its exact `result.dependencyGraph`, then build
with `--dependency-lock <saved-graph.json>`. Use `net10.0` for PowerShell 7 and
`net472` for Windows PowerShell 5.1. Preserve emitted source for inspection.

In separate fresh host processes run `Observe.ps1 -ModulePath <original-or-generated-manifest>
-OutputPath <unique-owned-json>` and compare all twenty observations within each
host. The unchanged function plus nine authored helpers emit ten methods. Only
Find-CimClass adds upstream coverage. Check that the module contains no copied
MMI or SMA runtime DLL; the host supplies them.

The driver creates sessions without invoking a connection operation and creates
in-memory classes using `New-CimInstance -ClientOnly`. Microsoft documents
[ClientOnly as an in-memory operation without going to a CIM server](https://learn.microsoft.com/en-us/powershell/module/cimcmdlets/new-ciminstance?view=powershell-7.6#-clientonly).
Before invoking Find-CimClass, the driver asserts that TestConnection and
EnumerateClasses are ETS ScriptMethods backed by in-memory rows. The namespace
provider is module-owned. This qualifies sorting/filtering, exclusion, empty
results, pipeline, partial failure, refusal paths and binding/metadata, with
actual SDK object identities. It never executes the real connection/enumeration
methods. Borrowed objects retain identity; the driver disposes owned instances
and sessions in finally. The generated owned-session factory and bare type
literal also validate net472 generated reference closure.

[The durable ledger](../../../m29a-management-sdk-data.net10.json) records complete
observations, source/artifact/reference hashes, compatibility builds, Strict
rejection and independent review. Proof is Windows x64 on the two claimed hosts;
live CIM, credentials, WMI/network/AD, GUI, non-Windows behavior and performance
remain unqualified. Runtime-free targets retain their fail-closed boundary.
