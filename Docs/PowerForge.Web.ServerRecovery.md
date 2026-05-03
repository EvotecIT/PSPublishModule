# PowerForge.Web Server Recovery

Last updated: 2026-05-03

This document defines the reusable PowerForge pattern for rebuilding a Linux-hosted static website from source control, encrypted secrets, and a site-owned recovery manifest.

The goal is not only to back up a VPS. The goal is to make a fresh server boring to rebuild.

## Goals

- Recreate a production host from a clean Ubuntu installation.
- Keep rebuildable assets in Git and generated web output out of backup history.
- Capture enough runtime state to compare a live server against a manifest.
- Keep raw secrets out of source repositories.
- Support site-specific wrappers without hardcoding Evotec-only behavior into the engine.
- Preserve the existing PowerForge deploy model: clean checkout, pipeline build, timestamped releases, symlink promotion, and verification.

## Recovery Model

```text
Git repositories + recovery manifest + encrypted secrets + deploy pipeline
  -> bootstrap host
  -> restore configuration
  -> deploy site
  -> verify runtime
```

PowerForge owns the generic workflow. Each website owns a small manifest that names hosts, paths, services, packages, domains, and secrets.

## Engine Commands

The first implementation should expose the same workflow through the CLI and PowerShell module.

Proposed CLI shape:

```bash
powerforge-web server inspect --manifest deploy/linux/example.serverrecovery.json
powerforge-web server capture --manifest deploy/linux/example.serverrecovery.json --out ./_server-state
powerforge-web server bootstrap --manifest deploy/linux/example.serverrecovery.json --host new-host
powerforge-web server restore-secrets --manifest deploy/linux/example.serverrecovery.json
powerforge-web server deploy --manifest deploy/linux/example.serverrecovery.json
powerforge-web server verify --manifest deploy/linux/example.serverrecovery.json
```

Current implemented slices:

```bash
powerforge-web server inspect --manifest deploy/linux/example.serverrecovery.json
powerforge-web server plan --manifest deploy/linux/example.serverrecovery.json
powerforge-web server capture --manifest deploy/linux/example.serverrecovery.json --out ./_server-state/example
powerforge-web server capture --manifest deploy/linux/example.serverrecovery.json --out ./_server-state/example --encrypt-remote
powerforge-web server deploy --manifest deploy/linux/example.serverrecovery.json --dry-run
powerforge-web server verify --manifest deploy/linux/example.serverrecovery.json --fail-on-failure
powerforge-web server bootstrap-plan --manifest deploy/linux/example.serverrecovery.json --out ./_server-state/bootstrap-plan
powerforge-web server restore-secrets-plan --manifest deploy/linux/example.serverrecovery.json --out ./_server-state/restore-secrets-plan --archive encrypted-secrets.tar.gz.age
```

`server inspect` runs read-only SSH checks against the target host and compares live state with the manifest: OS, SSH posture, packages, Apache modules/config, managed paths, systemd units, UFW policy, Certbot renewal config, required secret-path presence, and release symlinks. Pass `--fail-on-drift` when CI or automation should exit non-zero on drift.

`server plan` loads the manifest, summarizes recovery coverage, emits the planned stages, and warns when encrypted capture is configured but no encryption recipient environment variable is available.

`server capture` uses SSH to collect manifest-defined command outputs, streams the plain capture file set into `plain-files.tar.gz`, writes `capture-summary.json`, and creates a restore checklist. Encrypted files are skipped unless `backupTarget.recipient` is set or the configured `backupTarget.recipientEnv` environment variable is present. By default, the secret tar stream is encrypted by local `age`; with `--encrypt-remote`, the target host runs `tar | age` and only the encrypted `.age` blob is streamed back to the workstation.

`server deploy` runs the manifest's deploy commands over SSH. Use `--dry-run` to print the resolved remote commands without executing them, and `--fail-on-failure` when automation should exit non-zero if a required deploy command fails.

`server verify` runs the manifest's operational health checks, such as Apache config validation, local service health checks, Certbot dry-runs, Cloudflare origin sync, and public URL checks. Pass `--fail-on-failure` when the command should exit non-zero if a required command or URL check fails.

`server bootstrap-plan` generates a reviewable markdown plan, JSON plan, and LF-normalized shell script draft for rebuilding a fresh host. Manual and sensitive steps are left as blocking TODOs in the script so it cannot silently deploy without restored secrets.

