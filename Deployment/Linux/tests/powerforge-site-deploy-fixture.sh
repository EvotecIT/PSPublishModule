#!/usr/bin/env bash
set -Eeuo pipefail

[[ "$(id -u)" -eq 0 ]] || { echo 'This fixture must run as root.' >&2; exit 1; }

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
fixture_root="$(mktemp -d /tmp/powerforge-site-fixture.XXXXXXXX)"
base_run="$((900000 + $$))"
stage_paths=()

cleanup() {
  if [[ "$fixture_root" == /tmp/powerforge-site-fixture.* ]]; then
    rm -rf -- "$fixture_root"
  fi
  local stage_path
  for stage_path in "${stage_paths[@]}"; do
    [[ "$stage_path" == /tmp/powerforge-[0-9]*-1-example.com ]] && rm -rf -- "$stage_path"
  done
}
trap cleanup EXIT

mkdir -p "$fixture_root/bin" "$fixture_root/config" "$fixture_root/locks" "$fixture_root/trusted" "$fixture_root/content"
install -m 0755 "$repo_root/Deployment/Linux/powerforge-site-deploy.sh" "$fixture_root/bin/powerforge-site-deploy"
install -m 0755 "$repo_root/Deployment/Linux/powerforge-site-reconcile.sh" "$fixture_root/bin/powerforge-site-reconcile"
# The quoted literal is the body of the generated curl shim.
# shellcheck disable=SC2016
printf '%s\n' \
  '#!/usr/bin/env bash' \
  'printf "%s\\n" "$*" >>"$POWERFORGE_FIXTURE_CURL_LOG"' \
  'printf "%s\\n" "{\"success\":true}"' >"$fixture_root/bin/curl"
chmod 0755 "$fixture_root/bin/curl"
printf '%s\n' '<!doctype html><title>PowerForge deployment fixture</title>' >"$fixture_root/content/index.html"
tar -cf "$fixture_root/artifact.tar" -C "$fixture_root/content" .
artifact_sha="$(sha256sum "$fixture_root/artifact.tar" | awk '{print $1}')"
printf '%s' 'host-token' >"$fixture_root/host.token"
chmod 0600 "$fixture_root/host.token"

export PATH="$fixture_root/bin:$PATH"
export POWERFORGE_FIXTURE_CURL_LOG="$fixture_root/curl.log"
export POWERFORGE_SITE_CONFIG_ROOT="$fixture_root/config"
export POWERFORGE_SITE_LOCK_ROOT="$fixture_root/locks"
export POWERFORGE_SITE_TRUSTED_STAGE_ROOT="$fixture_root/trusted"
export POWERFORGE_SITE_PENDING_STATE_ROOT="$fixture_root/pending"
export POWERFORGE_SITE_DEPLOY_COMMAND="$fixture_root/bin/powerforge-site-deploy"
deploy_command="$fixture_root/bin/powerforge-site-deploy"

write_config() {
  local credential_mode="$1"
  local values=(
    "SITE_ROOT=$fixture_root/site"
    'PUBLIC_URL=https://example.com'
    'RELEASES_TO_KEEP=5'
    'SMOKE_PATHS="/"'
    'CLOUDFLARE_PURGE_ENABLED=1'
    'PENDING_TTL_SECONDS=600'
    'PENDING_RECONCILE_REQUIRED=0'
  )
  if [[ "$credential_mode" == 'host' ]]; then
    values+=(
      'CLOUDFLARE_ZONE_ID=0123456789abcdef0123456789abcdef'
      "CLOUDFLARE_API_TOKEN_FILE=$fixture_root/host.token"
    )
  fi
  printf '%s\n' "${values[@]}" >"$fixture_root/config/example.com.env"
}

stage_release() {
  local run_id="$1"
  local source_sha="$2"
  local credential_mode="$3"
  local stage_path="/tmp/powerforge-${run_id}-1-example.com"
  stage_paths+=("$stage_path")
  install -d -m 0700 "$stage_path"
  install -m 0600 "$fixture_root/artifact.tar" "$stage_path/artifact.tar"
  printf '{"sourceSha":"%s","artifactSha256":"%s","workflowRunId":"%s","workflowRunAttempt":"1"}\n' \
    "$source_sha" "$artifact_sha" "$run_id" >"$stage_path/deployment.json"
  chmod 0600 "$stage_path/deployment.json"
  if [[ "$credential_mode" == 'ephemeral' ]]; then
    printf '%s' 'fixture-token' >"$stage_path/cloudflare-api.token"
    printf '%s' '0123456789abcdef0123456789abcdef' >"$stage_path/cloudflare-zone-id"
    chmod 0600 "$stage_path/cloudflare-api.token" "$stage_path/cloudflare-zone-id"
  fi
}

