#!/usr/bin/env bash
set -Eeuo pipefail

[[ ${EUID} -eq 0 ]] || { echo 'Run this fixture as root.' >&2; exit 1; }
if [[ "${1:-}" != '--private-mount-namespace' ]]; then
  parent_namespace="$(readlink /proc/self/ns/mnt)"
  exec unshare --mount --propagation private --fork env PF_PARENT_MOUNT_NS="$parent_namespace" \
    bash "$0" --private-mount-namespace "$@"
fi
shift
[[ -n "${PF_PARENT_MOUNT_NS:-}" && "$(readlink /proc/self/ns/mnt)" != "$PF_PARENT_MOUNT_NS" ]] ||
  { echo 'Fault injection requires a new private mount namespace.' >&2; exit 1; }

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
capture_script="${1:-$script_dir/../powerforge-server-data-capture.sh}"
fixture_root="$(mktemp -d /var/tmp/powerforge-validation-fixture.XXXXXXXX)"
validation_root="$fixture_root/validation"
cleanup() {
  if [[ -f "$fixture_root/attacker.pid" ]]; then
    read -r pid started <"$fixture_root/attacker.pid"
    if [[ "$pid" =~ ^[0-9]+$ && -f "/proc/$pid/stat" && "$(awk '{print $22}' "/proc/$pid/stat")" == "$started" ]] &&
       tr '\0' '\n' <"/proc/$pid/cmdline" | grep -Fx "$fixture_root/retained-writer.py" >/dev/null; then
      kill "$pid" 2>/dev/null || true
    fi
  fi
  mountpoint -q "$validation_root" && umount "$validation_root"
  [[ "$fixture_root" == /var/tmp/powerforge-validation-fixture.* && -d "$fixture_root" && ! -L "$fixture_root" ]] && rm -rf -- "$fixture_root"
}
trap cleanup EXIT
chmod 0711 "$fixture_root"
install -d -m 0700 -o nobody "$fixture_root/data"
install -d -m 0700 "$fixture_root/artifacts" "$fixture_root/restore" "$fixture_root/tools/real" "$validation_root"
mount -t tmpfs -o size=10m,mode=0700 tmpfs "$validation_root"
for id in first second; do
  runuser -u nobody -- sqlite3 "$fixture_root/data/$id.db" \
    'CREATE TABLE evidence (id INTEGER PRIMARY KEY, payload BLOB NOT NULL); INSERT INTO evidence VALUES (7, zeroblob(6291456));'
done
printf 'private configuration\n' >"$fixture_root/config.env"
printf 'immutable artifact\n' >"$fixture_root/artifacts/release.txt"
age-keygen -o "$fixture_root/identity" >/dev/null 2>&1
recipient="$(age-keygen -y "$fixture_root/identity")"
jq -n --arg root "$fixture_root" --arg recipient "$recipient" \
  '{schemaVersion:2,name:"validation-fixture",target:{host:"localhost"},durableBackup:{exportRoot:($root+"/export"),exportGroup:"root",recipient:$recipient,stagingRetentionHours:48,sqliteValidationRoot:($root+"/validation"),sqliteValidationReserveBytes:1048576,databases:[{id:"first",provider:"sqlite",database:($root+"/data/first.db"),runAs:"nobody",required:true},{id:"second",provider:"sqlite",database:($root+"/data/second.db"),runAs:"nobody",required:true}],encryptedFiles:[{target:($root+"/config.env"),required:true,sensitive:true}],artifactStores:[{id:"releases",path:($root+"/artifacts"),required:true}]}}' >"$fixture_root/base.json"

# Faults replace only native I/O tools in this disposable mount namespace.
# Real copies still run; the wrappers introduce corruption or an I/O failure.
# Some distributions dispatch a combined coreutils binary by argv[0].
# Preserve each real tool's basename when relocating it behind a fault wrapper.
cp -- /usr/bin/install "$fixture_root/tools/real/install"
cp -- /usr/bin/dd "$fixture_root/tools/real/dd"
cp -- /usr/bin/rm "$fixture_root/tools/real/rm"
export PF_FIXTURE_ROOT="$fixture_root" PF_FAULT_MODE='none'
cat >"$fixture_root/tools/install" <<'INSTALL'
#!/usr/bin/env bash
set -Eeuo pipefail
destination="${*: -1}"
if [[ "$PF_FAULT_MODE" == 'retained' && "$*" == *'-o root'* &&
      "$destination" == /var/tmp/powerforge-server-data-capture.*/sqlite/first && -f "$destination/backup.sqlite" ]]; then
  runuser -u nobody -- python3 "$PF_FIXTURE_ROOT/retained-writer.py" "$destination/backup.sqlite" "$PF_FIXTURE_ROOT" >"$PF_FIXTURE_ROOT/attacker.log" 2>&1 &
  pid=$!
  printf '%s %s\n' "$pid" "$(awk '{print $22}' "/proc/$pid/stat")" >"$PF_FIXTURE_ROOT/attacker.pid"
  for ((i=0;i<200;i++)); do [[ -f "$PF_FIXTURE_ROOT/data/attacker-ready" ]] && break; sleep 0.01; done
  [[ -f "$PF_FIXTURE_ROOT/data/attacker-ready" ]]
