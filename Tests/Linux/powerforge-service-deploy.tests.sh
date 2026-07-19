#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
deploy_script="$repo_root/Deployment/Linux/powerforge-service-deploy.sh"
test_root="$(mktemp -d)"
trap 'rm -rf "$test_root" /tmp/powerforge-service-example /tmp/powerforge-service-fresh' EXIT

mkdir -p "$test_root/config" "$test_root/locks" "$test_root/bin" "$test_root/service"
export POWERFORGE_SERVICE_CONFIG_ROOT="$test_root/config"
export POWERFORGE_SERVICE_LOCK_ROOT="$test_root/locks"
export POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT="$test_root/trusted-stage"
export TEST_SYSTEMCTL_LOG="$test_root/systemctl.log"

cat >"$test_root/bin/systemctl" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
printf '%s\n' "$*" >>"$TEST_SYSTEMCTL_LOG"
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
create_stage example 92001 1 1111111111111111111111111111111111111111
TEST_SERVICE_ROOT="$test_root/service" "$deploy_script" \
  --service example

first_target="$(readlink -f "$test_root/service/current")"
[[ -s "$first_target/package.json" ]]
grep -q '1111111111111111111111111111111111111111' "$first_target/_powerforge/deployment.json"
grep -q '^restart example.service$' "$TEST_SYSTEMCTL_LOG"
[[ ! -e /tmp/powerforge-service-example ]]
if TEST_SERVICE_ROOT="$test_root/service" "$deploy_script" --service example --archive /etc/passwd; then
  echo 'Promoter unexpectedly accepted a caller-controlled archive path.' >&2
  exit 1
fi
[[ "$(readlink -f "$test_root/service/current")" == "$first_target" ]]

create_stage example 92002 1 2222222222222222222222222222222222222222
if TEST_SERVICE_ROOT="$test_root/service" FAIL_SOURCE_SHA=2222222222222222222222222222222222222222 "$deploy_script" \
  --service example; then
  echo 'Deployment unexpectedly succeeded when exact provenance health failed.' >&2
  exit 1
fi
[[ "$(readlink -f "$test_root/service/current")" == "$first_target" ]]
[[ ! -e /tmp/powerforge-service-example ]]
[[ "$(grep -c '^restart example.service$' "$TEST_SYSTEMCTL_LOG")" -ge 3 ]]

mkdir -p "$test_root/fresh-service"
write_config fresh "$test_root/fresh-service"
create_stage fresh 92003 1 3333333333333333333333333333333333333333
if TEST_SERVICE_ROOT="$test_root/fresh-service" FAIL_SOURCE_SHA=3333333333333333333333333333333333333333 "$deploy_script" \
  --service fresh; then
  echo 'First deployment unexpectedly succeeded when health failed.' >&2
  exit 1
fi
[[ ! -e "$test_root/fresh-service/current" ]]
grep -q '^stop fresh.service$' "$TEST_SYSTEMCTL_LOG"

if [[ -d "$POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT" ]] && find "$POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT" -mindepth 1 -maxdepth 1 | grep -q .; then
  echo 'Root-owned service deployment staging was not cleaned.' >&2
  exit 1
fi

echo 'powerforge-service-deploy integration tests passed.'
