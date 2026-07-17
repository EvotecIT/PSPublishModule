# PowerForge.Web Server Recovery

Last updated: 2026-07-17

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

For systemd application releases, pair this recovery model with [PowerForge.Web Linux Service Deployment](PowerForge.Web.LinuxServiceDeployment.md). The service promoter owns reproducible code release and rollback; the recovery manifest owns host rebuild, certificates, encrypted secrets, and mutable state.

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
powerforge-web server capture --manifest deploy/linux/example.serverrecovery.json --out ./_server-state/example --fail-on-failure
powerforge-web server capture --manifest deploy/linux/example.serverrecovery.json --out ./_server-state/example --encrypt-remote
powerforge-web server deploy --manifest deploy/linux/example.serverrecovery.json --dry-run
powerforge-web server verify --manifest deploy/linux/example.serverrecovery.json --fail-on-failure
powerforge-web server scaffold --domain example.com --repository Owner/Site --repository-ref <commit> --engine-ref <commit> --host web.example.net --backup-repository Owner/ServerBackups --backup-recipient <age-public-recipient> --out .
powerforge-web server bootstrap-plan --manifest deploy/linux/example.serverrecovery.json --out ./_server-state/bootstrap-plan
powerforge-web server restore-secrets-plan --manifest deploy/linux/example.serverrecovery.json --out ./_server-state/restore-secrets-plan --archive encrypted-secrets.tar.gz.age
```

`server inspect` runs read-only SSH checks against the target host and compares live state with the manifest: OS, SSH posture, packages, pinned and clean repository checkouts, Apache modules/config, managed path kind/owner/group/mode and source bytes, systemd unit source bytes, UFW policy, Certbot renewal config, required secret-path presence, and release symlinks. Pass `--fail-on-drift` when CI or automation should exit non-zero on drift.

`server plan` loads the manifest, summarizes recovery coverage, emits the planned stages, and warns when encrypted capture is configured but no encryption recipient environment variable is available. `server validate` is currently an alias for `server plan`; it does not run a schema-only validation pass.

`server capture` uses SSH to collect manifest-defined command outputs, streams the plain capture file set into `plain-files.tar.gz`, writes `capture-summary.json`, and creates a restore checklist. Repositories with `refCaptureCommandId` are pinned in the captured `manifest.json` from that required command's exact commit output; missing or malformed output removes any stale source ref and raises a capture warning. Encrypted files are skipped unless `backupTarget.recipient` is set or the configured `backupTarget.recipientEnv` environment variable is present. By default, the secret tar stream is encrypted by local `age`; with `--encrypt-remote`, the target host delegates to the root-owned `powerforge-server-encrypted-capture` wrapper whose sudoers entry fixes the recipient and exact capture paths, so the restricted backup identity can only receive ciphertext. Capture warnings remain non-fatal for interactive use unless `--fail-on-failure` is passed, which exits non-zero when required command captures, revision hydration, archive capture, or encrypted capture reports warnings. The reusable backup action surfaces only bounded archive/encryption stderr on failure; it never prints command captures or archive contents into workflow logs.

`server deploy` runs the manifest's deploy commands over SSH. Use `--dry-run` to print the resolved remote commands without executing them, and `--fail-on-failure` when automation should exit non-zero if a required deploy command fails.

`server verify` runs the manifest's operational health checks, such as Apache config validation, local service health checks, Certbot dry-runs, Cloudflare origin sync, and public URL checks. Pass `--fail-on-failure` when the command should exit non-zero if a required command or URL check fails.

`server bootstrap-plan` generates a reviewable markdown plan, JSON plan, and LF-normalized shell script draft for rebuilding a fresh Ubuntu host. The preflight checks the declared Ubuntu release and CPU architecture instead of continuing on a mismatched host. The proven runtime lane is Ubuntu 24.04 LTS with .NET 8 or .NET 10 from Ubuntu's package feed; PowerShell uses Microsoft's official Ubuntu repository and currently requires x64. Managed accounts are created before owned directories. Repositories may declare `sshIdentityFile` and `sshKnownHostsFile`; PowerForge then applies both to clone and fetch through a strict `GIT_SSH_COMMAND` with `IdentitiesOnly` and host-key checking. Both paths must also appear in `bootstrapRequiredFiles`, so a private clone fails closed until its isolated key and pinned host keys have been restored. Set repository `ref` to an immutable commit or tag when recovery must restore an exact engine or application revision after cloning the configured branch. A repository using `refCaptureCommandId` also fails closed when a source manifest has not yet been hydrated; executable recovery plans must use the captured manifest. Bootstrap creates missing repository parents, requires each checkout root and every managed source path and ancestor to be root-owned, non-symlinked, non-group/world-writable, requires a clean checkout, and runs non-root target writes as the declared owner/group. Repository-owned managed, Apache, and systemd files all use the same exact-target installer: PowerForge rejects repository-overlapping targets, canonically escaping sources, and sources or targets that overlap declared secrets or encrypted capture paths. Set `validation` to `sudoers` for a root-owned `0440` file directly below `/etc/sudoers.d`; bootstrap validates the candidate and the complete sudoers policy, rejects non-file or symlink targets, and restores the previous exact target if validation fails or replacement is interrupted. Secret steps are rerunnable presence guards: they stop with an operator-facing restore instruction while state is missing and continue without exposing values after restoration.

Recovery schema v2 makes those hardened file and runtime contracts explicit. A schema-v1 manifest remains tied to its pinned v1 engine revision; migrate `schemaVersion`, remove the retired Apache `reloadCommand`, validate against the new engine ref, and repin the schema URL and workflow together. Apache activation remains a declared deploy command so a site can use the shared certificate-aware activation helper or another transactional implementation instead of an implicit bootstrap reload.

The generated runtime commands follow the supported package-manager paths documented by [Microsoft for .NET on Ubuntu](https://learn.microsoft.com/dotnet/core/install/linux-ubuntu-install) and [Microsoft for PowerShell on Ubuntu](https://learn.microsoft.com/powershell/scripting/install/install-ubuntu). The manifest stores only requested runtime versions, never repository credentials or package secrets.

`server scaffold` generates the thin caller workflows, recovery manifest, site environment, strict sudoers, restricted-key examples, Apache baseline, and onboarding checklist for a static Linux site. The required `--repository-ref` seeds the first bootstrap with an exact application commit; later captures replace it from the live deployment through `refCaptureCommandId`. Private-repository scaffolds use typed strict-SSH repository fields instead of a host alias or client config that would itself depend on the first clone. Default site IDs include a short hash of the full domain to avoid cross-domain collisions and always begin with a letter for sudoers compatibility. The scaffold writes no private key or API-token value, refuses to overwrite by default, emits no `www` Apache/certificate alias unless `--www` is explicit, keeps Cloudflare disabled unless `--cloudflare` is explicit, and derives backup sudoers from the generated manifest capture sets.

`server restore-secrets-plan` generates a markdown plan, JSON plan, and LF-normalized `restore-secrets.sh` draft for an encrypted secret bundle. The script requires `age` and `python3`, decrypts into a mode-700 temporary directory, validates every archive member against exact encrypted capture roots, rejects traversal, duplicate paths, hard links, unsafe symlinks, special files, and extraction through existing symlink parents, then refuses to restore unless it runs as root with `POWERFORGE_RESTORE_SECRETS_CONFIRM=YES`. Declared owner, group, and mode values replace filename-based permission guesses. For a directory secret, owner and group apply to its restored archive members through descriptor-relative no-follow traversal; the declared mode applies with `fchmod` to restored directories and derives a non-executable regular-file mode from the same read/write bits (for example, `0750` directories and `0640` files). Symbolic links and unrelated pre-existing descendants are not modified, numeric UID/GID declarations remain supported, and overlapping secret roots are applied from parent to child so the most-specific metadata wins.

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
- service accounts required by managed path ownership
- private-repository bootstrap prerequisite files
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

Mark capture entries with `required: true` when a successful recovery depends on them. If either archive contains a required entry, PowerForge runs that archive in strict mode and any missing listed path fails capture. An archive whose entries are all optional retains best-effort missing-file behavior.

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

Use the `powerforge-server-backup` composite action to run encrypted capture and publication on a schedule or manual dispatch. Pin the action to an exact commit and keep the job in the caller repository with its protected deployment environment. GitHub does not pass a caller repository's environment secrets into a cross-repository reusable workflow, so this caller-owned job is a required security boundary, not duplicated implementation.

The protected backup action rejects `pull_request`, `pull_request_target`, and
`merge_group` events so a pull-request head cannot select the recovery manifest that
runs with server and backup-repository credentials. Use a trusted schedule or manual
dispatch instead.

- `SERVER_SSH_PRIVATE_KEY` and `SERVER_SSH_KNOWN_HOSTS` for the least-privilege server capture account
- `BACKUP_REPOSITORY_SSH_PRIVATE_KEY` and `BACKUP_REPOSITORY_SSH_KNOWN_HOSTS` for a write-scoped deploy key on the private backup repository

The two identities must remain separate. The server account should only be able to execute the exact read-only inspection and archive commands required by the manifest. The backup repository key should be a deploy key for that repository only.

```yaml
jobs:
  backup:
    runs-on: ubuntu-latest
    environment: production
    permissions:
      contents: read
    steps:
      - uses: EvotecIT/PSPublishModule/.github/actions/powerforge-server-backup@POWERFORGE_COMMIT
        with:
          manifest-path: deploy/linux/example.serverrecovery.json
          capture-user: powerforge-example-backup
          server-ssh-private-key: ${{ secrets.SERVER_SSH_PRIVATE_KEY }}
          server-ssh-known-hosts: ${{ secrets.SERVER_SSH_KNOWN_HOSTS }}
          backup-repository-ssh-private-key: ${{ secrets.BACKUP_REPOSITORY_SSH_PRIVATE_KEY }}
          backup-repository-ssh-known-hosts: ${{ secrets.BACKUP_REPOSITORY_SSH_KNOWN_HOSTS }}

concurrency:
  group: powerforge-server-backup
  cancel-in-progress: false
```

The action requires remote age encryption, a non-empty plain archive, a non-empty encrypted archive, a warning-free capture summary, exact source/engine/run provenance, and SHA-256 checksums before it clones the backup repository. It commits a timestamped directory under `backupTarget.path`, applies `backupTarget.retention.keepLatestInTree`, and retries fetch/rebase/push races without uploading recovery material as a GitHub Actions artifact. The older `keepLatest` property remains a compatibility alias.

This setting deliberately controls only the captures visible in the current Git tree. Git history is preserved, so it is not a data-destruction or repository-size retention policy. Use a separately reviewed history rewrite or a non-Git backup backend if historical deletion is required.

## Evotec Reference

Evotec uses this pattern with a site-local manifest under the Website repository:

```text
deploy/linux/evotec.serverrecovery.json
```

The manifest names the OVH host, Apache/Cloudflare/certbot/contact-relay runtime, and recovery capture policy. The reusable behavior belongs here in PowerForge; the Evotec-specific paths and domains belong in the Website repo.
