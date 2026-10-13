# Database backups to Synology

PowerForge.Backups captures consistent SQL Server and SQLite databases, packages service configuration and recovery secrets, and sends an age-encrypted recovery archive to a restricted Synology receiver. DbaClientX owns database capture and SQL Server restore safety. The backup library owns the archive, transfer receipt, local state and disposable restore drill; the CLI supplies command dispatch.

The Windows scheduled task runs without an interactive login. A separate NAS task verifies stored ciphertext and retains the newest successful capture from each of fourteen distinct days. Failed or stale backups hold retention. The upload key permits only the receiver protocol, with no shell, forwarding, deletion or retention command.

## Capture and verification

The configuration lists explicit database ids, providers and paths or SQL Server connection strings. SQL Server uses a checksummed `COPY_ONLY` full backup followed by native verification. SQLite uses DbaClientX's online backup API, including a held snapshot for WAL databases. Captures are independent points in time; the archive records each database's capture time. Cross-service ingestion and cursors need application checks during recovery.

Each SQLite capture passes `quick_check` and configured representative count queries. The archive includes a SHA-256 inventory, service configuration and any explicitly selected machine-bound DPAPI or environment secrets. Recovered secrets enter the encryption stream directly; no plaintext recovered-secret file is created. User-bound DPAPI requires its own recovery identity and is refused by this unattended path.

The NAS rereads the stored ciphertext, verifies its size and SHA-256, binds the capture metadata to a verification receipt, and publishes the complete directory atomically. The workstation removes its marked temporary capture directory after the attempt. Failures remain visible in `latest-run.json`; the previous successful receipt remains in `last-success.json`.

## Machine-local installation

Build the CLI for the workstation platform, install age from its official release, and prepare an X25519 public recipient whose private identity has a separate recoverable copy. Keep the private decryption identity off the NAS. Configuration, keys, status and installed binaries remain outside the source checkout.

`Deployment/Windows/Install-PowerForgeDatabaseBackup.ps1` installs the published CLI, age executable, restricted SSH identity and a SYSTEM scheduled task. Its parameters specify the installation and staging roots, source files, monitor identity, daily time and SQL Server target. The staging volume must be mounted, and paths must not traverse reparse points. Protect staging for SYSTEM, Administrators and the SQL Server service identity. Give the monitor read access only to projected status.

The SQL Server task identity needs `db_backupoperator` on the selected database and `CREATE ANY DATABASE` for native backup verification. The initial restore drill runs under an operator identity that can create, check and drop its disposable database. Neither task changes the source database's recovery model.

Install `Deployment/Synology/powerforge-database-backup.py` as a root-owned, non-writable executable and provide a private root-owned JSON configuration. Required configuration fields are `root`, `hostId`, `databases` (id-to-provider map), `keepDaily`, `maxAgeHours`, `maxArchiveBytes` and `minimumFreeBytes`; `minimumRecoveredSecrets` can require known recovery material. Create the root marker `.powerforge-database-backup-root` containing `powerforge-database-backup-v1`, with private permissions and physical paths.

Bind a dedicated Ed25519 public key to a fixed source address, `restrict`, and a forced command invoking the receiver with its exact configuration path and `forced` operation. Preserve unrelated authorized keys. Use DSM's supported Task Scheduler interface to run the receiver's `maintain` operation daily. Do not edit its scheduler database.

## Run and recover

```text
PowerForge.Backups.Cli run <machine-local-config.json>
PowerForge.Backups.Cli restore-drill <config.json> <selected-nas-archive.age> <private-identity.agekey> <expected-sha256>
```

Retrieve the selected ciphertext from the NAS through the operator access path and pin its SHA-256 from the NAS receipt. The upload key cannot retrieve or remove prior archives. The drill authenticates decryption, rejects links and unsafe archive paths, verifies database and configuration hashes, performs SQLite `integrity_check`, and restores SQL Server under a generated new database name. DbaClientX refuses replacement of an existing database. The drill checks the restored database and removes its owned database/files after validation.

The drill verifies recoverability without replacing live service files. During an actual recovery, provision the service identity and connection, rebind protected configuration secrets from encrypted recovery material, then validate service readiness and ingestion before starting normal operation. SQL Server backups require a compatible server version. A successful transfer or `RESTORE VERIFYONLY` alone is not an application restore proof.

## Monitoring

`Deployment/Monitoring/verify-database-backups.py` reads projected Windows state, the exact Windows scheduled task, NAS receipt and maintenance state, DSM task configuration, and the latest restore drill. Its workstation check covers DDCT, DDNext and Sectigo and flags captures older than 30 hours, disabled or changed schedules, failed attempts, missing verification, cleanup failures and restore drills older than 30 days. A running job can use its still-fresh verified prior capture. History starts with one successful recovery point and accumulates over fourteen days.

The Evotec health monitor calls this helper alongside the existing public web, OVH and Synology checks. It reports access failures as coverage gaps and never dispatches backups, decrypts production data, restores databases or prunes storage.
