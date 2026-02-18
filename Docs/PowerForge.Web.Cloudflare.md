# Cloudflare Cache Purge (Optional)

Last updated: 2026-02-09

If your site is behind Cloudflare and you cache HTML aggressively, deploys can appear "stale"
until Cloudflare revalidates or you hard refresh. The best long-term pattern is:

- hash/version static assets (CSS/JS/images) so they can be cached for a long time
- keep HTML caching conservative, or purge HTML after deploy

PowerForge.Web includes a small Cloudflare purge command to make this repeatable in CI.

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

Example step:

```json
{
  "task": "cloudflare",
  "id": "purge-cloudflare",
  "modes": ["ci"],
  "zoneId": "YOUR_ZONE_ID",
  "tokenEnv": "CLOUDFLARE_API_TOKEN",
  "baseUrl": "https://example.com",
  "paths": "/,/docs/,/api/"
}
```

## GitHub Actions (Recommended)

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
    powerforge-web cloudflare purge --zone-id ${{ secrets.CLOUDFLARE_ZONE_ID }} --token-env CLOUDFLARE_API_TOKEN --base-url https://example.com --path /,/docs/,/api/
```

## Cloudflare Rules (Free Plan Friendly)

Use 3 cache rules for all PowerForge websites.
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
- Leave `Browser TTL`, `Cache key`, and other optional fields at default unless you have a specific reason.
