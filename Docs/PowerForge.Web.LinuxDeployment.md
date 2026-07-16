# PowerForge.Web Linux Deployment Pattern

Last updated: 2026-07-16

This pattern is the reusable baseline for static PowerForge.Web sites hosted on Linux with Apache or another file-based web server.

## Goals

- Build deploy artifacts from a clean checkout.
- Build on the runner required by each website instead of assuming the web host can build it.
- Publish each deploy into a timestamped release directory.
- Promote the release by moving a `current` symlink.
- Record the exact source commit, PowerForge commit, workflow run, and artifact checksum.
- Roll back automatically when origin or externally verified public smoke checks fail.
- Keep generated web-server redirect artifacts in sync.
- Keep provider-specific cache purges in environment variables, not in repo files.

## CI Artifact Flow (Recommended)

Keep the reusable build and the protected deployment as separate jobs. GitHub does
not pass a caller repository's environment secrets into a cross-repository reusable
workflow. The deployment job must therefore belong to the caller repository, name
the protected environment itself, and invoke the pinned shared composite action.
The protected deployment action rejects `pull_request`, `pull_request_target`, and
`merge_group` events; invoke it only from a trusted `push` or `workflow_dispatch`
job after the build artifact has passed its normal validation lane.
The build still runs on `runner_labels_json`, so Windows-only and Linux-compatible
sites use the same publication contract without moving their toolchains onto the web
server.

```yaml
jobs:
  build:
    permissions:
      actions: write
      contents: read
      packages: read
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-website-run.yml@POWERFORGE_COMMIT
    with:
      website_root: Website
      pipeline_config: Website/pipeline.json
      site_artifact_name: powerforge-site-example.com

  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment:
      name: production
      url: https://example.com
    permissions:
      actions: read
      contents: read
    steps:
      - uses: EvotecIT/PSPublishModule/.github/actions/powerforge-linux-site-deploy@POWERFORGE_COMMIT
        with:
          artifact-name: powerforge-site-example.com
          deployment-site: example.com
          deployment-public-url: https://example.com
          deployment-smoke-paths: "/ /sitemap.xml"
          deployment-host: ${{ vars.POWERFORGE_WEBSITE_DEPLOY_HOST }}
          deployment-port: ${{ vars.POWERFORGE_WEBSITE_DEPLOY_PORT }}
          deployment-user: ${{ vars.POWERFORGE_WEBSITE_DEPLOY_USER }}
          deployment-cloudflare-zone: example.com
          deployment-ssh-private-key: ${{ secrets.WEBSITE_DEPLOY_SSH_PRIVATE_KEY }}
          deployment-ssh-known-hosts: ${{ secrets.WEBSITE_DEPLOY_SSH_KNOWN_HOSTS }}
          deployment-cloudflare-api-token: ${{ secrets.CLOUDFLARE_API_TOKEN }}
          cloudflare-zone-id: ${{ secrets.CLOUDFLARE_ZONE_ID }}
          source-repository: ${{ github.repository }}
          source-sha: ${{ github.sha }}
          engine-mode: ${{ needs.build.outputs.engine_mode }}
          engine-repository: ${{ needs.build.outputs.engine_repository }}
          engine-ref: ${{ needs.build.outputs.engine_ref }}
          engine-sha: ${{ needs.build.outputs.engine_sha }}
          engine-asset: ${{ needs.build.outputs.engine_asset }}
          engine-asset-sha256: ${{ needs.build.outputs.engine_asset_sha256 }}

concurrency:
  group: powerforge-site-example.com
  cancel-in-progress: false
```

This two-job lane:

1. runs the normal PowerForge CI pipeline and quality guardrails
2. archives the validated `_site` output on its native build runner
3. records exact source and engine SHAs plus the artifact SHA-256
4. transfers the artifact through a pinned SSH host key
5. invokes the root-owned server promoter for an allowlisted site id
6. verifies the promoted release directly at the origin and from the GitHub runner
7. finalizes the release, or restores the previous symlink and purges the rejected release from cache