promote_release() {
  local run_id="$1"
  local output
  output="$($deploy_command --site example.com \
    --archive "/tmp/powerforge-${run_id}-1-example.com/artifact.tar" \
    --metadata "/tmp/powerforge-${run_id}-1-example.com/deployment.json" \
    --defer-public-verification)"
  release_id="$(sed -n 's/^POWERFORGE_RELEASE_ID=//p' <<<"$output" | tail -n 1)"
  [[ -n "$release_id" ]]
}

write_config ephemeral
run1="$base_run"
stage_release "$run1" '1111111111111111111111111111111111111111' ephemeral
promote_release "$run1"
release1="$release_id"
[[ -s "$fixture_root/pending/example.com/cloudflare-api.token" ]]
[[ -z "$(sed -n '2p' "$fixture_root/pending/example.com/state")" ]]
$deploy_command --site example.com --expire-pending
[[ -d "$fixture_root/pending/example.com" ]]
$deploy_command --site example.com --finalize --release-id "$release1"
[[ ! -e "$fixture_root/pending/example.com" ]]
[[ "$(basename "$(readlink -f "$fixture_root/site/current")")" == "$release1" ]]

run2="$((base_run + 1))"
stage_release "$run2" '2222222222222222222222222222222222222222' ephemeral
promote_release "$run2"
release2="$release_id"
[[ "$(sed -n '2p' "$fixture_root/pending/example.com/state")" == "$fixture_root/site/releases/$release1" ]]
$deploy_command --site example.com --rollback --release-id "$release2"
[[ ! -e "$fixture_root/pending/example.com" && ! -e "$fixture_root/site/releases/$release2" ]]
[[ "$(basename "$(readlink -f "$fixture_root/site/current")")" == "$release1" ]]

run3="$((base_run + 2))"
stage_release "$run3" '3333333333333333333333333333333333333333' ephemeral
promote_release "$run3"
release3="$release_id"
sed -i '$s/.*/0/' "$fixture_root/pending/example.com/state"
"$fixture_root/bin/powerforge-site-reconcile"
[[ ! -e "$fixture_root/pending/example.com" && ! -e "$fixture_root/site/releases/$release3" ]]
[[ "$(basename "$(readlink -f "$fixture_root/site/current")")" == "$release1" ]]

prepared_release="prepared-${base_run}"
mkdir -p "$fixture_root/site/releases/$prepared_release" "$fixture_root/pending/example.com"
printf '%s\n%s\n%s\n' "$prepared_release" "$fixture_root/site/releases/$release1" '0' >"$fixture_root/pending/example.com/state"
printf '%s' 'fixture-token' >"$fixture_root/pending/example.com/cloudflare-api.token"
printf '%s' '0123456789abcdef0123456789abcdef' >"$fixture_root/pending/example.com/cloudflare-zone-id"
chmod 0700 "$fixture_root/pending/example.com"
chmod 0600 "$fixture_root/pending/example.com/"*
"$fixture_root/bin/powerforge-site-reconcile"
[[ ! -e "$fixture_root/pending/example.com" && ! -e "$fixture_root/site/releases/$prepared_release" ]]
[[ "$(basename "$(readlink -f "$fixture_root/site/current")")" == "$release1" ]]

write_config host
run4="$((base_run + 3))"
stage_release "$run4" '4444444444444444444444444444444444444444' host
promote_release "$run4"
release4="$release_id"
[[ "$(sed -n '2p' "$fixture_root/pending/example.com/state")" == "$fixture_root/site/releases/$release1" ]]
[[ ! -e "$fixture_root/pending/example.com/cloudflare-api.token" ]]
$deploy_command --site example.com --rollback --release-id "$release4"
[[ ! -e "$fixture_root/pending/example.com" && ! -e "$fixture_root/site/releases/$release4" ]]

external_target="$fixture_root/external-release"
mkdir -p "$external_target"
rm -f "$fixture_root/site/current"
ln -s "$external_target" "$fixture_root/site/current"
run5="$((base_run + 4))"
stage_release "$run5" '5555555555555555555555555555555555555555' host
promote_release "$run5"
release5="$release_id"
[[ "$(sed -n '2p' "$fixture_root/pending/example.com/state")" == "$external_target" ]]
$deploy_command --site example.com --rollback --release-id "$release5"
[[ "$(readlink -f "$fixture_root/site/current")" == "$external_target" ]]
[[ "$(wc -l <"$fixture_root/curl.log")" -eq 10 ]]

printf '%s\n' \
  'deferred_finalize_and_rollback=passed' \
  'expired_and_prepared_release_reconciliation=passed' \
  'ephemeral_and_host_cache_purge=passed' \
  'external_rollback_target=passed'
