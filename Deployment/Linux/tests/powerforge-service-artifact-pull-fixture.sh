#!/usr/bin/env bash
set -Eeuo pipefail

[[ $(id -u) -eq 0 ]] || { echo 'Run this isolated fixture as root so it can model root-owned host configuration.' >&2; exit 1; }
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source_script="$script_dir/../powerforge-service-artifact-pull.sh"
task_root=$(mktemp -d /var/lib/powerforge-service-artifact-test.XXXXXXXX)
service="fixture${task_root##*.}"
service=${service,,}
[[ $task_root == /var/lib/powerforge-service-artifact-test.* && $service =~ ^fixture[a-z0-9]+$ ]] || exit 1
deploy_user="pfs${task_root##*.}"
deploy_user=${deploy_user,,}
[[ $deploy_user =~ ^pfs[a-z0-9]{8}$ ]] || exit 1
if getent passwd "$deploy_user" >/dev/null || getent group "$deploy_user" >/dev/null; then
  echo 'Temporary fixture account already exists.' >&2; exit 1
fi
trap 'userdel "$deploy_user" >/dev/null 2>&1 || true; groupdel "$deploy_user" >/dev/null 2>&1 || true; rm -rf -- "$task_root" "/tmp/powerforge-service-${service}"' EXIT
groupadd "$deploy_user"
useradd -M -N -g "$deploy_user" -s /usr/sbin/nologin "$deploy_user"
repo='ExampleOrg/Service'
sha='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
mkdir -p "$task_root/bin" "$task_root/etc/powerforge/service-pull" "$task_root/etc/powerforge/services" \
  "$task_root/package" "$task_root/service/releases" "$task_root/work"
chmod 0755 "$task_root" "$task_root/etc" "$task_root/etc/powerforge" \
  "$task_root/service" "$task_root/service/releases" \
  "$task_root/bin" "$task_root/package"
chmod 0750 "$task_root/etc/powerforge/service-pull" "$task_root/etc/powerforge/services"
setfacl -m "u:${deploy_user}:--x" "$task_root/etc/powerforge/service-pull" "$task_root/etc/powerforge/services"
chown "$deploy_user:$deploy_user" "$task_root/work"
chmod 0700 "$task_root/work"
printf 'published service\n' >"$task_root/package/artifact.tar"
digest=$(sha256sum "$task_root/package/artifact.tar")
digest=${digest%% *}
jq -n --arg repo "$repo" --arg sha "$sha" --arg digest "$digest" \
  '{schemaVersion:1,sourceRepository:$repo,sourceSha:$sha,workflowRunId:"12345",workflowRunAttempt:"2",artifactSha256:$digest}' \
  >"$task_root/package/package.json"
jq '.workflowRunAttempt="1"' "$task_root/package/package.json" >"$task_root/package/package-partial.json"
jq '.artifactSha256="ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"' \
  "$task_root/package/package.json" >"$task_root/package/package-tamper.json"
python3 - "$task_root/package" <<'PY'
import pathlib
import sys
import zipfile

