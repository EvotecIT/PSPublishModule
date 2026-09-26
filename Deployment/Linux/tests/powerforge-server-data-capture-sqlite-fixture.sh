#!/usr/bin/env bash
set -Eeuo pipefail

[[ ${EUID} -eq 0 ]] || { echo 'Run this fixture as root.' >&2; exit 1; }
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
capture_script="$script_dir/../powerforge-server-data-capture.sh"
fixture_root="$(mktemp -d /var/tmp/powerforge-sqlite-capture.XXXXXXXX)"
pg_database=''
cleanup() {
  if [[ -n "$pg_database" ]]; then
    runuser -u postgres -- dropdb --if-exists "$pg_database" >/dev/null 2>&1 || true
  fi
  if [[ "$fixture_root" == /var/tmp/powerforge-sqlite-capture.* && -d "$fixture_root" && ! -L "$fixture_root" ]]; then
    rm -rf -- "$fixture_root"
  fi
}
trap cleanup EXIT

chmod 0711 "$fixture_root"
install -d -m 0700 -o nobody "$fixture_root/data"
install -d -m 0700 -o root "$fixture_root/artifacts" "$fixture_root/restore"
database="$fixture_root/data/public.db"
runuser -u nobody -- sqlite3 "$database" 'CREATE TABLE updates (id INTEGER PRIMARY KEY, title TEXT NOT NULL); INSERT INTO updates (title) VALUES ("public update");'
printf 'fixture=yes\n' >"$fixture_root/config.env"
printf 'immutable release\n' >"$fixture_root/artifacts/release.txt"
age-keygen -o "$fixture_root/identity" >/dev/null 2>&1
recipient="$(age-keygen -y "$fixture_root/identity")"

jq -n \
  --arg exportRoot "$fixture_root/export" \
  --arg recipient "$recipient" \
  --arg database "$database" \
  --arg encryptedFile "$fixture_root/config.env" \
  --arg artifacts "$fixture_root/artifacts" \
  '{schemaVersion:2,name:"sqlite-fixture",target:{host:"localhost"},durableBackup:{exportRoot:$exportRoot,exportGroup:"root",recipient:$recipient,stagingRetentionHours:48,databases:[{id:"public",provider:"sqlite",database:$database,runAs:"nobody",required:true}],encryptedFiles:[{target:$encryptedFile,required:true,sensitive:true}],artifactStores:[{id:"release",path:$artifacts,required:true}]}}' \
  >"$fixture_root/manifest.json"

bash "$capture_script" "$fixture_root/manifest.json"
snapshot="$(find "$fixture_root/export/snapshots" -mindepth 1 -maxdepth 1 -type d -name '????????T??????Z' -print -quit)"
[[ -n "$snapshot" && -f "$snapshot/READY" ]] || { echo 'No ready snapshot was produced.' >&2; exit 1; }
age -d -i "$fixture_root/identity" -o "$fixture_root/recovery.tar.gz" "$snapshot/recovery.tar.gz.age"
tar -xzf "$fixture_root/recovery.tar.gz" -C "$fixture_root/restore" databases/public.sqlite
[[ "$(sqlite3 -readonly "$fixture_root/restore/databases/public.sqlite" 'PRAGMA integrity_check;')" == 'ok' ]]
[[ "$(sqlite3 -readonly "$fixture_root/restore/databases/public.sqlite" 'SELECT title FROM updates;')" == 'public update' ]]

# A manifest accepted directly by the root script must not export live SQLite bytes
# through the plaintext artifact-store lane.
jq --arg root "$fixture_root/export-overlap" --arg path "$fixture_root/data" \
  '.durableBackup.exportRoot = $root | .durableBackup.artifactStores[0].path = $path' \
  "$fixture_root/manifest.json" >"$fixture_root/overlap.json"
if bash "$capture_script" "$fixture_root/overlap.json" >"$fixture_root/overlap.log" 2>&1; then
  echo 'A plaintext artifact store accepted a live SQLite database.' >&2
  exit 1
fi
grep -q 'SQLite database overlaps plaintext artifact store' "$fixture_root/overlap.log"
[[ -z "$(find "$fixture_root/export-overlap/snapshots" -mindepth 1 -maxdepth 1 -type d -name '????????T??????Z' -print -quit)" ]]

jq --arg root "$fixture_root/export-sidecar" --arg path "$database-wal" \
  '.durableBackup.exportRoot = $root | .durableBackup.encryptedFiles[0].target = $path | .durableBackup.encryptedFiles[0].required = false' \
  "$fixture_root/manifest.json" >"$fixture_root/sidecar.json"
