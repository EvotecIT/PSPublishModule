# PowerForge Benchmark Standard

This is the standard every project follows when it measures its libraries against
others and publishes the results. A benchmark that follows it looks and behaves
the same in every repository, and its numbers mean what they say: the libraries
did the same work, the work covers the realistic range of inputs, every library
had the same chance, the statistics are honest, and nothing is published that a
licence does not allow.

PowerForge enforces what a machine can check (section 10). The rest is checked by
the author and the reviewer with the checklist in section 11.

## 1. Terms

| Term | Meaning |
| --- | --- |
| Library | One implementation being measured, such as `OfficeIMO`, `ClosedXML` or `EPPlus`. It has an id, a package, a pinned version and a licence class. |
| Comparison | One unit of equivalent work, such as "read a typed XLSX sheet". Its id never contains a framework or affinity: `xlsx-read-typed`, not `xlsx-read-typed-net10.0`. |
| Axis | One dimension of a comparison's scenario matrix, such as `rows = 1000, 100000, 1000000`. |
| Case | One point of the matrix, as the benchmark code names it. |
| Cell | One measurement: comparison × case × operation × library × parameters. Every cell has a status, and failed cells keep their reason. |
| Lane | One run of a comparison on one platform, framework and profile, plus lane parameters such as processor affinity. Lanes are never merged into one number. |
| Profile | `diagnostic` (exploration, never published), `standard` (any runner that meets the policy) or `full` (complete BenchmarkDotNet run). |

## 2. Declarations come from the benchmark code

A project keeps one declaration document (schema 5) with two lists: the library
policy and the comparisons.

```json
{
  "schemaVersion": 5,
  "libraries": [
    { "id": "OfficeIMO", "package": "OfficeIMO.Excel", "version": "1.4.2", "license": "MIT", "restriction": "open" },
    { "id": "ClosedXML", "package": "ClosedXML", "version": "0.105.0", "license": "MIT", "restriction": "open" },
    { "id": "EPPlus", "package": "EPPlus", "version": "8.2.1", "license": "LicenseRef-PolyForm-Noncommercial", "licenseUrl": "https://polyformproject.org/licenses/noncommercial/1.0.0/", "restriction": "acceptance-required", "reason": "Needs an explicit licence context before it runs." }
  ],
  "comparisons": [
    {
      "comparisonId": "xlsx-read-typed",
      "area": "excel",
      "group": "read",
      "title": "Read a typed worksheet",
      "subject": "OfficeIMO",
      "libraries": ["OfficeIMO", "ClosedXML", "EPPlus"],
      "axes": [
        { "name": "Rows", "values": ["1000", "100000", "1000000"] },
        { "name": "Shape", "values": ["narrow", "wide", "sparse"] },
        { "name": "Mode", "values": ["dom", "streaming"] }
      ],
      "cases": ["ReadTyped"],
      "expectedPlatforms": ["windows", "linux", "macos"],
      "frameworks": ["net8.0", "net10.0"]
    }
  ]
}
```

The library entries carry exactly the fields a project's library policy needs:
`id`, `package`, `version` (one pinned version, never a range), `license` (SPDX
expression or `LicenseRef-*`), optional `licenseUrl`, `restriction` and `reason`.

**Never maintain the case list by hand next to the code.** A hand-written list
drifts: a new `OnePdfLayout` case with `Scale=Easy|Medium|High` appears in the code,
nobody adds it to the list, and the comparison silently stops publishing. Instead:

- Tag every BenchmarkDotNet method with its library and the shared case it measures:
  `[BenchmarkCategory("library:ClosedXML", "case:ReadTyped")]`. The importer reads
  those categories, so no name matching is needed, and methods of different
  libraries that measure the same case line up as one workload. A project that
  cannot use categories passes explicit maps (`Import-BenchmarkResult -LibraryMap … -CaseMap …`).
  When a library map is given, an unmapped method fails the import. Without a map or
  categories, rows without a library are imported as they are and then reported as
  drift (`missing-library-identity`) by `Test-BenchmarkDeclaration` and at publication.
  Two methods of one library that resolve to the same cell fail the catalog update.
- Generate the `cases`, `axes`, `libraries` and, where not every combination is
  valid, `cells` of each comparison from the benchmark code (for example a `--list-declarations` switch in the benchmark host, or a small
  generator over the benchmark attributes) and check the generated file in.
- In CI, regenerate and compare, then run `Test-BenchmarkDeclaration` on the
  document. Run it again with a short diagnostic result
  (`Import-BenchmarkResult ... | Test-BenchmarkDeclaration -Path ... -ComparisonId ...`)
  before a long run, so drift fails in minutes, not after hours of measuring.