root = pathlib.Path(sys.argv[1])
for suffix, metadata in (("", "package.json"), ("-partial", "package-partial.json"),
                         ("-tamper", "package-tamper.json"), ("-extra", "package.json")):
    with zipfile.ZipFile(root / f"bundle{suffix}.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        archive.write(root / "artifact.tar", "artifact.tar")
        archive.write(root / metadata, "package.json")
        if suffix == "-extra":
            archive.writestr("unexpected.txt", "not a service artifact")
PY
printf 'fixture-token\n' >"$task_root/etc/powerforge/service-pull/${service}.token"
printf 'SOURCE_REPOSITORY=%s\nSOURCE_BRANCH=main\nSOURCE_WORKFLOW=package-service.yml\nARTIFACT_NAME=powerforge-service-example\nWORK_ROOT=%s/work\n' \
  "$repo" "$task_root" >"$task_root/etc/powerforge/service-pull/${service}.env"
printf 'SERVICE_ROOT=%s/service\nARTIFACT_PULL_STAGE_ROOT=%s/work/.stage\nSYSTEMD_SERVICE=%s.service\nLOCAL_HEALTH_URL=http://127.0.0.1:12345/healthz\n' \
  "$task_root" "$task_root" "$service" >"$task_root/etc/powerforge/services/${service}.env"
chown "root:$deploy_user" "$task_root/etc/powerforge/service-pull/${service}.token" \
  "$task_root/etc/powerforge/service-pull/${service}.env" "$task_root/etc/powerforge/services/${service}.env"
chmod 0640 "$task_root/etc/powerforge/service-pull/${service}.token" \
  "$task_root/etc/powerforge/service-pull/${service}.env" "$task_root/etc/powerforge/services/${service}.env"
mkdir -m 0700 "$task_root/work/.stage"
chown "$deploy_user:$deploy_user" "$task_root/work/.stage"
env SUDO_UID="$(id -u "$deploy_user")" \
  POWERFORGE_SERVICE_CONFIG_ROOT="$task_root/etc/powerforge/services" \
  POWERFORGE_SERVICE_PULL_CONFIG_ROOT="$task_root/etc/powerforge/service-pull" \
  POWERFORGE_SERVICE_LOCK_ROOT="$task_root/locks" \
  POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT="$task_root/trusted" \
  POWERFORGE_SERVICE_TRANSACTION_ROOT="$task_root/transactions" \
  bash "$script_dir/../powerforge-service-deploy.sh" --service "$service" --recover-only >"$task_root/real-recovery.log"
[[ ! -e $task_root/work/.stage ]]

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
  endpoint=${endpoint//exampleorg\/service/ExampleOrg\/Service}
  case "$endpoint" in
    repos/ExampleOrg/Service/branches/main)
      counter_file="$POWERFORGE_FIXTURE_ROOT/work/branch-reads"
      counter=0
      [[ ! -f $counter_file ]] || counter=$(<"$counter_file")
      counter=$((counter + 1))
      printf '%s\n' "$counter" >"$counter_file"
      if [[ ${POWERFORGE_FIXTURE_MODE:-} == config-race && $counter -eq 1 ]]; then
        touch "$POWERFORGE_FIXTURE_ROOT/work/config-read"
        for ((attempt=0; attempt<100; attempt++)); do
          [[ ! -e $POWERFORGE_FIXTURE_ROOT/work/config-updated ]] || break
          sleep 0.05
        done
        [[ -e $POWERFORGE_FIXTURE_ROOT/work/config-updated ]] || exit 1
      fi
      if [[ ${POWERFORGE_FIXTURE_MODE:-} == advance && $counter -gt 1 ]]; then
        printf '{"commit":{"sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}}\n'
      else
        printf '{"commit":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}\n'
      fi
      ;;
    repos/ExampleOrg/Service/branches/release%2F1.x)
      printf '{"commit":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}\n'
      ;;
    repos/ExampleOrg/Service/branches/feature%2Ffoo%2Bbar)
      printf '{"commit":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}\n'
      ;;
    repos/ExampleOrg/Service/branches/release%2Fv1%40beta)
      printf '{"commit":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}\n'
      ;;
    repos/ExampleOrg/Service/actions/workflows/package-service.yml/runs*)
      if [[ ${POWERFORGE_FIXTURE_MODE:-} == missing ]]; then
        printf '{"workflow_runs":[]}\n'
      else
        branch=main
        case ${POWERFORGE_FIXTURE_MODE:-} in
          slash) branch=release/1.x ;;
          plus) branch=feature/foo+bar ;;
          at) branch=release/v1@beta ;;
        esac
        printf '{"workflow_runs":[{"id":12345,"head_sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","head_branch":"%s","status":"completed","conclusion":"success","event":"push","run_number":8,"run_attempt":2}]}\n' "$branch"
      fi
      ;;
    repos/ExampleOrg/Service/actions/runs/12345)
      branch=main
      case ${POWERFORGE_FIXTURE_MODE:-} in
        slash) branch=release/1.x ;;
        plus) branch=feature/foo+bar ;;
        at) branch=release/v1@beta ;;
      esac
      printf '{"id":12345,"head_sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","head_branch":"%s","status":"completed","conclusion":"success","event":"push","run_attempt":2}\n' "$branch"
      ;;
    repos/ExampleOrg/Service/actions/runs/12345/artifacts*)
      if [[ ${POWERFORGE_FIXTURE_MODE:-} == oversize ]]; then
        printf '{"artifacts":[{"id":54321,"name":"powerforge-service-example","expired":false,"size_in_bytes":2147483648}]}\n'
      else
        printf '{"artifacts":[{"id":54321,"name":"powerforge-service-example","expired":false,"size_in_bytes":1024}]}\n'
      fi
      ;;
    *) exit 1 ;;
  esac
