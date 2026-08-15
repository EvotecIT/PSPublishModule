# PowerForge.Web Warning Codes

This catalog documents warning code prefixes used by PowerForge.Web verify/pipeline output.

Use these codes with:
- `site.json -> Verify.SuppressWarnings`
- pipeline step `suppressWarnings`
- targeted CI policy tuning

Notes:
- Codes are emitted as `[CODE] message`.
- Verify baselines normalize keys by stripping `[CODE]`, so adding/changing codes does not invalidate existing baselines.
- Treat prefixes as stable contracts; individual message text can evolve.

## Site Verify Codes (`powerforge-web verify`)

| Code / Prefix | Meaning |
| --- | --- |
| `PFWEB.NAV.LINT` | Navigation lint issues (surfaces, menu/profile consistency, route coverage). |
| `PFWEB.THEME.CONTRACT` | Theme contract issues (missing required fragments/layout/manifest contract checks). |
| `PFWEB.THEME.CSS.CONTRACT` | Theme CSS selector contract issues. |
| `PFWEB.MD.HYGIENE` | Markdown hygiene warnings (raw HTML/media tag hygiene). |
| `PFWEB.XREF` | Xref resolution warnings. |
| `PFWEB.DATA.VALIDATION` | Known data-shape validation warnings. |
| `PFWEB.RELEASE.NO_MATCH` | Release/download selectors reference products with no matching assets (or missing release-hub data file). |
| `PFWEB.RELEASE.PRODUCT_MISSING` | Release-hub assets reference product ids missing from release-hub `products` catalog. |
| `PFWEB.RELEASE.ASSET_COLLISION` | Duplicate release-hub asset entries detected (same release/name/url tuple). |
| `PFWEB.RELEASE.PLACEMENT_MISSING` | Release shortcode placement references are missing from `data/release_placements.json` (or placement file is unavailable). |
| `PFWEB.COLLECTION` | Collection-level warnings (missing files, etc.). |
| `PFWEB.LOCALIZATION` | Localization mapping/translation configuration warnings. |
| `PFWEB.SEO.DATE` | Editorial post missing date metadata. |
| `PFWEB.BESTPRACTICE` | Best-practice guidance warnings. |

## API Docs Codes (`apidocs` step / generator)

| Code / Prefix | Meaning |
| --- | --- |
| `PFWEB.APIDOCS` | General API docs warning bucket. |
| `PFWEB.APIDOCS.NAV` | API docs navigation config/token warnings. |
| `PFWEB.APIDOCS.NAV.REQUIRED` | NAV tokens required but nav input missing. |
| `PFWEB.APIDOCS.NAV.FALLBACK` | API docs fell back to generic header/footer fragments. |
| `PFWEB.APIDOCS.CSS.CONTRACT` | API docs CSS contract warning. |
| `PFWEB.APIDOCS.QUICKSTART` | Quick start section quality/config warnings. |
| `PFWEB.APIDOCS.DISPLAY` | Display name mode/config warnings. |
| `PFWEB.APIDOCS.MEMBER.SIGNATURES` | Duplicate/ambiguous member signature grouping warnings. |
| `PFWEB.APIDOCS.COVERAGE` | Coverage threshold/report warnings. |
| `PFWEB.APIDOCS.XREF` | API docs xref generation warnings. |
| `PFWEB.APIDOCS.SUITE` | Multi-project API suite guidance/recommendation warnings, including missing suite onboarding/curation and untouched scaffold starter placeholders. |
| `PFWEB.APIDOCS.INPUT.*` | Input validation warnings (`INPUT.XML`, `INPUT.HELP`, `INPUT.ASSEMBLY`). |
| `PFWEB.APIDOCS.REFLECTION` | Assembly reflection/enrichment warnings. |
| `PFWEB.APIDOCS.SOURCE` | Source link/source mapping warnings. |
| `PFWEB.APIDOCS.POWERSHELL` | PowerShell help/examples preflight warnings. |

## Pipeline Security / Operational Codes

| Code / Prefix | Meaning |
| --- | --- |
| `PFWEB.GITSYNC.SECURITY` | Inline token detected in `git-sync` config; prefer `tokenEnv` + CI secrets. |

## Agent Content Audit Codes

These identifiers are emitted directly by the optional final-artifact scanner.
Within a site audit they are retained in the normalized issue rule/hint under
category `agent-content`, so the findings flow through baseline, summary, and
SARIF output.

| Code / Prefix | Meaning |
| --- | --- |
| `PFAGENT.ARTIFACT` | A configured machine-facing artifact is missing, oversized, or invalid JSON. |
| `PFAGENT.TEXT` | Invalid UTF-8, invisible Unicode controls, or a high-confidence prompt directive. |
| `PFAGENT.COMMAND.REMOTE_EXECUTION` | Downloaded content reaches a shell or interpreter directly or through intermediate pipeline stages. |
| `PFAGENT.COMMAND.RUNTIME_INJECTION` | A runtime startup-hook or module-path environment variable can execute or redirect code before a package command. |
| `PFAGENT.PACKAGE.INVALID_ID` | A package identifier uses an invalid registry shape or non-ASCII lookalike characters. |
| `PFAGENT.PACKAGE.OBFUSCATED_COMMAND` | Shell escaping, variable expansion, path qualification, or quote concatenation hides a supported package-manager executable. |
| `PFAGENT.PACKAGE.UNVERIFIABLE_*` | A command or dependency set cannot be reduced to static package identifiers and canonical sources. |
| `PFAGENT.PACKAGE.NOT_FOUND` | The referenced package is not registered. |
| `PFAGENT.PACKAGE.VERSION_NOT_FOUND` | The referenced exact version is not registered. |
| `PFAGENT.PACKAGE.UNTRUSTED_SOURCE` | A command, environment value, or direct package-manager configuration write can override the canonical public registry. |
| `PFAGENT.PACKAGE.OWNER_*` | Required owner-scoped publication proof is absent or mismatched. |
| `PFAGENT.PACKAGE.REGISTRY_*` | Registry verification timed out, was unavailable, or returned malformed data. |
| `PFAGENT.HOST` | An optional external-host check found an unresolved, non-public, or dangling-service destination. |

## Suppression Examples

`site.json`:
```json
{
  "Verify": {
    "SuppressWarnings": [
      "PFWEB.NAV.LINT",
      "PFWEB.APIDOCS.SOURCE",
      "PFWEB.APIDOCS.INPUT.*",
      "re:^\\[PFWEB\\.THEME\\."
    ]
  }
}
```

pipeline step:
```json
{
  "task": "verify",
  "config": "./site.json",
  "suppressWarnings": [
    "PFWEB.NAV.LINT"
  ]
}
```

## Related Docs

- `Docs/PowerForge.Web.Pipeline.md`
- `Docs/PowerForge.Web.QualityGates.md`
- `Docs/PowerForge.Web.ContentSpec.md`
