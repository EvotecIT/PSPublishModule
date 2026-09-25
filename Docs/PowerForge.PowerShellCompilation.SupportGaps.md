# PowerShell compiler: observed support gaps

Updated: 2026-09-25. This is the M29a discovery inventory, not a list of cmdlets that never work. The five-module counts describe the pinned source and compiler state on `feature/powershell-compiler` before new M29a implementation. The [next milestones](PowerForge.PowerShellCompilation.NextMilestones.md#milestone-29a--make-ordinary-powershell-forms-useful) own the work and exit gate; the [assessment](PowerForge.PowerShellCompilation.Assessment.md) owns prior execution evidence.

The five unchanged M29 module packets contain 565 source functions. The final same-input Hybrid census emitted 341 complete functions on `net10.0` and 340 on `net472`; 224 and 225 respectively remained hosted. The [per-function ledger](../Benchmarks/PowerShellCompilation/Corpus/m29-portfolio-ledger.net10-net472.json) records exactly which ones. An emitted method may itself call PowerShell. A retained function may contain a promoted typed region. Neither count is a percentage of the PowerShell language.

| Pinned module | Complete methods, net10.0 | Complete methods, net472 | Retained, net10.0 |
| --- | ---: | ---: | ---: |
| PSSharedGoods | 205/282 | 204/282 | 77 |
| PowerInfoblox | 49/66 | 49/66 | 17 |
| PSScriptTools | 22/109 | 22/109 | 87 |
| platyPS | 22/40 | 22/40 | 18 |
| CleanupMonster | 43/68 | 43/68 | 25 |

The final-source five-packet, two-target census summary has SHA-256 `250abe61cdc546b8f8ac91258e243249fe761abff7ec6ba16db9bfd8900e9982`. Its `net10.0` feature-impact counts below are **affected source units**, not distinct functions unlocked by one fix; categories overlap, and a diagnostic can also occur inside an emitted Hybrid method. The reported “sole” count is only a visible single-feature explanation, not proof that emission and execution would follow if that feature were relaxed.

| Reported gap family | Affected units / visible sole, net10.0 | Concrete observed form |
| --- | ---: | --- |
| Broad `syntax.unsupported` | 99 / 83 | An umbrella for different control-flow, collection, member, and capture rules; it is not one implementation task. |
| Parameter metadata | 66 / 60 | Mostly `[OutputType(...)]` whose CLR type cannot be resolved to the current single-type metadata contract; 64 units are in PSScriptTools. |
| Parameter types | 21 / 10 | Target-unavailable or unresolved authored types, including platyPS `PSSession` and external AD types. |
| Nested function definitions | 19 / 6 | Functions declared inside a function or statement body need closure, scope, and lifetime behavior. |
| Method-invocation expressions | 12 / 12 | Eleven reported diagnostics are calls inside observed `catch` boundaries; argument effects and caught-error identity are not yet preserved there. |
| `Register-ArgumentCompleter` | 9 / 5 | PSScriptTools registration has a PowerShell host/session effect and no admitted typed equivalent. |
| `begin`/`process` lifecycle | 9 / 0 | Advanced function lifecycle and its missing lowering contract occur together. |
| File-scoped class/enum declarations | 8 / 3 | PSScriptTools functions sharing a file with authored types remain hosted. The separate pinned powershell-yaml packet has 0/14 complete methods because of a top-level enum and still needs loader qualification. |
| Typed nested script blocks | 6 / 3 | A delegate or explicit hosted boundary is needed for the observed captures. |
| Parameter defaults | 5 / 0 | Runtime-evaluated defaults, including platyPS `$Encoding`, are outside the current typed parameter contract; PSScriptTools also reports `ValidateScript` metadata separately. |
| Unqualified `Write-Verbose` | 6 / 0 | The host can resolve a different command than the built-in provider; this often co-occurs with other blockers. |
| Runtime scope | 5 / 0 | `$PSBoundParameters` and `$PSCmdlet` uses require their PowerShell invocation context. |
| Unqualified `ForEach-Object` | 4 / 0 | Some PSScriptTools/platyPS pipelines retain runtime command resolution. The special bounded PSScriptTools file loader is already recognized for source discovery; that does not compile every `ForEach-Object` invocation. |

The broad syntax group contains these **specific observed gaps**. Counts here are diagnostic occurrences in the five `net10.0` census packets, so they are useful for finding examples but must not be added to the affected-unit table:

| Authored form or effect | Observed occurrences | Current boundary |
| --- | ---: | --- |
| Capturing statement output into a typed variable or return | 14 plus 3 enclosing-return diagnostics | Requires a qualified success-stream host and a safe target/return transfer; `Get-ObjectPropertiesAdvanced` is one retained PSSharedGoods example. |
| Dictionary or `[pscustomobject]` values assembled from statements or conditionals | 11 dictionary and 3 note-property diagnostics | Current literal owner requires one value expression or a qualified native-hosted conditional; `Convert-FolderEncoding` is one PSSharedGoods example. |
| Member invocation within `catch` | 11 | Error identity and argument side effects must survive the boundary; `Get-LocalComputerSid` is one retained PSSharedGoods example, not an approved execution probe. |
| Nested definitions | 39 combined syntax diagnostics | A single authored definition can produce multiple diagnostics; `ConvertTo-JsonLiteral` is one PSSharedGoods example. |
| `begin`/`process` blocks | 9 diagnostics for each block | Pipeline lifecycle, stream behavior, stop, and cleanup remain jointly hosted. |
| Object-valued `switch` and observed `$_`/`$switch` state | 7 object-type and 8 automatic-variable diagnostics | Scalar switch lowering does not cover these runtime-dependent shapes; `Convert-Size` is one retained example. |
| Indexed mutation through an unsupported receiver | 5 target and 5 direct-return diagnostics | The current typed rule handles narrower arrays, lists, and dictionaries; `Get-ComputerPorts` is one retained example. |
| Subexpressions and nested script blocks | 10 diagnostics for each | Several PSScriptTools forms need explicit statement-output and closure rules. |
| Range operator `..` in the observed expression shape | 6 | PSScriptTools reached an unbound `DotDot` binary operator; other range shapes must be checked independently. |
| Labeled `break`/`continue` | 4 labeled-break diagnostics plus labeled-continue cases | CleanupMonster control transfer is not represented by the current typed loops. |
| Bare `throw` outside a supported catch and `throw` of a string | 3 PSSharedGoods throw-statement diagnostics plus PowerInfoblox examples | `New-PSRegistry` is one retained example. Preserve PowerShell error construction and continuation rather than treating bare `throw` as an ordinary CLR rethrow. |

**Ordinary command shapes also need attention.** The [module quickstart](PowerForge.PowerShellCompilation.ModuleConsumers.md) uses `Get-Date -Format yyyy`. A fresh one-function Hybrid build on both Windows hosts emits its complete CLR method with one hosted command region; the original and generated imports return the current integer year, and both honor a session function shadowing `Get-Date`. The earlier description of the whole function as retained was too broad. The new bounded intrinsic binds a literal `-Format` to a runtime-free string result only when command identity is safe: a canonical module-qualified call, or an unqualified call in a host-free Strict target without a local shadow. Dynamic formats, PowerShell's named `FileDate*` formats, other parameters, and an unqualified Hybrid call keep runtime resolution. The no-argument `Get-Date` rule remains. PSScriptTools also reports unqualified `Write-Debug`, `Write-Host`, `Write-Warning`, `New-Object`, and `ForEach-Object`; PSSharedGoods reports `Write-Host` and `Select-Object`; `Out-String` is hosted in PSScriptTools. These are observed invocation forms, not claims that every use of each named cmdlet is unsupported.

| Representative source shape | Result or effect to preserve | Current qualified route and remaining gap |
| --- | --- | --- |
| `Get-Date` with no arguments | One local `DateTime` value | Direct runtime-state binding with safe command identity; an unqualified Hybrid call remains session-resolved. |
| `Get-Date -Format yyyy` or a literal .NET pattern | One culture-formatted String | Direct binding with safe identity in this M29a slice; unqualified Hybrid invocation stays a hosted command inside an emitted method when the surrounding function is admissible. Named `FileDate*`, variable formats, and other parameter sets remain outside the rule. |
| PSScriptTools `[OutputType('Name')]`, multiple types, or `ParameterSetName` | Advisory `Get-Command` output metadata, not a value guarantee | A single non-whitespace literal string name now propagates on metadata-capable targets without becoming an inferred return type. Multiple names, repeated attributes, whitespace-only names, and parameter-set-specific declarations still need a richer or exact-preservation contract and remain hosted. The 66/60 diagnostic baseline above predates this partial implementation. |
| Unqualified `Write-Verbose` and related stream commands | Stream records, preferences, command lookup, error behavior | The host owns runtime resolution and stream routing. A reusable hosted command region may permit surrounding CLR code, but its presence is not direct translation of the command. |
| `ForEach-Object` or `Select-Object` with a script block or provider-bound input | Pipeline order/cardinality, `$_`, script-block scope, downstream streams | Bounded native forms need separate proof. Arbitrary unqualified pipeline commands and their session effects remain hosted. |
| Statement output captured into an assignment or return | PowerShell success-stream enumeration and no-output distinction | Narrow native capture forms exist, but 14 observed syntax diagnostics plus three enclosing returns remain outside that contract. |

The separate inspect-only external assessment reports **Locksmith 13/44** and **Locksmith2 21/100** complete emitted functions on `net10.0`, with no complete workload execution. Locksmith's unresolved authored AD parameter types must not be admitted by pretending they are CLR types in the generated artifact. Locksmith2 prominently hits unresolved `[OutputType]`, member calls in `catch`, and unavailable `ErrorRecord` reference shapes. `Test-IsADAdmin` and `Test-IssueExists` have documented hosted dispositions in the [assessment](PowerForge.PowerShellCompilation.Assessment.md); they are not safe targets for an AD execution probe. Provider calls such as `Get-AD*`, network requests, prompts, and installer operations remain explicit host/effect boundaries until separately qualified. The external assessment evidence SHA-256 is `5e409b06e136b463caa5dd1c44b47ca852cd2deee579653529e61d0a091ab682`.

For the next implementation selection, first split each retained function's co-blockers and identify a safe unchanged-source workflow. The clearest first repro is the formatted-date quickstart; the largest measured group is parameter metadata, but its 66 affected units are **not** 66 ready-to-compile functions. A support claim should name the exact form, mode, host, command-resolution assumption, and remaining PowerShell boundary, then show original/generated behavior and same-input no-loss results. The [M29a checklist](PowerForge.PowerShellCompilation.NextMilestones.md#milestone-29a--make-ordinary-powershell-forms-useful) defines that proof without requiring the broad artifact matrix on every edit.
