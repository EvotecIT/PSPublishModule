# Host-initiated website deployment

`Deployment/Linux/powerforge-site-pull-deploy.sh` lets an Ubuntu website host fetch a reviewed GitHub branch, build it with an exact PowerForge revision, and promote the resulting static site with the existing `powerforge-site-deploy` release promoter. The host only makes outbound GitHub and public-site requests. It does not need an inbound SSH connection from a GitHub-hosted runner.

The script runs as a dedicated, unprivileged site account. It archives the pinned PowerForge source from a local, root-owned checkout, builds the CLI in private scratch space, runs the site's `pipeline.json` in CI mode, and packages `_site` as `artifact.tar`. It records the fetched source SHA, engine SHA, and artifact SHA-256 in `deployment.json`. The existing root promoter checks the archive, promotes it atomically, verifies the origin, and keeps a rollback release. The pull script verifies the public release marker and smoke paths before finalizing; failure rolls back.

## Site setup

1. Install the reviewed shared script at `/usr/local/sbin/powerforge-site-pull-deploy`, owned by root and executable by the site account.
2. Create a dedicated account and a mode `0700` work directory owned by that account. The account needs outbound HTTPS, .NET 10, Git, tar, jq, curl, and the existing exact-command sudo permission for `powerforge-site-deploy`. Keep the PowerForge checkout root-owned and readable; the script trusts only its configured engine revision for the build.
3. Install a root-owned, non-writable site file at `/etc/powerforge/site-pull/<site>.env`. Set `SOURCE_REPOSITORY`, `SOURCE_NAME`, `SOURCE_BRANCH`, `WEBSITE_DIRECTORY`, `PIPELINE_CONFIG`, `ENGINE_REPOSITORY_PATH`, `ENGINE_SHA`, `WORK_ROOT`, `CURRENT_LINK`, `PUBLIC_URL`, and `SMOKE_PATHS`. The repository URL must be an HTTPS GitHub URL and must match `SOURCE_NAME`. Keep credentials out of this file.
4. Configure the root-owned `/etc/powerforge/sites/<site>.env` for the release promoter. If Cloudflare caching needs a purge, give the promoter a token file scoped to that site's zone and Cache Purge permission. The build account does not read that token.
5. Run `powerforge-site-pull-deploy <site> --build-only` as the site account. This exercises the actual Git fetch, pinned engine build, website pipeline, and archive packaging without promoting a release.
6. Run one normal deployment as the site account. Check the public `/_powerforge/deployment.json` marker, the configured smoke paths, the active `current` symlink, and the promoter's previous release. Enable the site's timer only after that proof.
7. Retire the old inbound deployment job only after the timer has completed a scheduled check and rollback has been exercised in a staging or fixture environment. Keep the prior release and workflow available during cutover.

The timer compares the remote branch SHA and configured engine SHA with the active release marker. Unchanged sites do no build work. A new commit is built from a fresh clone; the script refuses a branch that moves between the initial check and clone. A failed build leaves the active release alone. The work directory and staging archive are removed after each attempt.

## Backup and SSH boundary

Keep encrypted server recovery capture initiated from a fixed-egress runner outside the website host. Its GitHub backup-repository write credential stays off the production server. The restricted capture account remains reachable from that runner's fixed address. Once every deployment and backup account has been observed using only that address, restrict the host's SSH firewall rule to the address and verify a fresh login plus each scheduled job.

Source and generated static pages are rebuildable from Git and the pinned engine. Back up server configuration, private certificate material, and mutable application data separately. Do not publish plaintext secrets or database dumps to the source repository.
