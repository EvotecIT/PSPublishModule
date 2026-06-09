# PowerForge.Web Release Hub (Downloads + Changelog)

Last updated: 2026-02-28

## Why this exists

Websites need one shared engine capability for:

- changelog history from GitHub releases
- download buttons outside changelog pages (home/docs/product cards)
- multi-asset releases (for example one release with many product ZIPs)
- scalable rendering for hundreds of releases

This document defines a concrete contract so productized docs and download sites can all use the same model.

## Current engine state

Already available:

- `pipeline` task: `changelog` (local file or GitHub releases)
- release body (`body_md`) and release assets are already emitted
- themes can already render `data/*.json` on any page/layout

Current gaps:

- no first-class product/channel/platform asset classification
- no selector helper API for reusable download buttons
- no pagination/caching contract for very large release histories
- no standard output model for "one release, many products"

## Feature: `release-hub` pipeline task

Keep `changelog` for simple sites. Add `release-hub` for productized downloads.

### Pipeline step

```json
{
  "task": "release-hub",
  "source": "github",
  "repo": "ExampleOrg/ExampleSuite",
  "tokenEnv": "GITHUB_TOKEN",
  "out": "./data/release-hub.json",
  "maxReleases": 400,
  "pageSize": 100,
  "maxPages": 5,
  "includeDraft": false,
  "includePrerelease": true,
  "defaultChannel": "stable",
  "assetRules": [
    {
      "product": "example.writer",
      "label": "Example Writer",
      "match": ["Example.Writer*.zip", "example-writer*.zip"],
      "kind": "zip"
    },
    {
      "product": "example.sheet",
      "label": "Example Sheet",
      "match": ["Example.Sheet*.zip", "example-sheet*.zip"],
      "kind": "zip"
    }
  ],
  "products": [
    { "id": "example.writer", "name": "Example Writer", "order": 10 },
    { "id": "example.sheet", "name": "Example Sheet", "order": 20 }
  ]
}
```

### Source options

- `source`: `github` | `file` | `auto`
- `repo` or `repoUrl` (`owner/repo` preferred)
- `tokenEnv` preferred over inline `token`
- optional `etagCachePath` for conditional fetches

### Scale options

- `pageSize` (default `100`)
- `maxPages` and `maxReleases`
- default sorting: newest first
- output should include deterministic IDs for stable pagination

## Output contract (`data/release-hub.json`)

```json
{
  "title": "Example Suite Releases",
  "generatedAtUtc": "2026-02-28T12:00:00Z",
  "source": "github",
  "repo": "ExampleOrg/ExampleSuite",
  "latest": {
    "stableTag": "v1.2.0",
    "prereleaseTag": "v1.3.0-preview1"
  },
  "products": [
    { "id": "example.writer", "name": "Example Writer", "order": 10 },
    { "id": "example.sheet", "name": "Example Sheet", "order": 20 }
  ],
  "releases": [
    {
      "id": "v1.2.0",
      "tag": "v1.2.0",
      "name": "Example Suite 1.2.0",
      "url": "https://github.com/ExampleOrg/ExampleSuite/releases/tag/v1.2.0",
      "publishedAt": "2026-02-20T19:01:10Z",
      "isPrerelease": false,
      "isDraft": false,
      "isLatestStable": true,
      "body_md": "## Added\n- ...",
      "assets": [
        {
          "id": "example-writer-v1.2.0.zip",
          "name": "Example.Writer-v1.2.0.zip",
          "downloadUrl": "https://github.com/.../Example.Writer-v1.2.0.zip",
          "size": 1812345,
          "contentType": "application/zip",
          "product": "example.writer",
          "channel": "stable",
          "platform": "any",
          "arch": "any",
          "kind": "zip",
          "thumbnailUrl": "https://github.com/.../Example.Writer-v1.2.0-thumb.png",
          "sha256": null
        }
      ]
    }
  ],
  "warnings": []
}
```

Notes:

- `body_md` intentionally mirrors current conventions; `body` HTML can be auto-derived by existing markdown data normalization.
- unknown/unmatched assets should stay in output with `product: "unknown"` instead of being dropped.

## Asset classification contract

`assetRules` are evaluated top-to-bottom. First match wins.

Rule fields:

- `product` (required)
- `label` (optional, UI display fallback)
- `match` (glob array for filename)
- `contains` (optional substring array)
- `regex` (optional advanced matcher)
- `channel` (optional forced value: `stable`/`preview`/`nightly`)
- `platform` (optional forced: `windows`/`linux`/`macos`/`any`)
- `arch` (optional forced: `x64`/`arm64`/`any`)
- `kind` (optional forced: `zip`/`msi`/`exe`/`nupkg`/`tar.gz`/`pkg`)
- `priority` (optional for future rule ordering; default file order)

Engine fallback heuristics (when rule does not set values):