else
  exit 1
fi
SH
cat >"$task_root/bin/getent" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
if [[ ${POWERFORGE_FIXTURE_MODE:-} == shared-group && $1 == group ]]; then
  /usr/bin/getent "$@" | awk -F: 'BEGIN {OFS=":"} {$4="other-user"; print}'
else
  exec /usr/bin/getent "$@"
fi
SH
cat >"$task_root/bin/curl" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
if [[ $* == *'powerforge-deploy='* ]]; then
  cat "$POWERFORGE_FIXTURE_ROOT/service/current/_powerforge/deployment.json"
  exit 0
fi
[[ ${GH_TOKEN:-} == fixture-token ]]
[[ $* != *fixture-token* && $* != *fixture-signed-url* ]]
[[ $* == *'--config -'* ]]
config=$(cat)
if [[ $config == 'header = "Authorization: Bearer fixture-token"' ]]; then
  [[ $* == *'api.github.com/repos/'*'/actions/artifacts/54321/zip'* ]]
  printf 'https://artifact-storage.example.test/bundle.zip?sig=fixture-signed-url&x=1'
  exit 0
fi
[[ $config == 'url = "https://artifact-storage.example.test/bundle.zip?sig=fixture-signed-url&x=1"' ]]
while (($#)); do
  if [[ $1 == --output ]]; then destination=$2; shift 2; else shift; fi
done
[[ -n ${destination:-} ]]
case ${POWERFORGE_FIXTURE_MODE:-} in
  tamper) suffix=-tamper ;;
  partial) suffix=-partial ;;
  extra) suffix=-extra ;;
  *) suffix= ;;
esac
cp "$POWERFORGE_FIXTURE_ROOT/package/bundle${suffix}.zip" "$destination"
SH
cat >"$task_root/bin/sudo" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
stage="$POWERFORGE_FIXTURE_ROOT/work/.stage"
if [[ "$*" == "-- /usr/local/sbin/powerforge-service-deploy --service $POWERFORGE_FIXTURE_SERVICE --recover-only" ]]; then
  [[ ! -e $stage ]] || rm -rf -- "$stage"
  printf 'recovered\n' >>"$POWERFORGE_FIXTURE_ROOT/work/recovery.log"
  exit 0
fi
[[ "$*" == "-- /usr/local/sbin/powerforge-service-deploy --service $POWERFORGE_FIXTURE_SERVICE" ]]
[[ -s $stage/artifact.tar && -s $stage/deployment.json ]]
jq -e '.sourceSha == "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" and .workflowRunId == "12345" and .workflowRunAttempt == "2" and (.packageRunAttempt == "1" or .packageRunAttempt == "2") and (.pullConfigurationSha256 | test("^[0-9a-f]{64}$"))' \
  "$stage/deployment.json" >/dev/null
