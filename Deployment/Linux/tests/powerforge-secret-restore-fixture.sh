#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cli="$repo_root/PowerForge.Web.Cli/bin/Release/net10.0/PowerForge.Web.Cli.dll"
test_root="$(mktemp -d /tmp/powerforge-secret-restore.XXXXXX)"
restore_root="$test_root/restore-target"
plan_root="$test_root/plan"
fake_bin="$test_root/bin"
trap 'rm -rf "$test_root"' EXIT
service_user='nobody'
service_gid="$(id -g "$service_user")"

if [[ ! -f "$cli" ]]; then
  build_command=(
    dotnet build "$repo_root/PowerForge.Web.Cli/PowerForge.Web.Cli.csproj"
    --configuration Release
    --nologo
  )
  if [[ "$(id -u)" -eq 0 && -n "${SUDO_USER:-}" && "$SUDO_USER" != root ]]; then
    sudo -u "$SUDO_USER" -H "${build_command[@]}" >/dev/null
  else
    "${build_command[@]}" >/dev/null
  fi
fi
[[ -f "$cli" ]] || { echo "PowerForge.Web CLI build did not produce $cli" >&2; exit 1; }

mkdir -p "$restore_root" "$plan_root" "$fake_bin"
chmod 0711 "$test_root" "$restore_root"
cat >"$test_root/manifest.json" <<EOF
{
  "schemaVersion": 2,
  "name": "restore-fixture",
  "target": { "host": "fixture.invalid" },
  "paths": [
    { "id": "fixture-secret", "path": "$restore_root/secret.env", "owner": "root", "group": "root", "mode": "600", "kind": "file" },
    { "id": "fixture-service", "path": "$restore_root/service", "owner": "0", "group": "$service_gid", "mode": "750", "kind": "directory" }
  ],
  "secrets": [
    { "id": "fixture-secret", "path": "$restore_root/secret.env", "capture": "encrypted", "restoreMode": "file" },
    { "id": "fixture-service", "path": "$restore_root/service", "capture": "encrypted", "restoreMode": "directory" }
  ],
  "capture": {
    "plainFiles": [],
    "encryptedFiles": [
      { "target": "$restore_root/secret.env", "required": true, "sensitive": true },
      { "target": "$restore_root/service", "required": true, "sensitive": true }
    ]
  }
}
EOF

dotnet "$cli" server restore-secrets-plan --manifest "$test_root/manifest.json" --out "$plan_root" >/dev/null

cat >"$fake_bin/age" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
output=''
input=''
while (($#)); do
  case "$1" in
    -d) shift ;;
    -o) output="$2"; shift 2 ;;
    *) input="$1"; shift ;;
  esac
done
cp "$input" "$output"
EOF
chmod 700 "$fake_bin/age"

cat >"$fake_bin/tar" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
extract=0
for argument in "$@"; do
  [[ "$argument" == -*x* ]] && extract=1
done
/usr/bin/tar "$@"
if ((extract)) && [[ -n "${POWERFORGE_FIXTURE_SWAP_PATH:-}" ]]; then
  rm -rf -- "$POWERFORGE_FIXTURE_SWAP_PATH"
  ln -s -- "$POWERFORGE_FIXTURE_SWAP_TARGET" "$POWERFORGE_FIXTURE_SWAP_PATH"
fi
EOF
chmod 700 "$fake_bin/tar"

archive_path="$test_root/encrypted-secrets.tar.gz.age"
printf 'fixture-secret\n' >"$restore_root/secret.env"
tar -czf "$archive_path" -C / "${restore_root#/}/secret.env"
rm -f "$restore_root/secret.env"

PATH="$fake_bin:$PATH" POWERFORGE_RESTORE_SECRETS_CONFIRM=YES \
  bash "$plan_root/restore-secrets.sh" "$archive_path" >/dev/null

test "$(cat "$restore_root/secret.env")" = 'fixture-secret'
test "$(stat -c '%a' "$restore_root/secret.env")" = '600'

service_root="$restore_root/service"
mkdir -p "$service_root/nested"
printf 'service-secret\n' >"$service_root/service.env"
printf 'nested-secret\n' >"$service_root/nested/key.p8"
chmod 0700 "$service_root" "$service_root/nested"
chmod 0600 "$service_root/service.env" "$service_root/nested/key.p8"
tar -czf "$archive_path" -C / "${service_root#/}"
rm -rf "$service_root"
install -d -o root -g root -m 0700 "$service_root"
printf 'preserve-me\n' >"$service_root/preexisting.env"
chmod 0600 "$service_root/preexisting.env"

PATH="$fake_bin:$PATH" POWERFORGE_RESTORE_SECRETS_CONFIRM=YES \
  bash "$plan_root/restore-secrets.sh" "$archive_path" >/dev/null

