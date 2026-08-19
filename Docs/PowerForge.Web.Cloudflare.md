# Cloudflare Site Policy, Purge, and Verification

Last updated: 2026-08-13

PowerForge.Web owns the repeatable Cloudflare policy for static sites behind the
Cloudflare proxy. It can:

- reconcile host-scoped cache rules without replacing unrelated rules
- set response security headers from the site's existing `AgentReadiness.SecurityHeaders` policy
- optionally reconcile Smart Tiered Cache for the zone
- purge changed deployment URLs incrementally, explicit URLs, a hostname, or the entire zone after deployment
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
zone. Add Zone Settings Read and Zone Settings Write when
`Cloudflare.SmartTieredCache` is configured,
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
| HTML, docs, and API | 2 hours | 5 minutes | Includes directory routes plus `.html` and `.htm`; does not cache 3xx/4xx/5xx responses. |
| Data and discovery | Origin-controlled | Origin-controlled | Covers JSON, XML, text, sitemap, and LLM discovery files. |
| Static assets | Origin-controlled | Origin-controlled | Covers CSS, JavaScript, images, fonts, audio/video media, maps, PDFs, archives, and Blazor binaries. |

For a generated static site, opt in to a longer edge TTL and incremental purge:

```json
{
  "Cloudflare": {
    "Cache": {
      "EdgeTtlSeconds": 604800
    },
    "PurgeMode": "incremental",
    "SmartTieredCache": true
  }
}
```

This applies the configured edge TTL to every successful GET response for the
opted-in static site, including HTML, data/discovery, static assets, and
precompressed Blazor resources. Responses below 200 and at or above 500 are not
cached;
3xx and 4xx responses receive a zero edge TTL. Seven days is the edge default
when the `Cache` block is present. Browser caching remains origin-controlled
because a Cloudflare purge cannot remove an object already stored in a visitor's
browser.

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

Pin `POWERFORGE_COMMIT` to an exact commit. The reusable website deployment
workflow can apply the same action. GitHub Pages applies it after a successful
Pages deployment so incremental purge can correlate the exact deployment record.

For GitHub Pages, pass the policy credential directly to the reusable policy job:

```yaml
with:
  manage_cloudflare_site_policy: true
secrets:
  cloudflare_zone_id: ${{ secrets.CLOUDFLARE_ZONE_ID }}
  cloudflare_api_token: ${{ secrets.CLOUDFLARE_API_TOKEN }}
```

Linux deployment has two distinct credential boundaries. The narrow purge-only
token is staged ephemerally for promotion and rollback. The broader policy token
stays on the protected Actions runner, is used only after promotion succeeds, and
performs a final hostname purge after policy reconciliation:

```yaml
with:
  deployment_target: linux
  deployment_cloudflare_zone: example.com
  manage_cloudflare_site_policy: true
secrets:
  deployment_cloudflare_api_token: ${{ secrets.CLOUDFLARE_CACHE_PURGE_TOKEN }}
  deployment_cloudflare_policy_api_token: ${{ secrets.CLOUDFLARE_SITE_POLICY_TOKEN }}
  cloudflare_zone_id: ${{ secrets.CLOUDFLARE_ZONE_ID }}
```

Never reuse the site-policy token as `deployment_cloudflare_api_token`: that
credential is deliberately copied into the protected remote promotion staging
area so the host can purge on finalize or rollback. The policy token needs Cache
Rules Write and Transform Rules Write, Zone Settings Read and Zone Settings
Write when Smart Tiered
Cache is enabled, and Cache Purge for the post-policy hostname invalidation. The
deployment token needs only Cache Purge (and Zone Read when no zone id is passed).

The existing `powerforge-cloudflare-cache-policy` action remains cache-only for
backward compatibility.

When `PurgeMode` is `incremental`, the reusable deployment workflow creates a
private manifest from the exact Pages `artifact.tar`. It reads the tar
sequentially and hashes each deployed file without reopening thousands of files
from the generated output tree. The manifest is uploaded as a one-day workflow
artifact and compared with the last successfully purged baseline. Successful
baselines are private, site-scoped repository artifacts, so deployments from
different branches share the same continuity state. They are retained for seven
days and are never published with the website;
after a longer idle period, the next deployment safely uses a hostname purge.
Purge-mode detection loads the effective site specification, including
`extends`, so an inherited `Cloudflare.PurgeMode` has the same behavior as a
value declared directly in the child configuration.
Current-manifest artifact names remain unique to each reusable-workflow
invocation so one caller run can deploy multiple sites or matrix entries without
collisions.

