#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
deploy_script="$repo_root/Deployment/Linux/powerforge-service-deploy.sh"
test_root="$(mktemp -d "${HOME}/powerforge-service-deploy-tests.XXXXXXXX")"
trap 'rm -rf "$test_root" /tmp/powerforge-service-example /tmp/powerforge-service-fresh' EXIT

mkdir -p "$test_root/config" "$test_root/locks" "$test_root/bin" "$test_root/service"
export POWERFORGE_SERVICE_CONFIG_ROOT="$test_root/config"
export POWERFORGE_SERVICE_LOCK_ROOT="$test_root/locks"
export POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT="$test_root/trusted-stage"
export POWERFORGE_SERVICE_TRANSACTION_ROOT="$test_root/transactions"
export POWERFORGE_SYSTEMD_CONFIG_ROOT="$test_root/systemd"
export TEST_SYSTEMCTL_LOG="$test_root/systemctl.log"
export TEST_SYNC_LOG="$test_root/sync.log"

cat >"$test_root/bin/systemctl" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
printf '%s\n' "$*" >>"$TEST_SYSTEMCTL_LOG"
if [[ "$*" == 'daemon-reload' && "${FAIL_DAEMON_RELOAD:-}" == '1' ]]; then
  exit 1
fi
if [[ "$*" == 'daemon-reload' && -n "${FAIL_DAEMON_RELOAD_COUNT_FILE:-}" ]]; then
  count=0
  [[ ! -f "$FAIL_DAEMON_RELOAD_COUNT_FILE" ]] || count="$(cat "$FAIL_DAEMON_RELOAD_COUNT_FILE")"
  count=$((count + 1))
  printf '%s\n' "$count" >"$FAIL_DAEMON_RELOAD_COUNT_FILE"
  if [[ -n "${FAIL_DAEMON_RELOAD_FROM_CALL:-}" ]] && (( count >= FAIL_DAEMON_RELOAD_FROM_CALL )); then
    exit 1
  fi
  if [[ -n "${FAIL_DAEMON_RELOAD_ON_CALL:-}" && "$count" -eq "$FAIL_DAEMON_RELOAD_ON_CALL" ]]; then
    exit 1
  fi
fi
if [[ -n "${FAIL_SYSTEMCTL_COMMAND:-}" && "$*" == "$FAIL_SYSTEMCTL_COMMAND" ]]; then
  exit 1
fi
if [[ -n "${SIGNAL_ON_SYSTEMCTL_COMMAND:-}" && "$*" == "$SIGNAL_ON_SYSTEMCTL_COMMAND" ]]; then
  kill -"${SIGNAL_NAME:-TERM}" "$PPID"
fi
EOF

cat >"$test_root/bin/curl" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
marker="$TEST_SERVICE_ROOT/current/_powerforge/deployment.json"
if [[ -n "${FAIL_SOURCE_SHA:-}" ]] && grep -q "$FAIL_SOURCE_SHA" "$marker"; then
  exit 22
fi
cat "$marker"
EOF
chmod +x "$test_root/bin/systemctl" "$test_root/bin/curl"

cat >"$test_root/bin/mv" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
if [[ "${FAIL_ROLLBACK_LINK_MOVE:-}" == '1' && "$*" == *'.current.rollback.'* ]]; then
  exit 1
fi
exec /usr/bin/mv "$@"
EOF
chmod +x "$test_root/bin/mv"
cat >"$test_root/bin/sync" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
[[ -z "${TEST_SYNC_LOG:-}" ]] || printf '%s\n' "$*" >>"$TEST_SYNC_LOG"
if [[ -n "${FAIL_SYNC_PATH:-}" && "${*: -1}" == "$FAIL_SYNC_PATH" ]]; then
  count=1
  if [[ -n "${FAIL_SYNC_COUNT_FILE:-}" ]]; then
    [[ ! -f "$FAIL_SYNC_COUNT_FILE" ]] || count=$(( $(cat "$FAIL_SYNC_COUNT_FILE") + 1 ))
    printf '%s\n' "$count" >"$FAIL_SYNC_COUNT_FILE"
  fi
  if [[ -z "${FAIL_SYNC_FROM_CALL:-}" || "$count" -ge "$FAIL_SYNC_FROM_CALL" ]]; then exit 1; fi
fi
exec /usr/bin/sync "$@"
EOF
chmod +x "$test_root/bin/sync"
export PATH="$test_root/bin:$PATH"

write_config() {
  local id="$1"
  local root="$2"
  cat >"$test_root/config/${id}.env" <<EOF
SERVICE_ROOT=$root
SYSTEMD_SERVICE=${id}.service
LOCAL_HEALTH_URL=http://127.0.0.1:8791/healthz
PUBLIC_HEALTH_URLS="https://push.example.test/healthz https://push-fallback.example.test/healthz"
REQUIRED_RELEASE_PATHS="package.json src/server.mjs"
RELEASES_TO_KEEP=3
REQUIRE_HEALTH_PROVENANCE=1
EOF
  chmod 0640 "$test_root/config/${id}.env"
}

