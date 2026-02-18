# Cloudflare Cache Purge (Optional)

Last updated: 2026-02-09

If your site is behind Cloudflare and you cache HTML aggressively, deploys can appear "stale"
until Cloudflare revalidates or you hard refresh. The best long-term pattern is:

- hash/version static assets (CSS/JS/images) so they can be cached for a long time
- keep HTML caching conservative, or purge HTML after deploy

PowerForge.Web includes a small Cloudflare purge command to make this repeatable in CI.

## CLI

Purge a set of URLs (recommended):

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

Verify cache status on key URLs (warm once, then assert expected statuses):

```bash
powerforge-web cloudflare verify --base-url https://example.com --path /,/docs/,/api/ --warmup 1
```

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