Install `Deployment/Linux/powerforge-site-deploy.sh` as
`/usr/local/sbin/powerforge-site-deploy`. Install one root-owned configuration from
`Deployment/Linux/powerforge-site.env.example` under
`/etc/powerforge/sites/<site>.env`. The workflow cannot provide release roots,
Cloudflare credentials, origin addresses, or smoke policy; those remain trusted host
configuration.

Give each repository a separate deployment key and a protected GitHub environment.
Restrict its server account/sudo rule to the expected site argument. Do not share one
unrestricted deployment key across every repository.

The promoter rejects path traversal, links, special files, checksum mismatches,
unconfigured site ids, mutable site configuration, and workflow staging files owned
by another account. It atomically promotes a timestamped release, purges Cloudflare
without disabling proxying, and verifies exact provenance directly at the origin. The
composite action then verifies the same provenance and configured smoke paths through
the public URL from the GitHub runner. This separation avoids treating an origin-host
Cloudflare challenge as a broken release. A public verification failure restores the
previous symlink remotely, removes the rejected release, and purges Cloudflare again.
Release pruning happens only after the runner finalizes a verified release.

On the first PowerForge deployment, an existing non-symlink `current` directory is
moved into the release history and becomes the rollback target. This lets the shared
promoter take over a legacy in-place site without a consumer-specific migration
script or an unprotected delete-and-replace step.

When `deployment-cloudflare-zone` is set, the action uses `cloudflare-zone-id`
directly and transfers the deploy-only `deployment-cloudflare-api-token` and zone
id only inside temporary deployment staging. A least-privilege cache-purge token
therefore does not need Zone Read. If
the zone-id secret is absent, the action may discover exactly one active zone by
name; that fallback also requires Zone Read and uses Cloudflare's valid page size.
The promoter copies the credentials into root-only staging,
purges before exact-SHA verification, and erases them on every success or failure.
The normal website-pipeline Cloudflare token remains isolated to the build and is
never reused for Linux promotion.
This keeps Cloudflare proxying and cache analytics enabled without storing a broad
CI token permanently on the web host. Sites may instead use a scoped, root-owned
host token file when that is their preferred recovery model.

## Cloudflare Origin Trust

For Apache hosts that receive traffic through Cloudflare, install the generic
`Deployment/Linux/powerforge-cloudflare-origin-sync.sh` helper and its systemd
service/timer. It downloads Cloudflare's published IPv4 and IPv6 ranges over HTTPS,
validates every CIDR, refreshes the Cloudflare-specific UFW rules, and generates an Apache
`mod_remoteip` configuration that trusts `CF-Connecting-IP` only from those edge
networks. This preserves Cloudflare proxy/cache analytics while restoring the real
browser address to Apache logs and upstream services.

```bash
sudo install -m 0755 Deployment/Linux/powerforge-cloudflare-origin-sync.sh /usr/local/sbin/powerforge-cloudflare-origin-sync
sudo install -m 0644 Deployment/Linux/systemd/powerforge-cloudflare-origin-sync.service /etc/systemd/system/powerforge-cloudflare-origin-sync.service
sudo install -m 0644 Deployment/Linux/systemd/powerforge-cloudflare-origin-sync.timer /etc/systemd/system/powerforge-cloudflare-origin-sync.timer
sudo systemctl daemon-reload
sudo systemctl enable --now powerforge-cloudflare-origin-sync.timer
sudo systemctl start powerforge-cloudflare-origin-sync.service
```

The service optionally reads `/etc/powerforge/cloudflare-origin.env`; use
`Deployment/Linux/powerforge-cloudflare-origin.env.example` as the starting point.
Set `POWERFORGE_CLOUDFLARE_MANAGE_UFW=0` only when another firewall owner manages
the Cloudflare origin rules. The synchronizer validates Apache before reload and
restores the previous generated configuration if validation or reload fails.

## Host Build Flow (Special Case)

Some sites intentionally regenerate from changing external inputs even when Git has
not changed. Those sites may retain a host-side daily rebuild as a safety lane. Use
one website checkout and one engine checkout:

```text
/srv/example/Website
/srv/example/PSPublishModule
```