create_stage() {
  local service_id="$1"
  local run_id="$2"
  local run_attempt="$3"
  local source_sha="$4"
  local stage="/tmp/powerforge-service-${service_id}"
  local source="$test_root/source-${run_id}-${run_attempt}"
  rm -rf "$stage" "$source"
  mkdir -p "$stage" "$source/src"
  printf '{"name":"example"}\n' >"$source/package.json"
  printf 'console.log("%s")\n' "$source_sha" >"$source/src/server.mjs"
  tar -C "$source" -cf "$stage/artifact.tar" .
  local artifact_sha
  artifact_sha="$(sha256sum "$stage/artifact.tar" | awk '{print $1}')"
  cat >"$stage/deployment.json" <<EOF
{
  "schemaVersion": 1,
  "sourceRepository": "Example/Service",
  "sourceSha": "$source_sha",
  "workflowRunId": "$run_id",
  "workflowRunAttempt": "$run_attempt",
  "artifactSha256": "$artifact_sha",
  "deployedAtUtc": "2026-07-15T00:00:00Z"
}
EOF
  chmod 0600 "$stage/artifact.tar" "$stage/deployment.json"
}

write_config example "$test_root/service"
mkdir -p "$test_root/example-data"
printf 'SYSTEMD_READ_WRITE_PATHS="%s"\n' "$test_root/example-data" >>"$test_root/config/example.env"
create_stage example 92001 1 1111111111111111111111111111111111111111
TEST_SERVICE_ROOT="$test_root/service" "$deploy_script" \
  --service example

first_target="$(readlink -f "$test_root/service/current")"
[[ -s "$first_target/package.json" ]]
grep -q '1111111111111111111111111111111111111111' "$first_target/_powerforge/deployment.json"
grep -q '^restart example.service$' "$TEST_SYSTEMCTL_LOG"
grep -q '^daemon-reload$' "$TEST_SYSTEMCTL_LOG"
grep -qxF -- "-f $test_root/service/releases" "$TEST_SYNC_LOG"
drop_in="$POWERFORGE_SYSTEMD_CONFIG_ROOT/example.service.d/powerforge-read-write-paths.conf"
transaction_dir="$POWERFORGE_SERVICE_TRANSACTION_ROOT/service-example.transaction"
grep -qxF '[Service]' "$drop_in"
grep -qxF "ReadWritePaths=$test_root/example-data" "$drop_in"
[[ ! -e /tmp/powerforge-service-example ]]

# A release-filesystem flush failure must happen before the commit marker and
# roll the current link back through the still-live transaction.
create_stage example 92012 1 cccccccccccccccccccccccccccccccccccccccc
set +e
release_sync_output="$(TEST_SERVICE_ROOT="$test_root/service" FAIL_SYNC_PATH="$test_root/service/releases" "$deploy_script" --service example 2>&1)"
release_sync_status=$?
set -e
[[ "$release_sync_status" -ne 0 ]]
[[ "$(readlink -f "$test_root/service/current")" == "$first_target" ]]
[[ ! -e "$POWERFORGE_SERVICE_TRANSACTION_ROOT/service-example.transaction.committed" ]]
[[ -d "$transaction_dir" ]]
grep -q 'Deployment failed; rolling back' <<<"$release_sync_output"
grep -q 'transaction retained' <<<"${release_sync_output,,}"
rm -rf -- "$transaction_dir"