expected_service_configuration_sha=$(sha256sum "$POWERFORGE_FIXTURE_ROOT/etc/powerforge/services/${POWERFORGE_FIXTURE_SERVICE}.env")
expected_service_configuration_sha=${expected_service_configuration_sha%% *}
jq -e --arg sha "$expected_service_configuration_sha" '.serviceConfigurationSha256 == $sha' "$stage/deployment.json" >/dev/null
cp "$stage/deployment.json" "$POWERFORGE_FIXTURE_ROOT/work/deployed.json"
printf 'promoted\n' >>"$POWERFORGE_FIXTURE_ROOT/work/sudo.log"
SH
cat >"$task_root/bin/systemctl" <<'SH'
#!/usr/bin/env bash
set -Eeuo pipefail
printf '%s\n' "$*" >>"$POWERFORGE_FIXTURE_ROOT/work/systemctl.log"
SH
chmod 0755 "$task_root/bin/gh" "$task_root/bin/getent" "$task_root/bin/curl" "$task_root/bin/sudo" "$task_root/bin/systemctl"

run_fixture() {
  local mode=$1
  shift
  rm -f -- "$task_root/work/branch-reads"
  runuser -u "$deploy_user" -- env POWERFORGE_FIXTURE_ROOT="$task_root" POWERFORGE_FIXTURE_SERVICE="$service" \
    POWERFORGE_FIXTURE_MODE="$mode" "$@" bash "$task_root/runner.sh" "$service"
}
if run_fixture missing >"$task_root/missing.log" 2>&1; then
  echo 'Accepted a missing successful run.' >&2; exit 1
fi
grep -Fq 'no successful service package run' "$task_root/missing.log" || { cat "$task_root/missing.log" >&2; exit 1; }
cp -p "$task_root/etc/powerforge/service-pull/${service}.env" "$task_root/pull.env.saved"
sed -i '/^SOURCE_REPOSITORY=/d' "$task_root/etc/powerforge/service-pull/${service}.env"
if run_fixture inherited-pull SOURCE_REPOSITORY="$repo" >"$task_root/inherited-pull.log" 2>&1; then
  echo 'Accepted a missing pull setting from the caller environment.' >&2; exit 1
fi
grep -Fq 'SOURCE_REPOSITORY: parameter null or not set' "$task_root/inherited-pull.log"
cp -p "$task_root/pull.env.saved" "$task_root/etc/powerforge/service-pull/${service}.env"
cp -p "$task_root/etc/powerforge/services/${service}.env" "$task_root/service.env.saved"
sed -i '/^SERVICE_ROOT=/d' "$task_root/etc/powerforge/services/${service}.env"
if run_fixture inherited-service SERVICE_ROOT="$task_root/service" >"$task_root/inherited-service.log" 2>&1; then
  echo 'Accepted a missing service setting from the caller environment.' >&2; exit 1
fi
grep -Fq 'SERVICE_ROOT: parameter null or not set' "$task_root/inherited-service.log"
cp -p "$task_root/service.env.saved" "$task_root/etc/powerforge/services/${service}.env"
if run_fixture shared-group >"$task_root/shared-group.log" 2>&1; then
  echo 'Accepted a deployment group with another member.' >&2; exit 1
fi
grep -Fq 'deployment group has other explicit members' "$task_root/shared-group.log"
if run_fixture tamper >"$task_root/tamper.log" 2>&1; then
  echo 'Accepted a package with the wrong SHA-256.' >&2; exit 1
fi
grep -Fq 'service package does not match' "$task_root/tamper.log"
if run_fixture extra >"$task_root/extra.log" 2>&1; then
  echo 'Accepted unexpected workflow artifact members.' >&2; exit 1
fi
grep -Fq 'workflow artifact contains invalid or oversized members' "$task_root/extra.log"
if run_fixture oversize >"$task_root/oversize.log" 2>&1; then
  echo 'Accepted an oversized workflow artifact.' >&2; exit 1
fi
grep -Fq 'no unique bounded service artifact' "$task_root/oversize.log"
if run_fixture advance >"$task_root/advance.log" 2>&1; then
  echo 'Promoted after the source branch advanced.' >&2; exit 1