Lowercase `index.html` files map to both their physical URL and clean directory
URL, so changing `docs/index.html` invalidates both `docs/index.html` and
`docs/`. Other filename casing remains a distinct deployed path. Added, changed,
and removed paths are purged. Up to 500 changed URL paths are sent in batches of
100, matching Cloudflare's per-request URL limit. A missing, unreadable, or
different-site baseline, or a larger diff, safely falls back to a hostname
purge. The new baseline is saved only after deployment and purge succeed.
The manifest also fingerprints the effective managed cache policy. Changing a
cache-affecting setting such as `Cloudflare.Cache.EdgeTtlSeconds` therefore
forces a hostname purge even when every deployed file is byte-for-byte
unchanged. A baseline created before policy fingerprints existed also receives
the same safe one-time fallback.
If the live managed site policy drifted, policy reconciliation forces the same
hostname fallback even when the desired manifest fingerprint and deployed files
are unchanged. This removes objects created under the drifted cache settings.
When a site keeps the same deployment scope but changes `BaseUrl`, the fallback
purges both the previous and current hostnames before advancing the baseline.
Cloudflare must accept both hostnames for the configured zone; otherwise the
purge fails closed so the old hostname is not silently abandoned.
Managed cache-rule expressions match both `GET` and Cloudflare's internal
`PURGE` evaluation, as required for reliable URL invalidation.
Action dry-runs calculate and report the same bounded decision but neither send
the purge request nor advance the last-successful deployment baseline.
The policy job correlates GitHub's actual Pages deployment records with the
exact Actions run and deployment job check-run id that produced the successful
baseline. This keeps separate reusable-site jobs in the same caller invocation
distinct. Retrying only an older policy job after a newer deployment skips the
stale policy, purge, and baseline update; deliberately rerunning the older Pages
deployment remains supported and is distinguished from the earlier attempt. If Pages succeeds but
the later policy or purge fails, the next run still sees that intervening Pages
deployment and uses a hostname purge instead of comparing against a non-adjacent
baseline. The last successful baseline remains available in this case only to
discover a previous hostname; it is never used for a file diff. A hostname
migration therefore invalidates both the old and current host even when an
intervening deployment prevents incremental comparison. This remains safe when
deployment and policy jobs from different runs overlap and does not depend on a
post-deployment receipt upload. The caller token must grant `actions: read` and
`deployments: read`; the reusable workflow declares both permissions.

Incremental purge targets the canonical URLs emitted by the deployment. It is
therefore intended for static sites whose query parameters do not select a
different representation. Cloudflare's default cache key includes the query
string, and a canonical URL purge does not enumerate arbitrary cached query
variants. List every known mutable variant in `Cloudflare.AlwaysPurgePaths` when
the variants are a small, bounded part of the site's deployment contract:

```json
{
  "Cloudflare": {
    "PurgeMode": "incremental",
    "AlwaysPurgePaths": [
      "/apps/converter/",
      "/apps/converter/?embedded=1"
    ]
  }
}
```

These site-relative URLs are included in every successful incremental purge,
deduplicated with changed deployment URLs, and counted against the same 500-URL
safety limit. Unknown or unbounded query-dependent responses should still keep
`PurgeMode` set to `hostname`.
The reusable managed-incremental action derives its hostname and base path from
the same `site.json` `BaseUrl` used to build the manifest. It rejects the
action's `hostname` and `base-path` overrides in this mode so purge targets
cannot silently diverge from the policy application target.

## Purge and verify after deployment

The purge command uses `Cloudflare.PurgeMode` from `site.json`. The reusable
deployment workflow supplies the private current and previous manifests
automatically. For manual incremental execution, provide the same inputs:

```bash
powerforge-web cloudflare purge \
  --zone-id <ZONE_ID> \
  --token-env CLOUDFLARE_API_TOKEN \
  --site-config ./site.json \
  --current-manifest ./current-manifest.json \
  --previous-manifest ./previous-manifest.json
```

Cloudflare accepts at most 30 normalized hostnames or 100 URLs in one purge
request. PowerForge validates explicit mode-specific limits and batches the
bounded incremental diff. Omitting the previous manifest intentionally triggers
the safe hostname fallback used for first deployment.

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
actually retain the response. Prefer incremental purge for a generated static
site deployed through the reusable workflow. Keep hostname purge as the broad
fallback or for deployment systems that cannot retain a trustworthy baseline;
file purge remains useful when only a small, explicit URL set should be
invalidated. Use zone-wide `everything` purge only when every hostname in the
zone must be cleared.

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
