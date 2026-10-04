# PSSharedGoods forest details: installed-host exception boundary

`Prepare.ps1` assembles the unchanged SHA-256-pinned Get-WinADForestDetails and Copy-DictionaryManual functions with eight owned provider functions. Supply the pinned external PSSharedGoods root and a new task-owned input directory. No RSAT assembly is redistributed.

Run `Observe.ps1` in fresh child processes for each original/generated artifact on both Windows hosts with installed ActiveDirectory. The driver verifies that all directory/network provider names resolve to module-local functions before workload invocation. Provider data uses owned `.offline.invalid` names and documentation IP addresses; no directory query, ping, WinRM or port connection occurs. The actual installed ADIdentityNotFoundException is constructed and thrown client-side to exercise typed-catch replica fallback.

Fifteen observations per host cover normal and partial fallback, ordinary listing and discovery failures, early forest returns, domain/controller/RODC filtering, writable/credential forwarding, Ping-only availability, extended indexes and cached-input filtering without provider calls or caller mutation. Only credential presence is recorded, never its value. The driver restores caller progress preference in finally after recording the authored effect.

Three pinned-source limitations are preserved: early-return/cached-input paths leave ProgressPreference changed, Ping-only availability still invokes the port-probe provider, and cached domain exclusion leaves a stale DomainsExtendedNetBIOS entry. Those fixes belong to PSSharedGoods and must not be silently applied to a compiler oracle.

The workload remains hosted with one advertised region; no complete-method or individually observed region execution gain is credited. Strict rejects both targets. This qualifies installed-Windows-host offline behavior, not live AD provider execution, native dependency deployment, portability, a full generated module, or arbitrary external typed-catch admission.