test "$(stat -c '%u:%g %a' "$service_root")" = "0:$service_gid 750"
test "$(stat -c '%u:%g %a' "$service_root/service.env")" = "0:$service_gid 640"
test "$(stat -c '%u:%g %a' "$service_root/nested")" = "0:$service_gid 750"
test "$(stat -c '%u:%g %a' "$service_root/nested/key.p8")" = "0:$service_gid 640"
test "$(stat -c '%u:%g %a' "$service_root/preexisting.env")" = '0:0 600'
runuser -u "$service_user" -- test -r "$service_root/service.env"
runuser -u "$service_user" -- test -r "$service_root/nested/key.p8"

rm -rf "$service_root"
mkdir -p "$service_root/nested"
printf 'race-secret\n' >"$service_root/nested/key.p8"
tar -czf "$archive_path" -C / "${service_root#/}"
rm -rf "$service_root"
mkdir -p "$test_root/outside-race"
printf 'outside-must-not-change\n' >"$test_root/outside-race/key.p8"
chmod 0600 "$test_root/outside-race/key.p8"

if PATH="$fake_bin:$PATH" \
  POWERFORGE_FIXTURE_SWAP_PATH="$service_root/nested" \
  POWERFORGE_FIXTURE_SWAP_TARGET="$test_root/outside-race" \
  POWERFORGE_RESTORE_SECRETS_CONFIRM=YES \
  bash "$plan_root/restore-secrets.sh" "$archive_path" >"$test_root/rejected-race.log" 2>&1; then
  echo 'Restore unexpectedly followed a post-validation intermediate symlink.' >&2
  exit 1
fi
grep -Eq 'Not a directory|Too many levels of symbolic links|Restored .+ changed type' "$test_root/rejected-race.log"
test "$(cat "$test_root/outside-race/key.p8")" = 'outside-must-not-change'
test "$(stat -c '%u:%g %a' "$test_root/outside-race/key.p8")" = '0:0 600'

mkdir -p "$test_root/outside"
printf 'must-not-restore\n' >"$test_root/outside/blocked.env"
tar -czf "$archive_path" -C / "${test_root#/}/outside/blocked.env"
rm -f "$test_root/outside/blocked.env"

if PATH="$fake_bin:$PATH" POWERFORGE_RESTORE_SECRETS_CONFIRM=YES \
  bash "$plan_root/restore-secrets.sh" "$archive_path" >"$test_root/rejected.log" 2>&1; then
  echo 'Restore unexpectedly accepted an archive path outside the manifest allowlist.' >&2
  exit 1
fi
grep -F 'Archive path is outside the manifest allowlist' "$test_root/rejected.log" >/dev/null
test ! -e "$test_root/outside/blocked.env"

python3 - "$archive_path" "${restore_root#/}/secret.env" <<'PY'
import io
import sys
import tarfile

archive_path, member_name = sys.argv[1:3]
with tarfile.open(archive_path, "w:gz") as archive:
    member = tarfile.TarInfo(member_name)
    member.type = tarfile.SYMTYPE
    member.linkname = "../../outside/blocked.env"
    archive.addfile(member, io.BytesIO())
PY

if PATH="$fake_bin:$PATH" POWERFORGE_RESTORE_SECRETS_CONFIRM=YES \
  bash "$plan_root/restore-secrets.sh" "$archive_path" >"$test_root/rejected-symlink.log" 2>&1; then
  echo 'Restore unexpectedly accepted an escaping symlink.' >&2
  exit 1
fi
grep -F 'Symlink target is outside the manifest allowlist' "$test_root/rejected-symlink.log" >/dev/null

python3 - "$archive_path" "${restore_root#/}/secret.env" <<'PY'
import io
import sys
import tarfile

archive_path, member_name = sys.argv[1:3]
payload = b"must-not-restore\n"
with tarfile.open(archive_path, "w:gz") as archive:
    member = tarfile.TarInfo(member_name)
    member.mode = 0o4755
    member.size = len(payload)
    archive.addfile(member, io.BytesIO(payload))
PY

rm -f "$restore_root/secret.env"
if PATH="$fake_bin:$PATH" POWERFORGE_RESTORE_SECRETS_CONFIRM=YES \
  bash "$plan_root/restore-secrets.sh" "$archive_path" >"$test_root/rejected-mode.log" 2>&1; then
  echo 'Restore unexpectedly accepted special permission bits.' >&2
  exit 1
fi
grep -F 'Archive member has unsafe special permission bits' "$test_root/rejected-mode.log" >/dev/null
test ! -e "$restore_root/secret.env"

echo 'PowerForge secret restore fixture passed.'