# A persisted transaction must restore the prior drop-in/current link after an
# uncatchable process or host failure, before a later deployment is considered.
mkdir -m 0700 "$transaction_dir"
printf '%s\n' "$test_root/service" >"$transaction_dir/service-root"
printf '%s\n' 'example.service' >"$transaction_dir/systemd-service"
printf '%s\n' "$POWERFORGE_SYSTEMD_CONFIG_ROOT" >"$transaction_dir/systemd-config-root"
printf '%s\n' "$first_target" >"$transaction_dir/previous-target"
printf '%s\n' 'present' >"$transaction_dir/drop-in-state"
printf '%s\n' "$(stat -c '%u' "$drop_in")" >"$transaction_dir/drop-in-owner"
printf '%s\n' "$(stat -c '%g' "$drop_in")" >"$transaction_dir/drop-in-group"
printf '%s\n' "$(stat -c '%a' "$drop_in")" >"$transaction_dir/drop-in-mode"
cp "$drop_in" "$transaction_dir/drop-in"
chmod 0600 "$transaction_dir"/*
stranded_release="$test_root/service/releases/stranded-release"
cp -a "$first_target" "$stranded_release"
ln -sfn "$stranded_release" "$test_root/service/current"
printf '[Service]\nReadWritePaths=%s\n' "$test_root/stranded-data" >"$drop_in"
mv "$test_root/config/example.env" "$test_root/config/example.env.unavailable"
set +e
recovery_output="$(POWERFORGE_SYSTEMD_CONFIG_ROOT="$test_root/systemd-renamed" TEST_SERVICE_ROOT="$test_root/service" "$deploy_script" --service example 2>&1)"
recovery_status=$?
set -e
[[ "$recovery_status" -ne 0 ]]
grep -q 'Recovering incomplete systemd writable-path transaction' <<<"$recovery_output"
grep -q 'Recovered incomplete systemd writable-path transaction' <<<"$recovery_output"
grep -q 'Service is not configured' <<<"$recovery_output"
grep -q '^restart example.service$' "$TEST_SYSTEMCTL_LOG"
[[ "$(readlink -f "$test_root/service/current")" == "$first_target" ]]
grep -qxF "ReadWritePaths=$test_root/example-data" "$drop_in"
[[ ! -e "$transaction_dir" ]]
rm -f -- "$test_root/config/example.env.unavailable"
write_config example "$test_root/service"
printf 'SYSTEMD_READ_WRITE_PATHS="%s"\n' "$test_root/example-data" >>"$test_root/config/example.env"

alias_transaction="$POWERFORGE_SERVICE_TRANSACTION_ROOT/service-aliasowner.transaction"
mkdir -m 0700 "$alias_transaction"
printf '%s\n' "$test_root/service" >"$alias_transaction/service-root"
printf '%s\n' 'example.service' >"$alias_transaction/systemd-service"
printf '%s\n' "$POWERFORGE_SYSTEMD_CONFIG_ROOT" >"$alias_transaction/systemd-config-root"
printf '%s\n' "$first_target" >"$alias_transaction/previous-target"
printf '%s\n' 'present' >"$alias_transaction/drop-in-state"
printf '%s\n' "$(stat -c '%u' "$drop_in")" >"$alias_transaction/drop-in-owner"
printf '%s\n' "$(stat -c '%g' "$drop_in")" >"$alias_transaction/drop-in-group"
printf '%s\n' "$(stat -c '%a' "$drop_in")" >"$alias_transaction/drop-in-mode"
cp "$drop_in" "$alias_transaction/drop-in"
chmod 0600 "$alias_transaction"/*
ln -sfn "$stranded_release" "$test_root/service/current"
printf '[Service]\nReadWritePaths=%s\n' "$test_root/stranded-data" >"$drop_in"
write_config aliasconsumer "$test_root/service/."
set +e
alias_output="$(TEST_SERVICE_ROOT="$test_root/service" "$deploy_script" --service aliasconsumer 2>&1)"
alias_status=$?
set -e
[[ "$alias_status" -ne 0 ]]
grep -q 'Recovered incomplete systemd writable-path transaction for example.service' <<<"$alias_output"
[[ ! -e "$alias_transaction" ]]
[[ "$(readlink -f "$test_root/service/current")" == "$first_target" ]]
grep -qxF "ReadWritePaths=$test_root/example-data" "$drop_in"

mkdir -p "$test_root/multi-current"
write_config multiinvoker "$test_root/multi-current"
sed -i 's/^SYSTEMD_SERVICE=.*/SYSTEMD_SERVICE=sharedmulti.service/' "$test_root/config/multiinvoker.env"
multi_transactions=()
for owner in aliasmulti1 aliasmulti2; do
  multi_transaction="$POWERFORGE_SERVICE_TRANSACTION_ROOT/service-${owner}.transaction"
  multi_transactions+=("$multi_transaction")
  mkdir -m 0700 "$multi_transaction"
  printf '%s\n' "$test_root/${owner}-root" >"$multi_transaction/service-root"
  printf '%s\n' 'sharedmulti.service' >"$multi_transaction/systemd-service"
  printf '%s\n' "$POWERFORGE_SYSTEMD_CONFIG_ROOT" >"$multi_transaction/systemd-config-root"
  chmod 0600 "$multi_transaction"/*
done
multi_lock_ready="$test_root/multi-lock-ready"
(
  exec 202>"$test_root/locks/powerforge-service-aliasmulti1.lock"
  flock 202
  : >"$multi_lock_ready"
  sleep 30
) &
multi_lock_holder=$!
for _ in {1..100}; do [[ -e "$multi_lock_ready" ]] && break; sleep 0.05; done
[[ -e "$multi_lock_ready" ]]
: >"$TEST_SYSTEMCTL_LOG"
set +e
multi_busy_output="$(TEST_SERVICE_ROOT="$test_root/multi-current" "$deploy_script" --service multiinvoker 2>&1)"
multi_busy_status=$?
set -e
[[ "$multi_busy_status" -ne 0 ]]
grep -q 'Another deployment is active for aliasmulti1' <<<"$multi_busy_output"
if grep -q '^stop sharedmulti.service$' "$TEST_SYSTEMCTL_LOG"; then
  echo 'Ambiguous recovery stopped a unit while a transaction owner was active.' >&2
  exit 1
fi
kill "$multi_lock_holder" 2>/dev/null || true
wait "$multi_lock_holder" 2>/dev/null || true
set +e
multi_output="$(TEST_SERVICE_ROOT="$test_root/multi-current" "$deploy_script" --service multiinvoker 2>&1)"
multi_status=$?
set -e
[[ "$multi_status" -ne 0 ]]
grep -q 'Multiple incomplete transactions overlap this deployment' <<<"$multi_output"
[[ "$(grep -c '^stop sharedmulti.service$' "$TEST_SYSTEMCTL_LOG")" -eq 1 ]]
for multi_transaction in "${multi_transactions[@]}"; do [[ -d "$multi_transaction" ]]; rm -rf -- "$multi_transaction"; done

mkdir -p "$test_root/recovery-current-service"
write_config recoverymissing "$test_root/recovery-current-service"
missing_transaction="$POWERFORGE_SERVICE_TRANSACTION_ROOT/service-recoverymissing.transaction"
mkdir -m 0700 "$missing_transaction"
printf '%s\n' "$test_root/recovery-root-missing" >"$missing_transaction/service-root"
printf '%s\n' 'recorded.service' >"$missing_transaction/systemd-service"
printf '%s\n' "$POWERFORGE_SYSTEMD_CONFIG_ROOT" >"$missing_transaction/systemd-config-root"
chmod 0600 "$missing_transaction"/*
set +e
missing_recovery_output="$(TEST_SERVICE_ROOT="$test_root/recovery-current-service" "$deploy_script" --service recoverymissing 2>&1)"
missing_recovery_status=$?
set -e
[[ "$missing_recovery_status" -ne 0 ]]
grep -q '^stop recorded.service$' "$TEST_SYSTEMCTL_LOG"
grep -q 'Recorded recovery paths are unavailable' <<<"$missing_recovery_output"
[[ -d "$missing_transaction" ]]
rm -rf -- "$missing_transaction"

create_stage example 92010 1 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
set +e
sync_failure_output="$(TEST_SERVICE_ROOT="$test_root/service" \
  FAIL_SOURCE_SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \
  FAIL_SYNC_PATH="$drop_in" \
  "$deploy_script" --service example 2>&1)"
sync_failure_status=$?
set -e
[[ "$sync_failure_status" -ne 0 ]]
grep -q 'rollback transaction retained' <<<"${sync_failure_output,,}"
[[ -d "$transaction_dir" ]]
[[ "$(readlink -f "$test_root/service/current")" == "$first_target" ]]
rm -rf -- "$transaction_dir"

mkdir -p "$test_root/commit-service" "$test_root/commit-data"
write_config commitdurable "$test_root/commit-service"
printf 'SYSTEMD_READ_WRITE_PATHS="%s"\n' "$test_root/commit-data" >>"$test_root/config/commitdurable.env"
create_stage commitdurable 92011 1 bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
commit_sync_count="$test_root/commit-sync-count"
set +e
commit_output="$(TEST_SERVICE_ROOT="$test_root/commit-service" \
  FAIL_SYNC_PATH="$POWERFORGE_SERVICE_TRANSACTION_ROOT" \
  FAIL_SYNC_COUNT_FILE="$commit_sync_count" \
  FAIL_SYNC_FROM_CALL=2 \
  "$deploy_script" --service commitdurable 2>&1)"
commit_status=$?
set -e
[[ "$commit_status" -ne 0 ]]
committed_marker="$POWERFORGE_SERVICE_TRANSACTION_ROOT/service-commitdurable.transaction.committed"
grep -q 'committed transaction retained' <<<"${commit_output,,}"
[[ -d "$committed_marker" ]]
grep -q 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' "$test_root/commit-service/current/_powerforge/deployment.json"
set +e
TEST_SERVICE_ROOT="$test_root/commit-service" "$deploy_script" --service commitdurable >/dev/null 2>&1
set -e
[[ ! -e "$committed_marker" ]]
if TEST_SERVICE_ROOT="$test_root/service" "$deploy_script" --service example --archive /etc/passwd; then
  echo 'Promoter unexpectedly accepted a caller-controlled archive path.' >&2
  exit 1
fi

previous_example_target="$(readlink -f "$test_root/service/current")"
mkdir -p "$test_root/example-data-next"
write_config example "$test_root/service"
printf 'SYSTEMD_READ_WRITE_PATHS="%s"\n' "$test_root/example-data-next" >>"$test_root/config/example.env"
create_stage example 92005 1 5555555555555555555555555555555555555555
: >"$TEST_SYSTEMCTL_LOG"
reload_count_file="$test_root/restore-reload-count"
if TEST_SERVICE_ROOT="$test_root/service" \
   FAIL_SOURCE_SHA=5555555555555555555555555555555555555555 \
   FAIL_DAEMON_RELOAD_COUNT_FILE="$reload_count_file" \
   FAIL_DAEMON_RELOAD_FROM_CALL=2 \
   "$deploy_script" --service example; then
  echo 'Deployment unexpectedly restarted after permission rollback failed.' >&2
  exit 1
fi
[[ "$(readlink -f "$test_root/service/current")" == "$previous_example_target" ]]
grep -qxF "ReadWritePaths=$test_root/example-data" "$drop_in"
[[ "$(grep -c '^restart example.service$' "$TEST_SYSTEMCTL_LOG")" -eq 1 ]]
grep -q '^stop example.service$' "$TEST_SYSTEMCTL_LOG"
[[ -d "$transaction_dir" ]]
rm -rf -- "$transaction_dir"
: >"$TEST_SYSTEMCTL_LOG"
[[ "$(readlink -f "$test_root/service/current")" == "$first_target" ]]

create_stage example 92002 1 2222222222222222222222222222222222222222
single_rollback_count_file="$test_root/single-rollback-count"
if TEST_SERVICE_ROOT="$test_root/service" \
   FAIL_SOURCE_SHA=2222222222222222222222222222222222222222 \
   FAIL_DAEMON_RELOAD_COUNT_FILE="$single_rollback_count_file" \
   FAIL_DAEMON_RELOAD_FROM_CALL=3 \
   "$deploy_script" \
  --service example; then
  echo 'Deployment unexpectedly succeeded when exact provenance health failed.' >&2
  exit 1
fi
[[ "$(readlink -f "$test_root/service/current")" == "$first_target" ]]
[[ ! -e /tmp/powerforge-service-example ]]
[[ "$(cat "$single_rollback_count_file")" == '2' ]]
[[ "$(grep -c '^restart example.service$' "$TEST_SYSTEMCTL_LOG")" -eq 2 ]]
if grep -q '^stop example.service$' "$TEST_SYSTEMCTL_LOG"; then
  echo 'Top-level permission rollback unexpectedly stopped the restored healthy service.' >&2
  exit 1
fi

mkdir -p "$test_root/early-service" "$test_root/early-data" "$POWERFORGE_SYSTEMD_CONFIG_ROOT/early.service.d"
write_config early "$test_root/early-service"
printf 'SYSTEMD_READ_WRITE_PATHS="%s"\n' "$test_root/early-data" >>"$test_root/config/early.env"
printf '[Service]\nReadWritePaths=/previous\n' >"$POWERFORGE_SYSTEMD_CONFIG_ROOT/early.service.d/powerforge-read-write-paths.conf"
create_stage early 92006 1 6666666666666666666666666666666666666666
: >"$TEST_SYSTEMCTL_LOG"
early_reload_count_file="$test_root/early-reload-count"
if TEST_SERVICE_ROOT="$test_root/early-service" \
   FAIL_DAEMON_RELOAD_COUNT_FILE="$early_reload_count_file" \
   FAIL_DAEMON_RELOAD_ON_CALL=1 \
   "$deploy_script" --service early; then
  echo 'Pre-switch reconciliation unexpectedly ignored a failed systemd reload.' >&2
  exit 1
fi
grep -qxF 'ReadWritePaths=/previous' "$POWERFORGE_SYSTEMD_CONFIG_ROOT/early.service.d/powerforge-read-write-paths.conf"
early_transaction_dir="$POWERFORGE_SERVICE_TRANSACTION_ROOT/service-early.transaction"
[[ ! -e "$early_transaction_dir" ]]
[[ ! -e "$test_root/early-service/current" ]]
if grep -q '^stop early.service$' "$TEST_SYSTEMCTL_LOG"; then
  echo 'Restored pre-switch state unexpectedly stopped the untouched service.' >&2
  exit 1
fi
: >"$TEST_SYSTEMCTL_LOG"

mkdir -p "$test_root/fresh-service"
write_config fresh "$test_root/fresh-service"
mkdir -p "$POWERFORGE_SYSTEMD_CONFIG_ROOT/fresh.service.d"
printf '[Service]\nReadWritePaths=/obsolete\n' >"$POWERFORGE_SYSTEMD_CONFIG_ROOT/fresh.service.d/powerforge-read-write-paths.conf"
chmod 0640 "$POWERFORGE_SYSTEMD_CONFIG_ROOT/fresh.service.d/powerforge-read-write-paths.conf"
create_stage fresh 92003 1 3333333333333333333333333333333333333333
if TEST_SERVICE_ROOT="$test_root/fresh-service" FAIL_SOURCE_SHA=3333333333333333333333333333333333333333 "$deploy_script" \
  --service fresh; then
  echo 'First deployment unexpectedly succeeded when health failed.' >&2
  exit 1
fi
[[ ! -e "$test_root/fresh-service/current" ]]
grep -qxF 'ReadWritePaths=/obsolete' "$POWERFORGE_SYSTEMD_CONFIG_ROOT/fresh.service.d/powerforge-read-write-paths.conf"
[[ "$(stat -c '%a' "$POWERFORGE_SYSTEMD_CONFIG_ROOT/fresh.service.d/powerforge-read-write-paths.conf")" == '640' ]]
grep -q '^stop fresh.service$' "$TEST_SYSTEMCTL_LOG"
create_stage fresh 92003 1 3333333333333333333333333333333333333333
TEST_SERVICE_ROOT="$test_root/fresh-service" "$deploy_script" --service fresh
[[ ! -e "$POWERFORGE_SYSTEMD_CONFIG_ROOT/fresh.service.d/powerforge-read-write-paths.conf" ]]

mkdir -p "$test_root/unsafe-service"
write_config unsafe "$test_root/unsafe-service"
printf 'SYSTEMD_READ_WRITE_PATHS="/"\n' >>"$test_root/config/unsafe.env"
if TEST_SERVICE_ROOT="$test_root/unsafe-service" "$deploy_script" --service unsafe; then
  echo 'Deployment unexpectedly accepted the filesystem root as a writable path.' >&2
  exit 1
fi

control_ids=(configcontrol systemdcontrol transactioncontrol stagecontrol lockcontrol)
control_paths=("$POWERFORGE_SERVICE_CONFIG_ROOT" "$POWERFORGE_SYSTEMD_CONFIG_ROOT" "$POWERFORGE_SERVICE_TRANSACTION_ROOT" "$POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT" "$(realpath -e "$POWERFORGE_SERVICE_LOCK_ROOT")")
for index in "${!control_ids[@]}"; do
  control_id="${control_ids[$index]}"
  control_path="${control_paths[$index]}"
  control_service="$test_root/${control_id}-service"
  mkdir -p "$control_service"
  write_config "$control_id" "$control_service"
  printf 'SYSTEMD_READ_WRITE_PATHS="%s"\n' "$control_path" >>"$test_root/config/${control_id}.env"
  set +e
  control_output="$(TEST_SERVICE_ROOT="$control_service" "$deploy_script" --service "$control_id" 2>&1)"
  control_status=$?
  set -e
  if [[ "$control_status" -eq 0 ]]; then
    echo "Deployment unexpectedly allowed writable access to deployment control state: $control_path" >&2
    exit 1
  fi
  grep -q 'Systemd writable path must not overlap deployment control path' <<<"$control_output"
done

# Deployment-owned state must never be placed inside prunable release storage.
mkdir -p "$test_root/control-overlap-service/releases"
write_config controloverlap "$test_root/control-overlap-service"
overlap_variables=(POWERFORGE_SERVICE_CONFIG_ROOT POWERFORGE_SYSTEMD_CONFIG_ROOT POWERFORGE_SERVICE_TRANSACTION_ROOT POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT POWERFORGE_SERVICE_LOCK_ROOT)
overlap_names=(config systemd transactions staging locks)
for index in "${!overlap_variables[@]}"; do
  overlap_variable="${overlap_variables[$index]}"
  overlap_root="$test_root/control-overlap-service/releases/${overlap_names[$index]}"
  if [[ "$overlap_variable" == 'POWERFORGE_SERVICE_CONFIG_ROOT' ]]; then
    mkdir -p "$overlap_root"
    cp "$test_root/config/controloverlap.env" "$overlap_root/controloverlap.env"
  fi
  set +e
  control_overlap_output="$(TEST_SERVICE_ROOT="$test_root/control-overlap-service" env "$overlap_variable=$overlap_root" "$deploy_script" --service controloverlap 2>&1)"
  control_overlap_status=$?
  set -e
  [[ "$control_overlap_status" -ne 0 ]]
  grep -qxF "[powerforge-service-deploy] ERROR: Deployment control path must not overlap release storage: $overlap_root" <<<"$control_overlap_output"
done

mkdir -p "$test_root/glob-service"
write_config glob "$test_root/glob-service"
printf 'SYSTEMD_READ_WRITE_PATHS="%s"\n' "$test_root/service-*" >>"$test_root/config/glob.env"
if TEST_SERVICE_ROOT="$test_root/glob-service" "$deploy_script" --service glob; then
  echo 'Deployment unexpectedly expanded a writable-path glob.' >&2
  exit 1
fi

mkdir -p "$test_root/overlap-service/releases/nested"
for overlap_path in \
  "$test_root/overlap-service" \
  "$test_root/overlap-service/releases" \
  "$test_root/overlap-service/releases/nested"; do
  write_config overlap "$test_root/overlap-service"
  printf 'SYSTEMD_READ_WRITE_PATHS="%s"\n' "$overlap_path" >>"$test_root/config/overlap.env"
  if TEST_SERVICE_ROOT="$test_root/overlap-service" "$deploy_script" --service overlap; then
    echo "Deployment unexpectedly allowed writable access to immutable release storage: $overlap_path" >&2
    exit 1
  fi
done

mkdir -p "$test_root/symlink-target/data" "$test_root/symlink-service"
ln -s "$test_root/symlink-target" "$test_root/symlink-parent"
write_config symlink "$test_root/symlink-service"
printf 'SYSTEMD_READ_WRITE_PATHS="%s"\n' "$test_root/symlink-parent/data" >>"$test_root/config/symlink.env"
if TEST_SERVICE_ROOT="$test_root/symlink-service" "$deploy_script" --service symlink; then
  echo 'Deployment unexpectedly accepted a symlinked writable-path parent.' >&2
  exit 1
fi

mkdir -p "$test_root/untrusted-parent/data" "$test_root/untrusted-service"
chmod 0777 "$test_root/untrusted-parent"
write_config untrusted "$test_root/untrusted-service"
printf 'SYSTEMD_READ_WRITE_PATHS="%s"\n' "$test_root/untrusted-parent/data" >>"$test_root/config/untrusted.env"
if TEST_SERVICE_ROOT="$test_root/untrusted-service" "$deploy_script" --service untrusted; then
  echo 'Deployment unexpectedly accepted a writable-path parent that can be redirected.' >&2
  exit 1
fi
chmod 0755 "$test_root/untrusted-parent"

mkdir -p "$test_root/systemd-symlink-service" "$test_root/systemd-attacker"
write_config dirsymlink "$test_root/systemd-symlink-service"
ln -s "$test_root/systemd-attacker" "$POWERFORGE_SYSTEMD_CONFIG_ROOT/dirsymlink.service.d"
if TEST_SERVICE_ROOT="$test_root/systemd-symlink-service" "$deploy_script" --service dirsymlink; then
  echo 'Deployment unexpectedly accepted a symlinked systemd drop-in directory.' >&2
  exit 1
fi
rm -f -- "$POWERFORGE_SYSTEMD_CONFIG_ROOT/dirsymlink.service.d"

mkdir -p "$test_root/systemd-untrusted-service" "$POWERFORGE_SYSTEMD_CONFIG_ROOT/diruntrusted.service.d"
chmod 0777 "$POWERFORGE_SYSTEMD_CONFIG_ROOT/diruntrusted.service.d"
write_config diruntrusted "$test_root/systemd-untrusted-service"
if TEST_SERVICE_ROOT="$test_root/systemd-untrusted-service" "$deploy_script" --service diruntrusted; then
  echo 'Deployment unexpectedly accepted a writable systemd drop-in directory.' >&2
  exit 1
fi
chmod 0755 "$POWERFORGE_SYSTEMD_CONFIG_ROOT/diruntrusted.service.d"

mkdir -p "$test_root/config-symlink-target" "$test_root/config-trust-service"
write_config configtrust "$test_root/config-trust-service"
cp "$test_root/config/configtrust.env" "$test_root/config-symlink-target/configtrust.env"
ln -s "$test_root/config-symlink-target" "$test_root/config-symlink"
set +e
config_symlink_output="$(POWERFORGE_SERVICE_CONFIG_ROOT="$test_root/config-symlink" TEST_SERVICE_ROOT="$test_root/config-trust-service" "$deploy_script" --service configtrust 2>&1)"
config_symlink_status=$?
set -e
if [[ "$config_symlink_status" -eq 0 ]]; then
  echo 'Deployment unexpectedly accepted a symlinked service config root.' >&2
  exit 1
fi
grep -q 'Service config root must be a real directory' <<<"$config_symlink_output"
mkdir -p "$test_root/config-writable"
cp "$test_root/config/configtrust.env" "$test_root/config-writable/configtrust.env"
chmod 0777 "$test_root/config-writable"
set +e
config_writable_output="$(POWERFORGE_SERVICE_CONFIG_ROOT="$test_root/config-writable" TEST_SERVICE_ROOT="$test_root/config-trust-service" "$deploy_script" --service configtrust 2>&1)"
config_writable_status=$?
set -e
if [[ "$config_writable_status" -eq 0 ]]; then
  echo 'Deployment unexpectedly accepted a writable service config root.' >&2
  exit 1
fi
grep -q 'Service config root must not be group/world writable' <<<"$config_writable_output"
chmod 0755 "$test_root/config-writable"

mkdir -p "$test_root/service-root-target" "$test_root/service-root-config"
ln -s "$test_root/service-root-target" "$test_root/service-root-link"
write_config rootsymlink "$test_root/service-root-link"
set +e
root_symlink_output="$(TEST_SERVICE_ROOT="$test_root/service-root-target" "$deploy_script" --service rootsymlink 2>&1)"
root_symlink_status=$?
set -e
if [[ "$root_symlink_status" -eq 0 ]]; then
  echo 'Deployment unexpectedly accepted a symlinked service root.' >&2
  exit 1
fi
grep -q 'Service root must be a real, pre-provisioned directory' <<<"$root_symlink_output"
mkdir -p "$test_root/service-root-writable"
chmod 0777 "$test_root/service-root-writable"
write_config rootwritable "$test_root/service-root-writable"
set +e
root_writable_output="$(TEST_SERVICE_ROOT="$test_root/service-root-writable" "$deploy_script" --service rootwritable 2>&1)"
root_writable_status=$?
set -e
if [[ "$root_writable_status" -eq 0 ]]; then
  echo 'Deployment unexpectedly accepted a writable service root.' >&2
  exit 1
fi
grep -q 'Service root must not be group/world writable' <<<"$root_writable_output"
chmod 0755 "$test_root/service-root-writable"

mkdir -p "$test_root/release-link-service" "$test_root/release-link-target"
ln -s "$test_root/release-link-target" "$test_root/release-link-service/releases"
write_config releaselink "$test_root/release-link-service"
set +e
release_link_output="$(TEST_SERVICE_ROOT="$test_root/release-link-service" "$deploy_script" --service releaselink 2>&1)"
release_link_status=$?
set -e
if [[ "$release_link_status" -eq 0 ]]; then
  echo 'Deployment unexpectedly accepted a symlinked release root.' >&2
  exit 1
fi
grep -q 'Release root must be a real directory' <<<"$release_link_output"

mkdir -p "$test_root/unit-lock-service"
write_config unitalias "$test_root/unit-lock-service"
sed -i 's/^SYSTEMD_SERVICE=.*/SYSTEMD_SERVICE=example.service/' "$test_root/config/unitalias.env"
unit_lock_key="$(printf '%s' 'example.service' | sha256sum | awk '{print $1}')"
unit_lock_ready="$test_root/unit-lock-ready"
(
  exec 200>"$test_root/locks/powerforge-systemd-${unit_lock_key}.lock"
  flock 200
  : >"$unit_lock_ready"
  sleep 30
) &
unit_lock_holder=$!
for _ in {1..100}; do [[ -e "$unit_lock_ready" ]] && break; sleep 0.05; done
[[ -e "$unit_lock_ready" ]]
set +e
unit_lock_output="$(TEST_SERVICE_ROOT="$test_root/unit-lock-service" "$deploy_script" --service unitalias 2>&1)"
unit_lock_status=$?
set -e
if [[ "$unit_lock_status" -eq 0 ]]; then
  echo 'Deployment unexpectedly bypassed serialization for a shared systemd unit.' >&2
  kill "$unit_lock_holder" 2>/dev/null || true
  wait "$unit_lock_holder" 2>/dev/null || true
  exit 1
