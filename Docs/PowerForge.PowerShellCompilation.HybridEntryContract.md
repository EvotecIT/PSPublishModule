# Hybrid executable entry contract

Status: design target. No Hybrid script-root statement is claimed as compiled by this document.

Hybrid executables already emit eligible named functions as CLR methods. The packaged program still calls `PowerShell.AddScript` for the authored entry script, so its top-level statements run in PowerShell. The final explanation marks an otherwise eligible retained script root with `artifact.executable-script-root`. A compiled named function called from that root is useful coverage, but it is not compiled root execution.

## Ownership and invocation

The packaged launcher parses process arguments; its PowerShell invocation owns the authored `param` block, defaults, validation, `$args`, common parameters, `#requires`, source/dependency declarations, and runspace lifetime. Those actions must occur once, in source order. The compiler owns only a selected executable body span and its typed operations. It must not turn the authored root into an ordinary public function and bind the same parameters again: an unbound default or a validation script can have effects, and `$PSBoundParameters` does not contain every defaulted value. A two-function probe on PowerShell 7 and 5.1 evaluated an omitted default twice and an explicitly bound `ValidateScript` twice when the outer function called the inner one with `@PSBoundParameters`.

The proposed entry is a private generated method with an explicit host entry frame. The frame carries the already-bound parameter values, bound-name set, remaining arguments, authored script path/root, invocation context needed by the selected operations, and a live output/error/stream and cancellation owner. The packaged script retains its prologue and invokes a hidden bridge at the selected body span. The bridge must use the existing runspace for runtime-resolved commands; it must not silently replace an unqualified command with a CLR intrinsic or invoke an authored parameter binder a second time. The entry method and its source map are separate from named-function methods and counts.

Source closure and shaping must be one decision shared by `explain` and `build`. The compiler may replace a root span only when its parsed source checksum, statement order, contained dependency closure, generated entry frame, and host-effect contract all agree. It should reuse the canonical bound pipeline and source remapping rather than a new syntax whitelist or a special case for `Get-Date`. An unsupported root remains the authored hosted script, with an exact blocker in the explanation. Strict continues to use its runtime-free entry and rejects host-dependent forms.

## Observable contract

The first admitted route must prove these behaviors, or reject a root that observes one not yet implemented:

- Parameter conversion, optional defaults, validation effects, aliases, positional and named arguments, `$PSBoundParameters`, and `$args` occur once with the same values and errors.
- Local functions and contained dot-source declarations are available at the same source point. Runtime command lookup and shadowing happen at invocation, in the same runspace.
- Success output preserves zero/one/many records, ordering, CLR value types, collection enumeration, and the no-output versus `$null` distinction. Warning, verbose, debug, information, and error records retain their routing and preferences.
- `return`, `exit`, caught and uncaught errors, `$?`, stopping, `finally`/`clean`, and repeated invocation preserve their authored effects. Until an individual transfer is proven, that shape remains hosted.
- `$MyInvocation`, `$PSCommandPath`, `$PSScriptRoot`, caller scope, and module/session state must refer to the authored script context when read. A generated method must not expose its private bridge identity as the authored command.

## Qualification sequence

1. Build a source-mapped synthetic entry through the shared semantic pipeline with Hybrid host capabilities. Keep its method and disposition distinct from named functions. Compare `explain` and build selection on the same source before executing it.
2. Add the private host entry frame and bridge. Keep the authored parameter/prologue execution, and replace the root body only after all required frame fields and transfer effects are available. Refuse overlapping edits, source changes, and declaration order that the bridge cannot preserve.
3. Compare an ordinary generated launch with the original on the claimed host. Start with the dynamic-format date script as a focused binding/command-resolution probe, then the already pinned, unchanged offline Fibonacci application as the standalone portfolio workflow. The latter currently has one emitted named helper but a hosted root; count a new root operation only after the generated body is demonstrably invoked. Do not redistribute its externally pinned source without license evidence.
4. Exercise omitted and explicit parameters, a side-effecting default/validation control, zero/one/many output, a shadowed command, relevant streams, a caught error, `return`, `exit`, stopping/cleanup, and repeated launch. Record any rejected case as a specific hosted boundary, not a passing compiled case. Run focused compiler checks and the affected artifact/host proofs; reserve the broad matrix for an integration candidate or a contract it alone covers.

The first claimed target may be `net10.0` on Windows x64, where the existing standalone generated workflow was observed. PowerShell 5.1 can serve as an original-source oracle, but this does not claim a generated `net472` executable or other platforms without their own artifact execution.