Custom and PowerShell runners write the library id on every row themselves.
`Invoke-BenchmarkSuite` records each engine as the row's library.

## 3. Equivalence: the same work on the same input

- Every library in a comparison does the same work on the same input file or data.
- Differences in features and defaults are either normalised (same culture, same
  number formats, same validation level, same output target) or declared in the
  comparison title or an axis. "OfficeIMO with formula evaluation vs X without" is
  two comparisons or one axis, never one hidden difference.
- **Validate every library's output before timing it.** Check the produced data
  against the expected result (row count, checksum, extracted text, round-tripped
  values) in setup or in a validation pass on the same input. Record the outcome
  per library (and per case where it differs) as an output validation:
  `{ "library": "ClosedXML", "case": "ReadTyped", "status": "passed", "detail": "rows=100000 sha256=…" }`.
  A cell without a passed validation is never published.
- Output that cannot be validated is not a comparison. Keep it as a diagnostic run.

## 4. Breadth: cover the realistic workload space

One headline case is a demo, not evidence. A comparison declares its scenario
matrix, and an area is expected to cover the workloads people actually run:

- sizes from small to large (rows, pages, files, nesting depth);
- data shapes (narrow, wide, sparse, many styles, formulas, images, unicode);
- file types and variants the library claims to support;
- read, write, edit and round-trip;
- streaming and in-memory models where both exist;
- first-call (cold) and steady-state costs where they differ for users.

Wide matrices are the normal case: an Excel area with a few hundred cells across
row counts, shapes and modes is expected, not exceptional. The catalog, the cells
and the website views are built for that size. Never drop the cases a library
loses; they stay in the matrix and in the published evidence.

## 5. Fairness: every library gets the same chance

- Same machine, same build configuration, same runtime and the same process
  isolation for every library in a lane.
- Interleave or rotate the order of libraries and cases so drift (thermal, power,
  background work) does not favour whoever runs first.
