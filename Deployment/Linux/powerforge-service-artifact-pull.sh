#!/usr/bin/env bash
set -Eeuo pipefail
set +x
umask 077
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export PATH

fail() { printf 'powerforge-service-artifact-pull: %s\n' "$*" >&2; exit 1; }
max_package_bytes=1073741824
max_metadata_bytes=1048576
max_archive_bytes=$((max_package_bytes - max_metadata_bytes))
[[ $# -eq 1 && $1 =~ ^[a-z0-9][a-z0-9.-]{0,62}$ ]] || fail 'usage: powerforge-service-artifact-pull <service>'
[[ $(id -u) -ne 0 ]] || fail 'run as the dedicated deployment account, not root'
service=$1
pull_config="/etc/powerforge/service-pull/${service}.env"
service_config="/etc/powerforge/services/${service}.env"
token_file="/etc/powerforge/service-pull/${service}.token"
command -v getfacl >/dev/null || fail 'missing command: getfacl'

for config_dir in /etc /etc/powerforge /etc/powerforge/service-pull /etc/powerforge/services; do
  [[ -d $config_dir && ! -L $config_dir && $(stat -c %u -- "$config_dir") -eq 0 ]] || fail "untrusted configuration directory: $config_dir"
  mode=$(stat -c %a -- "$config_dir")
  (( (8#$mode & 0022) == 0 )) || fail "writable configuration directory: $config_dir"
done
for config_file in "$pull_config" "$service_config" "$token_file"; do
  [[ -f $config_file && ! -L $config_file && $(stat -c %u -- "$config_file") -eq 0 ]] || fail "missing root-owned regular configuration file: $config_file"
  [[ $(stat -c %g -- "$config_file") -eq $(id -g) && $(stat -c %a -- "$config_file") == 640 ]] || fail "configuration file must be root-owned and readable only by the deployment group: $config_file"
  file_acl=$(getfacl -cp -- "$config_file") || fail "unable to inspect configuration ACL: $config_file"
  [[ $file_acl == $'user::rw-\ngroup::r--\nother::---' ]] || fail "configuration file has unexpected extended ACL entries: $config_file"
done
group_entry=$(getent group "$(id -g)") || fail 'unable to inspect the deployment group'
IFS=: read -r _ _ group_gid group_members <<<"$group_entry"
[[ $group_gid == "$(id -g)" && ( -z $group_members || $group_members == "$(id -un)" ) ]] || fail 'deployment group has other explicit members'
primary_group_peer=$(getent passwd | awk -F: -v gid="$(id -g)" -v self="$(id -un)" '$4 == gid && $1 != self { print $1; exit }') || fail 'unable to inspect primary group users'
[[ -z $primary_group_peer ]] || fail 'deployment group is another account primary group'

# Snapshot both root-owned files before reading any values. The digest must describe
# exactly the bytes that were sourced, even if an operator replaces a file mid-pull.
command -v git >/dev/null || fail 'missing command: git'
config_snapshot_root=$(mktemp -d /tmp/powerforge-service-config.XXXXXXXX) || fail 'unable to snapshot service configuration'
trap 'rm -rf -- "$config_snapshot_root"' EXIT
snapshot_pull="$config_snapshot_root/pull.env"
snapshot_service="$config_snapshot_root/service.env"
install -m 0600 -- "$pull_config" "$snapshot_pull" || fail 'unable to snapshot pull configuration'
install -m 0600 -- "$service_config" "$snapshot_service" || fail 'unable to snapshot service configuration'
configuration_sha=$(sha256sum -- "$snapshot_pull" "$snapshot_service" | awk '{print $1}' | sha256sum)
configuration_sha=${configuration_sha%% *}
initial_configuration_sha=$(sha256sum -- "$pull_config" "$service_config" | awk '{print $1}' | sha256sum)
[[ ${initial_configuration_sha%% *} == "$configuration_sha" ]] || fail 'service pull configuration changed while being snapshotted'

# The service root comes from the promoter's own configuration, so an up-to-date
# decision cannot be made against a different release directory.
unset SOURCE_REPOSITORY SOURCE_BRANCH SOURCE_WORKFLOW ARTIFACT_NAME WORK_ROOT
unset SERVICE_ROOT ARTIFACT_PULL_STAGE_ROOT
# shellcheck disable=SC1090
source "$snapshot_pull"
: "${SOURCE_REPOSITORY:?}" "${SOURCE_BRANCH:?}" "${SOURCE_WORKFLOW:?}" "${ARTIFACT_NAME:?}" "${WORK_ROOT:?}"
readonly SOURCE_REPOSITORY SOURCE_BRANCH SOURCE_WORKFLOW ARTIFACT_NAME WORK_ROOT
unset SERVICE_ROOT ARTIFACT_PULL_STAGE_ROOT
# shellcheck disable=SC1090
source "$snapshot_service"
: "${SERVICE_ROOT:?}" "${ARTIFACT_PULL_STAGE_ROOT:?}"
[[ $SOURCE_REPOSITORY =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail 'invalid GitHub repository'
git -C / check-ref-format --branch "$SOURCE_BRANCH" >/dev/null 2>&1 || fail 'invalid source branch'
[[ $SOURCE_WORKFLOW =~ ^[A-Za-z0-9._-]+\.ya?ml$ ]] || fail 'invalid workflow filename'
[[ $ARTIFACT_NAME =~ ^[A-Za-z0-9._-]+$ ]] || fail 'invalid artifact name'
[[ $WORK_ROOT == /* && $WORK_ROOT != / && -d $WORK_ROOT && ! -L $WORK_ROOT ]] || fail 'invalid deployment work root'
[[ $(stat -c %u -- "$WORK_ROOT") -eq $(id -u) && $(stat -c %a -- "$WORK_ROOT") == 700 ]] || fail 'deployment work root must belong only to the deployment account'
[[ $(realpath -e -- "$WORK_ROOT") == "$WORK_ROOT" ]] || fail 'deployment work root must be canonical'
[[ $ARTIFACT_PULL_STAGE_ROOT == "$WORK_ROOT/.stage" ]] || fail 'artifact pull staging path must be inside the private work root'
[[ $SERVICE_ROOT == /* && $SERVICE_ROOT != / && -d $SERVICE_ROOT && ! -L $SERVICE_ROOT ]] || fail 'invalid service release root'
[[ $(realpath -e -- "$SERVICE_ROOT") == "$SERVICE_ROOT" ]] || fail 'service release root must be canonical'
[[ -x $SERVICE_ROOT ]] || fail 'service release root is not traversable by the deployment account'
if [[ -d $SERVICE_ROOT/releases ]]; then
  [[ -x $SERVICE_ROOT/releases ]] || fail 'release directory is not traversable by the deployment account'
fi
assert_trusted_chain() {
  local path=$1 expected_leaf_owner=$2 allow_own_parent=$3 part=/ component owner mode
  local -a components
  IFS=/ read -r -a components <<<"${path#/}"
  for component in "${components[@]}"; do
    [[ -n $component ]] || continue
    part="${part%/}/$component"
    [[ -d $part && ! -L $part ]] || fail "untrusted directory chain: $part"
    owner=$(stat -c %u -- "$part")
    mode=$(stat -c %a -- "$part")
    [[ $owner -eq 0 || ( $allow_own_parent == 1 && $owner -eq $(id -u) ) ]] || fail "untrusted directory owner: $part"
    (( (8#$mode & 0022) == 0 )) || fail "writable directory chain: $part"
  done
  [[ $(stat -c %u -- "$path") -eq $expected_leaf_owner ]] || fail "unexpected directory owner: $path"
}
assert_trusted_chain "$WORK_ROOT" "$(id -u)" 1
assert_trusted_chain "$SERVICE_ROOT" 0 0
for command_name in gh jq sha256sum flock mktemp find stat realpath sudo date getent awk curl python3 git; do
  command -v "$command_name" >/dev/null || fail "missing command: $command_name"
done

exec 9>"$WORK_ROOT/.deploy.lock"
flock -n 9 || fail 'another service artifact pull is running'
workflow_stage="$ARTIFACT_PULL_STAGE_ROOT"
if [[ -e $workflow_stage || -L $workflow_stage ]]; then
  [[ -d $workflow_stage && ! -L $workflow_stage && -O $workflow_stage && $(stat -c %a -- "$workflow_stage") == 700 ]] || fail 'unsafe interrupted staging directory'
  [[ $(realpath -e -- "$workflow_stage") == "$workflow_stage" ]] || fail 'non-canonical interrupted staging directory'
fi
sudo -- /usr/local/sbin/powerforge-service-deploy --service "$service" --recover-only || fail 'unable to recover any interrupted service deployment'
[[ ! -e $workflow_stage && ! -L $workflow_stage ]] || fail 'interrupted staging directory remains after recovery'
for orphan in "$WORK_ROOT"/.download.*; do
  [[ -e $orphan || -L $orphan ]] || continue
  [[ -d $orphan && ! -L $orphan && -O $orphan && $(stat -c %a -- "$orphan") == 700 ]] || fail "unsafe abandoned download directory: $orphan"
  [[ $(realpath -e -- "$orphan") == "$orphan" ]] || fail "non-canonical abandoned download directory: $orphan"
  rm -rf -- "$orphan"
done
unset GH_TOKEN
IFS= read -r GH_TOKEN <"$token_file" || [[ -n ${GH_TOKEN:-} ]]
[[ ${GH_TOKEN:-} =~ ^[A-Za-z0-9_-]+$ ]] || fail 'GitHub read-only token file is empty or malformed'
GH_HOST=github.com
GH_PROMPT_DISABLED=1
GH_CONFIG_DIR="$WORK_ROOT/.gh"
install -d -m 0700 -- "$GH_CONFIG_DIR"
export GH_TOKEN GH_HOST GH_PROMPT_DISABLED GH_CONFIG_DIR

encoded_branch=$(jq -rn --arg branch "$SOURCE_BRANCH" '$branch|@uri')
branch_json=$(gh api -H 'Accept: application/vnd.github+json' "repos/${SOURCE_REPOSITORY}/branches/${encoded_branch}") || fail 'unable to read the source branch'
source_sha=$(jq -er '.commit.sha' <<<"$branch_json") || fail 'source branch has no commit SHA'
[[ $source_sha =~ ^[0-9a-f]{40}$ ]] || fail 'source branch did not resolve to a Git commit'
runs_json=$(gh api -H 'Accept: application/vnd.github+json' "repos/${SOURCE_REPOSITORY}/actions/workflows/${SOURCE_WORKFLOW}/runs?branch=${encoded_branch}&head_sha=${source_sha}&status=completed&per_page=100") || fail 'unable to read workflow runs'
run_id=$(jq -r --arg sha "$source_sha" --arg branch "$SOURCE_BRANCH" \
  '[.workflow_runs[]? | select(.head_sha == $sha and .head_branch == $branch and .status == "completed" and .conclusion == "success" and (.event == "push" or .event == "workflow_dispatch"))] | sort_by(.run_number, .run_attempt) | last | .id // empty' <<<"$runs_json") || fail 'invalid workflow runs response'
[[ $run_id =~ ^[1-9][0-9]*$ ]] || fail 'no successful service package run exists for the current source commit'
run_json=$(gh api -H 'Accept: application/vnd.github+json' "repos/${SOURCE_REPOSITORY}/actions/runs/${run_id}") || fail 'unable to verify the selected workflow run'
run_attempt=$(jq -er --arg sha "$source_sha" --arg branch "$SOURCE_BRANCH" --argjson id "$run_id" \
  'if .id == $id and .head_sha == $sha and .head_branch == $branch and .status == "completed" and .conclusion == "success" and (.event == "push" or .event == "workflow_dispatch") then .run_attempt else error("selected run changed") end' <<<"$run_json") || fail 'selected workflow run no longer matches the source branch'
[[ $run_attempt =~ ^[1-9][0-9]*$ ]] || fail 'workflow attempt is invalid'

current_metadata="$SERVICE_ROOT/current/_powerforge/deployment.json"
if [[ -e $SERVICE_ROOT/current || -L $SERVICE_ROOT/current ]]; then
  [[ -L $SERVICE_ROOT/current ]] || fail 'current release pointer is not a symlink'
  [[ -f $current_metadata && ! -L $current_metadata && -r $current_metadata ]] || fail 'current deployment metadata is missing or inaccessible'
  current_target=$(realpath -e -- "$SERVICE_ROOT/current") || fail 'unable to resolve the current release'
  if [[ $current_target == "$SERVICE_ROOT/releases/"* ]] && jq -e --arg sha "$source_sha" --arg run "$run_id" --arg attempt "$run_attempt" --arg configuration "$configuration_sha" \
      '.sourceSha == $sha and .workflowRunId == $run and .workflowRunAttempt == $attempt and .pullConfigurationSha256 == $configuration' "$current_metadata" >/dev/null 2>&1; then
    printf 'service=%s source=%s run=%s attempt=%s already deployed\n' "$service" "$source_sha" "$run_id" "$run_attempt"
    exit 0
  fi
fi

[[ ! -e $workflow_stage && ! -L $workflow_stage ]] || fail 'fixed service staging path already exists'
download_root=$(mktemp -d "$WORK_ROOT/.download.XXXXXXXX")
cleanup() {
  [[ ! -d $download_root ]] || rm -rf -- "$download_root"
  [[ ! -d $workflow_stage ]] || rm -rf -- "$workflow_stage"
  [[ ! -d $config_snapshot_root ]] || rm -rf -- "$config_snapshot_root"
}
trap cleanup EXIT
artifacts_json=$(gh api -H 'Accept: application/vnd.github+json' "repos/${SOURCE_REPOSITORY}/actions/runs/${run_id}/artifacts?name=${ARTIFACT_NAME}&per_page=100") || fail 'unable to inspect workflow artifacts'
artifact_id=$(jq -er --arg name "$ARTIFACT_NAME" \
  '[.artifacts[]? | select(.name == $name and .expired == false)] | if length == 1 and .[0].size_in_bytes > 0 and .[0].size_in_bytes <= 1074790400 then .[0].id else error("missing, duplicate, or oversized service artifact") end' \
  <<<"$artifacts_json") || fail 'no unique bounded service artifact exists for the selected run'
[[ $artifact_id =~ ^[1-9][0-9]*$ ]] || fail 'service artifact ID is invalid'
bundle="$download_root/workflow-artifact.zip"
artifact_url=$(printf 'header = "Authorization: Bearer %s"\n' "$GH_TOKEN" | curl --config - \
  --fail --silent --show-error --proto '=https' --max-time 30 --max-redirs 0 \
  --header 'Accept: application/vnd.github+json' --output /dev/null --write-out '%{redirect_url}' \
  "https://api.github.com/repos/${SOURCE_REPOSITORY}/actions/artifacts/${artifact_id}/zip") || fail 'unable to authorize the workflow artifact download'
[[ $artifact_url == https://* ]] || fail 'workflow artifact did not provide an HTTPS download URL'
[[ $artifact_url =~ ^[[:graph:]]+$ && $artifact_url != *\"* && $artifact_url != *\\* ]] || fail 'workflow artifact download URL contains unsupported characters'
printf 'url = "%s"\n' "$artifact_url" | curl --config - \
  --fail --silent --show-error --location --proto '=https' --proto-redir '=https' \
  --max-filesize 1074790400 --max-time 600 --output "$bundle" || fail 'unable to download the bounded workflow artifact'
python3 - "$bundle" "$download_root" "$max_archive_bytes" <<'PY' || fail 'workflow artifact contains invalid or oversized members'
import os
import stat
import sys
import zipfile

limits = {"artifact.tar": int(sys.argv[3]), "package.json": 1048576}
with zipfile.ZipFile(sys.argv[1]) as archive:
    members = archive.infolist()
    if len(members) != 2 or {member.filename for member in members} != set(limits):
        raise ValueError("unexpected artifact entries")
    for member in members:
        mode = member.external_attr >> 16
        if (member.is_dir() or stat.S_ISLNK(mode) or member.flag_bits & 1
                or not 0 < member.file_size <= limits[member.filename]):
            raise ValueError("invalid artifact member")
        target = os.path.join(sys.argv[2], member.filename)
        descriptor = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        with os.fdopen(descriptor, "wb") as destination, archive.open(member) as source:
            remaining = limits[member.filename]
            while chunk := source.read(min(1048576, remaining + 1)):
                remaining -= len(chunk)
                if remaining < 0:
                    raise ValueError("artifact member exceeded limit while extracting")
                destination.write(chunk)
PY
rm -f -- "$bundle"
mapfile -t entries < <(find "$download_root" -mindepth 1 -maxdepth 1 -printf '%f\n' | LC_ALL=C sort)
[[ ${#entries[@]} -eq 2 && ${entries[0]} == artifact.tar && ${entries[1]} == package.json ]] || fail 'service artifact must contain only artifact.tar and package.json'
archive="$download_root/artifact.tar"
package_metadata="$download_root/package.json"
[[ -f $archive && ! -L $archive && -s $archive && -f $package_metadata && ! -L $package_metadata && -s $package_metadata ]] || fail 'service artifact members must be non-empty regular files'
(( $(stat -c %s -- "$archive") <= max_archive_bytes && $(stat -c %s -- "$package_metadata") <= max_metadata_bytes )) || fail 'service artifact exceeds the promoter size limit'
artifact_sha=$(sha256sum -- "$archive")
artifact_sha=${artifact_sha%% *}
package_attempt=$(jq -er '.workflowRunAttempt' "$package_metadata") || fail 'service package has no workflow attempt'
[[ $package_attempt =~ ^[1-9][0-9]*$ && ${#package_attempt} -le 9 ]] || fail 'service package attempt is invalid'
(( 10#$package_attempt <= 10#$run_attempt )) || fail 'service package attempt is newer than the selected workflow run'
jq -e --arg repo "$SOURCE_REPOSITORY" --arg sha "$source_sha" --arg run "$run_id" --arg attempt "$package_attempt" --arg digest "$artifact_sha" \
  '.schemaVersion == 1 and (.sourceRepository | ascii_downcase) == ($repo | ascii_downcase) and .sourceSha == $sha and .workflowRunId == $run and .workflowRunAttempt == $attempt and .artifactSha256 == $digest' \
  "$package_metadata" >/dev/null || fail 'service package does not match the selected source, workflow run, and SHA-256'

install -d -m 0700 -- "$workflow_stage"
install -m 0600 -- "$archive" "$workflow_stage/artifact.tar"
jq -n --arg repo "$SOURCE_REPOSITORY" --arg sha "$source_sha" --arg run "$run_id" --arg attempt "$run_attempt" --arg package_attempt "$package_attempt" --arg digest "$artifact_sha" --arg configuration "$configuration_sha" --arg time "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --arg service_configuration "$(sha256sum -- "$snapshot_service" | awk '{print $1}')" \
  '{schemaVersion:1,sourceRepository:$repo,sourceSha:$sha,workflowRunId:$run,workflowRunAttempt:$attempt,packageRunAttempt:$package_attempt,artifactSha256:$digest,pullConfigurationSha256:$configuration,serviceConfigurationSha256:$service_configuration,deployedAtUtc:$time}' \
  >"$workflow_stage/deployment.json"
chmod 0600 -- "$workflow_stage/deployment.json"
archive_bytes=$(stat -c %s -- "$workflow_stage/artifact.tar")
metadata_bytes=$(stat -c %s -- "$workflow_stage/deployment.json")
(( metadata_bytes > 0 && metadata_bytes <= max_metadata_bytes && archive_bytes + metadata_bytes <= max_package_bytes )) || fail 'service artifact and deployment metadata exceed the promoter size limit'
latest_branch_json=$(gh api -H 'Accept: application/vnd.github+json' "repos/${SOURCE_REPOSITORY}/branches/${encoded_branch}") || fail 'unable to recheck the source branch before promotion'
[[ $(jq -er '.commit.sha' <<<"$latest_branch_json") == "$source_sha" ]] || fail 'source branch advanced before promotion; a later pull will use the new commit'
latest_configuration_sha=$(sha256sum -- "$pull_config" "$service_config" | awk '{print $1}' | sha256sum)
[[ ${latest_configuration_sha%% *} == "$configuration_sha" ]] || fail 'service pull configuration changed before promotion'
unset GH_TOKEN
sudo -- /usr/local/sbin/powerforge-service-deploy --service "$service"
printf 'service=%s source=%s run=%s attempt=%s promoted\n' "$service" "$source_sha" "$run_id" "$run_attempt"