fi
grep -q 'Another deployment is active for systemd unit example.service' <<<"$unit_lock_output"
kill "$unit_lock_holder" 2>/dev/null || true
wait "$unit_lock_holder" 2>/dev/null || true

write_config rootalias "$test_root/service"
service_root_lock_key="$(printf '%s' "$test_root/service" | sha256sum | awk '{print $1}')"
root_lock_ready="$test_root/root-lock-ready"
(
  exec 201>"$test_root/locks/powerforge-root-${service_root_lock_key}.lock"
  flock 201
  : >"$root_lock_ready"
  sleep 30
) &
root_lock_holder=$!
for _ in {1..100}; do [[ -e "$root_lock_ready" ]] && break; sleep 0.05; done
[[ -e "$root_lock_ready" ]]
set +e
root_lock_output="$(TEST_SERVICE_ROOT="$test_root/service" "$deploy_script" --service rootalias 2>&1)"
root_lock_status=$?
set -e
if [[ "$root_lock_status" -eq 0 ]]; then
  echo 'Deployment unexpectedly bypassed serialization for a shared service root.' >&2
  kill "$root_lock_holder" 2>/dev/null || true
  wait "$root_lock_holder" 2>/dev/null || true
  exit 1
fi
grep -q "Another deployment is active for service root $test_root/service" <<<"$root_lock_output"
kill "$root_lock_holder" 2>/dev/null || true
wait "$root_lock_holder" 2>/dev/null || true

