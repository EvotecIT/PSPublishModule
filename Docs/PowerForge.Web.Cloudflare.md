# Cloudflare Cache Policy, Purge, and Verification

Last updated: 2026-07-16

If your site is behind Cloudflare and you cache HTML aggressively, deploys can appear "stale"
until Cloudflare revalidates or you hard refresh. The best long-term pattern is:

- hash/version static assets (CSS/JS/images) so they can be cached for a long time
- keep HTML caching conservative, or purge HTML after deploy

PowerForge.Web owns the repeatable Cloudflare operations used by a static site:

- apply the standard host-scoped cache policy without replacing unrelated rules
- purge newly deployed routes
- verify public cache behavior after warmup

## Cache Policy

Apply the standard policy from a PowerForge `site.json`:

```bash
powerforge-web cloudflare cache-policy apply \
  --zone-id <ZONE_ID> \
  --token-env CLOUDFLARE_API_TOKEN \
  --site-config ./site.json
```

PowerForge derives the hostname from `BaseUrl`, the managed description prefix from
`Name`, and additional HTML routes from features and navigation. The policy manages
exactly three rules for that site:

- static assets, with query strings excluded from the cache key
- data and discovery files, preserving normal query behavior
- HTML, docs, API, and site-specific navigation routes, preserving normal query behavior

The command creates the phase entry-point ruleset when it is absent, preserves rules
outside the site's `PowerForge <Name>:` prefix, and avoids a ruleset update when the
effective policy is already current. Use `--dry-run` to read the current ruleset and
report whether a write would be required.

If a site specification is not available, pass `--hostname`, `--policy-name`, and
optional `--html-path` values explicitly.

## CLI

Purge using routes inferred from `site.json` (recommended for PowerForge sites):

```bash
powerforge-web cloudflare purge --zone-id <ZONE_ID> --token-env CLOUDFLARE_API_TOKEN --site-config ./site.json
```

Purge a custom set of URLs:

```bash
powerforge-web cloudflare purge --zone-id <ZONE_ID> --token-env CLOUDFLARE_API_TOKEN --base-url https://example.com --path /,/docs/,/api/
```

Purge everything (use with care):

```bash
powerforge-web cloudflare purge --zone-id <ZONE_ID> --token-env CLOUDFLARE_API_TOKEN --purge-everything
```

Dry-run:

```bash
powerforge-web cloudflare purge --zone-id <ZONE_ID> --token-env CLOUDFLARE_API_TOKEN --base-url https://example.com --path /,/docs/ --dry-run
```

Verify cache status using routes inferred from `site.json`:

```bash
powerforge-web cloudflare verify --site-config ./site.json --warmup 1
```

Verify cache status on custom URLs (warm once, then assert expected statuses):

```bash
powerforge-web cloudflare verify --base-url https://example.com --path /,/docs/,/api/ --warmup 1
```

When `--site-config` is used, PowerForge resolves a stable route profile from features/navigation:

- `features`: `/docs/`, `/api/`, `/blog/`, `/search/`
- nav surfaces + internal menu links (for product-specific pages like `/api/powershell/`, `/showcase/`, `/playground/`)
- always includes `/` and `/sitemap.xml`

## Pipeline Step (When It Makes Sense)

You can add a `cloudflare` task to `pipeline.json`, typically as a CI-only step.
Important: purging is most correct **after** the origin content has been updated.

Purge example step:

```json
{
  "task": "cloudflare",
  "id": "purge-cloudflare",
  "modes": ["ci"],
  "operation": "purge",
  "zoneId": "YOUR_ZONE_ID",
  "tokenEnv": "CLOUDFLARE_API_TOKEN",
  "siteConfig": "./site.json"
}
```

Verify example step (no token/zone required):

```json
{
  "task": "cloudflare",
  "id": "verify-cloudflare-cache",
  "dependsOn": "purge-cloudflare",
  "modes": ["ci"],
  "operation": "verify",
  "siteConfig": "./site.json",
  "warmupRequests": 1,
  "allowStatuses": "HIT,REVALIDATED,EXPIRED,STALE",
  "reportPath": "./_reports/cloudflare-cache.json",
  "summaryPath": "./_reports/cloudflare-cache.md"
}
```

## GitHub Actions

Configure cache rules from a caller-owned protected environment with the composite
action. The consumer workflow remains declarative; the API token is passed to the
PowerForge CLI through an environment-variable name and never appears in command
arguments.

