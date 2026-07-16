#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cli="$repo_root/PowerForge.Web.Cli/bin/Release/net10.0/PowerForge.Web.Cli.dll"
test_root="$(mktemp -d /tmp/powerforge-secret-restore.XXXXXX)"
restore_root="$test_root/restore-target"
plan_root="$test_root/plan"
fake_bin="$test_root/bin"
trap 'rm -rf "$test_root"' EXIT

mkdir -p "$restore_root" "$plan_root" "$fake_bin"
cat >"$test_root/manifest.json" <<EOF
{
  "schemaVersion": 1,
  "name": "restore-fixture",
  "target": { "host": "fixture.invalid" },
  "paths": [
    { "id": "fixture-secret", "path": "$restore_root/secret.env", "owner": "root", "group": "root", "mode": "600", "kind": "file" }
  ],
  "secrets": [
    { "id": "fixture-secret", "path": "$restore_root/secret.env", "capture": "encrypted", "restoreMode": "file" }
  ],
  "capture": {
    "plainFiles": [],
    "encryptedFiles": [
      { "target": "$restore_root/secret.env", "required": true, "sensitive": true }
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

archive_path="$test_root/encrypted-secrets.tar.gz.age"
printf 'fixture-secret\n' >"$restore_root/secret.env"
tar -czf "$archive_path" -C / "${restore_root#/}/secret.env"
rm -f "$restore_root/secret.env"

PATH="$fake_bin:$PATH" POWERFORGE_RESTORE_SECRETS_CONFIRM=YES \
  bash "$plan_root/restore-secrets.sh" "$archive_path" >/dev/null

test "$(cat "$restore_root/secret.env")" = 'fixture-secret'
test "$(stat -c '%a' "$restore_root/secret.env")" = '600'

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