mkdir -p "$test_root/signal-service"
write_config signal "$test_root/signal-service"
create_stage signal 92007 1 7777777777777777777777777777777777777777
set +e
TEST_SERVICE_ROOT="$test_root/signal-service" \
  SIGNAL_ON_SYSTEMCTL_COMMAND='restart signal.service' \
  SIGNAL_NAME=TERM \
  "$deploy_script" --service signal
signal_status=$?
set -e
[[ "$signal_status" -eq 143 ]]
[[ ! -e "$test_root/signal-service/current" ]]
grep -q '^stop signal.service$' "$TEST_SYSTEMCTL_LOG"

mkdir -p "$test_root/rollback-service"
write_config rollback "$test_root/rollback-service"
create_stage rollback 92008 1 8888888888888888888888888888888888888888
TEST_SERVICE_ROOT="$test_root/rollback-service" "$deploy_script" --service rollback
rollback_previous="$(readlink -f "$test_root/rollback-service/current")"
create_stage rollback 92009 1 9999999999999999999999999999999999999999
set +e
rollback_output="$(TEST_SERVICE_ROOT="$test_root/rollback-service" \
  FAIL_SOURCE_SHA=9999999999999999999999999999999999999999 \
  FAIL_ROLLBACK_LINK_MOVE=1 \
  FAIL_SYSTEMCTL_COMMAND='stop rollback.service' \
  "$deploy_script" --service rollback 2>&1)"
