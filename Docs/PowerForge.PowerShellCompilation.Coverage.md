# PowerShell compiler measurements: 2026-11-20

These results describe two fixed external packets on Windows x64 with SDK
10.0.404. The public module probes, external assessment runner, workload
observers and semantic oracle use the pinned PowerShell 7.6.6 package.
Applicable oracle cases also run on Windows PowerShell 5.1.

An emitted unit is a complete CLR method in the final output. A smaller compiled
region inside retained source does not count as another emitted unit. An
emitted method can call native PowerShell operations or pass an authored
script block to a native callback. PowerShell also owns dynamic parameter
discovery.

| Packet | Complete emitted units | Complete emitted functions | Execution proof |
| --- | ---: | ---: | --- |
| Ten public Gallery modules | 753/753 (100%) | Not measured separately | 10/10 Hybrid artifacts built, imported, and passed an invocation probe |
| Seven external workloads | 183/196 (93.37%) | 183/183 (100%) | 7/7 complete workloads passed. The packet covers 183 owned functions, one script root and four nested helpers in 34 scenarios on original, built, and installed surfaces. |

The external assessment discovers 155 source files, with zero parser errors and
zero acquisition or analysis failures. The previous increase of 23 complete
methods came from Classic Locksmith, including the complete `Set-RiskRating`
method. The Gallery packet records 553 runtime-routed units, zero retained
source units, and zero promoted regions. Its probes exercise the generated
Hybrid modules in PowerShell.

The generated [public baseline](../Benchmarks/PowerShellCompilation/Corpus/public-corpus-baseline.net10.json)
and [external baseline](../Benchmarks/PowerShellCompilation/Corpus/external-assessment-baseline.net10.json)
pin the packet hashes and counts. The September Gallery snapshot discovered 735 units. The current
compiler discovers 753 for the same packet, so each ratio uses its own census
denominator. Earlier documents and `m29a-*` files preserve development results. In particular, the five-module **545/565**
result from 2026-10-03 is a separate targeted maintainer packet and has not been
rerun here. It must not replace either external measurement above.

The same 753-unit packet rises from **114/753 (15.14%)** at the starting
measurement and **720/753 (95.62%)** at the previous refresh to **753/753
(100%)**. All six programs in the companion Strict packet also pass.

| Module | Starting measurement | Previous refresh | Current refresh |
| --- | ---: | ---: | ---: |
| ImportExcel | 0/2 | 0/2 | 2/2 |
| InvokeBuild | 0/1 | 0/1 | 1/1 |
| Microsoft.PowerShell.Archive | 19/21 | 19/21 | 21/21 |
| Pester | 0/491 | 478/491 | 491/491 |
| platyPS | 42/43 | 42/43 | 43/43 |
| Pode | 0/1 | 0/1 | 1/1 |
| powershell-yaml | 14/15 | 14/15 | 15/15 |
| PSFramework | 38/176 | 166/176 | 176/176 |
| PSScriptAnalyzer | 1/2 | 1/2 | 2/2 |
| ThreadJob | 0/1 | 0/1 | 1/1 |
| **Total** | **114/753** | **720/753** | **753/753** |

A separate three-test Pester run also passes on the pinned PowerShell 7.6.6 host
against both the original and generated modules: three passed, zero failed, and
zero skipped on each surface. It exercises pipeline assertions, object-property
assertions, and an expected throwing script block. It does not execute every
emitted Pester method or replace the fixed packet's invocation probes.

## Strict streaming executable proof

Three `net10.0` programs that Strict previously rejected now build as complete
runtime-free executables. Their original scripts and generated executables
run through the pinned semantic oracle.

| Complete Strict programs | Before | Current |
| --- | ---: | ---: |
| Filter and map, take with observable upstream stopping, and filter into a local advanced reducer | 0/3 | 3/3 |

