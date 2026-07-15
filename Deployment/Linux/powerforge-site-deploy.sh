#!/usr/bin/env bash
set -Eeuo pipefail

umask 022

CONFIG_ROOT="${POWERFORGE_SITE_CONFIG_ROOT:-/etc/powerforge/sites}"
LOCK_ROOT="${POWERFORGE_SITE_LOCK_ROOT:-/var/lock}"
site=""
archive=""
metadata=""
promoted=0
previous_target=""
release_dir=""

log() {
  printf '[powerforge-site-deploy] %s\n' "$*"
}

fail() {
  log "ERROR: $*" >&2
  return 1
}

usage() {
  echo 'Usage: powerforge-site-deploy --site <id> --archive <artifact.tar> --metadata <deployment.json>'
}

while (($# > 0)); do
  case "$1" in
    --site)
      site="${2:-}"
      shift 2
      ;;
    --archive)
      archive="${2:-}"
      shift 2
      ;;
    --metadata)
      metadata="${2:-}"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      usage >&2
      fail "Unknown argument: $1"
      ;;
  esac
done

[[ "$site" =~ ^[a-z0-9][a-z0-9.-]{0,62}$ ]] || fail 'Invalid site identifier.'
[[ -n "$archive" && -n "$metadata" ]] || fail 'Both --archive and --metadata are required.'

