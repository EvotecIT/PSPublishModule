# powershell-yaml whole-module workflow

This packet qualifies the intact public powershell-yaml 0.4.12 module through a Hybrid CompleteModule project. It requires both public functions (`ConvertFrom-Yaml`, `ConvertTo-Yaml`), both aliases (`cfy`, `cty`) and complete emission of all 14 owned functions, including its 12 private helpers.

Acquire the exact public package with the corpus's verified download and bounded extraction helpers. Its SHA-256 is `d4602bc7a4a093766520422d53ca8b09acde162286fae11e2ee6c8edfea07810`. The packets pin all 18 expanded files, including the manifest, root module, target-specific YamlDotNet and serializer assemblies, and license material. The package is a comparison input, not a production compiler dependency or redistributed payload in this directory.

Use `yaml-whole-module.net10.0.json` with a fresh PowerShell 7 host, or `yaml-whole-module.net472.json` with Windows PowerShell 5.1. Pass the unchanged expanded module as `SourceRoot` and explicit scratch workspace and evidence destinations to `Invoke-ExternalQualification.ps1`. The artifact basename remains `powershell-yaml`; project variant package names remain distinct.

The observer accepts `ImportPath`, `Surface`, `ScenarioRoot` and `ResultPath`. Its 105 scenarios cover typed scalars, mappings, ordered mappings, sequences, nulls, streams, pipeline use, serializer options, file bytes, overwrite/error behavior, private helpers, reimport and loader origins. All required positive cases must succeed. Dependencies must load from the imported module's own directory, including when the copied compiler input is unavailable during Built and Installed observation.

Unordered Hashtable enumeration is compared as typed key/value content in six declared cases. Two unordered serialization cases preserve values and require output order to follow their own observed input order. Complete raw enumeration and text remain diagnostic evidence. Ordered dictionaries, object properties, arrays, explicit ordered output and generated file bytes retain their strict order and value contracts. Other observations receive no generic ordering or error normalization.

The observer resets only its known owned scenario files and rejects unexpected or linked content. On Windows it sets execution policy only within its owned validation runspace so unsigned generated artifacts can be compared; it does not change persistent policy or use signing keys. Use a short net472 workspace to stay within Windows PowerShell's script and managed-loader path limits.

The common owner requires canonical Analyze, Lock, Restore, Build, Test, Pack and Install success, exact public metadata, all named bodies emitted, matching Original/Built/Installed observations and authenticated installed bytes. Hybrid module initialization and command regions can still use PowerShell. The packet makes no Strict, execution-without-PowerShell, exhaustive branch coverage, signing, public compiler release or performance claim.
