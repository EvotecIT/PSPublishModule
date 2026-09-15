#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

readonly max_payload_bytes=1073741824
readonly max_metadata_bytes=1048576
readonly payload_read_timeout_seconds=300
declare -a allowed_services=()

fail() {
  printf '[powerforge-service-deploy-ssh] ERROR: %s\n' "$*" >&2
  exit 1
}

assert_uncompressed_tar() {
  local path="$1" mime_type
  command -v file >/dev/null 2>&1 || fail 'The file utility is required to validate deployment payloads.'
  mime_type="$(file --brief --mime-type -- "$path")"
  [[ "$mime_type" == application/x-tar ]] || fail 'Deployment payload must be an uncompressed tar archive.'
}

while (($# > 0)); do
  case "$1" in
    --allow-service)
      [[ "${2:-}" =~ ^[a-z0-9][a-z0-9.-]{0,62}$ ]] || fail 'Invalid allowed service identifier.'
      allowed_services+=("$2")
      shift 2
      ;;
    *)
      fail 'Only repeated --allow-service arguments are supported.'
      ;;
  esac
done
(( ${#allowed_services[@]} > 0 )) || fail 'At least one allowed service is required.'

original_command="${SSH_ORIGINAL_COMMAND:-}"
[[ "$original_command" =~ ^powerforge-service-deploy-v1\ --service\ ([a-z0-9][a-z0-9.-]{0,62})$ ]] ||
  fail 'Unsupported SSH deployment command.'
service_id="${BASH_REMATCH[1]}"
allowed=0
for candidate in "${allowed_services[@]}"; do
  if [[ "$candidate" == "$service_id" ]]; then
    allowed=1
    break
  fi
done
(( allowed == 1 )) || fail 'The requested service is not authorized for this key.'

dispatcher_lock_target="$(realpath -e -- "${BASH_SOURCE[0]}")"
lock_path="/tmp/powerforge-service-${service_id}.lock"
[[ -f "$dispatcher_lock_target" ]] || fail 'Unable to resolve the installed deployment dispatcher.'
exec 8<"$dispatcher_lock_target"
flock -w 900 8 || fail 'Timed out waiting for the deployment upload lock.'
exec 9>"$lock_path"
flock -w 900 9 || fail 'Timed out waiting for the service deployment lock.'

stage_root="$(mktemp -d "/tmp/powerforge-service-${service_id}-upload.XXXXXXXX")"
payload_path="${stage_root}/payload.tar"
content_root="${stage_root}/content"
handoff_root="/tmp/powerforge-service-${service_id}"
# shellcheck disable=SC2317 # Invoked indirectly by the EXIT trap.
cleanup() {
  rm -rf -- "$stage_root"
}
trap cleanup EXIT

if ! timeout --foreground "${payload_read_timeout_seconds}s" \
  head -c "$((max_payload_bytes + 1))" >"$payload_path"; then
  fail "Deployment payload was not received within ${payload_read_timeout_seconds} seconds."
fi
payload_size="$(stat -c '%s' -- "$payload_path")"
(( payload_size > 0 && payload_size <= max_payload_bytes )) || fail 'Deployment payload is empty or exceeds 1 GiB.'
assert_uncompressed_tar "$payload_path"

archive_entries="$(LC_ALL=C tar --list --quoting-style=escape --file "$payload_path" | head --bytes=1024)" ||
  fail 'Unable to inspect the bounded deployment payload member list.'
sorted_entries="$(printf '%s\n' "$archive_entries" | LC_ALL=C sort)"
[[ "$sorted_entries" == $'artifact.tar\ndeployment.json' ]] ||
  fail 'Deployment payload must contain exactly artifact.tar and deployment.json.'

install -d -m 0700 "$content_root"
tar --extract --file "$payload_path" --directory "$content_root" \
  --no-same-owner --no-same-permissions -- artifact.tar deployment.json
for name in artifact.tar deployment.json; do
  path="${content_root}/${name}"
  [[ -f "$path" && ! -L "$path" && "$(realpath -e -- "$path")" == "$path" && -s "$path" ]] ||
    fail "Deployment payload file is invalid: $name"
  logical_size="$(stat -c '%s' -- "$path")"
  allocated_blocks="$(stat -c '%b' -- "$path")"
  allocated_bytes=$((allocated_blocks * 512))
  size_limit="$max_payload_bytes"
  if [[ "$name" == deployment.json ]]; then
    size_limit="$max_metadata_bytes"
  fi
  (( logical_size <= size_limit )) || fail "Deployment payload file exceeds its size limit: $name"
  (( allocated_bytes >= logical_size )) || fail "Sparse deployment payload files are not supported: $name"
  chmod 0600 "$path"
done

rm -rf -- "$handoff_root"
mv -- "$content_root" "$handoff_root"
promotion_status=0
sudo /usr/local/sbin/powerforge-service-deploy --service "$service_id" || promotion_status=$?
rm -rf -- "$handoff_root"
exit "$promotion_status"
