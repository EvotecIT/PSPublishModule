#!/usr/bin/env bash
set -Eeuo pipefail

umask 022

http_site=''
https_site=''
certificate_name=''
apache_service="${POWERFORGE_APACHE_SERVICE:-apache2}"

fail() {
  echo "powerforge-apache-site-enable: $*" >&2
  exit 1
}

usage() {
  cat >&2 <<'EOF'
Usage: powerforge-apache-site-enable --http-site <name> --https-site <name> --certificate-name <name>
EOF
  exit 2
}

while (($# > 0)); do
  case "$1" in
    --http-site)
      (($# >= 2)) || usage
      http_site="$2"
      shift 2
      ;;
    --https-site)
      (($# >= 2)) || usage
      https_site="$2"
      shift 2
      ;;
    --certificate-name)
      (($# >= 2)) || usage
      certificate_name="$2"
      shift 2
      ;;
    *)
      usage
      ;;
  esac
done

[[ "${EUID}" -eq 0 ]] || fail 'must run as root'
[[ "$http_site" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\.conf$ ]] || fail 'invalid HTTP site name'
[[ "$https_site" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\.conf$ ]] || fail 'invalid HTTPS site name'
[[ "$certificate_name" =~ ^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$ ]] || fail 'invalid certificate name'
[[ "$apache_service" =~ ^[A-Za-z0-9_.@-]+$ ]] || fail 'invalid Apache service name'
[[ "$http_site" != "$https_site" ]] || fail 'HTTP and HTTPS site names must differ'

required_commands=(a2dissite a2ensite apachectl systemctl)
for command_name in "${required_commands[@]}"; do
  command -v "$command_name" >/dev/null 2>&1 || fail "required command not found: $command_name"
done

http_enabled_path="/etc/apache2/sites-enabled/$http_site"
https_enabled_path="/etc/apache2/sites-enabled/$https_site"
http_was_enabled=0
https_was_enabled=0
[[ -e "$http_enabled_path" || -L "$http_enabled_path" ]] && http_was_enabled=1
[[ -e "$https_enabled_path" || -L "$https_enabled_path" ]] && https_was_enabled=1

certificate_dir="/etc/letsencrypt/live/$certificate_name"
certificate_available=0
if [[ -s "$certificate_dir/fullchain.pem" && -s "$certificate_dir/privkey.pem" ]]; then
  certificate_available=1
elif [[ "$https_was_enabled" == 1 ]]; then
  if ! a2dissite "$https_site" >/dev/null; then
    fail "certificate is missing and failed to disable stale HTTPS site $https_site"
  fi
fi

restore_site_state() {
  local site="$1"
  local was_enabled="$2"
  if [[ "$was_enabled" == 0 ]]; then
    a2dissite "$site" >/dev/null 2>&1 || true
  fi
  if apachectl configtest >/dev/null 2>&1; then
    systemctl reload "$apache_service" >/dev/null 2>&1 || true
  fi
}

if ! a2ensite "$http_site" >/dev/null; then
  restore_site_state "$http_site" "$http_was_enabled"
  fail "failed to enable HTTP site $http_site"
fi
if ! apachectl configtest >/dev/null; then
  restore_site_state "$http_site" "$http_was_enabled"
  fail 'HTTP Apache configuration validation failed; previous site state restored'
fi
if ! systemctl reload "$apache_service"; then
  restore_site_state "$http_site" "$http_was_enabled"
  fail 'HTTP Apache reload failed; previous site state restored'
fi

if [[ "$certificate_available" == 0 ]]; then
  echo "powerforge-apache-site-enable: HTTP site enabled; obtain or restore certificate $certificate_name before enabling HTTPS" >&2
  exit 3
fi

if ! a2ensite "$https_site" >/dev/null; then
  restore_site_state "$https_site" "$https_was_enabled"
  fail "failed to enable HTTPS site $https_site"
fi
if ! apachectl configtest >/dev/null; then
  restore_site_state "$https_site" "$https_was_enabled"
  fail 'HTTPS Apache configuration validation failed; previous site state restored'
fi
if ! systemctl reload "$apache_service"; then
  restore_site_state "$https_site" "$https_was_enabled"
  fail 'HTTPS Apache reload failed; previous site state restored'
fi

echo "powerforge-apache-site-enable: enabled $http_site and $https_site"
