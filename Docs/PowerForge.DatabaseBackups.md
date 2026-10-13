# Database backups to Synology

PowerForge.Backups captures consistent SQL Server and SQLite databases and sends their native files and service configuration to a restricted Synology receiver. Each completed folder contains ordinary `.bak`, `.sqlite` and JSON files. DbaClientX owns database capture and SQL Server restore safety. The backup library owns the transfer receipt, local state and disposable restore drill; the CLI supplies command dispatch.

Capture identifiers use Gregorian UTC timestamps regardless of the workstation's culture. Database and recovery-file identifiers must be portable lowercase names; Windows device basenames such as `aux` and `con` are rejected. Redirected SSH output is bounded while reading, and an input or output failure stops the child before waiting for completion.

The Windows scheduled task runs without an interactive login. A separate NAS task verifies stored native files and retains successful captures from fourteen distinct days. Once maintenance selects a daily recovery point, later uploads cannot replace that selection. The latest complete capture is also retained so same-day retries remain available. Failed or stale backups hold retention. The upload key permits only the receiver protocol, with no shell, forwarding, deletion or retention command.

## Capture and verification

The configuration lists explicit database ids, providers and paths or SQL Server connection strings. SQL Server uses a checksummed `COPY_ONLY` full backup followed by native verification. SQLite uses DbaClientX's online backup API, including a held snapshot for WAL databases. Captures are independent points in time; the manifest records each database's capture time. Cross-service ingestion and cursors need application checks during recovery.

Preflight rejects state outputs that overlap a database, SQLite sidecar, recovery configuration, SSH credential/pin file, executable or the loaded configuration itself. State and its companion receipts remain outside staging. Staging uses a dedicated directory rather than a volume root. Restore drills also reject output/staging paths that overlap the selected recovery folder before writing anything there.

The worker checks that the SSH executable, identity, host pins and recovery files are readable and non-empty before capturing any database. Null source inventories or validation-query arrays are rejected during configuration validation. Portable identifiers must match in full, including the final character.

Each SQLite capture passes `quick_check` and configured representative count queries. The set includes a SHA-256 inventory and copies of explicitly selected service configuration. Protected secrets retain their original protection. Keep the recovery credentials for machine-bound DPAPI and environment references in a separate secure credential store so a replacement machine can reprovision them.

The NAS rereads each stored file, verifies its size and SHA-256, binds the capture metadata to a verification receipt, and publishes the complete directory atomically. SSH protects and compresses transport; stored databases use their normal formats. The workstation removes its marked temporary capture directory after the attempt. Failures remain visible in `latest-run.json`; the previous successful receipt remains in `last-success.json`.

## Machine-local installation

Build the CLI for the workstation platform. Configuration, the dedicated SSH key, status and installed binaries remain outside the source checkout. The backup worker does not need a decryption key.

`Deployment/Windows/Install-PowerForgeDatabaseBackup.ps1` and its adjacent `DatabaseBackupInstallation.ps1` helper install the published CLI, restricted SSH identity and a SYSTEM scheduled task. Parameters specify the installation and staging roots, source files, monitor identity, daily time in `HH:mm` format and SQL Server target. The two roots must be separate, with neither inside the other. The staging volume must be mounted, and paths must not traverse reparse points. Existing trees with untrusted owners or write grants are rejected before installation. Accepted trees receive Administrators ownership and protected permissions, including removal of explicit descendant grants. Staging allows SYSTEM, Administrators and the SQL Server service identity; the monitor receives only projected-status read access. The scheduled task allows the configured backup timeout plus sixty minutes for cleanup; an omitted timeout retains the engine's 240-minute default.

Provision physical parent directories before installation. Every ancestor must have trusted ownership and deny untrusted deletion, child deletion, ownership changes and permission changes; a protected child alone cannot stop an ancestor from being renamed. Standard create-only grants and inheritance-only grants on an existing ancestor are allowed. The installer checks source trees and their ancestors before reading configuration or executing the supplied CLI. Run the installer itself from a trusted location. New managed roots receive their private permissions at creation. The installer does not rewrite shared parent or volume permissions; move staging to a separately protected operational namespace when development directories permit broad replacement rights.

Before registering or enabling the SYSTEM task, installation also verifies trusted ownership and write permissions on the configured SSH executable, identity and host-pin files and their ancestors. Standard Windows tools owned by TrustedInstaller are supported; externally writable tool or authentication paths are rejected.

The SQL Server task identity needs `db_backupoperator` on the selected database and `CREATE ANY DATABASE` for native backup verification. The initial restore drill runs under an operator identity that can create, check and drop its disposable database. Neither task changes the source database's recovery model.

