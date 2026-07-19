# PowerForge.Web Server Recovery

Last updated: 2026-07-18

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
powerforge-web server scaffold --domain example.com --repository Owner/Site --repository-ref <commit> --engine-ref <commit> --host web.example.net --backup-repository Owner/ServerBackups --backup-recipient <age-public-recipient> [--acme-account-id <certbot-account-id>] --out .
powerforge-web server bootstrap-plan --manifest deploy/linux/example.serverrecovery.json --out ./_server-state/bootstrap-plan
powerforge-web server restore-secrets-plan --manifest deploy/linux/example.serverrecovery.json --out ./_server-state/restore-secrets-plan --archive encrypted-secrets.tar.gz.age
```

`server inspect` runs read-only SSH checks against the target host and compares live state with the manifest: OS, SSH posture, packages, pinned and clean repository checkouts, Apache modules/config, managed path kind/owner/group/mode and source bytes, systemd unit source bytes, UFW policy, Certbot renewal config, required secret-path presence, and release symlinks. Pass `--fail-on-drift` when CI or automation should exit non-zero on drift.

`server plan` loads the manifest, summarizes recovery coverage, emits the planned stages, and warns when encrypted capture is configured but no encryption recipient environment variable is available. `server validate` is currently an alias for `server plan`; it does not run a schema-only validation pass.

`server capture` uses SSH to collect manifest-defined command outputs, streams the plain capture file set into `plain-files.tar.gz`, writes `capture-summary.json`, and creates a restore checklist. Repositories with `refCaptureCommandId` are pinned in the captured `manifest.json` from that required command's exact commit output. Multi-surface deployments can use `refCaptureCommandIds`; every listed required, non-sensitive command must return the same exact commit before the repository is pinned. Missing, malformed, or disagreeing output removes any stale source ref and raises a capture warning. Encrypted files are skipped unless `backupTarget.recipient` is set or the configured `backupTarget.recipientEnv` environment variable is present. Secret capture always delegates to the root-owned `powerforge-server-encrypted-capture` helper, whose exact sudoers command fixes the public recipient and required/optional paths so the restricted backup identity can receive only ciphertext. The older `--encrypt-remote` marker remains accepted for caller compatibility, but remote encryption is now the only secure execution mode. Capture warnings remain non-fatal for interactive use unless `--fail-on-failure` is passed, which exits non-zero when required command captures, revision hydration, archive capture, or encrypted capture reports warnings. The reusable backup action surfaces only bounded archive/encryption stderr on failure; it never prints command captures or archive contents into workflow logs.

`server deploy` runs the manifest's deploy commands over SSH. The default `deploy.operationLockOwner: engine` holds every declared operation lock for the complete command set and verifies that the lock session is still alive before and after each remote command. A command that owns the same lock, including `powerforge-site-deploy`, must instead use `deploy.operationLockOwner: command`; that mode requires exactly one non-sensitive deploy command, and that command must hold every declared operation lock for its complete lifetime. This explicit ownership boundary avoids nested self-deadlock without silently dropping coordination. Use `--dry-run` to print the resolved remote commands without executing them, and `--fail-on-failure` when automation should exit non-zero if a required deploy command fails.

`server verify` runs the manifest's operational health checks, such as Apache config validation, local service health checks, Certbot dry-runs, Cloudflare origin sync, and public URL checks. Pass `--fail-on-failure` when the command should exit non-zero if a required command or URL check fails.

`server bootstrap-plan` generates a reviewable markdown plan, JSON plan, and LF-normalized shell script draft for rebuilding a fresh Ubuntu host. The preflight checks the declared Ubuntu release and CPU architecture instead of continuing on a mismatched host. The proven runtime lane is Ubuntu 24.04 LTS with .NET 8 or .NET 10 from Ubuntu's package feed; PowerShell uses Microsoft's official Ubuntu repository and currently requires x64. Bootstrap prepares and acquires the declared operation locks before package, runtime, account, filesystem, repository, Apache, systemd, firewall, secret, or deploy mutation. Engine-owned deploy commands remain inside that lock window; immediately before a command-owned deploy, bootstrap closes its lock descriptors so the single deployment command can acquire the same locks without self-deadlock. Managed accounts are created before owned directories. Repositories may declare `sshIdentityFile` and `sshKnownHostsFile`; PowerForge then applies both to clone and fetch through a strict `GIT_SSH_COMMAND` with `IdentitiesOnly` and host-key checking. Both paths must also appear in `bootstrapRequiredFiles`, so a private clone fails closed until its isolated key and pinned host keys have been restored. Set repository `ref` to an immutable commit or tag when recovery must restore an exact engine or application revision after cloning the configured branch. A repository using `refCaptureCommandId` also fails closed when a source manifest has not yet been hydrated; executable recovery plans must use the captured manifest. Bootstrap rejects duplicate or nested checkout roots, creates missing repository parents, requires each checkout root and every managed source path and ancestor to be root-owned, non-symlinked, non-group/world-writable, and requires a clean checkout. Every managed source must also be a regular tracked file whose bytes match the declared repository revision immediately before use, so ignored, untracked, modified, or Git-symlink inputs cannot become recovery state. Non-root directories are created as the declared owner where that owner has access; service directories beneath a root-controlled parent fall back to a root-owned convergence step when the owner, group, or mode still differs. Repository-owned managed, Apache, and systemd files all use the same exact-target installer: PowerForge rejects repository-overlapping targets, canonically escaping sources, and sources or targets that overlap declared secrets or encrypted capture paths. Set `validation` to `sudoers` for a root-owned `0440` file directly below `/etc/sudoers.d`; bootstrap validates the candidate and the complete sudoers policy, rejects non-file or symlink targets, and restores the previous exact target if validation fails or replacement is interrupted. Secret presence guards and generated deferred-secret installers are executable and rerunnable: they stop with an operator-facing restore instruction while state is missing, install staged ignored repository secrets without exposing values, remove staging state, and accept a later rerun only when the existing target still has the declared metadata.

Managed owner and group values may use Unix names or canonical numeric IDs; ambiguous zero-padded and out-of-range IDs are rejected. Inspection applies the same root-controlled ancestor policy as bootstrap, and validation rejects descendants beneath managed file targets while allowing normal directory nesting.

Recovery schema v2 makes those hardened file and runtime contracts explicit. A schema-v1 manifest remains tied to its pinned v1 engine revision; migrate `schemaVersion`, remove the retired Apache `reloadCommand`, validate against the new engine ref, and repin the schema URL and workflow together. Apache `enabled` declarations are reconciled as one bootstrap transaction: PowerForge records the prior activation state, applies site/conf changes, validates the complete configuration, reloads the declared service, and restores the prior activation state if validation or reload fails. A later deploy command may still use the shared certificate-aware activation helper when HTTPS activation depends on certificate availability.

The generated runtime commands follow the supported package-manager paths documented by [Microsoft for .NET on Ubuntu](https://learn.microsoft.com/dotnet/core/install/linux-ubuntu-install) and [Microsoft for PowerShell on Ubuntu](https://learn.microsoft.com/powershell/scripting/install/install-ubuntu). The manifest stores only requested runtime versions, never repository credentials or package secrets.

`server scaffold` generates the thin caller workflows, recovery manifest, site environment, strict sudoers, restricted-key examples, Apache baseline, and onboarding checklist for a static Linux site. The required `--repository-ref` seeds the first bootstrap with an exact application commit; later captures replace it from the live deployment through `refCaptureCommandId`. Public-repository scaffolds keep the server host out of generated files and route both deployment and encrypted capture through the protected `DEPLOYMENT_HOST` environment secret; private-repository manifests retain the host as a backward-compatible fallback. Private source repositories use typed strict-SSH repository fields instead of a host alias or client config that would itself depend on the first clone. Default site IDs include a short hash of the full domain to avoid cross-domain collisions and always begin with a letter for sudoers compatibility. Use `--website-root .` for a site rooted at the repository top level. Repeat `--recovery-watch-path <glob>` when recovery validation must also run for repository files outside the generated deployment defaults; PowerForge validates, deduplicates, and safely quotes those positive repository-relative globs. After the first certificate has been issued, pass its exact Certbot account directory name through `--acme-account-id`; the scaffold then encrypts that one account directory and enables renewal dry-run verification. Without that option, the manifest explicitly records incomplete ACME renewal coverage and omits the dry-run instead of claiming a restorable account. The scaffold writes no private key or API-token value, refuses to overwrite by default, emits no `www` Apache/certificate alias unless `--www` is explicit, keeps Cloudflare disabled unless `--cloudflare` is explicit, and derives backup sudoers from the generated manifest capture sets. Encrypted capture also requires one managed non-root account whose home and key directory are root-controlled, plus one repository-pinned root-owned mode-600 `authorized_keys` file containing a single `restrict`-prefixed Ed25519 public key. The capture identity can read that authorization but cannot remove its restriction.

When a manifest contains both capture and deploy work, declare the same exact `/var/lock/*.lock` paths used by the host deployment commands in `operationLocks`. PowerForge provisions each volatile lock through `systemd-tmpfiles`, verifies a root-owned mode-644 regular file before every acquisition, acquires locks in deterministic order, holds every declared lock for the complete capture, engine-owned `server deploy`, or generated bootstrap mutation window, and fails the operation if the remote lock session disappears or lock metadata drifts. A command-owned `powerforge-site-deploy` must declare exactly the lock derived from its `--site` value; command-owned deployment then uses the explicit handoff described above. Apache `sites` and `conf` entries may declare `enabled: true` or `enabled: false`; bootstrap and inspect then manage both file content and activation state without caller-side `a2ensite` or `a2enconf` shell steps. The generated HTTPS site deliberately omits a fixed activation declaration because its state is certificate-dependent: the shared helper always enables valid HTTP configuration, returns success for an HTTP-only fresh host, and enables HTTPS only when the certificate files are present.

`server restore-secrets-plan` generates a markdown plan, JSON plan, and LF-normalized `restore-secrets.sh` draft for an encrypted secret bundle. The script requires `age` and `python3`, decrypts into a mode-700 temporary directory, validates every archive member against exact encrypted capture roots, rejects traversal, non-canonical paths, duplicate paths, hard links, unsafe symlinks, special files, and extraction through existing symlink parents, then refuses to restore unless it runs as root with `POWERFORGE_RESTORE_SECRETS_CONFIRM=YES`. Declared owner, group, and mode values replace filename-based permission guesses. For a directory secret, owner and group apply to its restored archive members through descriptor-relative no-follow traversal; the declared mode applies with `fchmod` to restored directories and derives a non-executable regular-file mode from the same read/write bits (for example, `0750` directories and `0640` files). Symbolic links and unrelated pre-existing descendants are not modified, numeric UID/GID declarations remain supported, and overlapping secret roots are applied from parent to child so the most-specific metadata wins. A file secret inside a declared repository must use one exact encrypted capture entry, set `restoreAfterRepositories: true`, be ignored and untracked by the pinned repository, and declare explicit owner, group, and mode. Restore stages it below a deterministic root-only directory, bootstrap clones the pinned repository first, rejects tracked or symlink targets, installs the ignored secret, and removes the staged copy. A later bootstrap rerun accepts the existing ignored, untracked, regular target only when its declared ownership and mode still match; otherwise it fails closed. This prevents secret extraction from pre-creating or replacing the clone target while preserving rerunnability.

All plain and encrypted archive entries must be exact paths; wildcard expansion is rejected by both manifest validation and runtime capture. Certificate backup paths must also stay site-scoped. Capture exact renewal configuration, one exact lineage directory directly below `archive` or `live`, and one exact account directory in the form `/etc/letsencrypt/accounts/<server>/<directory>/<account-id>`; shared roots, multi-account subtrees, and individual lineage/account descendants are rejected because they can include neighboring domains or produce incomplete recovery state. The `accounts`, `archive`, and `live` private-state roots and all of their descendants are also rejected from plain capture, even when a manifest forgets to declare a matching secret.

## Pull Request Validation

Use the credential-free recovery validation action in pull requests. It validates the manifest against the schema from the exact action commit, requires the manifest schema URL and action pin to match, generates bootstrap and secret-restore plans in runner-temporary storage, parses the generated scripts with Bash, and checks them with ShellCheck. It does not execute generated commands, contact the target host, capture state, restore secrets, deploy, or accept credential inputs.

```yaml
permissions:
  contents: read

jobs:
  recovery-manifest:
    runs-on: ubuntu-latest
    steps:
      - uses: EvotecIT/PSPublishModule/.github/actions/powerforge-server-recovery-validate@<exact-commit>
        with:
          manifest-path: deploy/linux/example.serverrecovery.json
          capture-user: powerforge-example-backup
```

Set `capture-user` to the dedicated non-root Linux account whose strict sudoers policy authorizes encrypted recovery capture. Validation binds that exact principal to the root-run hardened helper from the pinned PowerForge engine, rejects broader command aliases, validates all managed sudoers files both individually and as one combined policy, and accepts privileged capture inspection only from the shared read-only command allowlist. Extend that allowlist in PowerForge with focused security tests when another generally useful inspection command is needed; do not add a caller-controlled bypass. Keep `fail-on-warnings` at its default `true` for migration and maintenance pull requests. The protected backup action remains a separate environment-gated workflow because it needs SSH and backup-repository identities; PR validation must not receive those credentials.

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

Mark capture entries with `required: true` when a successful recovery depends on them. Plain archives containing a required entry remain strict, while an all-optional plain archive retains best-effort missing-file behavior. Remote encrypted capture preflights every required entry and includes each optional entry only when it exists, so one absent optional secret cannot discard otherwise valid encrypted recovery state.

## Bootstrap Stages

The engine should plan and run these stages:

1. Connect to the host and confirm OS support.
2. Install packages and language runtimes.
3. Create service users, directories, and permissions.
4. Configure SSH, UFW, and base security posture.
5. Install Apache modules, vhosts, and managed includes.
6. Install systemd services and timers and reload systemd.
7. Clone or update website and engine repositories.
8. Restore or prompt for secrets.
9. Run the site deploy script.
10. Enable and start declared systemd units after secrets and deployment are ready.
11. Verify local services, origin behavior, public URLs, and certificate renewal.

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