- Warm up every cell (at least the policy's warmup count) before measuring.
- Use affinity and priority only as a lane parameter applied to every library alike,
  and record it (`runner.affinity`, `runner.priority`). An affinity run is a separate
  lane of the same comparison, never a different comparison id.
- No product fast paths keyed to benchmark inputs: no special cases for fixture
  names, sizes or contents.
- Pin every competitor to an exact package version and record it with its package
  hash in the result metadata (`benchmark.package.<package>` and
  `benchmark.package.<package>.sha256`). The version must match the library policy.
- Configure every competitor for its best documented performance (its recommended
  reader, streaming API, buffer and caching options), the same way an informed
  user would. Note the configuration in the benchmark code next to the call.

## 6. Honest statistics

- Report one statistic per runner, recorded in the runner block: `median` by default,
  `mean` only when the runner cannot produce a median.
- Record a confidence interval for that statistic for every cell. From raw samples,
  PowerForge computes a distribution-free interval of the median, or a Student's t
  interval of the mean, using the same samples as the statistic (95% by default).
  A producer's own interval is used only for summary-only results.
- When the intervals of two cells overlap, the comparison says **even**, not "faster".
  Ratios between overlapping cells are not claims.
- Publish every run of a lane that was published. Never pick the best of several runs.
  A newer run replaces the latest lane; the earlier one stays in the catalog's
  superseded history with its date and hash. Result files are content-addressed
  (`windows.<sha256>.json`), so the superseded run's file is never overwritten.
- Keep failed and skipped cells in the evidence with their reason. A crash of one
  library is a result, not something to hide.

## 7. Licensing

Every library in the library policy has a restriction class:

| Class | Runs | Published |
| --- | --- | --- |
| `open` | Yes | Yes |
| `publish-restricted` | Locally, for development | No. Its cells are dropped from the published lane and shown as "not published (licence)"; the other libraries are still published. |
| `acceptance-required` | Only after the person running the benchmark explicitly accepts the licence for that run | Never under the standard policy. The accepted run's cells are recorded, then dropped from the published lane the same way. |
| `excluded` | Never | Never |

The standard publish policy publishes only `open` libraries
(`PublishableRestrictions = open`). Accepting a licence lets a library run; it never
makes its results publishable.

A restricted library never makes a comparison unpublishable. The published lane
removes that library's measurements from the stored result and keeps only a cell
marker reading "not published (licence)", then publishes the rest. A lane is
published when at least one cell remains eligible and the other policy checks pass;
if every library in a run is restricted, nothing is published. Only a comparison
declared with a single library, or one its author excludes, is never publishable.

Every library must declare its class. A library without one is neither selected
for runs nor published.

A catalog never stores measurements of a library whose class is not published: not
in published lanes, not in diagnostic lanes and not in superseded runs. Recording a
lane removes them from its stored result and the comparison view never shows them.
When a library becomes restricted after runs that measured it were recorded, an
update is refused; rebuild the catalog with `Merge-BenchmarkEvidenceCatalog` and the
new declarations, which redacts the current and superseded results.

### Classifying a library

The class follows what the licence says about **running benchmarks and publishing
their results**, not whether the vendor charges for commercial use. Read the
licence text of the exact pinned version and answer, in order:

1. Does the licence or its terms forbid benchmarking or comparison use outright? Then
   `excluded`.
2. Does using the library at all require accepting a licence, a key or a licence
   context (noncommercial-only terms, a mandatory licence-key call, a paid-licence
   switch) that the person running the benchmark must decide on? Then
   `acceptance-required`.
3. May it run, but does publishing benchmark or performance results need the
   licensor's permission? Then `publish-restricted`.
4. Otherwise, including licences with fees for unrelated uses (redistributing
   binaries in a revenue-generating product, support plans), it is `open`.

Classify per version: when a newer release adds a restriction, the last release
before it can stay `open` and the policy pins that version, or the library moves to
the stricter class with the newer version. Record the reason and the licence URL
for every class except `open`, and revisit the class whenever the pinned version
changes.

| Library and version | Licence terms that decide | Class |
| --- | --- | --- |
| NPOI | Apache-2.0; the OSMF fee applies only to binaries in revenue-generating use, not to benchmarking or publishing | `open` |
| QuestPDF 2026.5.0 | Last release before the benchmarking restriction | `open` |
| A later QuestPDF release | Classify by that release's own licence: `publish-restricted` when it restricts publishing benchmark results, `open` when it does not | per release |
| DocX (Xceed Community licence) | Noncommercial use only and agreement to its terms is required | `acceptance-required` |
| EPPlus 8 | PolyForm Noncommercial or a commercial licence, chosen through a licence context | `acceptance-required` |

### Selecting libraries for a run

- Runners select libraries generically from the policy: include and exclude lists
  plus an explicit per-library acceptance (`Select-BenchmarkLibrary -Include … -Exclude … -AcceptLicense EPPlus`).
  No runner hardcodes a library name, and no runner, script or agent accepts a
  licence on the person's behalf.
- The accepted libraries are recorded with the run (`Import-BenchmarkResult -AcceptLicense`).
  A cell from an acceptance-required library without a recorded acceptance is a
  policy violation.

## 8. Runs fail partially; evidence is written incrementally

A **failed cell** and **declaration drift** are different things, and PowerForge
treats them differently:

| Situation | Recorded as | Effect on publishing |
| --- | --- | --- |
| A declared case ran and crashed, timed out or failed validation | A cell with status `Failed` (or failed validation) and its reason | The cell is left out; the rest of the lane is published and marked `partial` |
| A case, library or axis value ran but is not declared | Drift: `unexpected-case`, `unexpected-library`, `unexpected-axis-value` | The lane is not published until the declaration is regenerated |
| A declared case or axis value has no cell at all | Drift: `missing-case`, `missing-axis-value`, `missing-axis-parameter` | The lane is not published |
| A declared case × library × axis-value combination has no cell, although each of its parts ran elsewhere | Drift: `missing-combination` | The lane is not published |
| A combination ran that the comparison's explicit `cells` do not list | Drift: `unexpected-combination` | The lane is not published |

A runner that crashes must still write the failed cell. A case that leaves no cell
behind is indistinguishable from one that was never run, so it is drift.

Each dimension being covered is not enough. With libraries A and B and
`Scale=Easy|Normal`, three rows can cover both libraries, both scales and the case
while `B, Scale=Normal` never ran. A comparison must therefore declare its `cases`
(or `cells`) to be published. Without them a run can't be checked for missing
combinations, so the comparison is held back with the reason "Declare the
comparison's cases (or cells) to publish it": its lanes are still recorded and
shown, but never published, and this is not reported as drift. When a comparison
declares its `cases`, PowerForge expects every combination of the declared cases and axis values for each library
that ran, and reports each absent one as `missing-combination`. Libraries that did
not run at all are not counted, because the run selects them from the library
policy. When not every combination is valid (a library that has no `Scale=Huge`
mode, a case that exists only for one library), list the valid cells explicitly in
the comparison's `cells`; each names its `case` (always: a cell never stands for
every case), optionally a `library` (absent means every library), and one value per
axis in `parameters`. Case names can't be blank. With `cells`, a combination
that ran but is not listed is `unexpected-combination`. Only the cases and axis
values of cells that apply to the libraries that ran are required, so a value only
an absent library supports is not reported missing. A comparison that lists `cells`
can leave out `cases`; the cells' case names are then its declared cases. Generate
`cells` from the benchmark code like the rest of the declaration.


