# Whole-module and ordinary-script qualification

These packets test complete user workflows through Original, Built and Installed surfaces. Each packet pins the intact workload, its dependency inputs, the observer and its support files. Discovery does not import third-party code; execution uses the explicit external-qualification lane.

| Workload | Required authored bodies | Public surface | Scenarios | Targets |
| --- | --- | --- | --- | --- |
| [powershell-yaml](Yaml/README.md) 0.4.12 | All 14 root functions, including 12 private helpers | Two functions and two aliases | 105 | net10.0 and net472 modules |
| [platyPS](PlatyPS/README.md) 0.14.2 | All 42 root declarations and the 23 nested bodies mapped to those owners | Ten functions | 77 | net10.0 and net472 modules |
| [Sitemap comparison](CompareWebSitemaps/README.md) | The script root and all 19 helpers from the real two-file application | Thirteen root parameters; JSON and three CSV reports | 10 | Windows x64/net10.0 Hybrid executable |

The two conditional platyPS completers are separate required declarations. Counting only its 40 ordinary roots cannot satisfy the packet. The sitemap source includes the small AMP summary variable repair; its pinned pre-repair evidence remains separate.

`Invoke-ExternalQualification.ps1` and `Corpus.Qualification.Workflow.ps1` own the lifecycle. They copy only reviewed source inputs, observe the original, then use the canonical project Analyze, Lock, Restore, Build, Test, Pack and Install operations. The generated artifact must keep its public module identity, exports, aliases, parameter metadata and dependency origins. The optional artifact basename in each module packet preserves that identity while project variants keep separate package names.

Both Built and Installed observers run in fresh selected-host processes with the copied compiler input directory unavailable. Every named owned body must have an emitted CLR method and no retained hosted body, shaping fallback, omission or rejection. Every declared scenario must execute, and comparable observations must match the original. Scenarios cover repeated use, empty and invalid inputs, error identities, initialization, cleanup and file effects. Workload-specific incidental values use explicit observer schemas; raw observations remain evidence.

This is Hybrid qualification. Module initialization and supported command regions can use PowerShell, and a complete emitted body can still cross a documented runtime boundary. The gate does not establish Strict compilation, execution without PowerShell, exhaustive branch coverage, signing, public package availability or performance. The installed artifact proof is separate from analysis eligibility and generated-code planning.

Windows PowerShell 5.1 qualification uses a short workspace because its script and managed assembly loaders have legacy path limits. The observers use an explicit policy inside their owned unsigned-validation runspaces; they do not change machine or user policy. Unsupported installation paths must fail before installation changes. Cabinet scenarios additionally need the existing Windows MakeCab and expand tools. The sitemap fixture uses only an owned loopback server and releases it after successful and failed runs.

The module packages and their binaries are external comparison inputs. Acquire their exact hashes through the existing verified download and bounded extraction helpers; do not add them to compiler output or checked-in production dependencies. See each packet's README for acquisition, licensing, host prerequisites, observer contracts and proof limits. Keep qualification workspaces on configured scratch storage and preserve compact reports before releasing generated output.
