# PowerForge.Web Linux Service Deployment

PowerForge provides a generic release workflow and a root-owned promoter for small Linux systemd services. It complements server recovery: the workflow deploys reproducible application code, while the recovery manifest captures host configuration, service units, certificates, encrypted secrets, and mutable state.

The service repository stays thin. It owns the service code, a validation script, and one workflow call. PowerForge owns packaging, provenance, SSH hygiene, archive validation, atomic promotion, health checks, retention, and rollback.

## Runtime Contract

The service must:

- run from a stable `current` symlink under `SERVICE_ROOT`
- keep secrets and mutable state outside the release directory
- read `_powerforge/deployment.json` from the current release
- expose the deployed `sourceSha` through each configured health endpoint
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

Pin the reusable workflow to an exact PowerForge commit:

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
  deploy:
    uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-service-deploy.yml@POWERFORGE_COMMIT
    with:
      service_root: Services/Example
      service_validation_script: deploy/linux/validate-service.sh
      deployment_service: example
      deployment_host: ${{ vars.POWERFORGE_SERVICE_DEPLOY_HOST }}
      deployment_port: ${{ fromJson(vars.POWERFORGE_SERVICE_DEPLOY_PORT) }}
      deployment_user: ${{ vars.POWERFORGE_SERVICE_DEPLOY_USER }}
      deployment_environment: production
      deployment_url: https://api.example.com/healthz
    secrets:
      deployment_ssh_private_key: ${{ secrets.SERVICE_DEPLOY_SSH_PRIVATE_KEY }}
      deployment_ssh_known_hosts: ${{ secrets.SERVICE_DEPLOY_SSH_KNOWN_HOSTS }}
```

The optional validation script runs in the checked-out caller repository before packaging. It should run contract tests and prepare generated output when needed. `service_root` is resolved after that script completes, so it may point at either committed source or a generated release directory.

## Promotion And Rollback

The workflow uploads an artifact unique to `deployment_service`, writes metadata containing the source repository, exact source SHA, workflow run identity, and archive SHA-256, then publishes both files with temporary SSH credentials.

The root promoter:

1. Validates the root-owned service configuration and dedicated workflow staging path.
2. Copies the workflow files into root-only staging before checksum or archive inspection.
3. Rejects checksum mismatches, path traversal, links, and special files.
4. Extracts a timestamped release and writes `_powerforge/deployment.json`.
5. Atomically switches `current` and restarts the configured systemd unit.
6. Requires local and public health endpoints to report the promoted source SHA.
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
