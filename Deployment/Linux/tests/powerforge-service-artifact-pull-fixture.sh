#!/usr/bin/env bash
set -Eeuo pipefail

[[ $(id -u) -eq 0 ]] || { echo 'Run this isolated fixture as root so it can model root-owned host configuration.' >&2; exit 1; }
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source_script="$script_dir/../powerforge-service-artifact-pull.sh"
task_root=$(mktemp -d /var/lib/powerforge-service-artifact-test.XXXXXXXX)
service="fixture${task_root##*.}"
service=${service,,}
[[ $task_root == /var/lib/powerforge-service-artifact-test.* && $service =~ ^fixture[a-z0-9]+$ ]] || exit 1
trap 'rm -rf -- "$task_root" "/tmp/powerforge-service-${service}"' EXIT
repo='ExampleOrg/Service'
sha='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
mkdir -p "$task_root/bin" "$task_root/etc/powerforge/service-pull" "$task_root/etc/powerforge/services" \
  "$task_root/package" "$task_root/service/releases" "$task_root/work"
chmod 0755 "$task_root" "$task_root/etc" "$task_root/etc/powerforge" \
  "$task_root/service" "$task_root/service/releases" \
  "$task_root/bin" "$task_root/package"
chmod 0750 "$task_root/etc/powerforge/service-pull" "$task_root/etc/powerforge/services"
setfacl -m u:nobody:--x "$task_root/etc/powerforge/service-pull" "$task_root/etc/powerforge/services"
chown nobody:nogroup "$task_root/work"
chmod 0700 "$task_root/work"
printf 'published service\n' >"$task_root/package/artifact.tar"
digest=$(sha256sum "$task_root/package/artifact.tar")
digest=${digest%% *}
jq -n --arg repo "$repo" --arg sha "$sha" --arg digest "$digest" \
  '{schemaVersion:1,sourceRepository:$repo,sourceSha:$sha,workflowRunId:"12345",workflowRunAttempt:"2",artifactSha256:$digest}' \
  >"$task_root/package/package.json"
printf 'fixture-token\n' >"$task_root/etc/powerforge/service-pull/${service}.token"
printf 'SOURCE_REPOSITORY=%s\nSOURCE_BRANCH=main\nSOURCE_WORKFLOW=package-service.yml\nARTIFACT_NAME=powerforge-service-example\nWORK_ROOT=%s/work\n' \
  "$repo" "$task_root" >"$task_root/etc/powerforge/service-pull/${service}.env"
printf 'SERVICE_ROOT=%s/service\n' "$task_root" >"$task_root/etc/powerforge/services/${service}.env"
chown root:nogroup "$task_root/etc/powerforge/service-pull/${service}.token" \
  "$task_root/etc/powerforge/service-pull/${service}.env" "$task_root/etc/powerforge/services/${service}.env"
chmod 0640 "$task_root/etc/powerforge/service-pull/${service}.token" \
  "$task_root/etc/powerforge/service-pull/${service}.env" "$task_root/etc/powerforge/services/${service}.env"

# Keep the production paths fixed; this copy only redirects the root-owned fixture config and tool shims.
sed -e "s#^PATH=/usr/local/sbin:#PATH=$task_root/bin:/usr/local/sbin:#" \
  -e "s#/etc/powerforge#$task_root/etc/powerforge#g" "$source_script" >"$task_root/runner.sh"
chmod 0755 "$task_root/runner.sh"
cat >"$task_root/bin/gh" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
[[ ${GH_TOKEN:-} == fixture-token ]] || exit 1
if [[ $1 == api ]]; then
  endpoint=${*: -1}
  case "$endpoint" in
    repos/ExampleOrg/Service/branches/main)
      counter_file="$POWERFORGE_FIXTURE_ROOT/work/branch-reads"
      counter=0
      [[ ! -f $counter_file ]] || counter=$(<"$counter_file")
      counter=$((counter + 1))
      printf '%s\n' "$counter" >"$counter_file"
      if [[ ${POWERFORGE_FIXTURE_MODE:-} == advance && $counter -gt 1 ]]; then
        printf '{"commit":{"sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}}\n'
      else
        printf '{"commit":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}\n'
      fi
      ;;
    repos/ExampleOrg/Service/actions/workflows/package-service.yml/runs*)
      if [[ ${POWERFORGE_FIXTURE_MODE:-} == missing ]]; then
        printf '{"workflow_runs":[]}\n'
      else
        printf '{"workflow_runs":[{"id":12345,"head_sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","head_branch":"main","status":"completed","conclusion":"success","event":"push","run_number":8,"run_attempt":2}]}\n'
      fi
      ;;
    repos/ExampleOrg/Service/actions/runs/12345)
      printf '{"id":12345,"head_sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","head_branch":"main","status":"completed","conclusion":"success","event":"push","run_attempt":2}\n'
      ;;
    *) exit 1 ;;
  esac
