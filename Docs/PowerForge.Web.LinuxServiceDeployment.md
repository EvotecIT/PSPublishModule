# PowerForge.Web Linux Service Deployment

PowerForge provides generic package and deployment actions plus a root-owned promoter for small Linux systemd services. It complements server recovery: the actions deploy reproducible application code, while the recovery manifest captures host configuration, service units, certificates, encrypted secrets, and mutable state.

The service repository stays thin. It owns the service code, a validation script, a secret-free package job, and a protected-environment deployment job. PowerForge owns checkout, packaging, artifact publication, provenance, SSH hygiene, archive validation, atomic promotion, health checks, retention, and rollback.

## Runtime Contract

The service must:

- run from a stable `current` symlink under `SERVICE_ROOT`
- keep secrets and mutable state outside the release directory
- read `_powerforge/deployment.json` from the current release
- expose the deployed `sourceSha`, `workflowRunId`, and `workflowRunAttempt` through each configured health endpoint
- use a systemd unit whose restart is safe during deployment and rollback

The provenance health contract is enabled by default. Set `REQUIRE_HEALTH_PROVENANCE=0` only for a deliberate transitional deployment; a successful HTTP response alone does not prove which release is serving traffic.

## Host Setup

Install the promoter as root:

```bash
install -o root -g root -m 0755 \
  Deployment/Linux/powerforge-service-deploy.sh \
  /usr/local/sbin/powerforge-service-deploy
```

Create one root-owned configuration per service under `/etc/powerforge/services`:

```bash
install -d -o root -g root -m 0750 /etc/powerforge/services
install -d -o root -g root -m 0755 /srv/example/service
install -d -o example-service -g example-service -m 0750 /var/lib/example-service
install -d -o root -g root -m 0755 /var/lib/powerforge
install -d -o root -g root -m 0700 \
  /var/lib/powerforge/service-deployment-staging \
  /var/lib/powerforge/service-deployment-state
install -o root -g root -m 0640 \
  Deployment/Linux/powerforge-service.env.example \
  /etc/powerforge/services/example.env
```

`example-service` is the account configured by the systemd unit. Create that account
first or substitute the unit's existing `User=`/`Group=` values. `ReadWritePaths=`
opens the systemd mount namespace but does not bypass normal filesystem ownership and
mode checks.

Example configuration:

```dotenv
SERVICE_ROOT=/srv/example/service
SYSTEMD_SERVICE=example.service
SYSTEMD_READ_WRITE_PATHS="/var/lib/example-service"
LOCAL_HEALTH_URL=http://127.0.0.1:8080/healthz
PUBLIC_HEALTH_URLS="https://api.example.com/healthz https://api-alt.example.com/healthz"
REQUIRED_RELEASE_PATHS="package.json src/server.mjs"
RELEASES_TO_KEEP=5
REQUIRE_HEALTH_PROVENANCE=1
```

`SYSTEMD_READ_WRITE_PATHS` is optional. Set it to existing, space-separated absolute
data directories when the service unit uses `ProtectSystem=strict`. The root-owned
promoter writes a PowerForge-owned systemd drop-in and reloads systemd before restart,
so application releases remain immutable while declared databases, uploads, or other
mutable service state stay writable. The deployment rejects missing, relative, root,
traversal, symlinked, redirectable, release/control-plane-overlapping, or
systemd-special paths. Writable exceptions cannot contain or reside beneath the
service configuration, systemd configuration, lock, transaction, trusted staging,
or service/release roots.
Every parent must be root-owned and not group/world writable when the promoter runs
as root. Removing the setting removes only PowerForge's owned drop-in after a
successful deployment, while a failed deployment restores the previous permissions
before rolling the application back. Restoration preserves the previous drop-in owner,
group, and mode. If the permissions cannot be restored and reloaded, the promoter
keeps its recovery backup and stops instead of restarting with unverified access.
The systemd configuration root and unit drop-in directory must also be real,
root-owned, non-writable directory chains; the promoter never follows a service-owned
drop-in directory or reloads configuration from one. The same trust rule protects the
root-sourced service configuration and the canonical service/release roots. Deployments
are serialized by service id, systemd unit, and canonical service root. Cancellation
uses explicit non-zero signal exits, and rollback retains a rejected release whenever
the previous link or safe service state cannot be proven.
Immediately before switching `current`, the promoter persists the previous permission
and current-release state under `/var/lib/powerforge/service-deployment-state`, then
reloads the candidate policy. Transactions are keyed by the stable service id, so a
later invocation restores the recorded unit, service root, and systemd configuration
root even when current configuration was renamed or is temporarily unavailable. Before
validating a new service root, the promoter also scans pending transactions for another
service id that shares the configured unit or root and recovers that state under both
service locks. It proves the service restarted or stopped before accepting a new
deployment, so process termination or host loss cannot strand an uncommitted writable
policy. Restored drop-in and service-root filesystems are flushed before recovery state
is removed. Successful promotion retains a recognizable committed marker until the
transaction-directory rename is durable, then safely clears that marker on this or the
next invocation.

Give the dedicated deployment account only the exact promoter command it needs. Keep the service identifier fixed in sudoers rather than granting general root shell or `systemctl` access:

```sudoers
powerforge-example ALL=(root) NOPASSWD: /usr/local/sbin/powerforge-service-deploy --service example
```

