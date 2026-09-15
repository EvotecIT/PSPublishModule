#!/usr/bin/env bash
set -euo pipefail

readonly APACHECTL="${POWERFORGE_CERTBOT_APACHECTL:-/usr/sbin/apache2ctl}"
readonly SYSTEMCTL="${POWERFORGE_CERTBOT_SYSTEMCTL:-/usr/bin/systemctl}"

if [[ "$APACHECTL" != /* || ! -x "$APACHECTL" ]]; then
  echo "powerforge-certbot-reload-apache: apache2ctl must be an executable absolute path" >&2
  exit 1
fi
if [[ "$SYSTEMCTL" != /* || ! -x "$SYSTEMCTL" ]]; then
  echo "powerforge-certbot-reload-apache: systemctl must be an executable absolute path" >&2
  exit 1
fi

if ! configtest_output="$("$APACHECTL" configtest 2>&1)"; then
  printf '%s\n' "$configtest_output" >&2
  exit 1
fi

"$SYSTEMCTL" reload apache2