config_path="${CONFIG_ROOT}/${site}.env"
[[ -f "$config_path" && ! -L "$config_path" ]] || fail "Site is not configured: $site"
if [[ "$(id -u)" -eq 0 ]]; then
  [[ "$(stat -c '%u' "$config_path")" -eq 0 ]] || fail "Site config must be owned by root: $config_path"
  config_mode="$(stat -c '%a' "$config_path")"
  (( (8#$config_mode & 0022) == 0 )) || fail "Site config must not be group/world writable: $config_path"
fi

# The config is trusted, root-owned operator input and contains no values supplied by the workflow.
# shellcheck disable=SC1090
source "$config_path"

: "${SITE_ROOT:?SITE_ROOT is required in $config_path}"
: "${PUBLIC_URL:?PUBLIC_URL is required in $config_path}"
: "${RELEASES_TO_KEEP:=5}"
: "${SMOKE_PATHS:=/}"
: "${CLOUDFLARE_PURGE_ENABLED:=0}"
: "${ORIGIN_ADDRESS:=}"
: "${ORIGIN_HOST:=}"

[[ "$SITE_ROOT" == /* && "$SITE_ROOT" != '/' ]] || fail 'SITE_ROOT must be an absolute non-root path.'
[[ "$SITE_ROOT" != *[[:space:]]* ]] || fail 'SITE_ROOT must not contain whitespace.'
[[ "$PUBLIC_URL" =~ ^https://[A-Za-z0-9.-]+(:[0-9]+)?$ ]] || fail 'PUBLIC_URL must be an HTTPS origin without a path.'
[[ "$RELEASES_TO_KEEP" =~ ^[1-9][0-9]*$ ]] || fail 'RELEASES_TO_KEEP must be a positive integer.'
if [[ -n "$ORIGIN_ADDRESS" || -n "$ORIGIN_HOST" ]]; then
  [[ -n "$ORIGIN_ADDRESS" && "$ORIGIN_HOST" =~ ^[A-Za-z0-9.-]+$ ]] || fail 'ORIGIN_ADDRESS and ORIGIN_HOST must be configured together.'
fi
if [[ "$CLOUDFLARE_PURGE_ENABLED" == '1' ]]; then
  [[ "${CLOUDFLARE_ZONE_ID:-}" =~ ^[A-Za-z0-9]+$ ]] || fail 'CLOUDFLARE_ZONE_ID is required when purge is enabled.'
  [[ "${CLOUDFLARE_API_TOKEN_FILE:-}" == /* && -s "$CLOUDFLARE_API_TOKEN_FILE" ]] || fail 'CLOUDFLARE_API_TOKEN_FILE must be an absolute, non-empty readable file when purge is enabled.'
  [[ -r "$CLOUDFLARE_API_TOKEN_FILE" ]] || fail 'Cloudflare API token file is not readable.'
fi

archive="$(realpath -e "$archive")"
metadata="$(realpath -e "$metadata")"
[[ -f "$archive" && ! -L "$archive" ]] || fail 'Artifact must be a regular file, not a symlink.'
[[ -f "$metadata" && ! -L "$metadata" ]] || fail 'Metadata must be a regular file, not a symlink.'
case "$archive" in
  /tmp/powerforge-[0-9]*-[0-9]*/artifact.tar) ;;
  *) fail 'Artifact is outside the workflow staging path.' ;;
esac
case "$metadata" in
  /tmp/powerforge-[0-9]*-[0-9]*/deployment.json) ;;
  *) fail 'Metadata is outside the workflow staging path.' ;;
esac
if [[ -n "${SUDO_UID:-}" ]]; then
  [[ "$(stat -c '%u' "$archive")" -eq "$SUDO_UID" ]] || fail 'Artifact owner does not match the invoking deployment account.'
  [[ "$(stat -c '%u' "$metadata")" -eq "$SUDO_UID" ]] || fail 'Metadata owner does not match the invoking deployment account.'
fi

json_string() {
  local key="$1"
  sed -n "s/.*\"${key}\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" "$metadata" | head -n 1
}

source_sha="$(json_string sourceSha)"
artifact_sha="$(json_string artifactSha256)"
run_id="$(json_string workflowRunId)"
run_attempt="$(json_string workflowRunAttempt)"
[[ "$source_sha" =~ ^[0-9a-fA-F]{40,64}$ ]] || fail 'Metadata sourceSha is missing or invalid.'
[[ "$artifact_sha" =~ ^[0-9a-f]{64}$ ]] || fail 'Metadata artifactSha256 is missing or invalid.'
[[ "$run_id" =~ ^[0-9]+$ && "$run_attempt" =~ ^[0-9]+$ ]] || fail 'Metadata workflow run identity is invalid.'
actual_artifact_sha="$(sha256sum "$archive" | awk '{print $1}')"
[[ "$actual_artifact_sha" == "$artifact_sha" ]] || fail 'Artifact checksum does not match deployment metadata.'

while IFS= read -r entry; do
  stripped="${entry#./}"
  [[ "$entry" != /* ]] || fail "Archive contains an absolute path: $entry"
  [[ "/${stripped}/" != *'/../'* ]] || fail "Archive contains path traversal: $entry"
done < <(tar -tf "$archive")

while IFS= read -r listing; do
  entry_type="${listing:0:1}"
  [[ "$entry_type" == '-' || "$entry_type" == 'd' ]] || fail 'Archive contains links or special files.'
done < <(tar -tvf "$archive")

mkdir -p "$SITE_ROOT/releases"
mkdir -p "$LOCK_ROOT"
exec 9>"${LOCK_ROOT}/powerforge-site-${site}.lock"
flock -n 9 || fail "Another deployment is active for $site."

release_id="$(date -u +%Y%m%d%H%M%S)-${run_id}-${run_attempt}-${source_sha:0:12}"
release_dir="$SITE_ROOT/releases/$release_id"
[[ ! -e "$release_dir" ]] || fail "Release already exists: $release_id"

purge_cloudflare() {
  [[ "$CLOUDFLARE_PURGE_ENABLED" == '1' ]] || return 0
  local response
  response="$(curl -fsS --retry 3 --max-time 30 \
    -X POST "https://api.cloudflare.com/client/v4/zones/${CLOUDFLARE_ZONE_ID}/purge_cache" \
    -H "Authorization: Bearer $(<"$CLOUDFLARE_API_TOKEN_FILE")" \
    -H 'Content-Type: application/json' \
    --data '{"purge_everything":true}')"
  grep -Eq '"success"[[:space:]]*:[[:space:]]*true' <<<"$response" || fail 'Cloudflare cache purge was rejected.'
}

smoke_url() {
  local base_url="$1"
  local path="$2"
  shift 2
  curl -fsS --retry 3 --retry-all-errors --max-time 30 "$@" "${base_url}${path}?powerforge-deploy=${run_id}-${run_attempt}"
}

verify_release() {
  local marker_path='/_powerforge/deployment.json'
  local public_marker
  public_marker="$(smoke_url "$PUBLIC_URL" "$marker_path")"
  grep -Eq "\"sourceSha\"[[:space:]]*:[[:space:]]*\"${source_sha}\"" <<<"$public_marker" || fail 'Public endpoint did not serve the promoted source SHA.'

  if [[ -n "$ORIGIN_ADDRESS" ]]; then
    local origin_marker
    origin_marker="$(smoke_url "https://${ORIGIN_HOST}" "$marker_path" --resolve "${ORIGIN_HOST}:443:${ORIGIN_ADDRESS}")"
    grep -Eq "\"sourceSha\"[[:space:]]*:[[:space:]]*\"${source_sha}\"" <<<"$origin_marker" || fail 'Origin endpoint did not serve the promoted source SHA.'
  fi

  local smoke_path
  for smoke_path in $SMOKE_PATHS; do
    [[ "$smoke_path" == /* ]] || fail "Smoke path must begin with '/': $smoke_path"
    smoke_url "$PUBLIC_URL" "$smoke_path" >/dev/null
    if [[ -n "$ORIGIN_ADDRESS" ]]; then
      smoke_url "https://${ORIGIN_HOST}" "$smoke_path" --resolve "${ORIGIN_HOST}:443:${ORIGIN_ADDRESS}" >/dev/null
    fi
  done
}

rollback() {
  local exit_code="$1"
  set +e
  if [[ "$promoted" == '1' ]]; then
    if [[ -n "$previous_target" && -d "$previous_target" ]]; then
      log "Deployment failed; rolling back to $previous_target"
      rollback_link="$SITE_ROOT/.current.rollback.$$"
      ln -s "$previous_target" "$rollback_link"
      mv -Tf "$rollback_link" "$SITE_ROOT/current"
    else
      log 'Deployment failed; removing the first release from current.'
      rm -f "$SITE_ROOT/current"
    fi
    purge_cloudflare || true
  fi
  [[ -z "$release_dir" || ! -d "$release_dir" || "$release_dir" == "$previous_target" ]] || rm -rf "$release_dir"
  rm -rf "$(dirname "$archive")"
  exit "$exit_code"
}
trap 'rollback $?' ERR INT TERM

mkdir -p "$release_dir"
tar --extract --file "$archive" --directory "$release_dir" --no-same-owner --no-same-permissions
[[ -s "$release_dir/index.html" ]] || fail 'Artifact does not contain a non-empty index.html.'
mkdir -p "$release_dir/_powerforge"
install -m 0644 "$metadata" "$release_dir/_powerforge/deployment.json"

if [[ -L "$SITE_ROOT/current" ]]; then
  previous_target="$(readlink -f "$SITE_ROOT/current")"
fi
candidate_link="$SITE_ROOT/.current.${run_id}.${run_attempt}"
ln -s "$release_dir" "$candidate_link"
mv -Tf "$candidate_link" "$SITE_ROOT/current"
promoted=1

purge_cloudflare
verify_release

mapfile -t old_releases < <(find "$SITE_ROOT/releases" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %p\n' | sort -rn | awk '{print $2}')
for ((index=RELEASES_TO_KEEP; index<${#old_releases[@]}; index++)); do
  [[ "${old_releases[$index]}" == "$release_dir" || "${old_releases[$index]}" == "$previous_target" ]] || rm -rf "${old_releases[$index]}"
done

trap - ERR INT TERM
rm -rf "$(dirname "$archive")"
log "Promoted $site release $release_id from $source_sha"
