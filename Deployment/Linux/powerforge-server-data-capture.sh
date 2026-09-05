#!/usr/bin/env bash
set -Eeuo pipefail
PATH='/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin'
export PATH

log() {
  printf 'powerforge-server-data-capture: %s\n' "$*" >&2
}

die() {
  log "$*"
  exit 1
}

require_command() {
  local name="$1" resolved
  resolved="$(command -v -- "$name" 2>/dev/null || true)"
  [[ -n "$resolved" && "$resolved" == /* ]] || die "required command is unavailable: $name"
  printf '%s\n' "$resolved"
}

assert_identifier() {
  [[ "$1" =~ ^[A-Za-z0-9_][A-Za-z0-9_-]{0,63}$ ]] || die "unsafe identifier: $1"
}

assert_unix_identity() {
  [[ "$1" =~ ^[A-Za-z_][A-Za-z0-9_-]{0,31}$ ]] || die "unsafe Unix identity: $1"
}

assert_database_name() {
  [[ "$1" =~ ^[A-Za-z_][A-Za-z0-9_]{0,62}$ ]] || die "unsafe PostgreSQL database name: $1"
}

assert_absolute_path() {
  local path="$1"
  [[ "$path" =~ ^/[A-Za-z0-9._/-]+$ ]] || die "unsafe absolute path: $path"
  [[ "$path" != '/' && "$path" != *'//'* && "$path" != */ ]] || die "path must be a dedicated exact path: $path"
  [[ ! "$path" =~ (^|/)\.{1,2}(/|$) ]] || die "path contains a traversal segment: $path"
}

assert_dedicated_export_root() {
  case "$1" in
    /etc|/home|/root|/srv|/var|/var/lib) die "export root must be a dedicated directory below a system data root: $1" ;;
  esac
}

paths_overlap() {
  [[ "$1" == "$2" || "$1" == "$2/"* || "$2" == "$1/"* ]]
}

[[ "${EUID}" -eq 0 ]] || die 'capture must run as root'
[[ "$#" -eq 1 ]] || die 'usage: powerforge-server-data-capture <server-recovery-manifest.json>'
umask 0027

readonly manifest="$1"
[[ -f "$manifest" && ! -L "$manifest" ]] || die "manifest must be a regular file: $manifest"

jq_bin="$(require_command jq)"
age_bin="$(require_command age)"
tar_bin="$(require_command tar)"
rsync_bin="$(require_command rsync)"
sha256_bin="$(require_command sha256sum)"
pg_dump_bin="$(require_command pg_dump)"
pg_dumpall_bin="$(require_command pg_dumpall)"
pg_restore_bin="$(require_command pg_restore)"
psql_bin="$(require_command psql)"
runuser_bin="$(require_command runuser)"
flock_bin="$(require_command flock)"
mktemp_bin="$(require_command mktemp)"
stat_bin="$(require_command stat)"
readlink_bin="$(require_command readlink)"
readonly jq_bin age_bin tar_bin rsync_bin sha256_bin pg_dump_bin pg_dumpall_bin pg_restore_bin psql_bin runuser_bin flock_bin mktemp_bin stat_bin readlink_bin

