#!/usr/bin/env bash
set -Eeuo pipefail
umask 022
CONFIG_ROOT="${POWERFORGE_SERVICE_CONFIG_ROOT:-/etc/powerforge/services}"
LOCK_ROOT="${POWERFORGE_SERVICE_LOCK_ROOT:-/var/lock}"
TRUSTED_STAGE_ROOT="${POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT:-/var/lib/powerforge/service-deployment-staging}"
TRANSACTION_ROOT="${POWERFORGE_SERVICE_TRANSACTION_ROOT:-/var/lib/powerforge/service-deployment-state}"
SYSTEMD_CONFIG_ROOT="${POWERFORGE_SYSTEMD_CONFIG_ROOT:-/etc/systemd/system}"
deployment_shell_pid="$BASHPID"
service_id="" archive="" metadata=""
promoted=0 previous_target="" release_dir="" candidate_link=""
workflow_stage="" trusted_stage=""
systemd_drop_in_backup="" systemd_drop_in_dir="" systemd_drop_in_path=""
systemd_drop_in_existed=0 systemd_drop_in_owner="" systemd_drop_in_group="" systemd_drop_in_mode=""
systemd_write_paths_snapshot_ready=0 systemd_transaction_path=""
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
assert_trusted_directory_chain() {
  local declared_path="$1"
  local description="$2"
  local deployment_uid component current owner mode
  local -a components
  deployment_uid="$(id -u)"
  current='/'
  IFS='/' read -r -a components <<<"${declared_path#/}"
  for component in "${components[@]}"; do
    [[ -n "$component" ]] || continue
    current="${current%/}/$component"
    [[ -d "$current" && ! -L "$current" ]] || fail "$description must be a real directory: $current"
    owner="$(stat -c '%u' -- "$current")"
    mode="$(stat -c '%a' -- "$current")"
    [[ "$owner" -eq 0 || "$owner" -eq "$deployment_uid" ]] || fail "$description has an untrusted owner: $current"
    (( (8#$mode & 0022) == 0 )) || fail "$description must not be group/world writable: $current"
  done
}
paths_overlap() {
  local first="$1"
  local second="$2"
  [[ "$first" == "$second" || "$first" == "$second"/* || "$second" == "$first"/* ]]
}
prepare_transaction_root() {
  local parent resolved
  [[ "$TRANSACTION_ROOT" == /* && "$TRANSACTION_ROOT" != '/' && "$TRANSACTION_ROOT" != *[[:space:]]* ]] ||
    fail 'Transaction root must be an absolute non-root path without whitespace.'
  parent="$(dirname -- "$TRANSACTION_ROOT")"
  assert_trusted_directory_chain "$parent" 'Transaction root parent'
  if [[ -e "$TRANSACTION_ROOT" || -L "$TRANSACTION_ROOT" ]]; then
    [[ -d "$TRANSACTION_ROOT" && ! -L "$TRANSACTION_ROOT" ]] || fail "Transaction root must be a real directory: $TRANSACTION_ROOT"
  else
    install -d -m 0700 "$TRANSACTION_ROOT"
  fi
  assert_trusted_directory_chain "$TRANSACTION_ROOT" 'Transaction root'
  resolved="$(realpath -e -- "$TRANSACTION_ROOT")"
  [[ "$resolved" == "$TRANSACTION_ROOT" ]] || fail "Transaction root must be canonical and contain no symlinked components: $TRANSACTION_ROOT"
  TRANSACTION_ROOT="$resolved"
}
prepare_trusted_stage_root() {
  local parent resolved
  [[ "$TRUSTED_STAGE_ROOT" == /* && "$TRUSTED_STAGE_ROOT" != '/' && "$TRUSTED_STAGE_ROOT" != *[[:space:]]* ]] ||
    fail 'Trusted staging root must be an absolute non-root path without whitespace.'
  parent="$(dirname -- "$TRUSTED_STAGE_ROOT")"
  assert_trusted_directory_chain "$parent" 'Trusted staging root parent'
  if [[ -e "$TRUSTED_STAGE_ROOT" || -L "$TRUSTED_STAGE_ROOT" ]]; then
    [[ -d "$TRUSTED_STAGE_ROOT" && ! -L "$TRUSTED_STAGE_ROOT" ]] || fail "Trusted staging root must be a real directory: $TRUSTED_STAGE_ROOT"
  else
    install -d -m 0700 "$TRUSTED_STAGE_ROOT"
  fi
  assert_trusted_directory_chain "$TRUSTED_STAGE_ROOT" 'Trusted staging root'
  resolved="$(realpath -e -- "$TRUSTED_STAGE_ROOT")"
  [[ "$resolved" == "$TRUSTED_STAGE_ROOT" ]] || fail "Trusted staging root must be canonical and contain no symlinked components: $TRUSTED_STAGE_ROOT"
  TRUSTED_STAGE_ROOT="$resolved"
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
assert_trusted_systemd_path() {
  local declared_path="$1"
  [[ ! -L "$declared_path" ]] || fail "Systemd writable path must not be a symlink: $declared_path"
  assert_trusted_directory_chain "$(dirname -- "$declared_path")" 'Systemd writable path parent'
}
prepare_systemd_drop_in_directory() {
  local config_parent
  config_parent="$(dirname -- "$SYSTEMD_CONFIG_ROOT")"
  assert_trusted_directory_chain "$config_parent" 'Systemd config parent' || return 1
  if [[ -e "$SYSTEMD_CONFIG_ROOT" || -L "$SYSTEMD_CONFIG_ROOT" ]]; then
    [[ -d "$SYSTEMD_CONFIG_ROOT" && ! -L "$SYSTEMD_CONFIG_ROOT" ]] || { fail "Systemd config root must be a real directory: $SYSTEMD_CONFIG_ROOT"; return 1; }
  else
    install -d -m 0755 "$SYSTEMD_CONFIG_ROOT" || return 1
  fi
  assert_trusted_directory_chain "$SYSTEMD_CONFIG_ROOT" 'Systemd config root' || return 1
  [[ "$(realpath -e -- "$SYSTEMD_CONFIG_ROOT")" == "$SYSTEMD_CONFIG_ROOT" ]] || { fail "Systemd config root must be canonical and contain no symlinked components: $SYSTEMD_CONFIG_ROOT"; return 1; }
  systemd_drop_in_dir="${SYSTEMD_CONFIG_ROOT}/${SYSTEMD_SERVICE}.d"
  systemd_drop_in_path="${systemd_drop_in_dir}/powerforge-read-write-paths.conf"
  if [[ -e "$systemd_drop_in_dir" || -L "$systemd_drop_in_dir" ]]; then
    [[ -d "$systemd_drop_in_dir" && ! -L "$systemd_drop_in_dir" ]] || { fail "Systemd drop-in directory must be a real directory: $systemd_drop_in_dir"; return 1; }
  else
    install -d -m 0755 "$systemd_drop_in_dir" || return 1
  fi
  assert_trusted_directory_chain "$systemd_drop_in_dir" 'Systemd drop-in directory' || return 1
}
prepare_service_release_root() {
  [[ -d "$SERVICE_ROOT" && ! -L "$SERVICE_ROOT" ]] || { fail "Service root must be a real, pre-provisioned directory: $SERVICE_ROOT"; return 1; }
  assert_trusted_directory_chain "$SERVICE_ROOT" 'Service root' || return 1
  resolved_service_root="$(realpath -e -- "$SERVICE_ROOT")" || return 1
  [[ "$resolved_service_root" == "$SERVICE_ROOT" ]] || { fail "Service root must be canonical and contain no symlinked components: $SERVICE_ROOT"; return 1; }
  SERVICE_ROOT="$resolved_service_root"
  resolved_release_root="${SERVICE_ROOT}/releases"
  if [[ -e "$resolved_release_root" || -L "$resolved_release_root" ]]; then
    [[ -d "$resolved_release_root" && ! -L "$resolved_release_root" ]] || { fail "Release root must be a real directory: $resolved_release_root"; return 1; }
  else
    install -d -m 0755 "$resolved_release_root" || return 1
  fi
  assert_trusted_directory_chain "$resolved_release_root" 'Release root' || return 1
  [[ "$(realpath -e -- "$resolved_release_root")" == "$resolved_release_root" ]] || fail "Release root must be canonical and contain no symlinked components: $resolved_release_root"
}
snapshot_systemd_write_paths() {
  local transaction_temporary drop_in_mode
  [[ ! -e "$systemd_transaction_path" && ! -L "$systemd_transaction_path" ]] ||
    fail "An incomplete systemd writable-path transaction already exists: $systemd_transaction_path"
  transaction_temporary="$(mktemp -d "${TRANSACTION_ROOT}/.service-${service_id}.XXXXXXXX")"
  chmod 0700 "$transaction_temporary"
  systemd_drop_in_existed=0
  systemd_drop_in_owner=""
  systemd_drop_in_group=""
  systemd_drop_in_mode=""
  printf '%s\n' "$SERVICE_ROOT" >"$transaction_temporary/service-root"
  printf '%s\n' "$SYSTEMD_SERVICE" >"$transaction_temporary/systemd-service"
  printf '%s\n' "$SYSTEMD_CONFIG_ROOT" >"$transaction_temporary/systemd-config-root"
  printf '%s\n' "$previous_target" >"$transaction_temporary/previous-target"
  if [[ -e "$systemd_drop_in_path" || -L "$systemd_drop_in_path" ]]; then
    [[ -f "$systemd_drop_in_path" && ! -L "$systemd_drop_in_path" ]] || fail "PowerForge systemd drop-in must be a regular file: $systemd_drop_in_path"
    if [[ "$(id -u)" -eq 0 ]]; then
      [[ "$(stat -c '%u' -- "$systemd_drop_in_path")" -eq 0 ]] || fail "PowerForge systemd drop-in must be owned by root: $systemd_drop_in_path"
      drop_in_mode="$(stat -c '%a' -- "$systemd_drop_in_path")"
      (( (8#$drop_in_mode & 0022) == 0 )) || fail "PowerForge systemd drop-in must not be group/world writable: $systemd_drop_in_path"
    fi
    systemd_drop_in_owner="$(stat -c '%u' -- "$systemd_drop_in_path")"
    systemd_drop_in_group="$(stat -c '%g' -- "$systemd_drop_in_path")"
    systemd_drop_in_mode="$(stat -c '%a' -- "$systemd_drop_in_path")"
    if ! install -m 0600 "$systemd_drop_in_path" "$transaction_temporary/drop-in"; then
      rm -rf -- "$transaction_temporary"
      return 1
    fi
    printf 'present\n' >"$transaction_temporary/drop-in-state"
    printf '%s\n' "$systemd_drop_in_owner" >"$transaction_temporary/drop-in-owner"
    printf '%s\n' "$systemd_drop_in_group" >"$transaction_temporary/drop-in-group"
    printf '%s\n' "$systemd_drop_in_mode" >"$transaction_temporary/drop-in-mode"
    systemd_drop_in_existed=1
  else
    printf 'absent\n' >"$transaction_temporary/drop-in-state"
  fi
  chmod 0600 "$transaction_temporary"/*
  sync -f "$transaction_temporary"/*
  sync -f "$transaction_temporary"
  mv -- "$transaction_temporary" "$systemd_transaction_path"
  sync -f "$TRANSACTION_ROOT"
  systemd_drop_in_backup="${systemd_transaction_path}/drop-in"
  systemd_write_paths_snapshot_ready=1
}
load_systemd_write_paths_transaction() {
  local stored_root stored_service stored_systemd_root stored_state
  [[ -d "$systemd_transaction_path" && ! -L "$systemd_transaction_path" ]] ||
    fail "Systemd writable-path transaction must be a real directory: $systemd_transaction_path"
  assert_trusted_directory_chain "$systemd_transaction_path" 'Systemd writable-path transaction'
  stored_root="$(<"$systemd_transaction_path/service-root")"
  stored_service="$(<"$systemd_transaction_path/systemd-service")"
  stored_systemd_root="$(<"$systemd_transaction_path/systemd-config-root")"
  previous_target="$(<"$systemd_transaction_path/previous-target")"
  stored_state="$(<"$systemd_transaction_path/drop-in-state")"
  [[ "$stored_root" == "$SERVICE_ROOT" ]] || fail "Incomplete transaction belongs to a different service root: $stored_root"
  [[ "$stored_service" == "$SYSTEMD_SERVICE" ]] || fail "Incomplete transaction belongs to a different systemd unit: $stored_service"
  [[ "$stored_systemd_root" == "$SYSTEMD_CONFIG_ROOT" ]] || fail "Incomplete transaction belongs to a different systemd config root: $stored_systemd_root"
  if [[ -n "$previous_target" ]]; then
    [[ -d "$previous_target" && "$previous_target" == "$resolved_release_root"/* ]] ||
      fail "Incomplete transaction contains an invalid previous release: $previous_target"
  fi
  if [[ "$stored_state" == 'present' ]]; then
    [[ -f "$systemd_transaction_path/drop-in" && ! -L "$systemd_transaction_path/drop-in" ]] ||
      fail 'Incomplete transaction is missing its systemd drop-in backup.'
    systemd_drop_in_owner="$(<"$systemd_transaction_path/drop-in-owner")"
    systemd_drop_in_group="$(<"$systemd_transaction_path/drop-in-group")"
    systemd_drop_in_mode="$(<"$systemd_transaction_path/drop-in-mode")"
    [[ "$systemd_drop_in_owner" =~ ^[0-9]+$ && "$systemd_drop_in_group" =~ ^[0-9]+$ && "$systemd_drop_in_mode" =~ ^[0-7]{3,4}$ ]] ||
      fail 'Incomplete transaction contains invalid systemd drop-in metadata.'
    systemd_drop_in_backup="${systemd_transaction_path}/drop-in"
    systemd_drop_in_existed=1
  elif [[ "$stored_state" == 'absent' ]]; then
    systemd_drop_in_backup=""
    systemd_drop_in_existed=0
  else
    fail "Incomplete transaction contains an invalid drop-in state: $stored_state"
  fi
  systemd_write_paths_snapshot_ready=1
}
restore_systemd_write_paths() {
  local restore_temporary=""
  [[ "$systemd_write_paths_snapshot_ready" == '1' ]] || return 0
  if [[ "$systemd_drop_in_existed" == '1' ]]; then
    restore_temporary="$(mktemp "${systemd_drop_in_dir}/.powerforge-read-write-paths.restore.XXXXXXXX")" || return 1
    if ! install -m "$systemd_drop_in_mode" "$systemd_drop_in_backup" "$restore_temporary" ||
       ! chown "$systemd_drop_in_owner:$systemd_drop_in_group" "$restore_temporary" ||
       ! mv -f -- "$restore_temporary" "$systemd_drop_in_path"; then
      rm -f -- "$restore_temporary"
      return 1
    fi
  else
    rm -f -- "$systemd_drop_in_path" || return 1
  fi
  systemctl daemon-reload || return 1
}
finish_systemd_write_paths_transaction() {
  [[ "$systemd_transaction_path" == "$TRANSACTION_ROOT"/service-*.transaction ]] ||
    fail "Refusing to remove an unexpected transaction path: $systemd_transaction_path"
  sync_deployment_state || return 1
  rm -rf -- "$systemd_transaction_path" || return 1
  sync -f "$TRANSACTION_ROOT" || return 1
  systemd_write_paths_snapshot_ready=0
  systemd_drop_in_backup=""
}
sync_deployment_state() {
  if [[ -f "$systemd_drop_in_path" ]]; then
    sync -f "$systemd_drop_in_path" || return 1
  fi
  sync -f "$systemd_drop_in_dir" || return 1
  sync -f "$SERVICE_ROOT" || return 1
  sync -f "$resolved_release_root" || return 1
}
commit_systemd_write_paths() {
  local committed_path="${systemd_transaction_path}.committed"
  # The rename is the durable commit point; ignore catchable termination so a signal cannot run rollback after it has
  # disappeared but before the in-memory state reflects that fact.
  sync_deployment_state || return 1
  trap - INT TERM
  mv -- "$systemd_transaction_path" "$committed_path" || return 1
  promoted=0
  systemd_write_paths_snapshot_ready=0
  systemd_drop_in_backup=""
  if ! sync -f "$TRANSACTION_ROOT"; then
    log "ERROR: committed transaction retained until its directory can be synchronized: $committed_path" >&2
    return 1
  fi
  rm -rf -- "$committed_path" || log "WARNING: committed transaction cleanup remains at $committed_path" >&2
  sync -f "$TRANSACTION_ROOT" || log "WARNING: committed transaction cleanup was not synchronized in $TRANSACTION_ROOT" >&2
}
report_systemd_restore_failure() {
  log "ERROR: failed to restore systemd writable paths; transaction retained at $systemd_transaction_path" >&2
}
restore_previous_current_link() {
  local rollback_link=""
  if [[ -n "$previous_target" ]]; then
    rollback_link="$SERVICE_ROOT/.current.rollback.$$"
    rm -f -- "$rollback_link"
    ln -s "$previous_target" "$rollback_link" || return 1
    mv -Tf "$rollback_link" "$SERVICE_ROOT/current" || { rm -f -- "$rollback_link"; return 1; }
    [[ "$(readlink -f "$SERVICE_ROOT/current" 2>/dev/null)" == "$previous_target" ]]
  else
    rm -f -- "$SERVICE_ROOT/current"
    [[ ! -e "$SERVICE_ROOT/current" && ! -L "$SERVICE_ROOT/current" ]]
  fi
}
peek_systemd_transaction_identity() {
  [[ -d "$systemd_transaction_path" && ! -L "$systemd_transaction_path" ]] ||
    fail "Systemd writable-path transaction must be a real directory: $systemd_transaction_path"
  assert_trusted_directory_chain "$systemd_transaction_path" 'Systemd writable-path transaction'
  transaction_service_root="$(<"$systemd_transaction_path/service-root")"
  transaction_systemd_service="$(<"$systemd_transaction_path/systemd-service")"
  transaction_systemd_config_root="$(<"$systemd_transaction_path/systemd-config-root")"
  [[ "$transaction_service_root" == /* && "$transaction_service_root" != '/' && "$transaction_service_root" != *[[:space:]]* ]] ||
    fail 'Incomplete transaction contains an invalid service root.'
  [[ "$transaction_systemd_service" =~ ^[A-Za-z0-9_.@-]+\.service$ ]] ||
    fail 'Incomplete transaction contains an invalid systemd unit.'
  [[ "$transaction_systemd_config_root" == /* && "$transaction_systemd_config_root" != '/' && "$transaction_systemd_config_root" != *[[:space:]]* ]] ||
    fail 'Incomplete transaction contains an invalid systemd config root.'
}
settle_committed_transaction() {
  local committed="$1"
  [[ -e "$committed" || -L "$committed" ]] || return 0
  [[ -d "$committed" && ! -L "$committed" ]] || fail "Committed transaction marker must be a real directory: $committed"
  assert_trusted_directory_chain "$committed" 'Committed transaction marker'
  sync -f "$TRANSACTION_ROOT" || fail "Committed transaction directory is not durable: $committed"
  rm -rf -- "$committed" || fail "Committed transaction marker could not be removed: $committed"
  sync -f "$TRANSACTION_ROOT" || fail "Committed transaction cleanup is not durable: $committed"
}
recover_incomplete_systemd_transaction() {
  local permissions_restored=1 current_restored=1 service_safe=0
  if [[ -e "$systemd_transaction_path" || -L "$systemd_transaction_path" ]]; then
    log "Recovering incomplete systemd writable-path transaction for $SYSTEMD_SERVICE."
    if ! load_systemd_write_paths_transaction; then
      systemctl stop "$SYSTEMD_SERVICE" || true
      fail "Incomplete deployment transaction is invalid; $SYSTEMD_SERVICE was stopped and operator recovery is required."
    fi
    restore_systemd_write_paths || permissions_restored=0
    restore_previous_current_link || current_restored=0
    if [[ "$permissions_restored" == '1' && "$current_restored" == '1' && -n "$previous_target" ]]; then
      if systemctl restart "$SYSTEMD_SERVICE"; then
        service_safe=1
      else
        systemctl stop "$SYSTEMD_SERVICE" && service_safe=1
      fi
    else
      systemctl stop "$SYSTEMD_SERVICE" && service_safe=1
    fi
    if [[ "$permissions_restored" != '1' || "$current_restored" != '1' || "$service_safe" != '1' ]]; then
      report_systemd_restore_failure
      fail "Incomplete deployment recovery could not prove $SYSTEMD_SERVICE safe."
    fi
    finish_systemd_write_paths_transaction
    log "Recovered incomplete systemd writable-path transaction for $SYSTEMD_SERVICE."
  fi
}
recover_selected_transaction() {
  SYSTEMD_SERVICE="$transaction_systemd_service"
  SERVICE_ROOT="$transaction_service_root"
  SYSTEMD_CONFIG_ROOT="$transaction_systemd_config_root"
  local preparation_status=0
  prepare_service_release_root || preparation_status=$?
  if [[ "$preparation_status" -eq 0 ]]; then prepare_systemd_drop_in_directory || preparation_status=$?; fi
  if [[ "$preparation_status" -ne 0 ]]; then
    systemctl stop "$SYSTEMD_SERVICE" || log "CRITICAL: failed to stop recorded unit $SYSTEMD_SERVICE after recovery preparation failed." >&2
    fail "Recorded recovery paths are unavailable; $SYSTEMD_SERVICE was stopped and operator recovery is required."
  fi
  recover_incomplete_systemd_transaction
}
reconcile_systemd_write_paths() (
  if ((${#systemd_read_write_paths[@]} == 0)); then
    [[ ! -f "$systemd_drop_in_path" ]] || rm -f -- "$systemd_drop_in_path"
    systemctl daemon-reload
    return 0
  fi
  temporary="$(mktemp "${systemd_drop_in_dir}/.powerforge-read-write-paths.XXXXXXXX")"
  trap 'rm -f -- "$temporary"' EXIT
  {
    printf '[Service]\n'
    for path in "${systemd_read_write_paths[@]}"; do
      printf 'ReadWritePaths=%s\n' "$path"
    done
  } >"$temporary"
  chmod 0644 "$temporary"
  if [[ -f "$systemd_drop_in_path" ]] && cmp -s -- "$temporary" "$systemd_drop_in_path"; then
    rm -f -- "$temporary"
  else
    mv -f -- "$temporary" "$systemd_drop_in_path"
  fi
  systemctl daemon-reload
)
mkdir -p "$LOCK_ROOT"
exec 9>"${LOCK_ROOT}/powerforge-service-${service_id}.lock"
flock -n 9 || fail "Another deployment is active for $service_id."
prepare_transaction_root
requested_systemd_config_root="$SYSTEMD_CONFIG_ROOT"
own_transaction="${TRANSACTION_ROOT}/service-${service_id}.transaction"
own_committed="${own_transaction}.committed"
settle_committed_transaction "$own_committed"
if [[ -e "$own_transaction" || -L "$own_transaction" ]]; then
  systemd_transaction_path="$own_transaction"
  peek_systemd_transaction_identity
  unit_lock_key="$(printf '%s' "$transaction_systemd_service" | sha256sum | awk '{print $1}')"
  service_root_lock_key="$(printf '%s' "$transaction_service_root" | sha256sum | awk '{print $1}')"
  exec 8>"${LOCK_ROOT}/powerforge-systemd-${unit_lock_key}.lock"
  flock -n 8 || fail "Another deployment is active for systemd unit $transaction_systemd_service."
  exec 7>"${LOCK_ROOT}/powerforge-root-${service_root_lock_key}.lock"
  flock -n 7 || fail "Another deployment is active for service root $transaction_service_root."
  recover_selected_transaction
  exec 8>&- 7>&-
fi
SYSTEMD_CONFIG_ROOT="$requested_systemd_config_root"
[[ "$CONFIG_ROOT" == /* && "$CONFIG_ROOT" != '/' && "$CONFIG_ROOT" != *[[:space:]]* ]] || fail 'Service config root must be an absolute non-root path without whitespace.'
[[ -d "$CONFIG_ROOT" && ! -L "$CONFIG_ROOT" ]] || fail "Service config root must be a real directory: $CONFIG_ROOT"
assert_trusted_directory_chain "$CONFIG_ROOT" 'Service config root'
resolved_config_root="$(realpath -e -- "$CONFIG_ROOT")"
[[ "$resolved_config_root" == "$CONFIG_ROOT" ]] || fail "Service config root must be canonical and contain no symlinked components: $CONFIG_ROOT"
CONFIG_ROOT="$resolved_config_root"
config_path="${CONFIG_ROOT}/${service_id}.env"
[[ -f "$config_path" && ! -L "$config_path" ]] || fail "Service is not configured: $service_id"
if [[ "$(id -u)" -eq 0 ]]; then
  [[ "$(stat -c '%u' "$config_path")" -eq 0 ]] || fail "Service config must be owned by root: $config_path"
  config_mode="$(stat -c '%a' "$config_path")"
  (( (8#$config_mode & 0022) == 0 )) || fail "Service config must not be group/world writable: $config_path"
fi
unset SERVICE_ROOT SYSTEMD_SERVICE SYSTEMD_READ_WRITE_PATHS LOCAL_HEALTH_URL RELEASES_TO_KEEP REQUIRED_RELEASE_PATHS PUBLIC_HEALTH_URLS REQUIRE_HEALTH_PROVENANCE
# shellcheck disable=SC1090
source "$config_path"
: "${SERVICE_ROOT:?SERVICE_ROOT is required in $config_path}" "${SYSTEMD_SERVICE:?SYSTEMD_SERVICE is required in $config_path}" "${LOCAL_HEALTH_URL:?LOCAL_HEALTH_URL is required in $config_path}"
: "${SYSTEMD_READ_WRITE_PATHS:=}" "${RELEASES_TO_KEEP:=5}" "${REQUIRED_RELEASE_PATHS:=}" "${PUBLIC_HEALTH_URLS:=}" "${REQUIRE_HEALTH_PROVENANCE:=1}"
[[ "$SERVICE_ROOT" == /* && "$SERVICE_ROOT" != '/' && "$SERVICE_ROOT" != *[[:space:]]* ]] || fail 'SERVICE_ROOT must be an absolute non-root path without whitespace.'
[[ "$TRUSTED_STAGE_ROOT" == /* && "$TRUSTED_STAGE_ROOT" != '/' ]] || fail 'Trusted staging root must be an absolute non-root path.'
[[ "$SYSTEMD_CONFIG_ROOT" == /* && "$SYSTEMD_CONFIG_ROOT" != '/' && "$SYSTEMD_CONFIG_ROOT" != *[[:space:]]* ]] || fail 'Systemd config root must be an absolute non-root path without whitespace.'
[[ "$SYSTEMD_SERVICE" =~ ^[A-Za-z0-9_.@-]+\.service$ ]] || fail 'SYSTEMD_SERVICE must be a systemd service unit name.'
[[ "$LOCAL_HEALTH_URL" =~ ^https?://[^[:space:]]+$ ]] || fail 'LOCAL_HEALTH_URL must be an HTTP or HTTPS URL.'
[[ "$RELEASES_TO_KEEP" =~ ^[1-9][0-9]*$ ]] || fail 'RELEASES_TO_KEEP must be a positive integer.'
[[ "$REQUIRE_HEALTH_PROVENANCE" == '0' || "$REQUIRE_HEALTH_PROVENANCE" == '1' ]] || fail 'REQUIRE_HEALTH_PROVENANCE must be 0 or 1.'
for health_url in $PUBLIC_HEALTH_URLS; do [[ "$health_url" =~ ^https://[^[:space:]]+$ ]] || fail "Public health URL must use HTTPS: $health_url"; done
read -r -a configured_systemd_read_write_paths <<<"$SYSTEMD_READ_WRITE_PATHS"
for read_write_path in "${configured_systemd_read_write_paths[@]}"; do
  [[ "$read_write_path" == /* && "$read_write_path" != '/' ]] || fail 'Systemd writable paths must be absolute non-root paths.'
  [[ "$read_write_path" =~ ^/[A-Za-z0-9._@:+,-]+(/[A-Za-z0-9._@:+,-]+)*$ ]] || fail "Systemd writable path contains unsupported characters: $read_write_path"
  [[ "/${read_write_path#/}/" != *'/../'* ]] || fail "Systemd writable path must not contain traversal: $read_write_path"
done
configured_service_root="$SERVICE_ROOT"
configured_service_root_identity="$(realpath -e -- "$SERVICE_ROOT" 2>/dev/null || true)"
configured_systemd_service="$SYSTEMD_SERVICE"
configured_systemd_config_root="$SYSTEMD_CONFIG_ROOT"
for candidate_committed in "$TRANSACTION_ROOT"/service-*.transaction.committed; do
  [[ "$candidate_committed" != "$own_committed" && ( -e "$candidate_committed" || -L "$candidate_committed" ) ]] || continue
  systemd_transaction_path="$candidate_committed"
  peek_systemd_transaction_identity
  if [[ "$transaction_systemd_service" == "$configured_systemd_service" || "$transaction_service_root" == "$configured_service_root" || ( -n "$configured_service_root_identity" && "$transaction_service_root" == "$configured_service_root_identity" ) ]]; then
    transaction_id="${candidate_committed##*/service-}"
    transaction_id="${transaction_id%.transaction.committed}"
    [[ "$transaction_id" =~ ^[a-z0-9][a-z0-9.-]{0,62}$ ]] || fail "Committed transaction has an invalid service identity: $candidate_committed"
    exec {committed_service_lock_fd}>"${LOCK_ROOT}/powerforge-service-${transaction_id}.lock"
    flock -n "$committed_service_lock_fd" || fail "Another deployment is active for $transaction_id."
    settle_committed_transaction "$candidate_committed"
  fi
done
related_transactions=()
related_units=()
related_ids=()
related_roots=()
for candidate_transaction in "$TRANSACTION_ROOT"/service-*.transaction; do
  [[ -e "$candidate_transaction" || -L "$candidate_transaction" ]] || continue
  systemd_transaction_path="$candidate_transaction"
  peek_systemd_transaction_identity
  transaction_id="${candidate_transaction##*/service-}"
  transaction_id="${transaction_id%.transaction}"
  [[ "$transaction_id" =~ ^[a-z0-9][a-z0-9.-]{0,62}$ ]] || fail "Incomplete transaction has an invalid service identity: $candidate_transaction"
  if [[ "$transaction_systemd_service" == "$configured_systemd_service" || "$transaction_service_root" == "$configured_service_root" || ( -n "$configured_service_root_identity" && "$transaction_service_root" == "$configured_service_root_identity" ) ]]; then
    related_transactions+=("$candidate_transaction")
    related_units+=("$transaction_systemd_service")
    related_ids+=("$transaction_id")
    related_roots+=("$transaction_service_root")
  fi
done
if ((${#related_transactions[@]} > 1)); then
  declare -A held_related_units=() held_related_roots=()
  for index in "${!related_ids[@]}"; do
    related_id="${related_ids[$index]}"
    if [[ "$related_id" != "$service_id" ]]; then
      exec {related_service_lock_fd}>"${LOCK_ROOT}/powerforge-service-${related_id}.lock"
      flock -n "$related_service_lock_fd" || fail "Another deployment is active for $related_id."
    fi
    unit_lock_key="$(printf '%s' "${related_units[$index]}" | sha256sum | awk '{print $1}')"
    if [[ -z "${held_related_units[$unit_lock_key]:-}" ]]; then
      exec {related_unit_lock_fd}>"${LOCK_ROOT}/powerforge-systemd-${unit_lock_key}.lock"
      flock -n "$related_unit_lock_fd" || fail "Another deployment is active for systemd unit ${related_units[$index]}."
      held_related_units[$unit_lock_key]="$related_unit_lock_fd"
    fi
    service_root_lock_key="$(printf '%s' "${related_roots[$index]}" | sha256sum | awk '{print $1}')"
    if [[ -z "${held_related_roots[$service_root_lock_key]:-}" ]]; then
      exec {related_root_lock_fd}>"${LOCK_ROOT}/powerforge-root-${service_root_lock_key}.lock"
      flock -n "$related_root_lock_fd" || fail "Another deployment is active for service root ${related_roots[$index]}."
      held_related_roots[$service_root_lock_key]="$related_root_lock_fd"
    fi
  done
  declare -A stopped_related_units=()
  for related_unit in "${related_units[@]}"; do
    [[ -n "${stopped_related_units[$related_unit]:-}" ]] && continue
    systemctl stop "$related_unit" || fail "Failed to stop ambiguous recorded unit $related_unit."
    stopped_related_units[$related_unit]=1
  done
  fail 'Multiple incomplete transactions overlap this deployment; recorded units were stopped and operator recovery is required.'
fi
locked_systemd_service=""
locked_service_root=""
if ((${#related_transactions[@]} == 1)); then
  systemd_transaction_path="${related_transactions[0]}"
  peek_systemd_transaction_identity
  transaction_id="${systemd_transaction_path##*/service-}"
  transaction_id="${transaction_id%.transaction}"
  if [[ "$transaction_id" != "$service_id" ]]; then
    exec {transaction_service_lock_fd}>"${LOCK_ROOT}/powerforge-service-${transaction_id}.lock"
    flock -n "$transaction_service_lock_fd" || fail "Another deployment is active for $transaction_id."
  fi
  unit_lock_key="$(printf '%s' "$transaction_systemd_service" | sha256sum | awk '{print $1}')"
  service_root_lock_key="$(printf '%s' "$transaction_service_root" | sha256sum | awk '{print $1}')"
  exec 8>"${LOCK_ROOT}/powerforge-systemd-${unit_lock_key}.lock"
  flock -n 8 || fail "Another deployment is active for systemd unit $transaction_systemd_service."
  exec 7>"${LOCK_ROOT}/powerforge-root-${service_root_lock_key}.lock"
  flock -n 7 || fail "Another deployment is active for service root $transaction_service_root."
  locked_systemd_service="$transaction_systemd_service"
  locked_service_root="$transaction_service_root"
  recover_selected_transaction
  SYSTEMD_SERVICE="$configured_systemd_service"
  SERVICE_ROOT="$configured_service_root"
  SYSTEMD_CONFIG_ROOT="$configured_systemd_config_root"
fi
prepare_service_release_root
for deployment_control_root in "$CONFIG_ROOT" "$SYSTEMD_CONFIG_ROOT" "$TRANSACTION_ROOT" "$TRUSTED_STAGE_ROOT" "$(realpath -e -- "$LOCK_ROOT")"; do
  paths_overlap "$deployment_control_root" "$resolved_release_root" && fail "Deployment control path must not overlap release storage: $deployment_control_root"
done
prepare_trusted_stage_root
if [[ "$locked_systemd_service" != "$SYSTEMD_SERVICE" ]]; then
  unit_lock_key="$(printf '%s' "$SYSTEMD_SERVICE" | sha256sum | awk '{print $1}')"
  exec {configured_unit_lock_fd}>"${LOCK_ROOT}/powerforge-systemd-${unit_lock_key}.lock"
  flock -n "$configured_unit_lock_fd" || fail "Another deployment is active for systemd unit $SYSTEMD_SERVICE."
fi
if [[ "$locked_service_root" != "$SERVICE_ROOT" ]]; then
  service_root_lock_key="$(printf '%s' "$SERVICE_ROOT" | sha256sum | awk '{print $1}')"
  exec {configured_root_lock_fd}>"${LOCK_ROOT}/powerforge-root-${service_root_lock_key}.lock"
  flock -n "$configured_root_lock_fd" || fail "Another deployment is active for service root $SERVICE_ROOT."
fi
systemd_transaction_path="${TRANSACTION_ROOT}/service-${service_id}.transaction"
prepare_systemd_drop_in_directory
previous_target=""
if [[ -e "$SERVICE_ROOT/current" || -L "$SERVICE_ROOT/current" ]]; then
  [[ -L "$SERVICE_ROOT/current" ]] || fail 'Current release pointer must be a symlink.'
  previous_target="$(readlink -f "$SERVICE_ROOT/current")"
  [[ -d "$previous_target" && "$previous_target" == "$resolved_release_root"/* ]] || fail 'Current release pointer must resolve inside the canonical release root.'
fi
systemd_read_write_paths=()
for read_write_path in "${configured_systemd_read_write_paths[@]}"; do
  [[ -d "$read_write_path" ]] || fail "Systemd writable path does not exist: $read_write_path"
  assert_trusted_systemd_path "$read_write_path"
  resolved_read_write_path="$(realpath -e -- "$read_write_path")"
  [[ -d "$resolved_read_write_path" && "$resolved_read_write_path" != '/' ]] || fail "Systemd writable path is not a safe directory: $read_write_path"
  [[ "$resolved_read_write_path" == "$read_write_path" ]] || fail "Systemd writable path must be canonical and contain no symlinked components: $read_write_path"
  [[ "$resolved_read_write_path" =~ ^/[A-Za-z0-9._@:+,-]+(/[A-Za-z0-9._@:+,-]+)*$ ]] || fail "Resolved systemd writable path contains unsupported characters: $read_write_path"
  protected_roots=("$CONFIG_ROOT" "$SYSTEMD_CONFIG_ROOT" "$TRANSACTION_ROOT" "$TRUSTED_STAGE_ROOT" "$SERVICE_ROOT" "$(realpath -e -- "$LOCK_ROOT")")
  for protected_root in "${protected_roots[@]}"; do
    paths_overlap "$resolved_read_write_path" "$protected_root" || continue
    fail "Systemd writable path must not overlap deployment control path $protected_root: $read_write_path"
  done
  systemd_read_write_paths+=("$resolved_read_write_path")
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
[[ "$source_sha" =~ ^([0-9a-fA-F]{40}|[0-9a-fA-F]{64})$ ]] || fail 'Metadata sourceSha is missing or invalid.'
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
release_dir="$resolved_release_root/$release_id"
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
      grep -Eq "\"workflowRunId\"[[:space:]]*:[[:space:]]*\"${run_id}\"" <<<"$response" || fail "Health endpoint did not report promoted workflow run: $url"
      grep -Eq "\"workflowRunAttempt\"[[:space:]]*:[[:space:]]*\"${run_attempt}\"" <<<"$response" || fail "Health endpoint did not report promoted workflow attempt: $url"
    fi
  done
}
rollback() {
  local exit_code="$1" permissions_restored=1 current_restored=1 service_safe=0 current_target=""
  set +e
  if ! restore_systemd_write_paths; then
    permissions_restored=0
    report_systemd_restore_failure
  fi
  if [[ "$promoted" == '1' ]]; then
    if [[ -n "$previous_target" && -d "$previous_target" ]]; then
      log "Deployment failed; rolling back to $previous_target"
      if ! restore_previous_current_link; then
        current_restored=0
        log 'ERROR: failed to restore the previous current release link.' >&2
      fi
      if [[ "$permissions_restored" == '1' && "$current_restored" == '1' ]]; then
        if systemctl restart "$SYSTEMD_SERVICE"; then
          service_safe=1
        else
          log "ERROR: failed to restart restored service $SYSTEMD_SERVICE; stopping it." >&2
          systemctl stop "$SYSTEMD_SERVICE" && service_safe=1
        fi
      else
        log 'Rollback state is unverified; stopping instead of restarting the service.' >&2
        systemctl stop "$SYSTEMD_SERVICE" && service_safe=1
      fi
    else
      log 'Deployment failed; removing the first release from current and stopping the service.'
      if ! rm -f -- "$SERVICE_ROOT/current" || [[ -e "$SERVICE_ROOT/current" || -L "$SERVICE_ROOT/current" ]]; then
        current_restored=0
        log 'ERROR: failed to remove the first release from current.' >&2
      fi
      systemctl stop "$SYSTEMD_SERVICE" && service_safe=1
    fi
    if [[ "$service_safe" != '1' ]]; then
      log "CRITICAL: failed to prove $SYSTEMD_SERVICE is safely restored or stopped." >&2
    fi
    if [[ "$permissions_restored" == '1' && "$current_restored" == '1' && "$service_safe" == '1' ]]; then
      finish_systemd_write_paths_transaction || log "WARNING: rollback transaction retained at $systemd_transaction_path" >&2
    else
      log "Rollback transaction retained for recovery: $systemd_transaction_path" >&2
    fi
  elif [[ "$systemd_write_paths_snapshot_ready" == '1' ]]; then
    if [[ "$permissions_restored" == '1' ]]; then
      finish_systemd_write_paths_transaction || log "WARNING: restored pre-switch transaction retained at $systemd_transaction_path" >&2
    else
      systemctl stop "$SYSTEMD_SERVICE" || log "CRITICAL: failed to stop $SYSTEMD_SERVICE after pre-switch permission rollback failure." >&2
      log "Pre-switch transaction retained for recovery: $systemd_transaction_path" >&2
    fi
  fi
  current_target="$(readlink -f "$SERVICE_ROOT/current" 2>/dev/null || true)"
  [[ -z "$candidate_link" || ! -L "$candidate_link" ]] || rm -f -- "$candidate_link"
  if [[ -n "$release_dir" && -d "$release_dir" && "$release_dir" != "$previous_target" ]]; then
    if [[ "$current_target" != "$release_dir" && ( "$promoted" != '1' || ( "$current_restored" == '1' && "$service_safe" == '1' ) ) ]]; then
      rm -rf -- "$release_dir" || log "WARNING: failed to remove rejected release $release_dir" >&2
    else
      log "Rejected release retained for recovery: $release_dir" >&2
    fi
  fi
  exit "$exit_code"
}
trap 'exit_code=$?; if [[ "$BASHPID" == "$deployment_shell_pid" ]]; then rollback "$exit_code"; else exit "$exit_code"; fi' ERR
trap 'if [[ "$BASHPID" == "$deployment_shell_pid" ]]; then rollback 130; else exit 130; fi' INT
trap 'if [[ "$BASHPID" == "$deployment_shell_pid" ]]; then rollback 143; else exit 143; fi' TERM
mkdir -p "$release_dir"
tar --extract --file "$archive" --directory "$release_dir" --no-same-owner --no-same-permissions
for required_path in $REQUIRED_RELEASE_PATHS; do
  [[ "$required_path" != /* && "/${required_path}/" != *'/../'* ]] || fail "Required release path must be relative and remain inside the release: $required_path"
  [[ -e "$release_dir/$required_path" ]] || fail "Artifact does not contain required release path: $required_path"
done
mkdir -p "$release_dir/_powerforge"
install -m 0644 "$metadata" "$release_dir/_powerforge/deployment.json"
candidate_link="$SERVICE_ROOT/.current.${run_id}.${run_attempt}"
ln -s "$release_dir" "$candidate_link"
snapshot_systemd_write_paths
reconcile_systemd_write_paths
promoted=1
mv -Tf "$candidate_link" "$SERVICE_ROOT/current"
systemctl restart "$SYSTEMD_SERVICE"
verify_health
mapfile -t old_releases < <(find "$resolved_release_root" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %p\n' | sort -rn | awk '{print $2}')
for ((index=RELEASES_TO_KEEP; index<${#old_releases[@]}; index++)); do
  [[ "${old_releases[$index]}" == "$release_dir" || "${old_releases[$index]}" == "$previous_target" ]] || rm -rf "${old_releases[$index]}"
done
commit_systemd_write_paths
trap - ERR INT TERM
cleanup_staging
trap - EXIT
log "Promoted $service_id release $release_id from $source_sha"
