#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
hook="$repo_root/Deployment/Linux/powerforge-certbot-reload-apache.sh"
fixture_root="$(mktemp -d)"
trap 'rm -rf "$fixture_root"' EXIT

cat >"$fixture_root/apache-success" <<'EOF'
#!/usr/bin/env bash
echo 'Syntax OK' >&2
EOF
cat >"$fixture_root/apache-failure" <<'EOF'
#!/usr/bin/env bash
echo 'synthetic invalid Apache configuration' >&2
exit 1
EOF
cat >"$fixture_root/systemctl" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$*" >>"$POWERFORGE_CERTBOT_TEST_LOG"
EOF
chmod 755 "$fixture_root/apache-success" "$fixture_root/apache-failure" "$fixture_root/systemctl"

export POWERFORGE_CERTBOT_TEST_LOG="$fixture_root/systemctl.log"
success_output="$(POWERFORGE_CERTBOT_APACHECTL="$fixture_root/apache-success" POWERFORGE_CERTBOT_SYSTEMCTL="$fixture_root/systemctl" "$hook" 2>&1)"
[[ -z "$success_output" ]]
grep -Fxq 'reload apache2' "$POWERFORGE_CERTBOT_TEST_LOG"

: >"$POWERFORGE_CERTBOT_TEST_LOG"
if POWERFORGE_CERTBOT_APACHECTL="$fixture_root/apache-failure" POWERFORGE_CERTBOT_SYSTEMCTL="$fixture_root/systemctl" "$hook" >"$fixture_root/failure.out" 2>&1; then
  echo 'Expected invalid Apache configuration to stop the deploy hook.' >&2
  exit 1
fi
grep -Fq 'synthetic invalid Apache configuration' "$fixture_root/failure.out"
[[ ! -s "$POWERFORGE_CERTBOT_TEST_LOG" ]]

echo 'powerforge-certbot-reload-apache integration tests passed.'