manifest_uid="$($stat_bin -c '%u' "$manifest")"
manifest_mode="$($stat_bin -c '%a' "$manifest")"
[[ "$manifest_uid" == '0' ]] || die 'manifest must be owned by root'
(( (8#$manifest_mode & 0022) == 0 )) || die 'manifest must not be group- or world-writable'

"$jq_bin" -e '
  .schemaVersion == 2 and
  (.durableBackup | type == "object") and
  (.durableBackup.databases | type == "array" and length > 0) and
  (.durableBackup.encryptedFiles | type == "array" and length > 0) and
  (.durableBackup.artifactStores | type == "array" and length > 0)
' "$manifest" >/dev/null || die 'manifest does not contain a complete schema-v2 durableBackup contract'

export_root="$($jq_bin -er '.durableBackup.exportRoot' "$manifest")"
export_group="$($jq_bin -er '.durableBackup.exportGroup' "$manifest")"
recipient="$($jq_bin -er '.durableBackup.recipient' "$manifest")"
retention_hours="$($jq_bin -er '.durableBackup.stagingRetentionHours' "$manifest")"
assert_absolute_path "$export_root"
assert_dedicated_export_root "$export_root"
assert_unix_identity "$export_group"
[[ "$recipient" =~ ^age1[a-z0-9]+$ ]] || die 'durable backup requires a literal age public recipient'
if [[ ! "$retention_hours" =~ ^[0-9]+$ ]] || ((retention_hours < 24 || retention_hours > 720)); then
  die 'staging retention must be from 24 through 720 hours'
fi
getent group "$export_group" >/dev/null || die "export group does not exist: $export_group"

export_parent="${export_root%/*}"
[[ -n "$export_parent" ]] || export_parent='/'
[[ -d "$export_parent" && ! -L "$export_parent" && "$($readlink_bin -f -- "$export_parent")" == "$export_parent" ]] ||
  die 'export root parent must be a canonical non-link directory'
if [[ -e "$export_root" || -L "$export_root" ]]; then
  [[ -d "$export_root" && ! -L "$export_root" && "$($readlink_bin -f -- "$export_root")" == "$export_root" ]] ||
    die 'existing export root must be a canonical non-link directory'
fi
if [[ -e "$export_root/snapshots" || -L "$export_root/snapshots" ]]; then
  [[ -d "$export_root/snapshots" && ! -L "$export_root/snapshots" && "$($readlink_bin -f -- "$export_root/snapshots")" == "$export_root/snapshots" ]] ||
    die 'existing export snapshot root must be a canonical non-link directory'
fi
install -d -m 0750 -o root -g "$export_group" "$export_root" "$export_root/snapshots"
[[ -d "$export_root" && ! -L "$export_root" && "$($readlink_bin -f -- "$export_root")" == "$export_root" ]] ||
  die 'export root must be a canonical non-link directory'
[[ -d "$export_root/snapshots" && ! -L "$export_root/snapshots" && "$($readlink_bin -f -- "$export_root/snapshots")" == "$export_root/snapshots" ]] ||
  die 'export snapshot root must be a canonical non-link directory'
exec 9>"$export_root/.capture.lock"
"$flock_bin" -n 9 || die 'another durable capture is already running'

stamp="$(date -u +%Y%m%dT%H%M%SZ)"
readonly stamp
[[ "$stamp" =~ ^[0-9]{8}T[0-9]{6}Z$ ]] || die 'unable to generate a safe UTC capture stamp'
partial="$export_root/snapshots/.partial-${stamp}-$$"
final="$export_root/snapshots/$stamp"
checksum_temp="$export_root/.checksums-${stamp}-$$"
work=''
[[ ! -e "$partial" && ! -e "$final" ]] || die "capture path already exists: $final"

cleanup() {
  if [[ -n "${partial:-}" && "$partial" == "$export_root/snapshots/.partial-"* && -d "$partial" ]]; then
    if [[ ! -L "$partial" && "$($readlink_bin -f -- "${partial%/*}")" == "$export_root/snapshots" ]]; then
      rm -rf -- "$partial"
    else
      log "refusing to clean unsafe partial capture path: $partial"
    fi
  fi
  if [[ -n "${checksum_temp:-}" && "$checksum_temp" == "$export_root/.checksums-"* && -f "$checksum_temp" ]]; then
    rm -f -- "$checksum_temp"
  fi
  if [[ -n "${work:-}" && "$work" == /var/tmp/powerforge-server-data-capture.* && -d "$work" ]]; then
    rm -rf -- "$work"
  fi
}
trap cleanup EXIT INT TERM

install -d -m 0750 -o root -g "$export_group" "$partial"
install -d -m 0750 -o root -g "$export_group" "$partial/artifacts"
work="$($mktemp_bin -d /var/tmp/powerforge-server-data-capture.XXXXXXXX)"
[[ "$work" == /var/tmp/powerforge-server-data-capture.* && -d "$work" && ! -L "$work" ]] ||
  die 'unable to create a safe database staging directory'
chown postgres:postgres "$work"
chmod 0700 "$work"
install -d -m 0700 -o postgres -g postgres "$work/databases"

database_ids=()
while IFS=$'\t' read -r id provider database required; do
  assert_identifier "$id"
  [[ "$provider" == 'postgresql' ]] || die "unsupported database provider for $id: $provider"
  assert_database_name "$database"
  [[ "$required" == 'true' || "$required" == 'false' ]] || die "invalid required flag for database: $id"

  if ! "$runuser_bin" -u postgres -- "$psql_bin" -Atqc "select 1 from pg_database where datname = '$database'" | grep -qx '1'; then
    [[ "$required" == 'false' ]] && { log "optional database is absent: $database"; continue; }
    die "required PostgreSQL database is absent: $database"
  fi

  dump_path="$work/databases/$id.dump"
  "$runuser_bin" -u postgres -- "$pg_dump_bin" --format=custom --no-owner --no-privileges --file="$dump_path" --dbname="$database"
  [[ -s "$dump_path" ]] || die "PostgreSQL dump is empty: $database"
  "$pg_restore_bin" --list "$dump_path" >/dev/null || die "PostgreSQL dump cannot be listed: $database"
  database_ids+=("$id")
done < <("$jq_bin" -r '.durableBackup.databases[] | [.id, .provider, .database, (.required // false)] | @tsv' "$manifest")
(( ${#database_ids[@]} > 0 )) || die 'durable capture did not produce any database dump'

"$runuser_bin" -u postgres -- "$pg_dumpall_bin" --globals-only >"$work/databases/postgresql-globals.sql"
[[ -s "$work/databases/postgresql-globals.sql" ]] || die 'PostgreSQL globals dump is empty'

tar_args=(-C "$work" databases)
encrypted_paths=()
while IFS=$'\t' read -r path required; do
  assert_absolute_path "$path"
  [[ "$required" == 'true' || "$required" == 'false' ]] || die "invalid required flag for encrypted file: $path"
  encrypted_paths+=("$path")
  if [[ ! -e "$path" && ! -L "$path" ]]; then
    [[ "$required" == 'false' ]] && { log "optional encrypted path is absent: $path"; continue; }
    die "required encrypted path is absent: $path"
  fi
  [[ -f "$path" && ! -L "$path" ]] || die "encrypted path must be a regular non-link file: $path"
  [[ "$($readlink_bin -f -- "$path")" == "$path" ]] || die "encrypted path must be canonical: $path"
  tar_args+=(-C / "${path#/}")
done < <("$jq_bin" -r '.durableBackup.encryptedFiles[] | [.target, (.required // false)] | @tsv' "$manifest")

"$tar_bin" -czf - "${tar_args[@]}" | "$age_bin" -r "$recipient" -o "$partial/recovery.tar.gz.age"
[[ -s "$partial/recovery.tar.gz.age" ]] || die 'encrypted recovery bundle is empty'

previous="$(find "$export_root/snapshots" -mindepth 1 -maxdepth 1 -type d -name '????????T??????Z' -print | sort | tail -n 1)"
artifact_ids=()
while IFS=$'\t' read -r id source required; do
  assert_identifier "$id"
  assert_absolute_path "$source"
  [[ "$required" == 'true' || "$required" == 'false' ]] || die "invalid required flag for artifact store: $id"
  paths_overlap "$source" "$export_root" &&
    die "artifact store overlaps durable export root: $source and $export_root"
  for encrypted_path in "${encrypted_paths[@]}"; do
    paths_overlap "$encrypted_path" "$source" &&
      die "encrypted path overlaps plaintext artifact store: $encrypted_path and $source"
  done
  if [[ ! -d "$source" || -L "$source" ]]; then
    [[ "$required" == 'false' ]] && { log "optional artifact store is absent: $source"; continue; }
    die "required artifact store is absent or unsafe: $source"
  fi
  [[ "$($readlink_bin -f -- "$source")" == "$source" ]] || die "artifact store must be canonical: $source"
  unsafe_source_entry="$(find "$source" -mindepth 1 ! -type f ! -type d -print -quit)"
  [[ -z "$unsafe_source_entry" ]] || die "artifact store contains a link or special entry: $unsafe_source_entry"

  destination="$partial/artifacts/$id"
  install -d -m 0750 -o root -g "$export_group" "$destination"
  # Ownership and modes are normalized after capture. Ignoring those source attributes lets
  # --link-dest reuse immutable bytes from the preceding root:export snapshot.
  rsync_args=(--archive --hard-links --no-owner --no-group --no-perms --no-links --no-devices --no-specials --delete-delay)
  if [[ -n "$previous" && -d "$previous/artifacts/$id" ]]; then
    rsync_args+=("--link-dest=$previous/artifacts/$id")
  fi
  "$rsync_bin" "${rsync_args[@]}" "$source/" "$destination/"
  artifact_ids+=("$id")
done < <("$jq_bin" -r '.durableBackup.artifactStores[] | [.id, .path, (.required // false)] | @tsv' "$manifest")
(( ${#artifact_ids[@]} > 0 )) || die 'durable capture did not export any artifact store'

unsafe_snapshot_entry="$(find "$partial" -mindepth 1 ! -type f ! -type d -print -quit)"
[[ -z "$unsafe_snapshot_entry" ]] || die "snapshot contains a link or special entry: $unsafe_snapshot_entry"
while IFS= read -r -d '' snapshot_entry; do
  relative_entry="${snapshot_entry#"$partial/"}"
  [[ "$relative_entry" != *$'\n'* && "$relative_entry" != *$'\r'* && "$relative_entry" != *\\* ]] ||
    die "snapshot path cannot be represented safely in SHA256SUMS: $relative_entry"
done < <(find "$partial" -mindepth 1 -print0)

database_json="$($jq_bin -c '[.durableBackup.databases[] | {id, provider, database, required: (.required // false)}]' "$manifest")"
artifact_json="$($jq_bin -c '[.durableBackup.artifactStores[] | {id, path, required: (.required // false)}]' "$manifest")"
manifest_sha="$($sha256_bin "$manifest" | awk '{print $1}')"
# shellcheck disable=SC2016 # jq variables are intentionally evaluated by jq, not Bash.
"$jq_bin" -n \
  --arg capturedAtUtc "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --arg sourceManifestSha256 "$manifest_sha" \
  --argjson databases "$database_json" \
  --argjson artifactStores "$artifact_json" \
  '{schemaVersion:1,capturedAtUtc:$capturedAtUtc,sourceManifestSha256:$sourceManifestSha256,databases:$databases,artifactStores:$artifactStores}' \
  >"$partial/metadata.json"

rm -rf -- "$work"
work=''
chown -R "root:$export_group" "$partial"
find "$partial" -type d -exec chmod 0750 {} +
find "$partial" -type f -exec chmod 0640 {} +
(
  cd "$partial"
  find . -type f ! -name SHA256SUMS ! -name READY -print0 |
    sort -z |
    xargs -0 "$sha256_bin" >"$checksum_temp"
  mv -- "$checksum_temp" SHA256SUMS
  "$sha256_bin" -c SHA256SUMS >/dev/null
)
printf 'ready\n' >"$partial/READY"
chown "root:$export_group" "$partial/READY" "$partial/SHA256SUMS"
chmod 0640 "$partial/READY" "$partial/SHA256SUMS"
mv -T -- "$partial" "$final"
partial=''
log "completed durable snapshot $stamp"

retention_minutes=$((retention_hours * 60))
mapfile -d '' expired < <(find "$export_root/snapshots" -mindepth 1 -maxdepth 1 -type d -name '????????T??????Z' -mmin "+$retention_minutes" -print0)
snapshot_count="$(find "$export_root/snapshots" -mindepth 1 -maxdepth 1 -type d -name '????????T??????Z' -print | wc -l)"
for expired_path in "${expired[@]}"; do
  ((snapshot_count > 2)) || break
  [[ "$expired_path" == "$export_root/snapshots/"????????T??????Z && -d "$expired_path" && ! -L "$expired_path" ]] ||
    die "refusing to prune unsafe capture path: $expired_path"
  [[ "$($readlink_bin -f -- "$expired_path")" == "$expired_path" && "$($readlink_bin -f -- "${expired_path%/*}")" == "$export_root/snapshots" ]] ||
    die "refusing to prune capture outside the canonical export root: $expired_path"
  rm -rf -- "$expired_path"
  snapshot_count=$((snapshot_count - 1))
done

trap - EXIT INT TERM