- `channel`: prerelease flag -> `preview`, else `stable`
- `platform`: detect from filename tokens (`win`, `linux`, `osx`, `mac`)
- `arch`: detect from tokens (`x64`, `arm64`, `x86`)
- `kind`: extension-driven

## Template helper API

Helpers should be page-agnostic and usable in any Scriban layout.

- `pf.release_button product [channel] [platform] [arch] [kind] [label] [css_class]`
- `pf.release_buttons product [channel] [limit] [group_by]`
- `pf.release_changelog [product] [limit] [include_preview]`

Expected behavior:

- return empty string when no match (never throw in template)
- choose newest matching asset by default
- `release-buttons` supports `product="*"` to render buttons across all products
- release buttons/changelog include `platform`/`arch`/`kind` badge markup when available
- optional thumbnail fields (`thumbnailUrl`/`imageUrl`/`screenshotUrl`) are rendered in matrices/changelog assets
- optional `strict` mode can emit verify warnings when nothing matches

## Markdown shortcode API

For content authors (without editing theme layouts):

```md
{{< release-button product="example.writer" channel="stable" label="Download Writer" class="btn btn-primary" >}}
{{< release-buttons product="example.writer" groupBy="platform" channel="stable" >}}
{{< release-buttons product="*" groupBy="product" channel="stable" limit="20" >}}
{{< release-changelog product="example.writer" limit="20" >}}
```

These shortcodes should consume the same `data.release_hub` payload and helper selection logic.

### Placement-driven shortcodes (recommended for non-template pages)

Shortcodes can now resolve selector settings from `data/release_placements.json` using `placement="..."`.

```md
{{< release-button placement="home.chat_primary" >}}
{{< release-buttons placement="home.chat_matrix" >}}
{{< release-changelog placement="changelog.chat_timeline" >}}
```

Aliases are also available:

- `release-button-placement`
- `release-buttons-placement`
- `release-changelog-placement`

Placement values merge with explicit shortcode attributes:

- explicit attributes win
- missing values are filled from placement map

Minimal placement schema:

```json
{
  "home": {
    "chat_primary": {
      "product": "example.desktop",
      "channel": "stable",
      "platform": "windows",
      "arch": "x64",
      "kind": "zip",
      "label": "Download Chat",
      "class": "btn btn-primary"
    },
    "downloads_matrix": {
      "product": "*",
      "channel": "stable",
      "groupBy": "product",
      "limit": 20
    },
    "chat_matrix": {
      "product": "example.desktop",
      "channel": "stable",
      "groupBy": "platform",
      "limit": 3
    }
  },
  "changelog": {
    "chat_timeline": {
      "product": "example.desktop",
      "limit": 50,
      "includePreview": true
    }
  }
}
```

## Placement examples

### Homepage CTA

- `pf.release_button "example.desktop" "stable" "windows" "x64" "zip" "Download App for Windows"`

### Product detail page

- show one primary button + secondary matrix (`pf.release_buttons`)

### Changelog page

- show release timeline (`pf.release_changelog`) with optional expandable assets

## Verify/Audit rules

Implemented verify checks:

- missing required product mapping (`PFWEB.RELEASE.PRODUCT_MISSING`)
- unresolved primary download CTA (`PFWEB.RELEASE.NO_MATCH`)
- duplicate asset classification collisions (`PFWEB.RELEASE.ASSET_COLLISION`)
- missing placement references (`PFWEB.RELEASE.PLACEMENT_MISSING`)

Planned audit checks:

- broken `downloadUrl` links in rendered output (`audit` link check category)

CI guidance:

- use baseline + `failOnNewWarnings:true` for verify
- use baseline + `failOnNewIssues:true` for audit

## Backward compatibility

- `changelog` task remains unchanged.
- `release-hub` can internally reuse changelog/GitHub fetch plumbing.
- existing sites can adopt incrementally:
  1. generate `data/release-hub.json`
  2. add one homepage CTA helper
  3. upgrade changelog/download pages later

## Implementation slices

### Slice 1 (MVP)

- `release-hub` task + schema
- GitHub pagination + `tokenEnv`
- asset classification rules
- normalized output JSON

### Slice 2

- Scriban helpers (`pf.release_*`)
- markdown shortcodes (`release-button`, `release-buttons`, `release-changelog`)

### Slice 3

- verify warnings implemented (`PFWEB.RELEASE.PRODUCT_MISSING`, `PFWEB.RELEASE.NO_MATCH`, `PFWEB.RELEASE.ASSET_COLLISION`)
- optional cache (`etagCachePath`) and profiling diagnostics remain open

## First Adoption Checklist

1. Add a `release-hub` step to the site pipeline writing `./data/release-hub.json`.
2. Replace placeholder changelog content with helper-based rendering.
3. Add homepage/product CTAs using the same selector contract.
4. Add verify baseline entries only for legacy noise; fail on new release/download warnings in CI.
