# Cloudflare Site Policy, Purge, and Verification

Last updated: 2026-08-13

PowerForge.Web owns the repeatable Cloudflare policy for static sites behind the
Cloudflare proxy. It can:

- reconcile host-scoped cache rules without replacing unrelated rules
- set response security headers from the site's existing `AgentReadiness.SecurityHeaders` policy
- optionally reconcile Smart Tiered Cache for the zone
- purge individual URLs, a hostname, or the entire zone after deployment
- verify live `CF-Cache-Status` behavior after warmup

GitHub Pages does not consume `_headers`. A proxied GitHub Pages site therefore
needs the Cloudflare response transform if those headers must be present on the
live response.

## Apply the complete site policy

Configure the desired headers in `site.json`, then dry-run before writing:

```bash
powerforge-web cloudflare site-policy apply \
  --zone-id <ZONE_ID> \
  --token-env CLOUDFLARE_API_TOKEN \
  --site-config ./site.json \
  --dry-run

powerforge-web cloudflare site-policy apply \
  --zone-id <ZONE_ID> \
  --token-env CLOUDFLARE_API_TOKEN \
  --site-config ./site.json
```

The token needs write access to Cache Rules and Transform Rules for the target
zone. Add Zone Settings Write when `Cloudflare.SmartTieredCache` is configured,
and add Cache Purge when the same token runs the post-deploy purge step. Keep the
token in a protected environment or secret and pass only its environment-variable
name to the CLI.

The command manages two Cloudflare ruleset phases and, when configured, the
zone's Smart Tiered Cache setting:

- `http_request_cache_settings`: three cache rules
- `http_response_headers_transform`: zero to five response-header rules (site-wide security, homepage discovery links, API Catalog media type/CORS, JSON discovery media type/CORS, and Markdown artifact media type/CORS, as configured)

Rules outside the site's `PowerForge <Name>:` description prefix retain their
positions. PowerForge avoids a write when the effective policy is already
current. The combined command preflights every managed surface before its first
write. If a later write fails, it restores the snapshots and the previous Smart
Tiered Cache state taken before the operation and reports any incomplete rollback
explicitly.

## HSTS must be an explicit site decision

PowerForge retains its existing `AgentReadiness.SecurityHeaders.Hsts` default of
`true` for compatibility. HSTS persists in browsers after it is received, so a
site whose HTTPS renewal or recovery path is not proven must explicitly set it
to `false` before applying the managed response-header policy.

```json
{
  "AgentReadiness": {
    "SecurityHeaders": {
      "Enabled": true,
      "Hsts": false,
      "ContentSecurityPolicyValue": "default-src 'self'; frame-ancestors 'self'",
      "XFrameOptionsValue": "SAMEORIGIN",
      "PermissionsPolicy": true
    }
  }
}
```

For a GitHub Pages origin behind Cloudflare, the Cloudflare edge certificate and
the GitHub Pages origin certificate are separate concerns. Applying response
headers or cache rules does not prove that GitHub can renew its certificate
while DNS remains proxied. Keep HSTS disabled until that renewal path is stable,
or until the site has moved to a host whose certificate lifecycle Cloudflare
controls.

## Cache policy

The policy uses Free-plan-compatible Rules language (`eq`, `wildcard`,
`starts_with()`, and `ends_with()`), not the paid `matches` operator.

Sites without a `Cloudflare.Cache` block retain the compatibility policy:

| Rule | Edge TTL | Browser TTL | Notes |
| --- | ---: | ---: | --- |
| HTML, docs, and API | 2 hours | 5 minutes | Includes directory routes and `.html`; does not cache 3xx/4xx/5xx responses. |
| Data and discovery | Origin-controlled | Origin-controlled | Covers JSON, XML, text, sitemap, and LLM discovery files. |
| Static assets | Origin-controlled | Origin-controlled | Covers CSS, JavaScript, images, fonts, maps, PDFs, archives, and Blazor binaries. |

For a generated static site, opt in to a longer edge TTL and hostname-wide purge:

```json
{
  "Cloudflare": {
    "Cache": {
      "EdgeTtlSeconds": 604800,
      "BrowserTtlSeconds": 300
    },
    "PurgeMode": "hostname",
    "SmartTieredCache": true
  }
}
```

This applies the configured TTL to successful HTML, data/discovery, and static
asset responses. Responses below 200 and at or above 500 are not cached; 3xx and
4xx responses receive a zero edge TTL. Seven days is the edge default when the
`Cache` block is present. The browser default remains five minutes because a
Cloudflare purge cannot remove an object already stored in a visitor's browser.

Query strings remain part of the normal cache key. This avoids serving the wrong
representation when an application uses query parameters for behavior rather
than cache busting. Strong ETags remain enabled. PowerForge does not infer
immutability from filename shape. Generated `_headers` grants a long immutable
policy only to exact asset paths whose content hash PowerForge created.

Large documentation navigation trees do not produce one expression clause per
directory. PowerForge represents trailing-slash and `.html` routes compactly and
reserves explicit route clauses for exceptional extensionless paths.

The cache-only command remains available for existing consumers:

```bash
powerforge-web cloudflare cache-policy apply \
  --zone-id <ZONE_ID> \
  --token-env CLOUDFLARE_API_TOKEN \
  --site-config ./site.json
```

## GitHub Actions

The complete policy action rejects pull-request events before reading protected
inputs:

```yaml
- uses: EvotecIT/PSPublishModule/.github/actions/powerforge-cloudflare-site-policy@POWERFORGE_COMMIT
  with:
    site-config: Website/site.json
    zone-id: ${{ secrets.CLOUDFLARE_ZONE_ID }}
    api-token: ${{ secrets.CLOUDFLARE_API_TOKEN }}
```

Pin `POWERFORGE_COMMIT` to an exact commit. The reusable GitHub Pages deployment
workflow can apply the same action after a successful deployment:

```yaml
with:
  manage_cloudflare_site_policy: true
secrets:
  cloudflare_zone_id: ${{ secrets.CLOUDFLARE_ZONE_ID }}
  cloudflare_api_token: ${{ secrets.CLOUDFLARE_API_TOKEN }}
```

The existing `powerforge-cloudflare-cache-policy` action remains cache-only for
backward compatibility.

## Purge and verify after deployment

The purge command uses `Cloudflare.PurgeMode` from `site.json`. With the static
profile above, it purges every cached object for the site's hostname after the
new origin content is available:

```bash
powerforge-web cloudflare purge \
  --zone-id <ZONE_ID> \
  --token-env CLOUDFLARE_API_TOKEN \
  --site-config ./site.json
```

Verify public cache behavior:

```bash
powerforge-web cloudflare verify --site-config ./site.json --warmup 1
```

A post-deploy pipeline should name representative HTML, API, static, and WASM
loader paths. Do not include `DYNAMIC` in `allowStatuses`: it means the response
did not use the intended cache policy.

```json
{
  "task": "cloudflare",
  "operation": "verify",
  "siteConfig": "./site.json",
  "paths": [
    "/",
    "/api/",
    "/app/app.css",
    "/app/_framework/blazor.webassembly.js"
  ],
  "warmupRequests": 1,
  "allowStatuses": "HIT,REVALIDATED,EXPIRED,STALE"
}
```

Run purge only after the new origin content is available. A first request may be
`MISS`; warmup followed by an allowed cache status proves that the edge can
actually retain the response. Prefer hostname purge for a generated static site
with many routes; file purge remains useful when only a small, explicit URL set
should be invalidated. Use zone-wide `everything` purge only when every hostname
in the zone must be cleared.

## Cloudflare Pages readiness gate

Before testing a move from GitHub Pages, gate the built artifact against the
current Cloudflare Pages plan limits rather than estimating from the source tree:

```json
{
  "task": "audit",
  "siteRoot": "./_site",
  "maxTotalFiles": 20000,
  "maxFileBytes": 26214400,
  "failOnCategories": "budget"
}
```

This proves file count and per-file size only. A migration candidate still needs
a real preview deployment, custom-domain certificate validation, redirects,
headers, cache behavior, application startup, and rollback proof before DNS is
changed.