fi
grep -Fq 'source branch advanced before promotion' "$task_root/advance.log"
run_fixture config-race >"$task_root/config-race.log" 2>&1 &
race_pid=$!
for ((attempt=0; attempt<100; attempt++)); do
  [[ ! -e $task_root/work/config-read ]] || break
  sleep 0.05
done
if [[ ! -e $task_root/work/config-read ]]; then
  wait "$race_pid" || true
  echo 'The puller did not reach the configuration race checkpoint.' >&2; exit 1
fi
cp "$task_root/etc/powerforge/service-pull/${service}.env" "$task_root/etc/powerforge/service-pull/${service}.next"
printf 'ARTIFACT_NAME=changed-artifact\n' >>"$task_root/etc/powerforge/service-pull/${service}.next"
chown "root:$deploy_user" "$task_root/etc/powerforge/service-pull/${service}.next"
chmod 0640 "$task_root/etc/powerforge/service-pull/${service}.next"
mv "$task_root/etc/powerforge/service-pull/${service}.next" "$task_root/etc/powerforge/service-pull/${service}.env"
touch "$task_root/work/config-updated"
if wait "$race_pid"; then
  echo 'Promoted after root replaced the pull configuration.' >&2; exit 1
fi
grep -Fq 'service pull configuration changed before promotion' "$task_root/config-race.log"
printf 'SOURCE_REPOSITORY=%s\nSOURCE_BRANCH=main\nSOURCE_WORKFLOW=package-service.yml\nARTIFACT_NAME=powerforge-service-example\nWORK_ROOT=%s/work\n' \
  "$repo" "$task_root" >"$task_root/etc/powerforge/service-pull/${service}.env"
chmod 0660 "$task_root/etc/powerforge/service-pull/${service}.token"
if run_fixture success >"$task_root/token-mode.log" 2>&1; then
  echo 'Accepted a group-writable GitHub token file.' >&2; exit 1
fi
grep -Fq 'configuration file must be root-owned' "$task_root/token-mode.log"
chmod 0640 "$task_root/etc/powerforge/service-pull/${service}.token"
setfacl -m u:nobody:r-- "$task_root/etc/powerforge/service-pull/${service}.token"
[[ $(stat -c %a -- "$task_root/etc/powerforge/service-pull/${service}.token") == 640 ]]
if run_fixture success >"$task_root/token-acl.log" 2>&1; then
  echo 'Accepted a GitHub token file readable by a named ACL user.' >&2; exit 1
fi
grep -Fq 'unexpected extended ACL entries' "$task_root/token-acl.log"
setfacl -b "$task_root/etc/powerforge/service-pull/${service}.token"
chmod 0640 "$task_root/etc/powerforge/service-pull/${service}.token"
mkdir "$task_root/work/.stage"
chown "$deploy_user:$deploy_user" "$task_root/work/.stage"
chmod 0700 "$task_root/work/.stage"
if run_fixture missing >"$task_root/stale-stage.log" 2>&1; then
  echo 'Accepted a missing package run after interrupted staging recovery.' >&2; exit 1
fi
grep -Fq 'no successful service package run' "$task_root/stale-stage.log"
[[ -z $(find "$task_root/work" -maxdepth 1 -name '.download.*' -print -quit) ]]
[[ ! -e $task_root/work/.stage ]]
[[ ! -e $task_root/work/sudo.log ]]
[[ ! -e $task_root/work/.stage ]]
mkdir -m 0700 "$task_root/work/.download.abandoned"
printf 'orphan\n' >"$task_root/work/.download.abandoned/workflow-artifact.zip"
chown -R "$deploy_user:$deploy_user" "$task_root/work/.download.abandoned"
mkdir "/tmp/powerforge-service-${service}"
run_fixture partial
[[ ! -e $task_root/work/.download.abandoned ]]
[[ -d /tmp/powerforge-service-${service} ]]
rmdir "/tmp/powerforge-service-${service}"
[[ $(jq -r '.packageRunAttempt' "$task_root/work/deployed.json") == 1 ]]
[[ $(wc -l <"$task_root/work/sudo.log") -eq 1 ]]
mkdir -p "$task_root/service/releases/verified/_powerforge"
cp "$task_root/work/deployed.json" "$task_root/service/releases/verified/_powerforge/deployment.json"
chmod 0644 "$task_root/service/releases/verified/_powerforge/deployment.json"
ln -s releases/verified "$task_root/service/current"
[[ -L $task_root/service/current ]]
[[ $(realpath -e "$task_root/service/current") == "$task_root/service/releases/"* ]]
jq -e --arg sha "$sha" '.sourceSha == $sha and .workflowRunId == "12345" and .workflowRunAttempt == "2"' \
  "$task_root/service/current/_powerforge/deployment.json" >/dev/null