```yaml
jobs:
  cloudflare-cache-policy:
    runs-on: ubuntu-latest
    environment: production
    permissions:
      contents: read
    steps:
      - uses: EvotecIT/PSPublishModule/.github/actions/powerforge-cloudflare-cache-policy@POWERFORGE_COMMIT
        with:
          site-config: Website/site.json
          zone-id: ${{ vars.CLOUDFLARE_ZONE_ID }}
          api-token: ${{ secrets.CLOUDFLARE_API_TOKEN }}
```

Pin `POWERFORGE_COMMIT` to an exact commit. The token needs `Cache Settings Write`
for the target zone. The action rejects pull-request events before protected inputs
are used.

### Post-Deploy Purge

For GitHub Pages workflows that deploy via `actions/deploy-pages@v4`, the purge should run
in the deploy job **after** the deployment step, using secrets:

- `CLOUDFLARE_API_TOKEN`
- `CLOUDFLARE_ZONE_ID`

Pseudo-snippet:

```yaml
- name: Purge Cloudflare
  env:
    CLOUDFLARE_API_TOKEN: ${{ secrets.CLOUDFLARE_API_TOKEN }}
  run: |
    powerforge-web cloudflare purge --zone-id ${{ secrets.CLOUDFLARE_ZONE_ID }} --token-env CLOUDFLARE_API_TOKEN --site-config ./site.json
    powerforge-web cloudflare verify --site-config ./site.json --warmup 1
```

## Standard Rule Expressions

The standard policy uses three cache rules for each PowerForge website.
These expressions only use `eq`/`wildcard` (no `matches`), so they work on Free plans.

1) `Static assets`

```txt
(http.request.method eq "GET" and (
  http.request.uri.path wildcard "/css/*" or
  http.request.uri.path wildcard "/js/*" or
  http.request.uri.path wildcard "/assets/*" or
  http.request.uri.path wildcard "/fonts/*" or
  http.request.uri.path wildcard "/images/*" or
  http.request.uri.path wildcard "/img/*" or
  http.request.uri.path wildcard "/*.css" or
  http.request.uri.path wildcard "/*.js" or
  http.request.uri.path wildcard "/*.mjs" or
  http.request.uri.path wildcard "/*.png" or
  http.request.uri.path wildcard "/*.jpg" or
  http.request.uri.path wildcard "/*.jpeg" or
  http.request.uri.path wildcard "/*.webp" or
  http.request.uri.path wildcard "/*.svg" or
  http.request.uri.path wildcard "/*.ico" or
  http.request.uri.path wildcard "/*.woff" or
  http.request.uri.path wildcard "/*.woff2"
))
```

2) `Data files`

```txt
(http.request.method eq "GET" and (
  http.request.uri.path wildcard "/data/*" or
  http.request.uri.path eq "/sitemap.xml" or
  http.request.uri.path eq "/llms.txt" or
  http.request.uri.path eq "/llms-full.txt" or
  http.request.uri.path eq "/llms.json"
))
```

3) `HTML / Docs / API`

```txt
(http.request.method eq "GET" and (
  http.request.uri.path eq "/" or
  http.request.uri.path wildcard "/docs/*" or
  http.request.uri.path wildcard "/api/*" or
  http.request.uri.path wildcard "/blog/*" or
  http.request.uri.path wildcard "/showcase/*" or
  http.request.uri.path wildcard "/playground/*" or
  http.request.uri.path wildcard "/pricing/*" or
  http.request.uri.path wildcard "/benchmarks/*" or
  http.request.uri.path wildcard "/faq/*" or
  http.request.uri.path eq "/search/" or
  http.request.uri.path wildcard "/search/*" or
  http.request.uri.path wildcard "*.html"
))
```

For all 3 rules use:
- `Cache eligibility`: `Eligible for cache`
- `Edge TTL`: `Use cache-control header if present, cache request with Cloudflare's default TTL for the response status if not`
- `Respect strong ETags`: enable
- Keep query handling default unless noted below.

Query-string guidance:
- `Static assets` rule:
  - `Ignore query string`: enable (improves hit rate for cache-busted/static URLs)
- `Data files` and `HTML / Docs / API` rules:
  - keep default query behavior (do not force ignore globally)

Route precedence for CLI:
- When `--url`/`--path` are provided, those explicit values are used.
- `--site-config` route inference is used only when explicit `--url`/`--path` are not provided.
