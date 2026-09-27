# Offline managed parser and model workflows

These observation drivers qualify six unchanged functions from the external
platyPS 0.14.2 package: `New-MarkdownHelp`, `Get-MarkdownMetadata`, `New-YamlHelp`,
`GetParserMode`, `SetOnlineVersionUrlLink`, and `ConvertMamlModelToMarkdown`.
Upstream source and assemblies remain external verification inputs; exact-version
redistribution licensing was not established.

The package SHA-256 is
`12176941c2972577d7adbf4877ad6aebcca0920c416b46ef1131e5a5248ec59b`.
The manifest, module, assembly hashes, reviewed dependency-lock identities,
artifact evidence and matching observations are in
[the managed-type ledger](../../../m29a-managed-dependency-types.net10.json).

For reproduction, verify the pinned package and analyze its root manifest as a
Hybrid binary module on each target. Use `Declared` resources and explicitly
include `platyPS.Resources.psd1`, which its initialization reads. Inspect the
dependency graph, save that exact lock, and pass it to `powershell build` with
`--dependency-lock`. Build `net10.0` for PowerShell 7 and `net472` for Windows
PowerShell 5.1. Run `Observe.ps1 -ModulePath <original-or-generated-manifest>
-OutputPath <unique-owned-output.json>` in separate fresh host processes. Each
run creates an output directory beside its JSON file for the Markdown and YAML
files. Compare the 21 observations within each host, including file contents,
parameter types, invalid model binding, enum values and model mutation.

The documentation command is a local function. The URL is stored as model data;
no request is made. These cases use the real managed parser/renderers and owned
files, with no external service, installer or administrative command.

`CastFixture.psm1` is an authored synthetic sibling probe, not upstream coverage.
Copy it and its manifest into a task-owned folder, then copy the two verified
assemblies from the external package beside them. Build against an inspected
dependency lock for each host and run `ObserveCasts.ps1` against the source and
generated manifests. Its 12 observations per host cover null, borrowed and
converted models, invalid casts and errors. Only `Convert-ManagedModel` emits;
the constrained invocation and index functions remain hosted to preserve exact
overload-selection semantics. The probe adds no functions to the portfolio count.

This qualification is for Hybrid native binding on Windows x64. It does not
establish runtime-free dependency types, generic containers, arbitrary type
imports, all platyPS operations, non-Windows behavior or performance.