chmod 0700 "$task_root/service"
if run_fixture success >"$task_root/inaccessible-root.log" 2>&1; then
  echo 'Accepted an inaccessible current service root.' >&2; exit 1
fi
grep -Fq 'service release root is not traversable' "$task_root/inaccessible-root.log"
chmod 0755 "$task_root/service"
chmod 0600 "$task_root/service/current/_powerforge/deployment.json"
if run_fixture success >"$task_root/inaccessible-metadata.log" 2>&1; then
  echo 'Accepted inaccessible current deployment metadata.' >&2; exit 1
fi
grep -Fq 'current deployment metadata is missing or inaccessible' "$task_root/inaccessible-metadata.log"
chmod 0644 "$task_root/service/current/_powerforge/deployment.json"
run_fixture success
[[ $(wc -l <"$task_root/work/sudo.log") -eq 1 ]]
printf '# change in root-owned promoter configuration\n' >>"$task_root/etc/powerforge/services/${service}.env"
run_fixture success
[[ $(wc -l <"$task_root/work/sudo.log") -eq 2 ]]
printf 'SOURCE_REPOSITORY=%s\nSOURCE_BRANCH=release/1.x\nSOURCE_WORKFLOW=package-service.yml\nARTIFACT_NAME=powerforge-service-example\nWORK_ROOT=%s/work\n' \
  "$repo" "$task_root" >"$task_root/etc/powerforge/service-pull/${service}.env"
run_fixture slash
[[ $(wc -l <"$task_root/work/sudo.log") -eq 3 ]]
printf 'SOURCE_REPOSITORY=%s\nSOURCE_BRANCH=feature/foo+bar\nSOURCE_WORKFLOW=package-service.yml\nARTIFACT_NAME=powerforge-service-example\nWORK_ROOT=%s/work\n' \
  "$repo" "$task_root" >"$task_root/etc/powerforge/service-pull/${service}.env"
run_fixture plus
[[ $(wc -l <"$task_root/work/sudo.log") -eq 4 ]]
printf 'SOURCE_REPOSITORY=%s\nSOURCE_BRANCH=release/v1@beta\nSOURCE_WORKFLOW=package-service.yml\nARTIFACT_NAME=powerforge-service-example\nWORK_ROOT=%s/work\n' \
  "$repo" "$task_root" >"$task_root/etc/powerforge/service-pull/${service}.env"
run_fixture at
[[ $(wc -l <"$task_root/work/sudo.log") -eq 5 ]]
printf 'SOURCE_REPOSITORY=exampleorg/service\nSOURCE_BRANCH=main\nSOURCE_WORKFLOW=package-service.yml\nARTIFACT_NAME=powerforge-service-example\nWORK_ROOT=%s/work\n' \
  "$task_root" >"$task_root/etc/powerforge/service-pull/${service}.env"
