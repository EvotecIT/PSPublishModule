# PowerForge.Web Linux Deployment Pattern

Last updated: 2026-05-02

This pattern is the reusable baseline for static PowerForge.Web sites hosted on Linux with Apache or another file-based web server.

## Goals

- Build deploy artifacts from a clean checkout.
- Publish each deploy into a timestamped release directory.
- Promote the release by moving a `current` symlink.
- Keep generated web-server redirect artifacts in sync.
- Run cheap hourly change checks without rebuilding when nothing changed.
- Keep provider-specific cache purges in environment variables, not in repo files.

## Repository Flow

Use one website checkout and, when developing against a local engine checkout, one engine checkout:

```text
/srv/example/Website
/srv/example/PSPublishModule
```

The deploy script should fetch both repositories, reset them to configured branches, and compare the current commits with the last successful deploy metadata. If both commits are unchanged and `DEPLOY_ONLY_ON_CHANGE=1`, exit successfully without running the pipeline.

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

## Timers

Use two timers:

- Daily full rebuild: catches generated content, external data, feeds, and slow-moving maintenance work.
- Hourly change rebuild: fetches repositories and deploys only when `Website` or the engine commit changed.

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
