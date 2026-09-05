#!/usr/bin/env bash
set -Eeuo pipefail
PATH='/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin'
export PATH

log() {
  printf 'powerforge-server-backup-pull: %s\n' "$*" >&2
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

assert_local_path() {
  local path="$1"
  [[ "$path" =~ ^/[A-Za-z0-9._/-]+$ ]] || die "unsafe local path: $path"
  [[ "$path" != '/' && "$path" != *'//'* && "$path" != */ ]] || die "local path must be a dedicated exact path: $path"
  [[ ! "$path" =~ (^|/)\.{1,2}(/|$) ]] || die "local path contains a traversal segment: $path"
}

verify_snapshot() {
  local snapshot="$1" unsafe entry relative expected actual manifest_sha
  [[ -d "$snapshot" && ! -L "$snapshot" ]] || die "snapshot directory is unsafe: $snapshot"
  [[ "$($readlink_bin -f -- "$snapshot")" == "$snapshot" ]] || die "snapshot directory is not canonical: $snapshot"
  [[ -f "$snapshot/READY" && ! -L "$snapshot/READY" ]] || die "snapshot is not ready: $snapshot"
  [[ -f "$snapshot/SHA256SUMS" && ! -L "$snapshot/SHA256SUMS" ]] || die "snapshot checksum manifest is missing or unsafe: $snapshot"
  [[ -f "$snapshot/recovery.tar.gz.age" && ! -L "$snapshot/recovery.tar.gz.age" ]] ||
    die "snapshot encrypted bundle is missing or unsafe: $snapshot"

  unsafe="$(find "$snapshot" -mindepth 1 ! -type f ! -type d -print -quit)"
  [[ -z "$unsafe" ]] || die "snapshot contains a link or special entry: $unsafe"
  while IFS= read -r -d '' entry; do
    relative="${entry#"$snapshot/"}"
    [[ "$relative" != *$'\n'* && "$relative" != *$'\r'* && "$relative" != *\\* ]] ||
      die "snapshot path cannot be represented safely in SHA256SUMS: $relative"
  done < <(find "$snapshot" -mindepth 1 -print0)

  expected="$($mktemp_bin "$destination/.verify-expected.XXXXXXXX")"
  actual="$($mktemp_bin "$destination/.verify-actual.XXXXXXXX")"
  verification_temps+=("$expected" "$actual")
  (
    cd "$snapshot"
    # shellcheck disable=SC2016 # The program is evaluated by awk, not Bash.
    "$awk_bin" '
      length($0) < 67 || substr($0, 1, 64) !~ /^[a-f0-9]{64}$/ || substr($0, 65, 2) != "  " { exit 1 }
      { print substr($0, 67) }
    ' SHA256SUMS | "$sort_bin" >"$expected"
    find . -type f ! -name SHA256SUMS ! -name READY ! -name VERIFIED -print | "$sort_bin" >"$actual"
    "$cmp_bin" -s "$expected" "$actual"
    "$sha256_bin" -c SHA256SUMS >/dev/null
  ) || die "snapshot tree or checksum verification failed: $snapshot"
  [[ "$(head -n 1 "$snapshot/recovery.tar.gz.age")" == 'age-encryption.org/v1' ]] ||
    die "snapshot age header is invalid: $snapshot"
  manifest_sha="$($sha256_bin "$snapshot/SHA256SUMS" | cut -d ' ' -f 1)"
  printf 'verifiedAtUtc=%s\nmanifestSha256=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$manifest_sha" >"$snapshot/VERIFIED"
  chmod 0600 "$snapshot/VERIFIED"
  rm -f -- "$expected" "$actual"
}

has_current_verification() {
  local snapshot="$1" recorded current
  [[ -f "$snapshot/VERIFIED" && ! -L "$snapshot/VERIFIED" && -f "$snapshot/SHA256SUMS" ]] || return 1
  recorded="$(sed -n 's/^manifestSha256=//p' "$snapshot/VERIFIED")"
  current="$($sha256_bin "$snapshot/SHA256SUMS" | cut -d ' ' -f 1)"
  [[ "$recorded" =~ ^[a-f0-9]{64}$ && "$recorded" == "$current" ]]
}

remote=''
remote_root='/'
destination=''
identity=''
known_hosts=''
port='22'
keep_recent='56'
keep_weekly='8'
keep_monthly='12'

while (($# > 0)); do
  case "$1" in
    --remote) remote="${2:-}"; shift 2 ;;
    --remote-root) remote_root="${2:-}"; shift 2 ;;
    --destination) destination="${2:-}"; shift 2 ;;
    --identity) identity="${2:-}"; shift 2 ;;
    --known-hosts) known_hosts="${2:-}"; shift 2 ;;
    --port) port="${2:-}"; shift 2 ;;
    --keep-recent) keep_recent="${2:-}"; shift 2 ;;
    --keep-weekly) keep_weekly="${2:-}"; shift 2 ;;
    --keep-monthly) keep_monthly="${2:-}"; shift 2 ;;
    *) die "unsupported argument: $1" ;;
  esac
done