run_fixture success
[[ $(wc -l <"$task_root/work/sudo.log") -eq 6 ]]
run_real_promoter() {
  env PATH="$task_root/bin:$PATH" POWERFORGE_FIXTURE_ROOT="$task_root" SUDO_UID="$(id -u "$deploy_user")" \
    POWERFORGE_SERVICE_CONFIG_ROOT="$task_root/etc/powerforge/services" \
    POWERFORGE_SERVICE_PULL_CONFIG_ROOT="$task_root/etc/powerforge/service-pull" \
    POWERFORGE_SERVICE_LOCK_ROOT="$task_root/locks" \
    POWERFORGE_SERVICE_TRUSTED_STAGE_ROOT="$task_root/trusted" \
    POWERFORGE_SERVICE_TRANSACTION_ROOT="$task_root/transactions" \
    POWERFORGE_SYSTEMD_CONFIG_ROOT="$task_root/systemd" \
    bash "$script_dir/../powerforge-service-deploy.sh" --service "$service"
}
mkdir -m 0700 "$task_root/generated-root"
printf 'real promoter boundary\n' >"$task_root/generated-root/promotion.txt"
mkdir -m 0700 "$task_root/work/.stage"
tar -C "$task_root/generated-root" -cf "$task_root/work/.stage/artifact.tar" .
cp "$task_root/work/deployed.json" "$task_root/work/.stage/deployment.json"
chown -R "$deploy_user:$deploy_user" "$task_root/work/.stage"
printf '# root rotated the service configuration after pull verification\n' >>"$task_root/etc/powerforge/services/${service}.env"
if run_real_promoter >"$task_root/promoter-config-race.log" 2>&1; then
  echo 'Real promoter accepted a service configuration changed after pull verification.' >&2; exit 1
fi
grep -Fq 'Service configuration changed between artifact verification and promotion' "$task_root/promoter-config-race.log" || { cat "$task_root/promoter-config-race.log" >&2; exit 1; }
prepare_real_stage() {
  mkdir -m 0700 "$task_root/work/.stage"
  tar -C "$task_root/generated-root" -cf "$task_root/work/.stage/artifact.tar" .
  local artifact_sha service_sha combined_sha
  artifact_sha=$(sha256sum "$task_root/work/.stage/artifact.tar")
  artifact_sha=${artifact_sha%% *}
  service_sha=$(sha256sum "$task_root/etc/powerforge/services/${service}.env")
  service_sha=${service_sha%% *}
  combined_sha=$(sha256sum "$task_root/etc/powerforge/service-pull/${service}.env" "$task_root/etc/powerforge/services/${service}.env" | awk '{print $1}' | sha256sum)
  combined_sha=${combined_sha%% *}
  jq --arg artifact "$artifact_sha" --arg service_configuration "$service_sha" --arg configuration "$combined_sha" \
    '.artifactSha256=$artifact | .serviceConfigurationSha256=$service_configuration | .pullConfigurationSha256=$configuration' \
    "$task_root/work/deployed.json" >"$task_root/work/.stage/deployment.json"
  chown -R "$deploy_user:$deploy_user" "$task_root/work/.stage"
}
prepare_real_stage
cp -p "$task_root/etc/powerforge/service-pull/${service}.env" "$task_root/pull-before-rotation.env"
printf '# root rotated pull policy after verification\n' >>"$task_root/etc/powerforge/service-pull/${service}.env"
if run_real_promoter >"$task_root/promoter-pull-config-race.log" 2>&1; then
  echo 'Real promoter accepted a pull configuration changed after verification.' >&2; exit 1
fi
grep -Fq 'Pull configuration changed between artifact verification and promotion' "$task_root/promoter-pull-config-race.log" || { cat "$task_root/promoter-pull-config-race.log" >&2; exit 1; }
cp -p "$task_root/pull-before-rotation.env" "$task_root/etc/powerforge/service-pull/${service}.env"
prepare_real_stage
run_real_promoter >"$task_root/promoter-bound-success.log" 2>&1 || { cat "$task_root/promoter-bound-success.log" >&2; exit 1; }
[[ -f $task_root/service/current/promotion.txt ]]
grep -Fq "restart ${service}.service" "$task_root/work/systemctl.log"
run_fixture success
[[ $(wc -l <"$task_root/work/sudo.log") -eq 6 ]]
echo 'Host-initiated Linux service artifact pull fixture passed.'
