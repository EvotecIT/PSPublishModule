#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
deploy_script="$repo_root/Deployment/Linux/powerforge-site-deploy.sh"
test_root="$(mktemp -d)"
trap 'rm -rf "$test_root" /tmp/powerforge-91001-1 /tmp/powerforge-91002-1 /tmp/powerforge-91003-1 /tmp/powerforge-91004-1' EXIT

mkdir -p "$test_root/config" "$test_root/locks" "$test_root/site" "$test_root/bin"
export POWERFORGE_SITE_TRUSTED_STAGE_ROOT="$test_root/trusted-stage"
cat >"$test_root/config/example.env" <<EOF
SITE_ROOT=$test_root/site
PUBLIC_URL=https://example.test
RELEASES_TO_KEEP=3
SMOKE_PATHS="/"
CLOUDFLARE_PURGE_ENABLED=0
EOF

cat >"$test_root/bin/curl" <<'EOF'
#!/usr/bin/env bash
set -e
if [[ "${FAKE_CURL_FAIL:-0}" == '1' ]]; then
  exit 22
fi
cat "$FAKE_SITE_ROOT/current/_powerforge/deployment.json"
EOF
chmod +x "$test_root/bin/curl"

create_deployment() {
  local run_id="$1"
  local source_sha="$2"
  local body="$3"
  local staging="/tmp/powerforge-${run_id}-1"
  local content="$test_root/content-${run_id}"
  mkdir -p "$staging" "$content"
  printf '%s\n' "$body" >"$content/index.html"
  tar -C "$content" -cf "$staging/artifact.tar" .
  local artifact_sha
  artifact_sha="$(sha256sum "$staging/artifact.tar" | awk '{print $1}')"
  cat >"$staging/deployment.json" <<EOF
{
  "schemaVersion": 1,
  "sourceRepository": "Example/Site",
  "sourceSha": "$source_sha",
  "engineRepository": "Example/PowerForge",
  "engineSha": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "workflowRunId": "$run_id",
  "workflowRunAttempt": "1",
  "artifactSha256": "$artifact_sha",
  "deployedAtUtc": "2026-07-15T00:00:00Z"
}
EOF
}

sha_one='1111111111111111111111111111111111111111'
sha_two='2222222222222222222222222222222222222222'
create_deployment 91001 "$sha_one" 'release-one'

env \
  PATH="$test_root/bin:$PATH" \
  FAKE_SITE_ROOT="$test_root/site" \
  POWERFORGE_SITE_CONFIG_ROOT="$test_root/config" \
  POWERFORGE_SITE_LOCK_ROOT="$test_root/locks" \
  "$deploy_script" --site example --archive /tmp/powerforge-91001-1/artifact.tar --metadata /tmp/powerforge-91001-1/deployment.json

grep -Fq 'release-one' "$test_root/site/current/index.html"
grep -Fq "$sha_one" "$test_root/site/current/_powerforge/deployment.json"
[[ "$(stat -L -c '%a' "$test_root/site/current")" == '755' ]]
[[ "$(stat -c '%a' "$test_root/site/current/index.html")" == '644' ]]
first_release="$(readlink -f "$test_root/site/current")"

create_deployment 91002 "$sha_two" 'release-two'
if env \
  PATH="$test_root/bin:$PATH" \
  FAKE_CURL_FAIL=1 \
  FAKE_SITE_ROOT="$test_root/site" \
  POWERFORGE_SITE_CONFIG_ROOT="$test_root/config" \
  POWERFORGE_SITE_LOCK_ROOT="$test_root/locks" \
  "$deploy_script" --site example --archive /tmp/powerforge-91002-1/artifact.tar --metadata /tmp/powerforge-91002-1/deployment.json; then
  echo 'Expected failed smoke deployment to return non-zero.' >&2
  exit 1
fi

[[ "$(readlink -f "$test_root/site/current")" == "$first_release" ]]
grep -Fq 'release-one' "$test_root/site/current/index.html"
if find "$test_root/site/releases" -mindepth 1 -maxdepth 1 -type d -name '*91002*' | grep -q .; then
  echo 'Failed release was not removed after rollback.' >&2
  exit 1
fi

mkdir -p "$test_root/first-site"
cat >"$test_root/config/first.env" <<EOF
SITE_ROOT=$test_root/first-site
PUBLIC_URL=https://first.example.test
RELEASES_TO_KEEP=3
SMOKE_PATHS="/"
CLOUDFLARE_PURGE_ENABLED=0
EOF
create_deployment 91003 '3333333333333333333333333333333333333333' 'failed-first-release'
if env \
  PATH="$test_root/bin:$PATH" \
  FAKE_CURL_FAIL=1 \
  FAKE_SITE_ROOT="$test_root/first-site" \
  POWERFORGE_SITE_CONFIG_ROOT="$test_root/config" \
  POWERFORGE_SITE_LOCK_ROOT="$test_root/locks" \
  "$deploy_script" --site first --archive /tmp/powerforge-91003-1/artifact.tar --metadata /tmp/powerforge-91003-1/deployment.json; then
  echo 'Expected failed first deployment to return non-zero.' >&2
  exit 1
fi
[[ ! -e "$test_root/first-site/current" ]]
if find "$test_root/first-site/releases" -mindepth 1 -maxdepth 1 -type d | grep -q .; then
  echo 'Failed first release was not removed.' >&2
  exit 1
fi

mkdir -p "$test_root/cloudflare-site"
cat >"$test_root/config/cloudflare.env" <<EOF
SITE_ROOT=$test_root/cloudflare-site
PUBLIC_URL=https://cloudflare.example.test
CLOUDFLARE_PURGE_ENABLED=1
EOF
create_deployment 91004 '4444444444444444444444444444444444444444' 'cloudflare-preflight-failure'
if env \
  PATH="$test_root/bin:$PATH" \
  FAKE_SITE_ROOT="$test_root/cloudflare-site" \
  POWERFORGE_SITE_CONFIG_ROOT="$test_root/config" \
  POWERFORGE_SITE_LOCK_ROOT="$test_root/locks" \
  "$deploy_script" --site cloudflare --archive /tmp/powerforge-91004-1/artifact.tar --metadata /tmp/powerforge-91004-1/deployment.json; then
  echo 'Expected incomplete Cloudflare configuration to fail preflight.' >&2
  exit 1
fi
[[ ! -e "$test_root/cloudflare-site/current" ]]
if [[ -d "$POWERFORGE_SITE_TRUSTED_STAGE_ROOT" ]] && find "$POWERFORGE_SITE_TRUSTED_STAGE_ROOT" -mindepth 1 -maxdepth 1 | grep -q .; then
  echo 'Root-owned deployment staging was not cleaned.' >&2
  exit 1
fi

echo 'powerforge-site-deploy integration tests passed.'
