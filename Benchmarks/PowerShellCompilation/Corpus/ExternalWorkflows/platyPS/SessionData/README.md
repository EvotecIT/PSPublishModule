# Offline session forwarding

Only the authored observation driver is included here. The upstream function
remains an external verification input; exact-version redistribution licensing
was not established for the pinned package.

The source is `GetCommands` from platyPS PSGallery package `0.14.2`:

- Package SHA-256: `12176941c2972577d7adbf4877ad6aebcca0920c416b46ef1131e5a5248ec59b`.
- Unchanged function-extent SHA-256: `afbf47da3698f2688c89966e8858473440c4639be273cdae7a6dff795dfa9407`.

For reproduction, verify the external package, parse its `platyPS.psm1` with
`System.Management.Automation.Language.Parser`, select the unique top-level
`FunctionDefinitionAst` named `GetCommands`, and write its `Extent.Text` to a
task-owned `GetCommands.psm1` using UTF-8 without a BOM or added newline. Check
the function hash before building a Hybrid binary module for each claimed host.
Run `Observe.ps1 -ModulePath <source-or-generated-module>` in separate fresh
PowerShell processes and compare its 24 JSON observations within each host.

The driver uses a local seeded module and unopened caller-owned session. Its
module-owned metadata provider refuses changed identity or opened runspaces;
the global fallback refuses unexpected provider scope. No transport opens and
the remote command body is never executed. Discovery, alias filtering, output
projection, identity forwarding and error preferences are qualified. Actual
remote metadata retrieval, authentication and complete platyPS execution are
outside this proof.