elif [[ $1 == run && $2 == download ]]; then
  while (($#)); do
    if [[ $1 == --dir ]]; then destination=$2; break; fi
    shift
  done
  [[ -n ${destination:-} ]] || exit 1
  cp "$POWERFORGE_FIXTURE_ROOT/package/artifact.tar" "$destination/artifact.tar"
  if [[ ${POWERFORGE_FIXTURE_MODE:-} == tamper ]]; then
    jq '.artifactSha256="ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"' \
      "$POWERFORGE_FIXTURE_ROOT/package/package.json" >"$destination/package.json"
  else
    cp "$POWERFORGE_FIXTURE_ROOT/package/package.json" "$destination/package.json"
  fi
else
  exit 1
fi
SH
cat >"$task_root/bin/sudo" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
[[ "$*" == "-- /usr/local/sbin/powerforge-service-deploy --service $POWERFORGE_FIXTURE_SERVICE" ]]
stage="/tmp/powerforge-service-${POWERFORGE_FIXTURE_SERVICE}"
[[ -s $stage/artifact.tar && -s $stage/deployment.json ]]
jq -e '.sourceSha == "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" and .workflowRunId == "12345" and .workflowRunAttempt == "2"' \
  "$stage/deployment.json" >/dev/null
cp "$stage/deployment.json" "$POWERFORGE_FIXTURE_ROOT/work/deployed.json"
printf 'promoted\n' >>"$POWERFORGE_FIXTURE_ROOT/work/sudo.log"
SH
chmod 0755 "$task_root/bin/gh" "$task_root/bin/sudo"

run_fixture() {
  local mode=$1
  rm -f -- "$task_root/work/branch-reads"
  runuser -u nobody -- env POWERFORGE_FIXTURE_ROOT="$task_root" POWERFORGE_FIXTURE_SERVICE="$service" \
    POWERFORGE_FIXTURE_MODE="$mode" bash "$task_root/runner.sh" "$service"
}
if run_fixture missing >"$task_root/missing.log" 2>&1; then
  echo 'Accepted a missing successful run.' >&2; exit 1
fi
grep -Fq 'no successful service package run' "$task_root/missing.log"
if run_fixture tamper >"$task_root/tamper.log" 2>&1; then
  echo 'Accepted a package with the wrong SHA-256.' >&2; exit 1
fi
grep -Fq 'service package does not match' "$task_root/tamper.log"
if run_fixture advance >"$task_root/advance.log" 2>&1; then
  echo 'Promoted after the source branch advanced.' >&2; exit 1
fi
grep -Fq 'source branch advanced before promotion' "$task_root/advance.log"
chmod 0660 "$task_root/etc/powerforge/service-pull/${service}.token"
if run_fixture success >"$task_root/token-mode.log" 2>&1; then
  echo 'Accepted a group-writable GitHub token file.' >&2; exit 1
fi
grep -Fq 'configuration file must be root-owned' "$task_root/token-mode.log"
chmod 0640 "$task_root/etc/powerforge/service-pull/${service}.token"
mkdir "/tmp/powerforge-service-${service}"
if run_fixture success >"$task_root/stale-stage.log" 2>&1; then
  echo 'Accepted an occupied staging path.' >&2; exit 1
fi
grep -Fq 'fixed service staging path already exists' "$task_root/stale-stage.log"
[[ -z $(find "$task_root/work" -maxdepth 1 -name '.download.*' -print -quit) ]]
rmdir "/tmp/powerforge-service-${service}"
[[ ! -e $task_root/work/sudo.log ]]
[[ ! -e /tmp/powerforge-service-${service} ]]
run_fixture success
[[ $(wc -l <"$task_root/work/sudo.log") -eq 1 ]]
mkdir -p "$task_root/service/releases/verified/_powerforge"
cp "$task_root/work/deployed.json" "$task_root/service/releases/verified/_powerforge/deployment.json"
chmod 0644 "$task_root/service/releases/verified/_powerforge/deployment.json"
ln -s releases/verified "$task_root/service/current"
[[ -L $task_root/service/current ]]
[[ $(realpath -e "$task_root/service/current") == "$task_root/service/releases/"* ]]
jq -e --arg sha "$sha" '.sourceSha == $sha and .workflowRunId == "12345" and .workflowRunAttempt == "2"' \
  "$task_root/service/current/_powerforge/deployment.json" >/dev/null
run_fixture success
[[ $(wc -l <"$task_root/work/sudo.log") -eq 1 ]]
echo 'Host-initiated Linux service artifact pull fixture passed.'
