# Repository architecture policy

PowerForge can enforce shared-capability ownership and dependency boundaries without copying repository-specific analysis scripts into every project.

The repository owns a small `.powerforge/architecture.json` policy. PowerForge owns project discovery, project and package reference inspection, source-usage discovery, change-impact selection, evidence coverage, reporting, and execution through the existing workspace validation engine.

## What the policy protects

- Exact or forbidden direct project and package references for selected boundary projects.
- One declared owner and an explicit set of production consumers for cross-cutting capabilities.
- New direct consumers discovered through configured source usage patterns.
- Contract, artifact, package, compatibility, or runtime evidence covering every owner and consumer.
- Impact-specific execution of registered workspace validation steps.

This is not a semantic clone detector or an evaluated MSBuild graph. Project and package edges come from literal `ProjectReference` and `PackageReference` declarations in repository-local project files. Imports, conditions, properties, item transforms, linked files, and references injected by props or targets are outside that inspection. Source consumer discovery is a lexical substring scan of configured repository paths; it ignores test projects and cannot recognize differently named copies or usages outside a discovered project directory.

Keep reusable implementation in the owner API, make lower-level bypasses inaccessible where practical, list shared props/targets in `globalImpactPaths`, and use review to sweep the defect class when a duplicate or imported path is discovered. The policy makes known boundaries and sibling surfaces executable instead of leaving them in PR prose.

## Configuration

```json
{
  "$schema": "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/powerforge.repository-architecture.schema.json",
  "schemaVersion": 1,
  "repositoryRoot": "..",
  "workspaceValidationConfig": ".powerforge/workspace.validation.json",
  "workspaceValidationProfile": "architecture",
  "globalImpactPaths": ["Directory.Build.*"],
  "projectRules": [
    {
      "id": "html-core-boundary",
      "project": "Product.Html/Product.Html.csproj",
      "allowedProjectReferences": ["Product.Core/Product.Core.csproj"],
      "requiredProjectReferences": ["Product.Core/Product.Core.csproj"],
      "forbiddenProjectReferences": ["Product.Email/Product.Email.csproj"]
    }
  ],
  "capabilities": [
    {
      "id": "tabular-row-projection",
      "ownerProjects": ["Product.Core/Product.Core.csproj"],
      "ownerPaths": ["Product.Core/Data/ObjectProjection*.cs"],
      "consumerProjects": [
        "Product.Excel/Product.Excel.csproj",
        "Product.Presentation/Product.Presentation.csproj"
      ],
      "usagePatterns": ["ObjectProjection.ProjectRows"],
      "usagePathExcludes": ["**/bin/**", "**/obj/**", "**/*.AotSmoke/**"],
      "requiredEvidenceKinds": ["contract", "artifact", "nativeAot"],
      "evidence": [
        {
          "id": "core-contract",
          "kind": "contract",
          "stepId": "projection-contracts",
          "path": "Product.Core.Tests/ProjectionContracts.cs",
          "coversProjects": ["Product.Core/Product.Core.csproj"]
        },
        {
          "id": "consumer-artifacts",
          "kind": "artifact",
          "stepId": "projection-consumer-artifacts",
          "path": "Product.Integration.Tests/ProjectionArtifacts.cs",
          "coversProjects": [
            "Product.Excel/Product.Excel.csproj",
            "Product.Presentation/Product.Presentation.csproj"
          ]
        },
        {
          "id": "native-aot",
          "kind": "nativeAot",
          "stepId": "projection-native-aot",
          "path": "Build/Test-AotScenarios.ps1",
          "coversProjects": [
            "Product.Core/Product.Core.csproj",
            "Product.Excel/Product.Excel.csproj",
            "Product.Presentation/Product.Presentation.csproj"
          ]
        }
      ]
    }
  ]
}
```

An omitted `allowedProjectReferences` or `allowedPackageReferences` leaves that reference family unrestricted. An explicitly empty array requires no direct references of that family. `requiredProjectReferences` and `requiredPackageReferences` protect edges that must remain present. Forbidden references are checked in addition to allowlists and required edges.

Capabilities that declare evidence must configure `workspaceValidationConfig`. Evidence `stepId` values must name an active base step in the selected PowerForge workspace validation profile. When that step expands through `items` or `frameworks`, selecting the base ID runs the complete matrix. `coversProjects` must collectively include every owner and declared consumer. `requiredEvidenceKinds` lets the repository require proof categories such as `contract`, `artifact`, `package`, `compatibility`, or `nativeAot` without teaching PowerForge product-specific commands. A changed evidence file or directory also marks the capability as impacted, and architecture execution fails when selected evidence is skipped because its required path is absent.

## Commands

Verify static boundaries and plan all declared evidence without executing it:

```powershell
powerforge architecture verify --config .powerforge/architecture.json
```

Select capabilities affected by a pull request and run only their evidence steps:

```powershell
powerforge architecture verify `
  --config .powerforge/architecture.json `
  --base origin/main `
  --head HEAD `
  --run-evidence
```

Include local tracked and untracked work:

```powershell
powerforge architecture verify --working-tree --run-evidence
```

Use `--report-json` and `--summary-markdown` for CI artifacts. The reusable `powerforge-repository-architecture.yml` enforcement workflow runs the same command and all selected evidence from an immutable PowerForge source ref. For a static audit without executing evidence, invoke the CLI directly and omit `--run-evidence`; the reusable enforcement workflow intentionally has no evidence-disable switch.

## Agent and reviewer contract

Run architecture verification at discovery time and again against the finished diff. A passing policy does not replace reading the owner and consumers. It proves that declared dependency edges, registered direct usages, evidence coverage, and selected commands agree. When a defect exposes another implementation path, register that consumer or tighten the owner API and sweep the sibling class before treating the PR as complete.