[[ "$remote" =~ ^[A-Za-z_][A-Za-z0-9_-]{0,31}@[A-Za-z0-9][A-Za-z0-9.-]{0,252}$ ]] || die 'remote must be a fixed user@host value'
[[ "$remote_root" == '/' || ( "$remote_root" =~ ^/[A-Za-z0-9._/-]*/$ && "$remote_root" != *'//'* ) ]] ||
  die 'remote root must be an absolute path ending in /'
[[ ! "$remote_root" =~ (^|/)\.{1,2}(/|$) ]] || die 'remote root contains a traversal segment'
assert_local_path "$destination"
assert_local_path "$identity"
assert_local_path "$known_hosts"
[[ "${EUID}" -eq 0 ]] || die 'destination pull must run as root'
umask 0077
[[ -f "$identity" && ! -L "$identity" ]] || die "SSH identity is missing or unsafe: $identity"
[[ -f "$known_hosts" && ! -L "$known_hosts" ]] || die "known-hosts file is missing or unsafe: $known_hosts"
if [[ ! "$port" =~ ^[0-9]+$ ]] || ((port < 1 || port > 65535)); then
  die 'SSH port must be from 1 through 65535'
fi
for value in "$keep_recent" "$keep_weekly" "$keep_monthly"; do
  [[ "$value" =~ ^[0-9]+$ ]] || die 'retention counts must be non-negative integers'
done
((keep_recent >= 1 && keep_recent <= 1000)) || die 'keep-recent must be from 1 through 1000'
((keep_weekly <= 260 && keep_monthly <= 120)) || die 'weekly or monthly retention is unreasonably large'

rsync_bin="$(require_command rsync)"
ssh_bin="$(require_command ssh)"
sha256_bin="$(require_command sha256sum)"
date_bin="$(require_command date)"
readlink_bin="$(require_command readlink)"
stat_bin="$(require_command stat)"
flock_bin="$(require_command flock)"
mktemp_bin="$(require_command mktemp)"
awk_bin="$(require_command awk)"
sort_bin="$(require_command sort)"
cmp_bin="$(require_command cmp)"
readonly rsync_bin ssh_bin sha256_bin date_bin readlink_bin stat_bin flock_bin mktemp_bin awk_bin sort_bin cmp_bin

readonly marker="$destination/.powerforge-server-backup-root"
[[ -d "$destination" && ! -L "$destination" ]] || die "destination must be an existing non-link directory: $destination"
[[ "$($readlink_bin -f -- "$destination")" == "$destination" ]] || die "destination must be a canonical physical path: $destination"
[[ -f "$marker" && ! -L "$marker" && "$(tr -d '\r\n' <"$marker")" == 'powerforge-server-backup-v1' ]] ||
  die "destination marker is missing or invalid: $marker"
[[ "$($stat_bin -c '%u:%a' "$destination")" == '0:700' ]] || die 'destination must be owned by root with mode 0700'
[[ "$($stat_bin -c '%u:%a' "$marker")" == '0:600' ]] || die 'destination marker must be owned by root with mode 0600'
snapshots_root="$destination/snapshots"
if [[ -e "$snapshots_root" || -L "$snapshots_root" ]]; then
  [[ -d "$snapshots_root" && ! -L "$snapshots_root" && "$($readlink_bin -f -- "$snapshots_root")" == "$snapshots_root" ]] ||
    die 'existing snapshot root must be a canonical non-link directory'
fi
install -d -m 0700 -o root -g root "$snapshots_root"
[[ ! -L "$snapshots_root" && "$($readlink_bin -f -- "$snapshots_root")" == "$snapshots_root" ]] ||
  die 'snapshot root must be a canonical non-link directory'

lock_file="$destination/.pull.lock"
[[ ! -e "$lock_file" || ( -f "$lock_file" && ! -L "$lock_file" ) ]] || die 'pull lock path is unsafe'
exec 9>"$lock_file"
"$flock_bin" -n 9 || die 'another backup pull is already running'
chown root:root "$lock_file"
chmod 0600 "$lock_file"

current_partial=''
remote_listing=''
remote_manifest=''
verification_temps=()
cleanup() {
  if [[ -n "${current_partial:-}" && "$current_partial" == "$snapshots_root/.partial-"* && -d "$current_partial" && ! -L "$current_partial" ]]; then
    rm -rf -- "$current_partial"
  fi
  for temporary in "${verification_temps[@]:-}" "$remote_listing" "$remote_manifest"; do
    [[ -n "$temporary" && "$temporary" == "$destination/"* && -f "$temporary" && ! -L "$temporary" ]] && rm -f -- "$temporary"
  done
  return 0
}
trap cleanup EXIT INT TERM