fi
if [[ "$destination" == /var/tmp/powerforge-server-data-capture.*/databases/first.sqlite ]]; then
  if [[ "$PF_FAULT_MODE" == 'copy-failure' ]]; then exit 42; fi
  if [[ "$PF_FAULT_MODE" == 'retained' ]]; then
    touch "$PF_FIXTURE_ROOT/attack-now"
    for ((i=0;i<200;i++)); do [[ -f "$PF_FIXTURE_ROOT/data/attacker-done" ]] && break; sleep 0.01; done
    [[ -f "$PF_FIXTURE_ROOT/data/attacker-done" ]]
  fi
fi
"$PF_FIXTURE_ROOT/tools/real/install" "$@"
if [[ "$PF_FAULT_MODE" == 'truncated-transfer' && "$destination" == /var/tmp/powerforge-server-data-capture.*/databases/first.sqlite ]]; then
  truncate -s 4096 "$destination"
fi
if [[ "$PF_FAULT_MODE" == 'corrupt-transfer' && "$destination" == /var/tmp/powerforge-server-data-capture.*/databases/first.sqlite ]]; then
  printf 'corrupt transfer' | "$PF_FIXTURE_ROOT/tools/real/dd" of="$destination" conv=notrunc status=none
fi
INSTALL
cat >"$fixture_root/tools/dd" <<'DD'
#!/usr/bin/env bash
set -Eeuo pipefail
"$PF_FIXTURE_ROOT/tools/real/dd" "$@"
destination=''
for argument in "$@"; do [[ "$argument" == of=* ]] && destination="${argument#of=}"; done
if [[ "$PF_FAULT_MODE" == 'corrupt-seal' && "$destination" == "$PF_FIXTURE_ROOT"/validation/.sqlite-validation.*/first.sqlite ]]; then
  printf 'not a SQLite db!' | "$PF_FIXTURE_ROOT/tools/real/dd" of="$destination" conv=notrunc status=none
fi
DD
cat >"$fixture_root/tools/rm" <<'RM'
#!/usr/bin/env bash
set -Eeuo pipefail
destination="${*: -1}"
if [[ "$PF_FAULT_MODE" == 'cleanup-failure' && -d "$destination" &&
      "$destination" == "$PF_FIXTURE_ROOT"/validation/.sqlite-validation.* ]]; then
  exit 42
fi
"$PF_FIXTURE_ROOT/tools/real/rm" "$@"
RM
cat >"$fixture_root/retained-writer.py" <<'PY'
import os,pathlib,sys,time
source,root=sys.argv[1],pathlib.Path(sys.argv[2])
with open(source,'r+b',buffering=0) as retained:
    (root/'data/attacker-ready').touch()
    deadline=time.monotonic()+15
    while not (root/'attack-now').exists():
        if time.monotonic()>deadline: raise TimeoutError('retained-descriptor fixture did not reach transfer')
        time.sleep(.01)
    retained.write(b'retained damage!')
    os.fsync(retained.fileno())
    (root/'data/attacker-done').touch()
PY
chmod 0755 "$fixture_root/tools/install" "$fixture_root/tools/dd" "$fixture_root/tools/rm"
chmod 0644 "$fixture_root/retained-writer.py"
mount --bind "$fixture_root/tools/install" /usr/bin/install
mount --bind "$fixture_root/tools/dd" /usr/bin/dd
mount --bind "$fixture_root/tools/rm" /usr/bin/rm

