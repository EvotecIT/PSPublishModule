# Dynamic parameter binding workflows

`Copy-HelpExample.ps1` and `ShowTree.ps1` are unchanged from PSScriptTools
revision `549fa3e3d769532320054fc0b3c8f42df0452361`, under the accompanying
MIT license. Source SHA-256 values are
`50f8151458aadc0581a65c51a5fa72769dbf489131fd9075a645545585c760e5` and
`247ed38488726d4ffdd93a7a6af81779ffec825585513a78ed812cddcfca5548`.

The observers run as scripts in isolated child hosts. The help observer uses
module-owned help, grid and clipboard providers with in-memory records. It
compares six cases: successful selection, an explicitly false dynamic switch,
empty help, provider failure, invalid command name and invalid path. A false
switch still enters the authored grid route because the function checks key
presence. No window opens and no clipboard or user terminal state changes.

The tree observer reads only its explicitly supplied owned filesystem fixture.
It compares eight cases: Path, LiteralPath, dynamic alias, false switch, zero
depth, files, unavailable dynamic metadata for another provider and discovery
failure. Its controlled ANSI map affects only the child process. The proof
compares output records and binding errors; it does not qualify visual rendering,
interactive providers or user filesystem traversal.

Each isolated Hybrid artifact emits one complete native-bound function on
net10.0/PowerShell 7 and net472/Windows PowerShell 5.1. Dynamic discovery remains
an explicit SDK-hosted block; executable clauses are compiled callbacks. These
workflows do not establish runtime-free compilation or execution of the full
PSScriptTools module. Development dependency locks are unreviewed.