if bash "$capture_script" "$fixture_root/sidecar.json" >"$fixture_root/sidecar.log" 2>&1; then
  echo 'A directly captured SQLite WAL sidecar was accepted.' >&2
  exit 1
fi
grep -q 'SQLite database overlaps directly captured encrypted file' "$fixture_root/sidecar.log"

# A separate artifact-store path can still name the live database inode.
ln -- "$database" "$fixture_root/artifacts/public-hardlink.db"
jq --arg root "$fixture_root/export-hardlink" '.durableBackup.exportRoot = $root' \
  "$fixture_root/manifest.json" >"$fixture_root/hardlink.json"
if bash "$capture_script" "$fixture_root/hardlink.json" >"$fixture_root/hardlink.log" 2>&1; then
  echo 'A plaintext artifact store accepted a hard link to live SQLite data.' >&2
  exit 1
fi
grep -q 'artifact store contains a hard link to live SQLite data' "$fixture_root/hardlink.log"
[[ -z "$(find "$fixture_root/export-hardlink/snapshots" -mindepth 1 -maxdepth 1 -type d -name '????????T??????Z' -print -quit)" ]]
rm -- "$fixture_root/artifacts/public-hardlink.db"

ln -- "$fixture_root/config.env" "$fixture_root/artifacts/config-hardlink.env"
jq --arg root "$fixture_root/export-secret-hardlink" '.durableBackup.exportRoot = $root' \
  "$fixture_root/manifest.json" >"$fixture_root/secret-hardlink.json"
if bash "$capture_script" "$fixture_root/secret-hardlink.json" >"$fixture_root/secret-hardlink.log" 2>&1; then
  echo 'A plaintext artifact store accepted a hard link to directly encrypted data.' >&2
  exit 1
fi
grep -q 'artifact store contains a hard link to directly encrypted data' "$fixture_root/secret-hardlink.log"
rm -- "$fixture_root/artifacts/config-hardlink.env"

install -d -m 0700 -o nobody "$fixture_root/artifacts/writable"
jq --arg root "$fixture_root/export-writable" '.durableBackup.exportRoot = $root' \
  "$fixture_root/manifest.json" >"$fixture_root/writable.json"
if bash "$capture_script" "$fixture_root/writable.json" >"$fixture_root/writable.log" 2>&1; then
  echo 'A SQLite-writer-owned artifact directory was accepted.' >&2
  exit 1
fi
grep -q 'SQLite account owns artifact-store directory' "$fixture_root/writable.log"
rmdir -- "$fixture_root/artifacts/writable"

# When PostgreSQL is present, both provider staging paths must produce one
# encrypted bundle without giving either service account write access to it.
if command -v pg_isready >/dev/null && pg_isready -q; then
  pg_database="pf_sqlite_fixture_$$"
  runuser -u postgres -- createdb "$pg_database"
  runuser -u postgres -- psql -d "$pg_database" -qAtc 'CREATE TABLE evidence (value integer NOT NULL); INSERT INTO evidence VALUES (7);'
  jq --arg root "$fixture_root/export-mixed" --arg database "$pg_database" \
    '.durableBackup.exportRoot = $root | .durableBackup.databases += [{id:"postgres",provider:"postgresql",database:$database,required:true}]' \
    "$fixture_root/manifest.json" >"$fixture_root/mixed.json"
  bash "$capture_script" "$fixture_root/mixed.json"
  mixed_snapshot="$(find "$fixture_root/export-mixed/snapshots" -mindepth 1 -maxdepth 1 -type d -name '????????T??????Z' -print -quit)"
  [[ -n "$mixed_snapshot" && -f "$mixed_snapshot/READY" ]]
  age -d -i "$fixture_root/identity" -o "$fixture_root/mixed-recovery.tar.gz" "$mixed_snapshot/recovery.tar.gz.age"
  tar -xzf "$fixture_root/mixed-recovery.tar.gz" -C "$fixture_root/restore" databases/public.sqlite databases/postgres.dump databases/postgresql-globals.sql
  [[ "$(sqlite3 -readonly "$fixture_root/restore/databases/public.sqlite" 'PRAGMA integrity_check;')" == 'ok' ]]
  pg_restore --list "$fixture_root/restore/databases/postgres.dump" >/dev/null
  [[ -s "$fixture_root/restore/databases/postgresql-globals.sql" ]]
fi
echo 'SQLite online capture, encrypted restore, overlap rejection, and mixed-provider capture passed.'