configuration() {
  jq --arg exportRoot "$fixture_root/export-$1" '.durableBackup.exportRoot=$exportRoot' "$fixture_root/base.json" >"$fixture_root/$1.json"
}
assert_no_plaintext() {
  [[ -z "$(find "$validation_root" -mindepth 1 ! -name .validation.lock -print -quit)" ]]
}
assert_restored() {
  local snapshot
  snapshot="$(find "$fixture_root/export-$1/snapshots" -mindepth 1 -maxdepth 1 -type d -name '????????T??????Z' -print -quit)"
  [[ -n "$snapshot" && -f "$snapshot/READY" ]]
  (cd "$snapshot" && sha256sum -c SHA256SUMS >/dev/null)
  age -d -i "$fixture_root/identity" -o "$fixture_root/recovery-$1.tar.gz" "$snapshot/recovery.tar.gz.age"
  tar -xzf "$fixture_root/recovery-$1.tar.gz" -C "$fixture_root/restore" databases/first.sqlite databases/second.sqlite
  for id in first second; do
    [[ "$(sqlite3 -readonly "$fixture_root/restore/databases/$id.sqlite" 'PRAGMA integrity_check; SELECT id || ":" || length(payload) FROM evidence;')" == $'ok\n7:6291456' ]]
  done
  assert_no_plaintext
  printf 'PASS %s\n' "$1"
}
expect_failure() {
  if bash "$capture_script" "$fixture_root/$1.json" >"$fixture_root/$1.log" 2>&1; then
    echo "Unexpected successful capture: $1" >&2; exit 1
  fi
  grep -F "$2" "$fixture_root/$1.log" >/dev/null
  [[ -z "$(find "$fixture_root/export-$1/snapshots" -name READY -print -quit)" ]]
  assert_no_plaintext
  printf 'PASS %s\n' "$1"
}

configuration fast_workspace_roundtrip_bounded
bash "$capture_script" "$fixture_root/fast_workspace_roundtrip_bounded.json"
assert_restored fast_workspace_roundtrip_bounded

for spec in 'corrupt-seal:reject_corrupt_seal:SQLite backup integrity check failed' \
            'truncated-transfer:reject_truncated_transfer:SQLite validated transfer verification failed' \
            'corrupt-transfer:reject_corrupt_transfer:SQLite validated transfer verification failed' \
            'copy-failure:reject_transfer_failure:SQLite validated transfer failed'; do
  IFS=: read -r PF_FAULT_MODE name message <<<"$spec"
  configuration "$name"
  expect_failure "$name" "$message"
done
PF_FAULT_MODE='cleanup-failure'
configuration reject_workspace_cleanup_failure
if bash "$capture_script" "$fixture_root/reject_workspace_cleanup_failure.json" >"$fixture_root/reject_workspace_cleanup_failure.log" 2>&1; then
  echo 'Unexpected successful capture: reject_workspace_cleanup_failure' >&2; exit 1
fi
grep -F 'unable to clean SQLite validation plaintext' "$fixture_root/reject_workspace_cleanup_failure.log" >/dev/null
[[ -z "$(find "$fixture_root/export-reject_workspace_cleanup_failure/snapshots" -name READY -print -quit)" ]]
mapfile -d '' retained_work < <(find "$validation_root" -mindepth 1 -maxdepth 1 -type d -name '.sqlite-validation.*' -print0)
[[ ${#retained_work[@]} -eq 1 && ! -L "${retained_work[0]}" &&
   "$(readlink -f "${retained_work[0]%/*}")" == "$validation_root" ]]
# Restore the real filesystem boundary before removing this fixture's residue.
PF_FAULT_MODE='none'
"$fixture_root/tools/real/rm" -rf -- "${retained_work[0]}"
assert_no_plaintext
printf 'PASS reject_workspace_cleanup_failure\n'
PF_FAULT_MODE='none'
configuration reject_insufficient_reserve
jq '.durableBackup.sqliteValidationReserveBytes=10485760' "$fixture_root/reject_insufficient_reserve.json" >"$fixture_root/changed.json"
mv "$fixture_root/changed.json" "$fixture_root/reject_insufficient_reserve.json"
expect_failure reject_insufficient_reserve 'insufficient SQLite validation space including reserve'

configuration reject_export_overlap
jq --arg root "$validation_root" '.durableBackup.artifactStores[0].path=$root' "$fixture_root/reject_export_overlap.json" >"$fixture_root/changed.json"
mv "$fixture_root/changed.json" "$fixture_root/reject_export_overlap.json"
expect_failure reject_export_overlap 'SQLite validation root overlaps an artifact store'

configuration reject_untrusted_root
chown nobody "$validation_root"
expect_failure reject_untrusted_root 'SQLite validation ancestry must be owned by root'
chown root "$validation_root"

configuration reject_symlink_root
ln -s "$validation_root" "$fixture_root/validation-link"
jq --arg root "$fixture_root/validation-link" '.durableBackup.sqliteValidationRoot=$root' "$fixture_root/reject_symlink_root.json" >"$fixture_root/changed.json"
mv "$fixture_root/changed.json" "$fixture_root/reject_symlink_root.json"
expect_failure reject_symlink_root 'SQLite validation root must be an existing canonical non-link directory'

configuration retained_descriptor_cannot_change_sealed_backup
PF_FAULT_MODE='retained'
bash "$capture_script" "$fixture_root/retained_descriptor_cannot_change_sealed_backup.json"
[[ -f "$fixture_root/data/attacker-done" ]]
assert_restored retained_descriptor_cannot_change_sealed_backup
