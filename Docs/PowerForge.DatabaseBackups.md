# Database backups to Synology

PowerForge.Backups captures consistent SQL Server and SQLite databases and sends their native files and service configuration to a restricted Synology receiver. Each completed folder contains ordinary `.bak`, `.sqlite` and JSON files. DbaClientX owns database capture and SQL Server restore safety. The backup library owns the transfer receipt, local state and disposable restore drill; the CLI supplies command dispatch.

The Windows scheduled task runs without an interactive login. A separate NAS task verifies stored native files and retains the newest successful capture from each of fourteen distinct days. Failed or stale backups hold retention. The upload key permits only the receiver protocol, with no shell, forwarding, deletion or retention command.

## Capture and verification

The configuration lists explicit database ids, providers and paths or SQL Server connection strings. SQL Server uses a checksummed `COPY_ONLY` full backup followed by native verification. SQLite uses DbaClientX's online backup API, including a held snapshot for WAL databases. Captures are independent points in time; the manifest records each database's capture time. Cross-service ingestion and cursors need application checks during recovery.

Each SQLite capture passes `quick_check` and configured representative count queries. The set includes a SHA-256 inventory and copies of explicitly selected service configuration. Protected secrets retain their original protection. Keep the recovery credentials for machine-bound DPAPI and environment references in a separate secure credential store so a replacement machine can reprovision them.

The NAS rereads each stored file, verifies its size and SHA-256, binds the capture metadata to a verification receipt, and publishes the complete directory atomically. SSH protects and compresses transport; stored databases use their normal formats. The workstation removes its marked temporary capture directory after the attempt. Failures remain visible in `latest-run.json`; the previous successful receipt remains in `last-success.json`.

## Machine-local installation

Build the CLI for the workstation platform. Configuration, the dedicated SSH key, status and installed binaries remain outside the source checkout. The backup worker does not need a decryption key.

`Deployment/Windows/Install-PowerForgeDatabaseBackup.ps1` installs the published CLI, restricted SSH identity and a SYSTEM scheduled task. Its parameters specify the installation and staging roots, source files, monitor identity, daily time and SQL Server target. The staging volume must be mounted, and paths must not traverse reparse points. Protect staging for SYSTEM, Administrators and the SQL Server service identity. Give the monitor read access only to projected status.

The SQL Server task identity needs `db_backupoperator` on the selected database and `CREATE ANY DATABASE` for native backup verification. The initial restore drill runs under an operator identity that can create, check and drop its disposable database. Neither task changes the source database's recovery model.

Install `Deployment/Synology/powerforge-database-backup.py` as a root-owned, non-writable executable and provide a private root-owned JSON configuration. Required configuration fields are `root`, `hostId`, `databases` (id-to-provider map), `configurationIds`, `keepDaily`, `maxAgeHours`, `maxArchiveBytes` (total set size ceiling) and `minimumFreeBytes`. Create the root marker `.powerforge-database-backup-root` containing `powerforge-database-backup-v1`, with private permissions and physical `snapshots` and `incoming` child directories.

Create private `.receiver.lock` and `.catalog.lock` files in that root. The receiver/maintenance lock serializes writers; the short catalog guard protects publication and deletion while status reads the previous completed set during uploads. Publication flushes populated directories before acknowledging completion. Before deleting same-day alternatives, retention rehashes the retained copy for that day.

Bind a dedicated Ed25519 public key to a fixed source address, `restrict`, and a forced command invoking the receiver with its exact configuration path and `forced` operation. Preserve unrelated authorized keys. Use DSM's supported Task Scheduler interface to run the receiver's `maintain` operation daily. Do not edit its scheduler database.

## Run and recover

```text
PowerForge.Backups.Cli run <machine-local-config.json>
PowerForge.Backups.Cli restore-drill <config.json> <selected-native-nas-folder> <expected-capture-sha256>
```

Retrieve the selected native folder from the NAS through the operator access path and pin its `capture.json` SHA-256 from the NAS receipt. The upload key cannot retrieve or remove prior sets. The drill rejects links and unsafe entry paths, verifies database and configuration hashes, performs SQLite `integrity_check`, and restores SQL Server under a generated new database name. DbaClientX refuses replacement of an existing database. Disposable SQL connections disable pooling. The drill checks the restored database and removes its owned database/files after validation; failed attempts and cleanup failures replace current drill health.

The drill verifies recoverability without replacing live service files. During an actual recovery, provision the service identity and connection, rebind protected configuration secrets from the secure recovery store, then validate service readiness and ingestion before starting normal operation. SQL Server backups require a compatible server version. A successful transfer or `RESTORE VERIFYONLY` alone is not an application restore proof.

## Monitoring

`Deployment/Monitoring/verify-database-backups.py` reads projected Windows state, the exact Windows scheduled task, NAS receipt and maintenance state, DSM task configuration, and the latest restore drill. Its workstation check covers DDCT, DDNext and Sectigo and flags captures older than 30 hours, disabled or changed schedules, failed attempts, missing verification, cleanup failures and restore drills older than 30 days. A running job can use its still-fresh verified prior capture. History starts with one successful recovery point and accumulates over fourteen days.

The Evotec health monitor calls this helper alongside the existing public web, OVH and Synology checks. It reports access failures as coverage gaps and never dispatches backups, decrypts production data, restores databases or prunes storage.
