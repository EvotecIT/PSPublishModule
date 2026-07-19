#!/usr/bin/env bash
set -Eeuo pipefail

PENDING_STATE_ROOT="${POWERFORGE_SITE_PENDING_STATE_ROOT:-/var/lib/powerforge/site-pending}"
CONFIG_ROOT="${POWERFORGE_SITE_CONFIG_ROOT:-/etc/powerforge/sites}"
DEPLOY_COMMAND="${POWERFORGE_SITE_DEPLOY_COMMAND:-/usr/local/sbin/powerforge-site-deploy}"

log() {
  printf '[powerforge-site-reconcile] %s\n' "$*"
}

[[ "$(id -u)" -eq 0 ]] || { log 'ERROR: Reconciliation must run as root.' >&2; exit 1; }
[[ "$PENDING_STATE_ROOT" == /* && "$PENDING_STATE_ROOT" != '/' ]] || { log 'ERROR: Pending state root is invalid.' >&2; exit 1; }
[[ "$CONFIG_ROOT" == /* && "$CONFIG_ROOT" != '/' ]] || { log 'ERROR: Config root is invalid.' >&2; exit 1; }
[[ "$DEPLOY_COMMAND" == /* && -x "$DEPLOY_COMMAND" ]] || { log 'ERROR: Site deploy command is not executable.' >&2; exit 1; }
[[ -e "$PENDING_STATE_ROOT" || -L "$PENDING_STATE_ROOT" ]] || exit 0
[[ -d "$PENDING_STATE_ROOT" && ! -L "$PENDING_STATE_ROOT" ]] || { log 'ERROR: Pending state root must be a directory, not a symlink.' >&2; exit 1; }
[[ "$(stat -c '%u' "$PENDING_STATE_ROOT")" -eq 0 ]] || { log 'ERROR: Pending state root must be owned by root.' >&2; exit 1; }

failed=0
shopt -s nullglob
for pending_dir in "$PENDING_STATE_ROOT"/*; do
  [[ -d "$pending_dir" && ! -L "$pending_dir" ]] || continue
  site="$(basename "$pending_dir")"
  [[ "$site" == .* ]] && continue
  if [[ ! "$site" =~ ^[a-z0-9][a-z0-9.-]{0,62}$ ]]; then
    log "ERROR: Ignoring invalid pending site directory: $site" >&2
    failed=1
    continue
  fi
  if [[ ! -f "$CONFIG_ROOT/$site.env" || -L "$CONFIG_ROOT/$site.env" ]]; then
    log "ERROR: Pending site has no trusted configuration: $site" >&2
    failed=1
    continue
  fi
  if ! "$DEPLOY_COMMAND" --site "$site" --expire-pending; then
    log "ERROR: Failed to reconcile pending site: $site" >&2
    failed=1
  fi
done

exit "$failed"
