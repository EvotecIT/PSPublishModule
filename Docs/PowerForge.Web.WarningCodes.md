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
| `PFWEB.APIDOCS.INPUT.*` | Input validation warnings (`INPUT.XML`, `INPUT.HELP`, `INPUT.ASSEMBLY`). |
| `PFWEB.APIDOCS.REFLECTION` | Assembly reflection/enrichment warnings. |
| `PFWEB.APIDOCS.SOURCE` | Source link/source mapping warnings. |
| `PFWEB.APIDOCS.POWERSHELL` | PowerShell help/examples preflight warnings. |

## Pipeline Security / Operational Codes

| Code / Prefix | Meaning |
| --- | --- |
| `PFWEB.GITSYNC.SECURITY` | Inline token detected in `git-sync` config; prefer `tokenEnv` + CI secrets. |

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
