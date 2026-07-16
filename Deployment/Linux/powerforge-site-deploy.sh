#!/usr/bin/env bash
set -Eeuo pipefail

umask 022

CONFIG_ROOT="${POWERFORGE_SITE_CONFIG_ROOT:-/etc/powerforge/sites}"
LOCK_ROOT="${POWERFORGE_SITE_LOCK_ROOT:-/var/lock}"
TRUSTED_STAGE_ROOT="${POWERFORGE_SITE_TRUSTED_STAGE_ROOT:-/var/lib/powerforge/deployment-staging}"
site=""
archive=""
metadata=""
operation="promote"
defer_public_verification=0
release_id_argument=""
previous_release_id_argument=""
promoted=0
legacy_migrated=0
previous_target=""
release_dir=""
workflow_stage=""
trusted_stage=""

cleanup_staging() {
  [[ -z "$workflow_stage" || ! -d "$workflow_stage" ]] || rm -rf -- "$workflow_stage"
  [[ -z "$trusted_stage" || ! -d "$trusted_stage" ]] || rm -rf -- "$trusted_stage"
}

trap cleanup_staging EXIT

log() {
  printf '[powerforge-site-deploy] %s\n' "$*"
}

fail() {
  log "ERROR: $*" >&2
  return 1
}

usage() {
  cat <<'EOF'
Usage:
  powerforge-site-deploy --site <id> --archive <artifact.tar> --metadata <deployment.json> [--defer-public-verification]
  powerforge-site-deploy --site <id> --finalize --release-id <id> [--previous-release-id <id>]
  powerforge-site-deploy --site <id> --rollback --release-id <id> [--previous-release-id <id>]
EOF
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
    --defer-public-verification)
      defer_public_verification=1
      shift
      ;;
    --finalize)
      [[ "$operation" == 'promote' ]] || fail 'Only one deployment operation may be selected.'
      operation="finalize"
      shift
      ;;
    --rollback)
      [[ "$operation" == 'promote' ]] || fail 'Only one deployment operation may be selected.'
      operation="rollback"
      shift
      ;;
    --release-id)
      release_id_argument="${2:-}"
      shift 2
      ;;
    --previous-release-id)
      previous_release_id_argument="${2:-}"
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
if [[ "$operation" == 'promote' ]]; then
  [[ -n "$archive" && -n "$metadata" ]] || fail 'Both --archive and --metadata are required.'
  [[ -z "$release_id_argument" && -z "$previous_release_id_argument" ]] || fail 'Release identity arguments are not valid during promotion.'
else
  [[ -z "$archive" && -z "$metadata" ]] || fail 'Archive and metadata are only valid during promotion.'
  [[ "$defer_public_verification" == '0' ]] || fail 'Deferred verification is only valid during promotion.'
fi

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
: "${CLOUDFLARE_ZONE_ID:=}"
: "${CLOUDFLARE_API_TOKEN_FILE:=}"
: "${ORIGIN_ADDRESS:=}"
: "${ORIGIN_HOST:=}"