The stages use CLR records and loops. The take stage stops inside the record
loop. The advanced reducer receives the retained records through the existing
typed collection binding after pure filters. PowerShell 7.6.6 and Windows
PowerShell 5.1 each pass 29 typed output comparisons, one terminating-process
comparison, and seven checks for shapes that Strict must reject, with zero
failures and zero skips. The comparisons cover record types and order, empty
and null input, early stopping, and `begin`, `process` and `end` execution.
The generated artifacts verify the runtime-free dependency closure. The
original six Strict programs continue to pass.
[Semantic oracle tests](../PowerForge.Tests/PowerShellCompilationSemanticStrictStreamingOracleTests.cs).

## Strict NativeAOT data-processing scripts

The starting compiler rejected all eight candidate scripts in the public
source survey. `Read-Host` was the first diagnostic in six. The frozen
selection uses three unchanged CC0 scripts at the same upstream revision.
Each builds and runs as a Strict NativeAOT executable on Windows x64 and
Linux x64 with a verified runtime-free dependency closure.

| Unchanged script | Starting Strict build | Observed native data path | Windows x64 bytes | Linux x64 bytes |
| --- | --- | --- | ---: | ---: |
| `list-files.ps1` | Rejected | Recursive filesystem records and ordered `FullName` projection | 3,140,608 | 3,248,408 |
| `convert-csv2txt.ps1` | Rejected | Explicit-header CSV reads and ordered row interpolation | 3,104,256 | 3,174,120 |
| `list-empty-files.ps1` | Rejected | Filesystem working-directory default, empty-file selection and counts | 3,054,080 | 3,178,368 |

The Windows builds match 30/30 executable observations against PowerShell
7.6.6 and Windows PowerShell 5.1.26100.9818 with UTF-8 reference console
output. Strict console tables display full path values. PowerShell can
shorten displayed paths to fit the console, and the oracles verify the
complete values through structured records. The comparisons cover ordered
file records, quoted CSV fields, redirected input, counted outputs, exit
status and selected failures. They
normalize the measured elapsed seconds and the layout of the nonterminating
missing-path diagnostic. The default Windows reference console can replace
an authored warning glyph under its active code page. All three Linux
executables run in an isolated filesystem containing the program images,
native OS libraries and test data. The Windows executables also run with
.NET lookup restricted to an empty directory. A framework-dependent control
fails to locate .NET in the same environment. A second uncached build on
each platform reproduces all six executable hashes.

