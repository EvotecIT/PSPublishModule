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
install -o root -g root -m 0640 \
  Deployment/Linux/powerforge-service.env.example \
  /etc/powerforge/services/example.env
```

Example configuration:

```dotenv
SERVICE_ROOT=/srv/example/service
SYSTEMD_SERVICE=example.service
LOCAL_HEALTH_URL=http://127.0.0.1:8080/healthz
PUBLIC_HEALTH_URLS="https://api.example.com/healthz https://api-alt.example.com/healthz"
REQUIRED_RELEASE_PATHS="package.json src/server.mjs"
RELEASES_TO_KEEP=5
REQUIRE_HEALTH_PROVENANCE=1
```

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
the caller repository.

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

The promoter never copies or removes files outside `SERVICE_ROOT`, its lock, and its deployment staging. Environment files, private keys, API credentials, queues, databases, registration stores, and other mutable state must remain external and be covered by the server-recovery manifest.

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