Install `Deployment/Synology/powerforge-database-backup.py` as a root-owned, non-writable executable and provide a private root-owned JSON configuration. Required configuration fields are `root`, `hostId`, `databases` (id-to-provider map), `configurationIds`, `keepDaily`, `maxAgeHours`, `maxArchiveBytes` (total set size ceiling) and `minimumFreeBytes`. Create the root marker `.powerforge-database-backup-root` containing `powerforge-database-backup-v1`, with private permissions and physical `snapshots` and `incoming` child directories.

The database map must be non-empty and use portable IDs with `sqlite` or `sqlserver` providers; recovery IDs must be portable and unique. `incoming` and `snapshots` must share a filesystem for atomic publication, and the receiver checks free capacity on `incoming`.

Create private, root-owned `.receiver.lock` and `.catalog.lock` files in that root. Managed directories, markers, receipts and recovery files must also be private and root-owned. The receiver/maintenance lock serializes writers; the short catalog guard protects publication and deletion while status reads the previous completed set during uploads. A NAS-side deadline bounds header, body and publication processing independently of the SSH client. Its default is five hours; optional `receiveTimeoutSeconds` accepts one to 86,400 seconds. A timed-out upload releases the writer lock and removes its marked partial folder. Publication flushes populated directories before acknowledging completion. If synchronization fails after the publication rename, the verified folder remains available and the original error is reported without an acknowledgement.

The recovery manifest must agree with the header's database/configuration inventories and capture identity. New uploads must be within `maxAgeHours`; this does not restrict inspection of retained older captures. Maintenance records its daily selections outside the upload inventory and preserves them on failure. Each maintenance run rehashes every retained daily recovery point before deleting alternatives. Upload credentials still authorize new backup content; checksums alone do not prove an uncompromised producer, and independent local receipts and restore checks remain required.

The native transfer header is limited to 64 KiB and the recovery manifest to 1 MiB. Stored receipt/checksum metadata is also size-checked before parsing; database files retain the configured total-set size limit. These bounds prevent metadata from becoming an unbounded memory allocation in the root receiver.

Bind a dedicated Ed25519 public key to a fixed source address, `restrict`, and a forced command invoking the receiver with its exact configuration path and `forced` operation. Preserve unrelated authorized keys. Use DSM's supported Task Scheduler interface to run the receiver's `maintain` operation daily. Do not edit its scheduler database.

## Run and recover

```text
PowerForge.Backups.Cli validate <machine-local-config.json>
PowerForge.Backups.Cli validate-config <machine-local-config.json>
PowerForge.Backups.Cli run <machine-local-config.json>
PowerForge.Backups.Cli restore-drill <config.json> <selected-native-nas-folder> <expected-capture-sha256>
```

`validate` checks configuration, physical paths, path separation and readable non-empty transfer inputs without starting a backup or opening databases. `validate-config` performs only configuration/path checks, allowing preparation before the dedicated SSH files are installed. Inputs, staging and durable state must not traverse junctions or symlinks. Path separation runs before state writes or staging cleanup, including when loading configuration or selecting a restore folder. The Windows installer checks configuration before changing the installation and checks actual transfer inputs after installing them, before registering the task. It requires projected state at `status/latest-run.json` under the installation root.

Retrieve the selected native folder from the NAS through the operator access path and pin its `capture.json` SHA-256 from the NAS receipt. The upload key cannot retrieve or remove prior sets. The drill rejects links and unsafe entry paths, verifies database and configuration hashes, performs SQLite `integrity_check`, and restores SQL Server under a generated new database name. DbaClientX refuses replacement of an existing database. Disposable SQL connections disable pooling. The drill checks the restored database and removes its owned database/files after validation; failed attempts and cleanup failures replace current drill health.

The drill verifies recoverability without replacing live service files. During an actual recovery, provision the service identity and connection, rebind protected configuration secrets from the secure recovery store, then validate service readiness and ingestion before starting normal operation. SQL Server backups require a compatible server version. A successful transfer or `RESTORE VERIFYONLY` alone is not an application restore proof.

## Monitoring

`Deployment/Monitoring/verify-database-backups.py` reads projected Windows state, the exact Windows scheduled task, NAS receipt and maintenance state, DSM task configuration, and the latest restore drill. Its workstation check covers DDCT, DDNext and Sectigo and flags captures older than 30 hours, disabled or changed schedules, failed attempts, missing verification, cleanup failures and restore drills older than 30 days. A running job can use its still-fresh verified prior capture. History starts with one successful recovery point and accumulates over fourteen days.

The installer writes a non-secret `status/installation.json` receipt with the installation root, task name, daily time and run budget. The monitor compares the actual task with that receipt. For a custom installation root, set `EVOTEC_DATABASE_BACKUP_ROOT` in the monitor's environment; the default is the `PowerForge/DatabaseBackups` directory beneath `ProgramData`. The receipt never contains connection strings or access keys.

The Evotec health monitor calls this helper alongside the existing public web, OVH and Synology checks. It reports access failures as coverage gaps and never dispatches backups, decrypts production data, restores databases or prunes storage.