The pinned oracles also check partial output after a terminating error and
stopping in the middle of a local advanced function. PowerShell 7.6.6 covers
`clean`, including early stop, discarded cleanup output, and locally handled
failures. Strict requires proven-total cleanup or an exhaustive local catch
around fallible operations.
[Upstream source revision](https://github.com/fleschutz/PowerShell/tree/50082bcec78864a42527a4291b34d4af7fdb8be9/scripts).
[Pinned oracle tests](../PowerForge.Tests/PowerShellCompilationSemanticStrictStreamingOracleTests.cs).

## Additional published Gallery packages

Eight additional version-pinned Gallery packages were measured with the Hybrid
`net10.0` post-emission census on Windows x64. The baseline compiler at
`833ca4cd6050a30249fcfc3dc6313c6e80083e2f` completed four package censuses.
The compiler now completes all eight, with 709 of 736 units emitted. The
packages were read from the original Gallery archives.

| Gallery package | Version | Baseline emitted units | Current emitted units |
| --- | --- | ---: | ---: |
| PSWriteColor | 1.0.7 | Input rejected | 2/2 |
| PSWriteHTML | 1.45.0 | Input rejected | 264/274 |
| PSSharedGoods | 0.0.313 | Input rejected | 280/289 |
| PowerInfoblox | 1.0.42 | Input rejected | 89/89 |
| PSWordPress | 0.0.3 | 17/17 | 17/17 |
| PSLansweeper | 0.0.3 | 2/2 | 2/2 |
| PSBlackListChecker | 0.8.7 | 12/14 | 12/14 |
| PasswordSolution | 2.1.0 | 43/49 | 43/49 |

The four input rejections came from export lists assigned as literals immediately
before `Export-ModuleMember`. Hybrid keeps these calls in the module so PowerShell
binds their actual values, including any existing variable constraints. These counts describe the additional packages and are
independent of the ten-module Gallery and seven-workload baselines.

Three offline scenarios matched between the original and Hybrid artifacts on
PowerShell 7.6.6 (`net10.0`) and Windows PowerShell 5.1.26100.9818
(`net472`). All six comparisons checked the public command list and passed
without skips.

| Package | Observed behavior |
| --- | --- |
| PSWriteColor | `Write-Colour` resolves to `Write-Color`. Text output, host color records and the UTF-16 log match. The tested function body has an emitted CLR method. |
| PSWordPress | `Connect-WordPress` and `Get-WordPressPost` match for two explicitly requested pages from a loopback server. The comparisons cover the GET path, query values, synthetic authorization and returned post data. Both entry points have emitted CLR bodies. |
| PSWriteHTML | `New-HTML` and `New-HTMLTable` produce the same report title, headers, escaped cell content and embedded-asset structure. Both entry points have emitted CLR bodies. |

## Retained standalone console applications

Unchanged `convert-csv2txt`, `check-xml-file` and `get-sha256` from the
independent packet built as reviewed Package executables. Each ran with a
path argument, the same path supplied through standard input, and a failure
input. All **9/9** comparisons matched standard output, standard error and
process exit code against PowerShell 7.6.6. These entries run their authored
source in the embedded engine.

The pinned PowerShell 7.6.6 and Windows PowerShell 5.1.26100.9818 oracle also
covers native root exits in `try`, `catch`, functions and nested expressions,
`finally` output, a trap scope, a native child's nonzero exit, plain redirected
input, empty lines and end-of-input. Package and Hybrid both pass those cases.

## Independent script and module assessment

A separate packet pins 12 Gallery modules and eight standalone scripts from
the Gallery and a public Git revision. On PowerShell 7.6.6 the starting census
completed for **15/20 inputs**, with **150/192 units emitted**. Support for
a top-level export call splatted from an immediately preceding literal
hashtable increased this to **17/20 inputs** and **220/284 units emitted**.
PoshRSJob contributed **1/2** and posh-git **69/90**. The packet also counted
three promoted regions in posh-git. Letting PowerShell bind exports derived
from the same file list used by a conventional module loader adds PSDepend
with **38/43 units emitted**. The packet now completes for **18/20 inputs**
with **258/327 units emitted**. PSProfiler selects a dot-sourced file through a
runtime switch. SimplySql has a binary module as its manifest root.

On PowerShell 7.6.6 and Windows PowerShell 5.1.26100.9818, posh-git returned
matching branch, untracked-file, repository, and export information for a
local Git fixture. PoshRSJob matched the exported commands, job completion,
and output of a start, wait, receive, and remove sequence using the
`CompleteModule` resource mode. PSDepend's exported functions and
`Get-PSDependType -DependencyType PSGalleryModule -SkipHelp` results matched
under the same resource mode. All six original and Hybrid comparisons passed
without skips.

The same change admits static references to PowerShell's `ActionPreference`
enum for hosted compilation. The unchanged PSWriteColor 1.0.7 improved from
**0/2** just after literal named exports were enabled to **2/2**. The other
seven Gallery packages in that packet kept their previous counts.

The switch-selected load assessment completed **18/20 inputs** with **221/287**
units emitted. PSProfiler 1.0.5 passes reviewed Hybrid builds using the
`CompleteModule` resource mode. It emits **1/3 units** on both targets. Original
and generated modules agree on the exported commands, four profiling rows,
the number of observations per row, and the executed script result on
PowerShell 7.6.6 and Windows PowerShell 5.1.26100.9818. The two visitor class
files are staged as runtime scripts. PowerShell selects and loads the matching
class in the module scope.

## Why these forms were selected

At 720/753, the remaining Gallery units comprised 13 in Pester, ten in
PSFramework, two each in ImportExcel and Microsoft.PowerShell.Archive, and
one in each of the other six modules. The Pester explanations centered on
command availability across load boundaries, a diagnostic on 12 units.
PSFramework had unsupported member invocation diagnostics on eight units,
including command-tree code that uses types created during module
initialization. Classic Locksmith retained 23 of 44 owned functions, with
repeated uses of declared ActiveDirectory types and local data classes.
Those type sources were the first targets for closing the last
complete-workload gap.

The final 33 Gallery units use the same rules across modules. Functions become
available when PowerShell reaches their authored declarations, including
declarations in separately loaded source files. A qualifying single-file module
can emit its startup as a complete method. PowerShell keeps import arguments,
the importing scope, authored class initialization, dynamic parameter discovery
and native variable storage. Literal module type loads and locked class
resources allow the native binder to resolve types at invocation. The compiler
also admits selected captured statements, scoped member and index updates,
and caller-loop transfers.

The starting census reproduced 114/753 complete methods. Pester accounted for
491 units and emitted none. Its document-wide `source.type-definition`
diagnostic affected all 491 units and was the sole visible blocker for 331.
PSFramework emitted 38/176; its overlapping type, syntax, and parameter-metadata
boundaries affected 68, 53, and 26 units respectively. The larger first gain was
therefore independent functions sharing a file with a hosted type, rather than
another isolated operator.

Removing that file-wide blocker exposed a shaping boundary: early command
discovery kept 475 Pester units hosted, with `function.graph` the sole remaining
visible blocker for 320. Native-bound Hybrid functions install at their authored
declaration statement, so those methods can preserve discovery timing without
eager command registration. Duplicate names, typed cmdlet hoisting, executable
entry rules, dependent authored types, and unsupported function metadata retain
their existing guards. These two frontier counts are sequential and overlap;
they are not additive coverage gains.

At the 436/753 measurement, Pester retained 169 units and PSFramework retained
138: together, 307 of the packet's 317 retained units. PSFramework's overlapping
parameter-type, syntax and parameter-metadata diagnostics affected 68, 53 and 26
units; they were the sole visible blockers for 55, 41 and 23 respectively.
The next slice therefore admits types from delivered, authenticated managed
dependencies and the selected PowerShell syntax-tree and completion types.
Those changes add 80 Pester methods and 84 PSFramework methods, reaching
600/753. Native caller transfer and exit admission add one further Pester
method, reaching the 601/753 checkpoint.

At that checkpoint, Pester's 88 retained units and PSFramework's 54 accounted for
142 of the packet's 152 retained units. Same-input explanations identified 40
Pester units with the selected SDK-object, parameter-metadata or syntax-helper
diagnostics and 19 with script-block placement diagnostics. PSFramework had 24
units with unresolved delivered type aliases, ten with the selected SDK forms
and two with callback placement diagnostics. These affected-unit groups overlap;
they are not additive predictions of complete-method gains. Pester also had five
eligible units lost during shaping.

The next slice therefore admits borrowed invocation objects, additional SDK AST
nodes and parameter metadata, returned and collected script-block values, and
literal aliases registered for authenticated delivered types. Native binding
requirements propagate to local callers as well as callees. The complete packet
adds 57 Pester and 24 PSFramework methods: **601/753 → 682/753**, with all ten
probes passing. The remaining 71 units comprise 31 Pester, 30 PSFramework and ten
in the other eight modules. The rules use syntax, SDK identity and locked
dependency metadata; module names are never compiler eligibility inputs.

At 682/753, Pester and PSFramework accounted for 61 of the 71 retained units.
The first interop measurement reached 707/753, including 17 complete
PSFramework methods admitted by closed generic dependency types and native
static storage. The full refresh reached **720/753**, with 18 more Pester
methods and 20 more PSFramework methods than at 682/753. All ten invocation
probes passed. The largest remaining groups at this measurement were 13 units
in Pester and ten in PSFramework. The refreshed artifact ledger records 2,719
hosted command regions, up from 2,536. Complete methods can contain these regions.

The Gallery runner builds with CompleteModule resources. At the type-admission
checkpoint, Pester's default Declared-resource census emits 333/491; its complete
artifact delivers both managed compatibility variants and emits 402/491. The
complete artifact at that checkpoint emits 403/491 after caller transfer and exit admission.
Same-identity variants leave
type selection to the authored loader and native binder within a non-conflicting
dependency graph. Distinct assembly owners
of the same type name remain ambiguous. The compiler reads managed metadata
without loading dependency code, and binding still depends on the actual module
initialization and host type identity. SDK syntax-tree parameters retain their
borrowed AST identity, parent and source extent. These forms require Hybrid
native binding and do not broaden Strict.

The emitted bodies use the existing PowerShell command and binding implementation for
pipeline stages, cmdlet invocation, advanced parameters, and
`begin`/`process`/`end`. A pinned 7.6.6 and installed 5.1 oracle compares original
and generated modules for zero/one/many records, by-value and by-property binding,
aliases and abbreviated names, validation errors and continuation, verbose
records, downstream stop and finally, repeated invocation, early discovery, and
retained class identity. General Strict pipelines and runtime-free authored
classes remain unsupported.

The external execution choices follow the complete-function census: the GitHub
helpers already emit 10/10 functions and the PowerShell installer 4/4; Scoop
emits 23/24 before its exit helper becomes complete. Their contracts qualify
actual entries and delivered artifacts. Those first three workflows exercise
all 38 of their owned functions.

## Independent module workflow verification

Four unmodified modules from the independent packet pass eight workflow
comparisons with zero skips on PowerShell 7.6.6 and Windows PowerShell
5.1.26100.9818. The runner builds each module in Hybrid mode with
`CompleteModule` resources and a reviewed dependency lock. It compares the
exported command list and the workflow result in separate PowerShell
processes, with the input directory withdrawn for generated-module invocation.

| Gallery package | Version | Emitted units on each target | Compared workflow |
| --- | --- | ---: | --- |
| PSFolderSize | 1.7.3 | 4/4 | Directory names, byte totals, file counts and output type for two fixture directories |
| PowerShellHumanizer | 3.2.0 | 12/12 | Piped Roman numeral conversions and an ordinal from packaged PowerShell type data |
| PSIni | 4.0.1 | 6/8 | INI file content, parsed settings and missing-file error identity |
| Plaster | 2.1.5 | 49/58 | Manifest validation and generation of a named file from a parameterized template |

The artifact ledger confirms complete emitted bodies for the exercised
commands, except `Import-Ini`, which remains in PowerShell. These probes
exercise named workflows and leave the separate seven-workload packet and
per-unit census baselines unchanged.
[Run the module workflow matrix](../Benchmarks/PowerShellCompilation/Corpus/README.md#verify-independent-module-workflows).

## Reproduce the measurements

Build the current CLI and choose a disposable scratch directory with enough room
for verified downloads and generated artifacts:

```powershell
dotnet build ./PowerForge.Cli/PowerForge.Cli.csproj -c Release -f net10.0
$cli = (Resolve-Path ./PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll).Path
$scratch = Join-Path ([IO.Path]::GetTempPath()) 'PowerForgeCoverage'
./Benchmarks/PowerShellCompilation/Corpus/Invoke-PublicCorpus.ps1 `
    -CliAssemblyPath $cli -WorkspacePath "$scratch/public" `
    -EvidencePath "$scratch/public.json" -PerformancePolicy RecordOnly -RefreshBaseline
./Benchmarks/PowerShellCompilation/Corpus/Invoke-ExternalAssessment.ps1 `
    -CliAssemblyPath $cli -WorkspacePath "$scratch/external" `
    -EvidencePath "$scratch/external.json" -ExecuteWorkloads -RefreshBaseline
```

Run external execution in PowerShell 7.6.6 with the Windows module and lab
prerequisites in the [corpus README](../Benchmarks/PowerShellCompilation/Corpus/README.md).

Both commands acquire exact hashes from the checked-in packets. Public module
probes import generated Hybrid artifacts in clean child processes and invoke
their checked-in probes. Those artifacts can execute retained PowerShell source;
the ten probes do not separately compare original modules. External assessment
with `-ExecuteWorkloads` runs the reviewed complete-workload contracts; without
that option it performs acquisition and census only. `-RefreshBaseline` deliberately
records new measured counts. Omit it for comparison against the committed counts.
The public refresh requires the complete module packet on the baseline host.
It keeps the comparison to the previous Hybrid results and saves the run report
before replacing the Hybrid baseline. The Strict timing budgets remain in place.
Execution mode creates fresh projects and requires online package restore;
verified source payloads alone do not supply that environment. It cannot be
combined with `-Offline`. Census-only assessment can use a verified offline cache.

The unchanged ten-function GitHub helper library passes ten scenarios covering
SHA512 hashing, logging, invocation redaction,
deep copying, temporary and reusable directories, noninteractive output, path
resolution, an HTTP response, and error-record creation. Configuration and time
are fixed in the importing module; HTTP uses an owned loopback server. All ten
functions have complete CLR bodies and execute through Analyze/Lock/Restore/
Build/Test/Pack/Install, with the compiler input withdrawn before observing the
built and installed artifacts.

Two unchanged installer inputs add complete application contracts through module
adapters. The PowerShell installer executes all four functions across portable
installation, isolated user/machine PATH registration and overwrite protection;
the installed 7.6.6 executable is launched and its bytes verified. Scoop executes
all 24 functions across installation from pinned Scoop/Main archives, file-lock
behavior and controlled failure. The observers invoke the delivered roots through
native script-block module scopes: advanced-script binding for PowerShell and
Scoop's documented downstream entry mode. They create actual repositories,
configuration and shims in owned directories. PowerShell and CMD shim dispatch
are checked with offline help; the PowerShell shim also preserves a failing
command's exit code. The Unix shim is created and target-checked, with no Unix
execution claim.

The installer contracts use exact content-pinned ZIP payloads over an owned
loopback server, real filesystem/process behavior and process-local replacement
registry hives. Their three scenarios each run on Original/Built/Installed with
compiler input withdrawn for artifact observation, through the same canonical
project lifecycle. These results do not qualify live GitHub operations, arbitrary
installer branches, MSI/global installations, direct executable packaging of
the installers, or runtime-free execution.

GetBIOS adds six scenarios across all four vendor branches, remote-provider
dispatch and a controlled provider failure. Its unchanged public command writes
actual CSV and HTML reports. The artifact ledger and PE metadata require all
four exact nested vendor-function extents to map to complete generated methods.
The fixture preserves the pinned source's missing HP/Lenovo values and duplicate
remote output. WMI, Dell provider paths, privilege membership, credentials,
remoting and UI are process-local test boundaries; no real BIOS, elevated token
or remote machine is qualified. Preserving its legacy `ModuleToProcess` manifest
key fixes a generated-import failure without rewriting the external source.

Autopilot adds four scenarios for two-record pipeline input, actual CSV creation
and append, partner fallback, and missing-data failure followed by recovery.
The explicit `net10.0` Windows x64 executable delivers a completely compiled
advanced script root with native `begin`/`process`/`end`. Observers call its public
native-entry factory in the pinned host, using the source bytes embedded in the
delivered assembly and withdrawing the compiler input. Console execution proves
startup and native help; the CIM scenarios run through that delivered factory.
Controlled sessions and device records bound the application proof. Live Graph
registration, group assignment, remote inventory and reboot remain unqualified.

Locksmith2 emits **100/100 functions**, up from 79/100, and is the sixth
complete workload. Five scenarios match across the original, built and
installed modules on pinned PowerShell 7.6.6. The read-only Auditing scenario
loads directory objects and reaches 59 owned functions. Local scenarios cover
console and disposable profile behavior, environment and binding, report
handling, and in-memory issue and searcher behavior. Together they observe all
100 owned functions through active PowerShell source or CLR method frames and
direct value and state checks. Artifact observation withdraws the compiler input and
verifies the installed manifest against the build receipt. The audit bypasses
environment repair and Gallery version checks.

Classic Locksmith emits **44/44 functions**, up from 21/44. Native Hybrid
binding now supports the local data classes and declared Windows module
types used by these functions. The selected scenarios use the installed
ActiveDirectory module for native in-memory objects, fixture responses for
directory queries, and disposable local state. The public read-only audit and
the incomplete-directory-input case compare command order, output, and errors.
Local checks cover record construction, reporting, and proposal text in memory.
Across three scenarios on each of the original, built, and installed surfaces,
the observer verifies 42 active function frames and checks direct calls to the
other two functions. The compiled and installed observations require complete
method bodies and withdraw the compiler input. The scenarios run on PowerShell
7.6.6. A separate probe verifies the identity of in-memory ActiveDirectory
objects and the locked assembly on both PowerShell 7.6.6 and Windows
PowerShell 5.1.

The default external census emits 183/196 units, including 44/45 for Classic
Locksmith and 100/108 for Locksmith2. Autopilot remains 0/1 in that census
because it has no explicit executable runtime contract. The qualified Windows
x64 executable requires the complete script-root method in its artifact ledger.
All seven attempted workloads pass. Every previously emitted function retains
its complete method on the same inputs. The Locksmith2 audit requires an
explicit approved Windows lab target.

Earlier pinned oracle cases cover returned/collected callback scope and closures;
borrowed SDK identity, mutation and dynamic binding; native local-call closure;
delivered alias registration, missing-alias failure, restoration, typed
reassignment, lifecycle, stopping and recovery; script-root lifecycle; and legacy
manifest-key import. Applicable forms run against both PowerShell 7.6.6 and
Windows PowerShell 5.1. A separate public executable check preserves retained
ownership for returned or collected blocks that require a defining module.
That focused suite passed **266/266 cases, with zero failures and zero
skips**, including the seven new semantic/artifact cases and the binder
regressions. The shared engine builds for `net472`, `net8.0` and `net10.0` with
zero warnings and errors. Three artifact oracle cases rerun against the preceding compiler all fail at
their complete-emission assertions, with zero skips, establishing that the new
proof detects the previously unsupported forms.

The earlier interop oracles compare both pinned hosts for closed delivered
generic metadata, borrowed directory and SDK objects, array-record boundaries
in multiple assignment, wildcard and exact switch labels, nested scoped writes,
reference conversions, repeated declarations and module loading requirements.
The class constructor case compares results, error identity and source position
when a locked class is also loaded by the module's contained source loader.

That Fast compiler contract gate passed **567/567 tests**. Its pinned semantic
oracle report recorded **155 passed, zero failed and zero skipped**, including
the seven required semantic and artifact families at that checkpoint.

Closed constant Boolean delegate values retain their typed conversion before
general module callback-value binding. The two-host SDK-object oracle verifies
delegate installation, invocation and restoration. The legacy compiler's full
`net472` smoke suite passes **28/28 tests with zero skips**.
The same conversion admits Locksmith2's `Get-WebEnrollmentEndpointStatus` as a
complete method, superseding its earlier partial typed region.
Other supported delegate callbacks retain native conversion. The existing
Action/Predicate/Func artifact oracle fails all three complete-emission checks
against an unconditional constant-delegate preference and passes after that
preference is restricted to successful constant conversions. This three-case
family is required by the normal Windows compiler check.

Explicit target contracts previously admitted executable roots whose compiled
callbacks require a defining module. A pre-correction artifact reports a complete root
but emits a new module-ownership error while creating a collected callback.
The public-build regression requires retained ownership and native output
parity for returned and dictionary-held blocks, named callback functions, switch
predicates and nested declarations in an executable. An independent scalar
lifecycle function and nested declarations still emit and execute. The binder
admits compiled script-block values and switch predicates only when the host
capability contract supplies a defining module.
This preserves the supported module-function callback forms.

The two-host type oracles compare original and emitted functions for borrowed
Windows module types, lexical class identity and initialization, validation
failures and recovery, arrays, overload selection, and static access after an
admitted same-block `Add-Type` load. They also cover converted delegate
assignment and the native error for a missing member on an executed branch.
The returned-callback case invokes the block after the declaring call exits
and checks that its lexical type still accepts the matching record and rejects
an unrelated object.
Six two-host oracle cases and one assembly architecture check passed with
zero skips. The Windows PowerShell compiler build passed for `net472`. The
semantic and performance measurements below retain their earlier revision and
scope.

The preceding type/application checkpoint's focused suite passed 29 cases with zero failures and zero skips.
Its semantic cases compare original and generated artifacts on the pinned
7.6.6 and installed 5.1 profiles for delivered type identity, native conversion
and validation, lazy attributes, shared state, borrowed syntax trees, completion
enums, compatibility variants, missing initialization and recovery. Caller
transfer cases cover native and compiled loops, begin/process/end partial output,
finally, calls outside a loop and subsequent invocation. Exit cases cover ten
value/computed-operand forms through a real child script, preserving native
conversion, output, catch bypass, cleanup, script termination and host recovery.
An integer-output function also preserves its normal return and native exit path.
Project acceptance cases additionally compare three source encodings with native
file execution on both hosts and reject changed script bytes and schema-1
acceptance declarations. A failed acceptance attempt invalidates the prior pass,
so packing and installation require a new successful test. These checks preserve
the distinct retained boundary for switch predicates that transfer outside their
own bodies.

## Semantic and performance limits

Windows compiler CI downloads and verifies the pinned PowerShell **7.6.6** archive,
sets `POWERFORGE_PWSH76_PATH` and
`POWERFORGE_REQUIRE_PINNED_SEMANTIC_HOSTS=true`, and selects the semantic oracle in
the normal compiler tests. The report records pass/fail/skip counts and refuses
skips. Local verification executed all **27 pinned tests with 27 passed and zero
skipped**; the complete selected suite passed **158 tests with zero skips**. The exact executable bytes and version remain pinned; the OS build belongs
to the runner. Windows PowerShell 5.1 is tested as the installed supported profile,
with its observed identity recorded. Its historical Windows component pin is not
a provisionable package for current hosted runner images.

All six small Strict programs built and passed on Windows x64 in this run. The [user guide](PowerForge.PowerShellCompilation.Guide.md)
contains two additional small Strict examples built and executed for this change.

The Strict startup measurements now include committed raw samples and runtime
locations. On Ubuntu 24.04 under WSL2, with both the executable and .NET on ext4,
the 2026-11-17 framework-dependent run measured **25 ms p50 / 26 ms p95**. The
2026-11-18 run measured **32/35 ms**, versus **242/278 ms** for source, using
SDK 10.0.403 and .NET 10.0.14. Both passed the existing **100/250 ms** budgets.
The later Windows x64 run measured **168/221 ms**, versus **757/1,570 ms** for
source, with .NET 10.0.15. It records source-host identity before timing and
checks it afterward; its existing startup budgets passed. The earlier
**79/85 ms** Windows samples remain in the index with their source-identity limit.
The separate Linux NativeAOT comparison measured **3.878/4.370 ms** across 48
launches. These are measurements of the small Strict application, with warmed OS
caches, on one development host; they do not predict arbitrary module performance.
See the [startup records](../Benchmarks/PowerShellCompilation/Corpus/Performance/startup-evidence.json)
for exact commits, hosts, deployment details and every sample.

The [2026-10-16 Linux result](../Benchmarks/PowerShellCompilation/Corpus/m29a-whole-workflow-qualification.json)
is retained as a superseded startup measurement: its **346/1,506 ms** result
loaded .NET from a Windows-mounted **9p** filesystem even though the executable
was on ext4. A controlled comparison of identical runtime files measured
**273.444 ms p50** on 9p and **24.571 ms** on ext4. It establishes the runtime
location cost without reproducing the original tail exactly. The
[raw comparison](../Benchmarks/PowerShellCompilation/Corpus/Performance/2026-11-17-runtime-location-ab.json)
preserves both sets of samples. The timing runner now checks the resolved Linux
CoreCLR and the other existing runtime-library paths resolved in the host trace
with `findmnt`. It refuses 9p/drvfs, missing CoreCLR or missing mount evidence
before measuring startup, including with `-PerformancePolicy RecordOnly`.

The [Gallery performance comparison](performance/powershell-compiler.md#hybrid-modules-versus-source)
records separate first-import, reimport, first-invocation and loaded-module
invocation measurements for all ten modules. Hybrid first-import p50 is slower
in **10 of 10 modules**, and reimport p50 is slower in **8 of 10**. All declared
probes pass and the export counts match, but these results do not establish a
general Hybrid speedup. The runtime now reuses parsed assignment documents and
command-region plans within their defining PowerShell context. A separate,
alternating revision comparison measured **2.59x** and **4.45x** lower loaded-call
p50 for YAML and Archive after command-plan reuse. Both still trail source;
remaining assignment/storage preparation has samples and profiles recorded for
the next performance milestone in the linked report.