The deploy script should fetch both repositories, reset them to configured branches,
and compare current commits with the last successful metadata. It must install every
runtime dependency required by the strict pipeline, including the configured
Playwright browser. Prefer the CI artifact flow for normal commits so host tool drift
cannot silently stop publication.

Recommended environment variables:

```bash
WEBSITE_ROOT=/srv/example/Website
ENGINE_ROOT=/srv/example/PSPublishModule
WEBSITE_BRANCH=main
ENGINE_BRANCH=main
PIPELINE_CONFIG=pipeline.deploy.json
DEPLOY_ONLY_ON_CHANGE=0
DEPLOY_STATE_ROOT=/srv/example/deploy-state/website
```

## Optional Timers

Use two timers:

- Daily full rebuild: catches generated content, external data, feeds, and slow-moving maintenance work.
- Hourly change rebuild: only for sites that intentionally use the host-build flow.

Example change-check service:

```ini
[Unit]
Description=Example website rebuild and deploy when repository revisions changed
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
EnvironmentFile=/srv/example/Website/deploy/linux/example-website.env
Environment=DEPLOY_ONLY_ON_CHANGE=1
WorkingDirectory=/srv/example/Website
ExecStart=/usr/bin/env bash /srv/example/Website/deploy/linux/deploy-website.sh
```

Example hourly timer:

```ini
[Unit]
Description=Check example website repositories for deployable changes

[Timer]
OnBootSec=5min
OnUnitActiveSec=1h
AccuracySec=1min
Persistent=false
Unit=example-website-change-rebuild.service

[Install]
WantedBy=timers.target
```

## External Content

Do not make the Linux host poll every contribution or project repository directly unless the site really needs that. Prefer this pattern:

1. GitHub Actions or another CI runner imports external content into the website repository.
2. The import commit becomes the reviewable source of truth.
3. The Linux host only watches `Website` and the engine commits.

This keeps the deploy host simple and makes rebuild decisions reproducible from Git history.

## Cache Purge

Cache purge settings should live in the server environment file or CI secrets:

```bash
CLOUDFLARE_PURGE_ENABLED=0
CLOUDFLARE_PURGE_HOSTS=example.com,www.example.com
CLOUDFLARE_PURGE_EVERYTHING=0
CLOUDFLARE_PURGE_PATHS=/,/blog/,/sitemap.xml,/sitemap-index.xml
# CLOUDFLARE_API_TOKEN=...
```

The deploy script may also keep a scheduled CI purge as a fallback, but the server-side purge is the fastest path after a successful deploy.

## What Belongs In PowerForge

Reusable:

- pipeline execution
- `sources-sync` and lock verification
- generated project/catalog/docs content
- contribution imports
- redirect generation
- sitemap/feed/IndexNow outputs
- deploy metadata shape and change-detection rules
- server recovery manifest schema, inspection, capture, bootstrap, restore, deploy, and verification workflow

Site-local:

- host names
- release root paths
- Apache service names
- cache purge hosts
- contact relay or other site-specific services
- concrete recovery manifests that name site domains, secrets, services, and server paths

When a site-local script becomes useful for more than one site, lift the convention into this pattern first, then into a scaffold/install command.

## Server Recovery

Linux deployment scripts handle the normal happy path on an already prepared host. Server recovery covers the bigger case: a fresh Ubuntu server needs to be made production-ready and then redeployed from source control.

Use a site-local manifest such as:

```text
deploy/linux/example.serverrecovery.json
```

The manifest should point at the reusable schema:

```text
Schemas/powerforge.web.serverrecovery.schema.json
```

PowerForge should use that manifest to support these stages:

1. inspect the existing host for drift
2. capture plain configuration and encrypted secrets
3. bootstrap a new host
4. restore or prompt for required secrets
5. run the normal deploy script
6. verify services, certificates, origin behavior, and public URLs

Generated site output and timestamped release folders are not primary backup state. They are rebuildable from Git, lock files, and the deployment pipeline. Private keys, API tokens, SMTP credentials, and environment files must only enter GitHub backup storage as encrypted bundles.