rollback_status=$?
set -e
[[ "$rollback_status" -ne 0 ]]
grep -q 'CRITICAL: failed to prove rollback.service is safely restored or stopped.' <<<"$rollback_output"
rollback_current="$(readlink -f "$test_root/rollback-service/current")"
[[ "$rollback_current" != "$rollback_previous" && -d "$rollback_current" ]]
grep -q '9999999999999999999999999999999999999999' "$rollback_current/_powerforge/deployment.json"

mkdir -p "$test_root/reload-service" "$test_root/reload-data"
write_config reload "$test_root/reload-service"
printf 'SYSTEMD_READ_WRITE_PATHS="%s"\n' "$test_root/reload-data" >>"$test_root/config/reload.env"
create_stage reload 92004 1 4444444444444444444444444444444444444444
if TEST_SERVICE_ROOT="$test_root/reload-service" FAIL_DAEMON_RELOAD=1 "$deploy_script" --service reload; then
  echo 'Deployment unexpectedly ignored a failed systemd reload.' >&2
  exit 1
fi
[[ ! -e "$POWERFORGE_SYSTEMD_CONFIG_ROOT/reload.service.d/powerforge-read-write-paths.conf" ]]
create_stage reload 92004 1 4444444444444444444444444444444444444444
TEST_SERVICE_ROOT="$test_root/reload-service" "$deploy_script" --service reload
[[ -L "$test_root/reload-service/current" ]]

if [[ -d "$POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT" ]] && find "$POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT" -mindepth 1 -maxdepth 1 | grep -q .; then
  echo 'Root-owned service deployment staging was not cleaned.' >&2
  exit 1
fi

echo 'powerforge-service-deploy integration tests passed.'