[[ "$SITE_ROOT" == /* && "$SITE_ROOT" != '/' ]] || fail 'SITE_ROOT must be an absolute non-root path.'
[[ "$SITE_ROOT" != *[[:space:]]* ]] || fail 'SITE_ROOT must not contain whitespace.'
[[ "$TRUSTED_STAGE_ROOT" == /* && "$TRUSTED_STAGE_ROOT" != '/' ]] || fail 'Trusted staging root must be an absolute non-root path.'
[[ "$PUBLIC_URL" =~ ^https://[A-Za-z0-9.-]+(:[0-9]+)?$ ]] || fail 'PUBLIC_URL must be an HTTPS origin without a path.'
[[ "$RELEASES_TO_KEEP" =~ ^[1-9][0-9]*$ ]] || fail 'RELEASES_TO_KEEP must be a positive integer.'
[[ "$CLOUDFLARE_PURGE_ENABLED" == '0' || "$CLOUDFLARE_PURGE_ENABLED" == '1' ]] || fail 'CLOUDFLARE_PURGE_ENABLED must be 0 or 1.'
if [[ -n "$ORIGIN_ADDRESS" || -n "$ORIGIN_HOST" ]]; then
  [[ -n "$ORIGIN_ADDRESS" && "$ORIGIN_HOST" =~ ^[A-Za-z0-9.-]+$ ]] || fail 'ORIGIN_ADDRESS and ORIGIN_HOST must be configured together.'
fi
if [[ "$CLOUDFLARE_PURGE_ENABLED" == '1' ]]; then
  [[ -z "$CLOUDFLARE_ZONE_ID" || "$CLOUDFLARE_ZONE_ID" =~ ^[A-Fa-f0-9]{32}$ ]] || fail 'CLOUDFLARE_ZONE_ID must be a 32-character hexadecimal id.'
  if [[ -n "$CLOUDFLARE_API_TOKEN_FILE" ]]; then
    [[ "$CLOUDFLARE_API_TOKEN_FILE" == /* && -s "$CLOUDFLARE_API_TOKEN_FILE" ]] || fail 'CLOUDFLARE_API_TOKEN_FILE must be an absolute, non-empty readable file.'
    [[ -r "$CLOUDFLARE_API_TOKEN_FILE" ]] || fail 'Cloudflare API token file is not readable.'
  fi
fi

validate_release_id() {
  [[ "$1" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$ ]] || fail "Invalid release id: $1"
}

release_path() {
  validate_release_id "$1"
  printf '%s/releases/%s' "$SITE_ROOT" "$1"
}

prune_releases() {
  local current_release="$1"
  local rollback_release="$2"
  local index
  mapfile -t old_releases < <(find "$SITE_ROOT/releases" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %p\n' | sort -rn | awk '{print $2}')
  for ((index=RELEASES_TO_KEEP; index<${#old_releases[@]}; index++)); do
    [[ "${old_releases[$index]}" == "$current_release" || "${old_releases[$index]}" == "$rollback_release" ]] || rm -rf "${old_releases[$index]}"
  done
}

finalize_deferred_release() {
  local selected_release
  local selected_previous=""
  local current_target
  selected_release="$(release_path "$release_id_argument")"
  [[ -d "$selected_release" ]] || fail "Deferred release does not exist: $release_id_argument"
  if [[ -n "$previous_release_id_argument" ]]; then
    selected_previous="$(release_path "$previous_release_id_argument")"
    [[ -d "$selected_previous" ]] || fail "Deferred rollback release does not exist: $previous_release_id_argument"
  fi
  [[ -L "$SITE_ROOT/current" ]] || fail 'Current release is not a symlink during finalization.'
  current_target="$(readlink -f "$SITE_ROOT/current")"
  [[ "$current_target" == "$selected_release" ]] || fail 'Current release changed before deferred finalization.'
  [[ -s "$selected_release/_powerforge/deployment.json" ]] || fail 'Deferred release provenance is missing.'
  prune_releases "$selected_release" "$selected_previous"
  log "Finalized $site release $release_id_argument after external public verification"
}

rollback_deferred_release() {
  local selected_release
  local selected_previous=""
  local current_target=""
  selected_release="$(release_path "$release_id_argument")"
  [[ -d "$selected_release" ]] || fail "Deferred release does not exist: $release_id_argument"
  if [[ -n "$previous_release_id_argument" ]]; then
    selected_previous="$(release_path "$previous_release_id_argument")"
    [[ -d "$selected_previous" ]] || fail "Deferred rollback release does not exist: $previous_release_id_argument"
  fi
  if [[ -L "$SITE_ROOT/current" ]]; then
    current_target="$(readlink -f "$SITE_ROOT/current")"
  fi
  [[ "$current_target" == "$selected_release" ]] || fail 'Current release changed before deferred rollback.'
  if [[ -n "$selected_previous" ]]; then
    rollback_link="$SITE_ROOT/.current.rollback.$$"
    ln -s "$selected_previous" "$rollback_link"
    mv -Tf "$rollback_link" "$SITE_ROOT/current"
  else
    rm -f "$SITE_ROOT/current"
  fi
  rm -rf "$selected_release"
  log "Rolled back deferred $site release $release_id_argument"
}

mkdir -p "$SITE_ROOT/releases"
mkdir -p "$LOCK_ROOT"
exec 9>"${LOCK_ROOT}/powerforge-site-${site}.lock"
flock -n 9 || fail "Another deployment is active for $site."

if [[ "$operation" == 'finalize' ]]; then
  [[ -n "$release_id_argument" ]] || fail '--release-id is required during finalization.'
  finalize_deferred_release
  exit 0
fi
if [[ "$operation" == 'rollback' ]]; then
  [[ -n "$release_id_argument" ]] || fail '--release-id is required during rollback.'
  rollback_deferred_release
  exit 0
fi

archive="$(realpath -e "$archive")"
metadata="$(realpath -e "$metadata")"
[[ -f "$archive" && ! -L "$archive" ]] || fail 'Artifact must be a regular file, not a symlink.'
[[ -f "$metadata" && ! -L "$metadata" ]] || fail 'Metadata must be a regular file, not a symlink.'
workflow_stage="$(dirname "$archive")"
[[ "$workflow_stage" =~ ^/tmp/powerforge-([0-9]+)-([0-9]+)-([a-z0-9][a-z0-9.-]{0,62})$ ]] || fail 'Artifact is outside the workflow staging path.'
[[ "${BASH_REMATCH[3]}" == "$site" ]] || fail 'Artifact staging site does not match the configured site.'
[[ "$archive" == "$workflow_stage/artifact.tar" ]] || fail 'Artifact filename is invalid.'
[[ "$metadata" == "$workflow_stage/deployment.json" ]] || fail 'Metadata must share the artifact workflow staging directory.'
if [[ -n "${SUDO_UID:-}" ]]; then
  [[ "$(stat -c '%u' "$archive")" -eq "$SUDO_UID" ]] || fail 'Artifact owner does not match the invoking deployment account.'
  [[ "$(stat -c '%u' "$metadata")" -eq "$SUDO_UID" ]] || fail 'Metadata owner does not match the invoking deployment account.'
fi

workflow_cloudflare_token="$workflow_stage/cloudflare-api.token"
workflow_cloudflare_zone="$workflow_stage/cloudflare-zone-id"
if [[ -e "$workflow_cloudflare_token" || -L "$workflow_cloudflare_token" || -e "$workflow_cloudflare_zone" || -L "$workflow_cloudflare_zone" ]]; then
  [[ "$CLOUDFLARE_PURGE_ENABLED" == '1' ]] || fail 'Ephemeral Cloudflare credentials were provided but purge is disabled.'
  [[ -f "$workflow_cloudflare_token" && ! -L "$workflow_cloudflare_token" && -s "$workflow_cloudflare_token" ]] || fail 'Ephemeral Cloudflare token must be a non-empty regular file.'
  [[ -f "$workflow_cloudflare_zone" && ! -L "$workflow_cloudflare_zone" && -s "$workflow_cloudflare_zone" ]] || fail 'Ephemeral Cloudflare zone id must be a non-empty regular file.'
  if [[ -n "${SUDO_UID:-}" ]]; then
    [[ "$(stat -c '%u' "$workflow_cloudflare_token")" -eq "$SUDO_UID" ]] || fail 'Cloudflare token owner does not match the invoking deployment account.'
    [[ "$(stat -c '%u' "$workflow_cloudflare_zone")" -eq "$SUDO_UID" ]] || fail 'Cloudflare zone owner does not match the invoking deployment account.'
  fi
fi
install -d -m 0700 "$TRUSTED_STAGE_ROOT"
trusted_stage="$(mktemp -d "${TRUSTED_STAGE_ROOT}/${site}.XXXXXXXX")"
chmod 0700 "$trusted_stage"
install -m 0600 "$archive" "$trusted_stage/artifact.tar"
install -m 0600 "$metadata" "$trusted_stage/deployment.json"
if [[ -f "$workflow_cloudflare_token" ]]; then
  install -m 0600 "$workflow_cloudflare_token" "$trusted_stage/cloudflare-api.token"
  install -m 0600 "$workflow_cloudflare_zone" "$trusted_stage/cloudflare-zone-id"
  ephemeral_zone_id="$(tr -d '[:space:]' <"$trusted_stage/cloudflare-zone-id")"
  [[ "$ephemeral_zone_id" =~ ^[A-Fa-f0-9]{32}$ ]] || fail 'Ephemeral Cloudflare zone id is invalid.'
  if [[ -n "$CLOUDFLARE_ZONE_ID" && "${CLOUDFLARE_ZONE_ID,,}" != "${ephemeral_zone_id,,}" ]]; then
    fail 'Ephemeral Cloudflare zone id does not match the host configuration.'
  fi
  CLOUDFLARE_ZONE_ID="${ephemeral_zone_id,,}"
  CLOUDFLARE_API_TOKEN_FILE="$trusted_stage/cloudflare-api.token"
fi
archive="$trusted_stage/artifact.tar"
metadata="$trusted_stage/deployment.json"

if [[ "$CLOUDFLARE_PURGE_ENABLED" == '1' ]]; then
  CLOUDFLARE_ZONE_ID="${CLOUDFLARE_ZONE_ID,,}"
  [[ "$CLOUDFLARE_ZONE_ID" =~ ^[a-f0-9]{32}$ ]] || fail 'Cloudflare zone id is required when purge is enabled.'
  [[ "$CLOUDFLARE_API_TOKEN_FILE" == /* && -s "$CLOUDFLARE_API_TOKEN_FILE" && -r "$CLOUDFLARE_API_TOKEN_FILE" ]] || fail 'A readable Cloudflare API token is required when purge is enabled.'
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

verify_marker() {
  local marker="$1"
  local endpoint="$2"
  grep -Eq "\"sourceSha\"[[:space:]]*:[[:space:]]*\"${source_sha}\"" <<<"$marker" || fail "$endpoint did not serve the promoted source SHA."
  grep -Eq "\"artifactSha256\"[[:space:]]*:[[:space:]]*\"${artifact_sha}\"" <<<"$marker" || fail "$endpoint did not serve the promoted artifact."
  grep -Eq "\"workflowRunId\"[[:space:]]*:[[:space:]]*\"${run_id}\"" <<<"$marker" || fail "$endpoint did not serve the promoted workflow run."
  grep -Eq "\"workflowRunAttempt\"[[:space:]]*:[[:space:]]*\"${run_attempt}\"" <<<"$marker" || fail "$endpoint did not serve the promoted workflow attempt."
}

verify_public_release() {
  local marker_path='/_powerforge/deployment.json'
  local public_marker
  local smoke_path
  public_marker="$(smoke_url "$PUBLIC_URL" "$marker_path")"
  verify_marker "$public_marker" 'Public endpoint'
  for smoke_path in $SMOKE_PATHS; do
    [[ "$smoke_path" == /* ]] || fail "Smoke path must begin with '/': $smoke_path"
    smoke_url "$PUBLIC_URL" "$smoke_path" >/dev/null
  done
}

verify_origin_release() {
  local marker_path='/_powerforge/deployment.json'
  local smoke_path
  if [[ -n "$ORIGIN_ADDRESS" ]]; then
    local origin_marker
    origin_marker="$(smoke_url "https://${ORIGIN_HOST}" "$marker_path" --resolve "${ORIGIN_HOST}:443:${ORIGIN_ADDRESS}")"
    verify_marker "$origin_marker" 'Origin endpoint'
    for smoke_path in $SMOKE_PATHS; do
      [[ "$smoke_path" == /* ]] || fail "Smoke path must begin with '/': $smoke_path"
      smoke_url "https://${ORIGIN_HOST}" "$smoke_path" --resolve "${ORIGIN_HOST}:443:${ORIGIN_ADDRESS}" >/dev/null
    done
  fi
}

verify_release() {
  verify_public_release
  verify_origin_release
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
  elif [[ "$legacy_migrated" == '1' && -n "$previous_target" && -d "$previous_target" && ! -e "$SITE_ROOT/current" ]]; then
    log 'Promotion failed; restoring the legacy current directory.'
    mv -T "$previous_target" "$SITE_ROOT/current"
  fi
  [[ -z "$release_dir" || ! -d "$release_dir" || "$release_dir" == "$previous_target" ]] || rm -rf "$release_dir"
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
elif [[ -e "$SITE_ROOT/current" ]]; then
  [[ -d "$SITE_ROOT/current" ]] || fail 'Existing current path must be a directory or symlink.'
  previous_target="$SITE_ROOT/releases/legacy-$(date -u +%Y%m%d%H%M%S)-${source_sha:0:12}"
  [[ ! -e "$previous_target" ]] || fail "Legacy release already exists: $previous_target"
  log "Migrating legacy current directory to $previous_target"
  mv -T "$SITE_ROOT/current" "$previous_target"
  legacy_migrated=1
fi
candidate_link="$SITE_ROOT/.current.${run_id}.${run_attempt}"
ln -s "$release_dir" "$candidate_link"
mv -Tf "$candidate_link" "$SITE_ROOT/current"
promoted=1

purge_cloudflare
if [[ "$defer_public_verification" == '1' ]]; then
  verify_origin_release
else
  verify_release
  prune_releases "$release_dir" "$previous_target"
fi

trap - ERR INT TERM
cleanup_staging
trap - EXIT
if [[ "$defer_public_verification" == '1' ]]; then
  printf 'POWERFORGE_RELEASE_ID=%s\n' "$release_id"
  if [[ -n "$previous_target" ]]; then
    printf 'POWERFORGE_PREVIOUS_RELEASE_ID=%s\n' "$(basename "$previous_target")"
  else
    printf 'POWERFORGE_PREVIOUS_RELEASE_ID=\n'
  fi
  log "Promoted $site release $release_id pending external public verification"
else
  log "Promoted $site release $release_id from $source_sha"
fi