`server restore-secrets-plan` generates a markdown plan, JSON plan, and LF-normalized `restore-secrets.sh` draft for an encrypted secret bundle. The script requires `age`, decrypts to a temporary directory, lists archive contents, rejects absolute or path-traversal archive entries, and refuses to extract into `/` unless `POWERFORGE_RESTORE_SECRETS_CONFIRM=YES` is set.

Proposed PowerShell shape:

```powershell
Test-PowerForgeServerRecovery -Manifest .\deploy\linux\example.serverrecovery.json
Backup-PowerForgeServerState -Manifest .\deploy\linux\example.serverrecovery.json
Initialize-PowerForgeServer -Manifest .\deploy\linux\example.serverrecovery.json -ComputerName new-host
Restore-PowerForgeServerSecret -Manifest .\deploy\linux\example.serverrecovery.json
Invoke-PowerForgeServerDeploy -Manifest .\deploy\linux\example.serverrecovery.json
```

The PowerShell cmdlets should stay thin. Manifest parsing, planning, redaction, command generation, and report shaping belong in shared PowerForge services so the CLI, module, and future UI can reuse them.

## Manifest Responsibilities

A recovery manifest should describe:

- target host and SSH connection defaults
- expected operating system family and version
- package prerequisites
- repository checkouts and branches
- filesystem layout
- Apache modules, vhosts, and managed includes
- systemd services and timers
- firewall policy, including Cloudflare-origin locking when used
- Certbot certificates and renewal validation commands
- capture sets for plain configuration and encrypted secrets
- bootstrap, deploy, and verification commands
- Cloudflare DNS/cache expectations when a site uses Cloudflare

The manifest must not contain secret values. It may contain secret paths and secret environment variable names.

Manifest files are trusted operator input. Command fields are executed on the target host through SSH, so review changes to bootstrap, deploy, verify, and capture command entries with the same care as shell scripts.

## Capture Sets

Recovery uses two capture sets.

Plain capture:

- Apache vhosts and managed includes
- systemd unit files and drop-ins
- UFW status
- package list
- current release symlink targets
- Certbot renewal config without private keys
- deploy metadata and current Git commits

Encrypted capture:

- deployment environment files
- Cloudflare API token credentials
- contact relay SMTP/API secrets
- certificate private keys, if the site chooses to make certificate-key recovery possible

Encrypted capture requires an explicit recipient such as an age or GPG public key. If no recipient is configured, PowerForge should skip encrypted capture and report the missing secret recovery coverage.

## Bootstrap Stages

The engine should plan and run these stages:

1. Connect to the host and confirm OS support.
2. Install packages and language runtimes.
3. Create service users, directories, and permissions.
4. Configure SSH, UFW, and base security posture.
5. Install Apache modules, vhosts, and managed includes.
6. Install systemd services and timers.
7. Clone or update website and engine repositories.
8. Restore or prompt for secrets.
9. Run the site deploy script.
10. Verify local services, origin behavior, public URLs, and certificate renewal.

Stages must be resumable. A failed stage should leave a report that tells the operator what already happened and what remains.

## What Not To Back Up As Primary State

Do not treat these as the primary recovery source:

- generated `_site-*` outputs
- timestamped release directories
- pipeline temp folders
- downloaded project-source working trees when they are reproducible from lock files
- package caches

Those are rebuildable. Capture them only when diagnosing an incident, not as the normal recovery path.

## Drift Detection

`server inspect` should compare live state against the manifest and report:

- missing packages
- disabled or failed services
- unmanaged Apache includes
- SSH port/authentication drift
- UFW rules outside the expected policy
- Certbot certificates that cannot renew
- release symlink targets that do not match deploy metadata
- secrets that are required but not restorable

The default result should be a human-readable table plus a JSON report for CI or scheduled checks.

## GitHub Backup Target

PowerForge may publish capture artifacts to a private GitHub repository, but only with this split:

- plain config snapshots may be committed directly
- secret bundles must be encrypted before commit
- raw tokens, private keys, and env files must never be committed

For most sites, a separate private backup repository is cleaner than storing generated backup artifacts in the engine source repository.

`backupTarget.retention.keepLatest` can define how many timestamped captures should remain in the backup repository working tree. This is a publication policy for wrappers or future publish commands; Git history still preserves older committed captures unless repository history is rewritten.

## Evotec Reference

Evotec uses this pattern with a site-local manifest under the Website repository:

```text
deploy/linux/evotec.serverrecovery.json
```

The manifest names the OVH host, Apache/Cloudflare/certbot/contact-relay runtime, and recovery capture policy. The reusable behavior belongs here in PowerForge; the Evotec-specific paths and domains belong in the Website repo.
