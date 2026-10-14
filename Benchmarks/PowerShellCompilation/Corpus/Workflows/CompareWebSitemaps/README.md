# Complete sitemap comparison workflow

`workflow.json` qualifies the complete `Build/Compare-WebSitemaps.ps1` application
and its entire helper file. Supply the repository's `Build` directory as
`-SourceRoot` to `Invoke-ExternalQualification.ps1`, together with an explicitly
built CLI and task-owned workspace/evidence directories. Execution requires the
runner's `-AllowExternalExecution` opt-in.

The packet selects the corrected application by exact source hashes. The AMP
loop's local variable repair preserves the legacy URL collection used by report
counts. The historical unchanged source and its observations remain separate
evidence; this packet does not silently substitute corrected output for that
baseline.

The target is Hybrid, net10.0, win-x64, with the PowerShell 7.6 semantic profile.
Qualification requires an emitted CLR body for the script root and all 19
helpers, with no retained hosted body, omission, rejection or shaping fallback.
Source admission alone does not satisfy the packet. The runner compares all ten
scenarios across fresh original, built and installed processes, including the
complete JSON/CSV outputs and failure/output-preservation observations.

The reviewed fixture starts one bounded loopback-only server before the original
observer. It seals its PID/start identity and immutable inputs, and supplies the
actual mandatory argument vector before the project is locked. The same server
serves built/installed observation and the canonical project test. The common
runner calls Stop in `finally`; Stop releases only the recorded process and
checks that its listener is gone. A failed partial Start has its own recovery
receipt. Observer and fixture support files are pinned in the packet.

The full fixture has eight sitemap URLs and one unique additional legacy URL:
both full AMP cases expect nine legacy URLs and one exact overlap. The separate
AMP case without the additional URL, and the plain case, expect eight/one. All
19 helpers are observed through a separate original-process diagnostic whose
reports must equal the ordinary original reports after only `GeneratedAt`
normalization. Raw observations remain available; no timing claim is made.

The `output-refusal` case deliberately supplies an owned directory as its JSON
output. Its stderr comparison replaces only that scenario's exact physical
output-root prefix with `<scenario-output>`, preserving the `blocked-json` suffix.
Original-versus-Original controls using the same pinned source and same fixture
inputs reproduce this difference when only equal-length output roots vary. Source
headers, authored lines, message/scaffolding, stdout formatting and all other
error paths remain exact. Raw stdout/stderr, their hashes and generated file
hashes are retained beside every scenario; no generic path or error normalization
is applied.

The authored URL-origin helpers omit nondefault ports from some computed output
URLs. Those values remain part of the unchanged application behavior being
compared. The fixture uses known loopback request routes, refuses redirects and
does not apply redirects, publish output or administer an external site.