- A crash, timeout or non-zero exit of one library's case is recorded as a failed
  cell with its reason (exit code, exception message). The run continues with the
  other cells and the other comparisons.
- Write evidence per comparison as soon as it is measured (`Update-BenchmarkEvidenceCatalog`
  once per comparison), so a later failure never loses earlier results.
- The publish policy decides per cell: failed, skipped and unstable cells are left
  out of public claims, and the lane says `partial`. The lane is still published
  when its remaining cells meet the policy.

## 9. Profiles and the publish policy

A project may make the policy stricter; a policy weaker than these defaults is
rejected for published lanes.

| Rule | Default | Checked for |
| --- | --- | --- |
| Profiles that may publish | `standard`, `full` | lane |
| Runner kinds allowed for `full` | `benchmarkdotnet` | lane |
| Runner block recorded (kind, statistic, warmups, iterations, ordering) | required | lane |
| Warmups per cell | at least 3 | lane |
| Clean source commit (`gitSha`, `gitWorktreeClean`) | required | lane |
| OS, architecture, CPU, logical cores, runtime, SDK (for .NET frameworks) | required | lane |
| Machine or user name | must be absent | lane |
| Comparison declared with its cases or cells, eligible, no drift, framework declared | required | lane |
| Package version and hash for every non-subject library, matching the pinned version | required | lane |
| Policy at least as strict as the standard | required | lane |
| Measured samples per cell | at least 10 | cell |
| Confidence interval of the statistic | 95% | cell |
| Output validation | passed | cell |
| Library identity and publishable restriction class | required | cell |
| Status succeeded, no failed samples | required | cell |

The thresholds live in one `BenchmarkPublishPolicy` object stored in every catalog,
so readers see the rules the evidence was held to. A project may tighten them; the
standard thresholds are the floor for public claims.

## 10. What PowerForge enforces

| Requirement | Enforced by |
| --- | --- |
| Declaration fields, pinned versions, licence and reason, clean comparison ids | `Test-BenchmarkDeclaration` (lint), and every catalog update |
| Declaration drift (unexpected or missing cases, libraries, axis values and combinations; failed cells are not drift) | `Test-BenchmarkDeclaration` with a result; publishing fails on drift |
| Library identity from categories or an explicit map | `Import-BenchmarkResult`; unmapped methods fail the import |
| Runner block, statistic, warmups, samples, confidence interval | publish policy |
| Output validation per library and case | publish policy |
| Licence classes and explicit acceptance; restricted cells dropped as "not published (licence)" | `Select-BenchmarkLibrary`, publish policy |
| Clean provenance, no machine name, pinned packages with hashes | publish policy, provenance capture |
| Failed cells kept, partial lanes marked | catalog cells and `completeness` |
| Lane identity (framework and affinity as dimensions) | catalog lane keys; framework or affinity in an id is rejected |
| Superseded runs kept with date and hash, their files never overwritten | catalog `superseded` history, content-addressed result paths |
| Recorded lanes still meet replaced declarations or policy | every catalog update re-evaluates all lanes |
| Measurements unchanged after provenance capture | import and publish validation |

Equivalence of the work, breadth of the matrix, competitor configuration and the
absence of benchmark-specific fast paths need a person. They are in the checklist.

## 11. Checklist before adding or publishing a benchmark

Author and reviewer both go through it.

1. Each library in the comparison does the same work on the same input; feature and
   default differences are normalised or declared.
2. Every library's output is validated before timing, and the validation is recorded
   per library (and per case where outputs differ).
3. The comparison declares its axes, and the matrix covers small to large sizes,
   realistic shapes, every supported file variant, and read/write/round-trip and
   streaming/in-memory where they apply.
4. The declaration is generated from the benchmark code, checked in, and
   `Test-BenchmarkDeclaration` passes in CI and against a diagnostic run.
5. Every competitor is pinned to an exact version that matches the library policy,
   configured per its own documentation for best performance, and recorded with its
   package hash.
6. Every library's licence class is set from its licence's terms on benchmarking and
   publishing (section 7), with a reason and licence URL for anything other than
   `open`. Nothing is accepted on the runner's behalf.
7. The product has no code paths keyed to benchmark inputs.
8. The runner rotates or interleaves order, warms up every cell, applies isolation,
   affinity and priority to every library alike, and records its runner block.
9. The run uses the `standard` or `full` profile from a clean commit with a provenance
   capture, and the result carries no machine or user name.
10. Failed cells are kept with their reason; evidence is written per comparison.
11. The published claims use the recorded statistic and confidence interval, say
    "even" where intervals overlap, and include every case, including the ones the
    subject loses.
12. Earlier runs are replaced, not deleted; their dates and hashes remain in the
    superseded history.