export RSYNC_RSH="$ssh_bin -p $port -i $identity -o UserKnownHostsFile=$known_hosts -o StrictHostKeyChecking=yes -o IdentitiesOnly=yes -o BatchMode=yes"
remote_listing="$($mktemp_bin "$destination/.remote-list.XXXXXXXX")"
"$rsync_bin" --list-only "$remote:${remote_root}snapshots/" >"$remote_listing"
mapfile -t remote_stamps < <(
  # shellcheck disable=SC2016 # The program is evaluated by awk, not Bash.
  "$awk_bin" '$1 ~ /^d/ && $NF ~ /^[0-9]{8}T[0-9]{6}Z\/?$/ { name=$NF; sub(/\/$/, "", name); print name }' "$remote_listing" |
    "$sort_bin"
)
(( ${#remote_stamps[@]} > 0 )) || die 'remote export does not contain a completed snapshot'

verified_now=0
for stamp in "${remote_stamps[@]}"; do
  snapshot="$snapshots_root/$stamp"
  if [[ -e "$snapshot" ]]; then
    [[ -d "$snapshot" && ! -L "$snapshot" && "$($readlink_bin -f -- "$snapshot")" == "$snapshot" ]] ||
      die "existing snapshot path is unsafe: $snapshot"
    remote_manifest="$destination/.remote-sha-${stamp}-$$"
    "$rsync_bin" "$remote:${remote_root}snapshots/$stamp/SHA256SUMS" "$remote_manifest"
    "$cmp_bin" -s "$remote_manifest" "$snapshot/SHA256SUMS" || die "remote snapshot changed after local publication: $stamp"
    rm -f -- "$remote_manifest"
    remote_manifest=''
    verify_snapshot "$snapshot"
  else
    current_partial="$snapshots_root/.partial-${stamp}-$$"
    install -d -m 0700 -o root -g root "$current_partial"
    rsync_args=(--archive --hard-links --no-links --no-devices --no-specials --partial --delay-updates)
    previous="$(find "$snapshots_root" -mindepth 1 -maxdepth 1 -type d -name '????????T??????Z' -print | "$sort_bin" | tail -n 1)"
    if [[ -n "$previous" && -d "$previous" && ! -L "$previous" ]]; then
      rsync_args+=("--link-dest=$previous")
    fi
    "$rsync_bin" "${rsync_args[@]}" "$remote:${remote_root}snapshots/$stamp/" "$current_partial/"
    verify_snapshot "$current_partial"
    mv -- "$current_partial" "$snapshot"
    current_partial=''
  fi
  verified_now=$((verified_now + 1))
done

mapfile -t snapshots < <(find "$snapshots_root" -mindepth 1 -maxdepth 1 -type d -name '????????T??????Z' -print | "$sort_bin" -r)
declare -A keep=()
for ((index = 0; index < ${#snapshots[@]} && index < keep_recent; index++)); do
  keep["${snapshots[$index]}"]=1
done

weekly_kept=0
declare -A weeks=()
for ((index = keep_recent; index < ${#snapshots[@]} && weekly_kept < keep_weekly; index++)); do
  snapshot="${snapshots[$index]}"
  stamp="${snapshot##*/}"
  calendar_date="${stamp:0:4}-${stamp:4:2}-${stamp:6:2}"
  week="$($date_bin -u -d "$calendar_date" +%G-%V 2>/dev/null || true)"
  [[ "$week" =~ ^[0-9]{4}-[0-9]{2}$ ]] || die 'date command cannot calculate ISO retention weeks'
  if [[ -z "${weeks[$week]:-}" ]]; then
    weeks["$week"]=1
    keep["$snapshot"]=1
    weekly_kept=$((weekly_kept + 1))
  fi
done

monthly_kept=0
declare -A months=()
for ((index = keep_recent; index < ${#snapshots[@]} && monthly_kept < keep_monthly; index++)); do
  snapshot="${snapshots[$index]}"
  stamp="${snapshot##*/}"
  month="${stamp:0:6}"
  if [[ -z "${months[$month]:-}" ]]; then
    months["$month"]=1
    keep["$snapshot"]=1
    monthly_kept=$((monthly_kept + 1))
  fi
done

removed=0
for snapshot in "${snapshots[@]}"; do
  [[ -n "${keep[$snapshot]:-}" ]] && continue
  verify_snapshot "$snapshot"
  has_current_verification "$snapshot" || { log "retaining unverified snapshot: $snapshot"; continue; }
  [[ "$snapshot" == "$snapshots_root/"????????T??????Z && -d "$snapshot" && ! -L "$snapshot" ]] ||
    die "refusing to prune unsafe snapshot path: $snapshot"
  [[ "$($readlink_bin -f -- "$snapshot")" == "$snapshot" && "$($readlink_bin -f -- "${snapshot%/*}")" == "$snapshots_root" ]] ||
    die "refusing to prune snapshot outside the canonical destination: $snapshot"
  rm -rf -- "$snapshot"
  removed=$((removed + 1))
done

latest="${snapshots[0]:-}"
if [[ -z "$latest" ]] || ! has_current_verification "$latest"; then
  die 'no verified durable snapshot is available after pull'
fi
printf 'latest=%s\nverifiedNow=%s\nremoved=%s\n' "${latest##*/}" "$verified_now" "$removed" >"$destination/health.txt"
chmod 0600 "$destination/health.txt"
log "pull complete; latest=${latest##*/}, verified=$verified_now, pruned=$removed"

trap - EXIT INT TERM
cleanup
