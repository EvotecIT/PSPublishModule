#!/usr/bin/env bash
set -Eeuo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
dispatcher="$(cd -- "${script_dir}/.." && pwd)/powerforge-service-deploy-ssh.sh"
task_root="$(mktemp -d)"
service_id='licensing-intake-syncse'
lock_path="/tmp/powerforge-service-${service_id}.lock"
trap 'rm -rf -- "$task_root" "/tmp/powerforge-service-${service_id}"; rm -f -- "$lock_path"' EXIT

mkdir -p "$task_root/bin" "$task_root/input" "$task_root/extra"
printf 'artifact\n' >"$task_root/input/artifact.tar"
printf '{"schemaVersion":1}\n' >"$task_root/input/deployment.json"
tar -C "$task_root/input" -cf "$task_root/payload.tar" artifact.tar deployment.json

cat >"$task_root/bin/sudo" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
printf '%s\n' "$*" >"$POWERFORGE_SUDO_LOG"
[[ "$1" == '/usr/local/sbin/powerforge-service-deploy' ]]
[[ "$2" == '--service' ]]
[[ -n "${3:-}" && -z "${4:-}" ]]
[[ -s "/tmp/powerforge-service-${3}/artifact.tar" ]]
[[ -s "/tmp/powerforge-service-${3}/deployment.json" ]]
SH
chmod 0755 "$task_root/bin/sudo"
export POWERFORGE_SUDO_LOG="$task_root/sudo.log"
export PATH="$task_root/bin:$PATH"

export SSH_ORIGINAL_COMMAND="powerforge-service-deploy-v1 --service ${service_id}"
bash "$dispatcher" --allow-service "$service_id" <"$task_root/payload.tar"
grep -Fxq "/usr/local/sbin/powerforge-service-deploy --service ${service_id}" "$POWERFORGE_SUDO_LOG"
[[ ! -e "/tmp/powerforge-service-${service_id}" ]]

export SSH_ORIGINAL_COMMAND='sh -c id'
if bash "$dispatcher" --allow-service "$service_id" <"$task_root/payload.tar" 2>/dev/null; then
  echo 'The forced deployment command accepted an arbitrary shell.' >&2
  exit 1
fi

export SSH_ORIGINAL_COMMAND='powerforge-service-deploy-v1 --service licensing-control'
if bash "$dispatcher" --allow-service "$service_id" <"$task_root/payload.tar" 2>/dev/null; then
  echo 'The forced deployment command accepted a service outside its allowlist.' >&2
  exit 1
fi

cp "$task_root/input/artifact.tar" "$task_root/extra/artifact.tar"
cp "$task_root/input/deployment.json" "$task_root/extra/deployment.json"
printf 'unexpected\n' >"$task_root/extra/extra.txt"
tar -C "$task_root/extra" -cf "$task_root/extra.tar" artifact.tar deployment.json extra.txt
export SSH_ORIGINAL_COMMAND="powerforge-service-deploy-v1 --service ${service_id}"
if bash "$dispatcher" --allow-service "$service_id" <"$task_root/extra.tar" 2>/dev/null; then
  echo 'The forced deployment command accepted an unexpected archive entry.' >&2
  exit 1
fi

mkdir -p "$task_root/sparse"
truncate -s 536870912 "$task_root/sparse/artifact.tar"
cp "$task_root/input/deployment.json" "$task_root/sparse/deployment.json"
tar --sparse -C "$task_root/sparse" -cf "$task_root/sparse.tar" artifact.tar deployment.json
if bash "$dispatcher" --allow-service "$service_id" <"$task_root/sparse.tar" 2>/dev/null; then
  echo 'The forced deployment command accepted an oversized sparse member.' >&2
  exit 1
fi

gzip -c "$task_root/payload.tar" >"$task_root/compressed.tar.gz"
if bash "$dispatcher" --allow-service "$service_id" <"$task_root/compressed.tar.gz" 2>/dev/null; then
  echo 'The forced deployment command accepted a compressed outer archive.' >&2
  exit 1
fi

echo 'Restricted Linux service deployment transport fixture passed.'
