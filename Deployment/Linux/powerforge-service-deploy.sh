#!/usr/bin/env bash
set -Eeuo pipefail

umask 022

CONFIG_ROOT="${POWERFORGE_SERVICE_CONFIG_ROOT:-/etc/powerforge/services}"
LOCK_ROOT="${POWERFORGE_SERVICE_LOCK_ROOT:-/var/lock}"
TRUSTED_STAGE_ROOT="${POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT:-/var/lib/powerforge/service-deployment-staging}"
service_id=""
archive=""
metadata=""
promoted=0
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
  printf '[powerforge-service-deploy] %s\n' "$*"
}

fail() {
  log "ERROR: $*" >&2
  return 1
}

usage() {
  echo 'Usage: powerforge-service-deploy --service <id>'
}

while (($# > 0)); do
  case "$1" in
    --service)
      service_id="${2:-}"
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

[[ "$service_id" =~ ^[a-z0-9][a-z0-9.-]{0,62}$ ]] || fail 'Invalid service identifier.'
workflow_stage="/tmp/powerforge-service-${service_id}"
archive="$workflow_stage/artifact.tar"
metadata="$workflow_stage/deployment.json"

config_path="${CONFIG_ROOT}/${service_id}.env"
[[ -f "$config_path" && ! -L "$config_path" ]] || fail "Service is not configured: $service_id"
if [[ "$(id -u)" -eq 0 ]]; then
  [[ "$(stat -c '%u' "$config_path")" -eq 0 ]] || fail "Service config must be owned by root: $config_path"
  config_mode="$(stat -c '%a' "$config_path")"
  (( (8#$config_mode & 0022) == 0 )) || fail "Service config must not be group/world writable: $config_path"
fi

# The config is trusted, root-owned operator input and contains no values supplied by the workflow.
# shellcheck disable=SC1090
source "$config_path"

: "${SERVICE_ROOT:?SERVICE_ROOT is required in $config_path}"
: "${SYSTEMD_SERVICE:?SYSTEMD_SERVICE is required in $config_path}"
: "${LOCAL_HEALTH_URL:?LOCAL_HEALTH_URL is required in $config_path}"
: "${RELEASES_TO_KEEP:=5}"
: "${REQUIRED_RELEASE_PATHS:=}"
: "${PUBLIC_HEALTH_URLS:=}"
: "${REQUIRE_HEALTH_PROVENANCE:=1}"

[[ "$SERVICE_ROOT" == /* && "$SERVICE_ROOT" != '/' ]] || fail 'SERVICE_ROOT must be an absolute non-root path.'
[[ "$SERVICE_ROOT" != *[[:space:]]* ]] || fail 'SERVICE_ROOT must not contain whitespace.'
[[ "$TRUSTED_STAGE_ROOT" == /* && "$TRUSTED_STAGE_ROOT" != '/' ]] || fail 'Trusted staging root must be an absolute non-root path.'
[[ "$SYSTEMD_SERVICE" =~ ^[A-Za-z0-9_.@-]+\.service$ ]] || fail 'SYSTEMD_SERVICE must be a systemd service unit name.'
[[ "$LOCAL_HEALTH_URL" =~ ^https?://[^[:space:]]+$ ]] || fail 'LOCAL_HEALTH_URL must be an HTTP or HTTPS URL.'
[[ "$RELEASES_TO_KEEP" =~ ^[1-9][0-9]*$ ]] || fail 'RELEASES_TO_KEEP must be a positive integer.'
[[ "$REQUIRE_HEALTH_PROVENANCE" == '0' || "$REQUIRE_HEALTH_PROVENANCE" == '1' ]] || fail 'REQUIRE_HEALTH_PROVENANCE must be 0 or 1.'
for health_url in $PUBLIC_HEALTH_URLS; do
  [[ "$health_url" =~ ^https://[^[:space:]]+$ ]] || fail "Public health URL must use HTTPS: $health_url"
done

archive="$(realpath -e "$archive")"
metadata="$(realpath -e "$metadata")"
[[ -f "$archive" && ! -L "$archive" ]] || fail 'Artifact must be a regular file, not a symlink.'
[[ -f "$metadata" && ! -L "$metadata" ]] || fail 'Metadata must be a regular file, not a symlink.'
[[ "$archive" == "$workflow_stage/artifact.tar" ]] || fail 'Artifact is outside the service staging path.'
[[ "$metadata" == "$workflow_stage/deployment.json" ]] || fail 'Metadata is outside the service staging path.'
if [[ -n "${SUDO_UID:-}" ]]; then
  [[ "$(stat -c '%u' "$archive")" -eq "$SUDO_UID" ]] || fail 'Artifact owner does not match the invoking deployment account.'
  [[ "$(stat -c '%u' "$metadata")" -eq "$SUDO_UID" ]] || fail 'Metadata owner does not match the invoking deployment account.'
fi

install -d -m 0700 "$TRUSTED_STAGE_ROOT"
trusted_stage="$(mktemp -d "${TRUSTED_STAGE_ROOT}/${service_id}.XXXXXXXX")"
chmod 0700 "$trusted_stage"
install -m 0600 "$archive" "$trusted_stage/artifact.tar"
install -m 0600 "$metadata" "$trusted_stage/deployment.json"
archive="$trusted_stage/artifact.tar"
metadata="$trusted_stage/deployment.json"

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

mkdir -p "$SERVICE_ROOT/releases"
mkdir -p "$LOCK_ROOT"
exec 9>"${LOCK_ROOT}/powerforge-service-${service_id}.lock"
flock -n 9 || fail "Another deployment is active for $service_id."

release_id="$(date -u +%Y%m%d%H%M%S)-${run_id}-${run_attempt}-${source_sha:0:12}"
release_dir="$SERVICE_ROOT/releases/$release_id"
[[ ! -e "$release_dir" ]] || fail "Release already exists: $release_id"

health_response() {
  local url="$1"
  curl -fsS --retry 3 --retry-all-errors --max-time 30 "${url}?powerforge-deploy=${run_id}-${run_attempt}"
}

verify_health() {
  local url response
  for url in "$LOCAL_HEALTH_URL" $PUBLIC_HEALTH_URLS; do
    response="$(health_response "$url")"
    if [[ "$REQUIRE_HEALTH_PROVENANCE" == '1' ]]; then
      grep -Eq "\"sourceSha\"[[:space:]]*:[[:space:]]*\"${source_sha}\"" <<<"$response" || fail "Health endpoint did not report promoted source SHA: $url"
    fi
  done
}

rollback() {
  local exit_code="$1"
  set +e
  if [[ "$promoted" == '1' ]]; then
    if [[ -n "$previous_target" && -d "$previous_target" ]]; then
      log "Deployment failed; rolling back to $previous_target"
      rollback_link="$SERVICE_ROOT/.current.rollback.$$"
      ln -s "$previous_target" "$rollback_link"
      mv -Tf "$rollback_link" "$SERVICE_ROOT/current"
      systemctl restart "$SYSTEMD_SERVICE"
    else
      log 'Deployment failed; removing the first release from current and stopping the service.'
      rm -f "$SERVICE_ROOT/current"
      systemctl stop "$SYSTEMD_SERVICE"
    fi
  fi
  [[ -z "$release_dir" || ! -d "$release_dir" || "$release_dir" == "$previous_target" ]] || rm -rf "$release_dir"
  exit "$exit_code"
}
trap 'rollback $?' ERR INT TERM

mkdir -p "$release_dir"
tar --extract --file "$archive" --directory "$release_dir" --no-same-owner --no-same-permissions
for required_path in $REQUIRED_RELEASE_PATHS; do
  [[ "$required_path" != /* && "/${required_path}/" != *'/../'* ]] || fail "Required release path must be relative and remain inside the release: $required_path"
  [[ -e "$release_dir/$required_path" ]] || fail "Artifact does not contain required release path: $required_path"
done
mkdir -p "$release_dir/_powerforge"
install -m 0644 "$metadata" "$release_dir/_powerforge/deployment.json"

if [[ -L "$SERVICE_ROOT/current" ]]; then
  previous_target="$(readlink -f "$SERVICE_ROOT/current")"
fi
candidate_link="$SERVICE_ROOT/.current.${run_id}.${run_attempt}"
ln -s "$release_dir" "$candidate_link"
mv -Tf "$candidate_link" "$SERVICE_ROOT/current"
promoted=1

systemctl restart "$SYSTEMD_SERVICE"
verify_health

mapfile -t old_releases < <(find "$SERVICE_ROOT/releases" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %p\n' | sort -rn | awk '{print $2}')
for ((index=RELEASES_TO_KEEP; index<${#old_releases[@]}; index++)); do
  [[ "${old_releases[$index]}" == "$release_dir" || "${old_releases[$index]}" == "$previous_target" ]] || rm -rf "${old_releases[$index]}"
done

trap - ERR INT TERM
cleanup_staging
trap - EXIT
log "Promoted $service_id release $release_id from $source_sha"