Validate the effective sudoers rule with `visudo -cf` and `sudo -l -U powerforge-example` on the host.
The privileged command accepts no caller-controlled paths. It only reads
`/tmp/powerforge-service-example/artifact.tar` and `deployment.json`, then validates
their ownership and copies them into root-only staging before inspection.

## Caller Workflow

Pin both shared actions to the same exact PowerForge commit. Validation and packaging
run in a job with no protected environment. The deployment job downloads that exact
workflow artifact and owns the `production` environment so repository code never runs
in a process or job that can access deployment credentials:

```yaml
name: Deploy service

on:
  push:
    branches: [main]
    paths:
      - "Services/Example/**"
      - "deploy/linux/validate-service.sh"
      - ".github/workflows/deploy-service.yml"
  workflow_dispatch:

jobs:
  package:
    runs-on: ubuntu-latest
    permissions:
      contents: read
    steps:
      - uses: EvotecIT/PSPublishModule/.github/actions/powerforge-linux-service-package@POWERFORGE_COMMIT
        with:
          service-root: Services/Example
          service-validation-script: deploy/linux/validate-service.sh
          artifact-name: powerforge-service-example

  deploy:
    needs: package
    runs-on: ubuntu-latest
    environment:
      name: production
      url: https://api.example.com/healthz
    permissions:
      contents: read
    steps:
      - uses: EvotecIT/PSPublishModule/.github/actions/powerforge-linux-service-deploy@POWERFORGE_COMMIT
        with:
          artifact-name: powerforge-service-example
          deployment-service: example
          deployment-host: ${{ vars.POWERFORGE_SERVICE_DEPLOY_HOST }}
          deployment-port: ${{ vars.POWERFORGE_SERVICE_DEPLOY_PORT }}
          deployment-user: ${{ vars.POWERFORGE_SERVICE_DEPLOY_USER }}
          deployment-ssh-private-key: ${{ secrets.DEPLOYMENT_SSH_PRIVATE_KEY }}
          deployment-ssh-known-hosts: ${{ secrets.DEPLOYMENT_SSH_KNOWN_HOSTS }}
          source-repository: ${{ github.repository }}
          source-sha: ${{ github.sha }}

concurrency:
  group: powerforge-service-example
  cancel-in-progress: false
```

Store `DEPLOYMENT_SSH_PRIVATE_KEY` and `DEPLOYMENT_SSH_KNOWN_HOSTS` in the
protected environment named by the caller job. Do not use `secrets: inherit` or
move this job into a cross-repository reusable workflow; GitHub does not pass the
caller repository's environment secrets across that boundary. The deploy action
validates both values before it transfers the already packaged artifact.

The protected deployment action rejects `pull_request`, `pull_request_target`, and
`merge_group` events. The optional validation script runs only in the secret-free
package job, without persisted checkout credentials or GitHub workflow-command file
paths. It should run contract tests and prepare generated output when needed.
`service-root` is resolved and canonicalized after that script completes, so it may
point at either committed source or a generated release directory without escaping
the caller repository. Service artifacts are retained for seven days by default so
manual protected-environment approval can outlive a short queue or weekend; override
`artifact-retention-days` only when the repository has a different approval policy.

## Promotion And Rollback

The package action binds the archive SHA-256 to the source repository, exact source
SHA, and workflow run in an immutable artifact sidecar. The deployment action verifies
that binding before it writes SSH credentials, then emits the runtime deployment
metadata and transfers both files. Each run uploads into a unique remote staging
directory; a deployment-account lock serializes the atomic handoff into the fixed
root-promoter path.

The root promoter:

1. Validates the root-owned service configuration and dedicated workflow staging path.
2. Copies the workflow files into root-only staging before checksum or archive inspection.
3. Rejects checksum mismatches, path traversal, links, and special files.
4. Extracts a timestamped release and writes `_powerforge/deployment.json`.
5. Atomically switches `current` and restarts the configured systemd unit.
6. Requires local and public health endpoints to report the promoted source SHA and workflow run identity.
7. Restores the previous symlink and restarts it when deployment or health verification fails.
8. Stops the service after a failed first deployment and removes the failed release.
9. Retains the configured number of known-good releases.

The promoter mutates only the configured `SERVICE_ROOT`, its lock and root-only
transaction/staging roots, and the PowerForge-owned
`SYSTEMD_CONFIG_ROOT/<unit>.d/powerforge-read-write-paths.conf`. It reloads systemd
after changing or restoring that drop-in. Environment files, private keys, API
credentials, queues, databases, registration stores, and other mutable state remain
external and must be covered by the server-recovery manifest. Recovery planning must
also preserve the service configuration and systemd unit/drop-ins; transaction state
is temporary and is either committed or replayed by the next promoter invocation.

## Recovery Coverage

For a recoverable service, the repository recovery manifest should include:

- the promoter and root-owned service configuration
- the systemd unit and any Apache or nginx reverse-proxy configuration
- certificate names and renewal dry-runs
- encrypted capture of service environment files and private key material
- encrypted capture of non-rebuildable mutable state
- plain capture of service status, current symlink, and deployment metadata
- bootstrap, deploy, local health, public health, and provenance verification commands

Use `powerforge-web server inspect`, `capture`, `bootstrap-plan`, `restore-secrets-plan`, `deploy`, and `verify` to prove that deployment and disaster recovery describe the same runtime.
