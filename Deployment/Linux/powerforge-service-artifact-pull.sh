#!/usr/bin/env bash
set -Eeuo pipefail
set +x
umask 077
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export PATH

fail() { printf 'powerforge-service-artifact-pull: %s\n' "$*" >&2; exit 1; }
[[ $# -eq 1 && $1 =~ ^[a-z0-9][a-z0-9.-]{0,62}$ ]] || fail 'usage: powerforge-service-artifact-pull <service>'
[[ $(id -u) -ne 0 ]] || fail 'run as the dedicated deployment account, not root'
service=$1
pull_config="/etc/powerforge/service-pull/${service}.env"
service_config="/etc/powerforge/services/${service}.env"
token_file="/etc/powerforge/service-pull/${service}.token"

for config_dir in /etc /etc/powerforge /etc/powerforge/service-pull /etc/powerforge/services; do
  [[ -d $config_dir && ! -L $config_dir && $(stat -c %u -- "$config_dir") -eq 0 ]] || fail "untrusted configuration directory: $config_dir"
  mode=$(stat -c %a -- "$config_dir")
  (( (8#$mode & 0022) == 0 )) || fail "writable configuration directory: $config_dir"
done
for config_file in "$pull_config" "$service_config" "$token_file"; do
  [[ -f $config_file && ! -L $config_file && $(stat -c %u -- "$config_file") -eq 0 ]] || fail "missing root-owned regular configuration file: $config_file"
  [[ $(stat -c %g -- "$config_file") -eq $(id -g) && $(stat -c %a -- "$config_file") == 640 ]] || fail "configuration file must be root-owned and readable only by the deployment group: $config_file"
done

# Both files are root-owned. The service root comes from the promoter's own configuration,
# so an up-to-date decision cannot be made against a different release directory.
# shellcheck disable=SC1090
source "$pull_config"
# shellcheck disable=SC1090
source "$service_config"
: "${SOURCE_REPOSITORY:?}" "${SOURCE_BRANCH:?}" "${SOURCE_WORKFLOW:?}" "${ARTIFACT_NAME:?}" "${WORK_ROOT:?}" "${SERVICE_ROOT:?}"
[[ $SOURCE_REPOSITORY =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail 'invalid GitHub repository'
[[ $SOURCE_BRANCH =~ ^[A-Za-z0-9._-]+$ ]] || fail 'invalid source branch'
[[ $SOURCE_WORKFLOW =~ ^[A-Za-z0-9._-]+\.ya?ml$ ]] || fail 'invalid workflow filename'
[[ $ARTIFACT_NAME =~ ^[A-Za-z0-9._-]+$ ]] || fail 'invalid artifact name'
[[ $WORK_ROOT == /* && $WORK_ROOT != / && -d $WORK_ROOT && ! -L $WORK_ROOT ]] || fail 'invalid deployment work root'
[[ $(stat -c %u -- "$WORK_ROOT") -eq $(id -u) && $(stat -c %a -- "$WORK_ROOT") == 700 ]] || fail 'deployment work root must belong only to the deployment account'
[[ $(realpath -e -- "$WORK_ROOT") == "$WORK_ROOT" ]] || fail 'deployment work root must be canonical'
[[ $SERVICE_ROOT == /* && $SERVICE_ROOT != / && -d $SERVICE_ROOT && ! -L $SERVICE_ROOT ]] || fail 'invalid service release root'
[[ $(realpath -e -- "$SERVICE_ROOT") == "$SERVICE_ROOT" ]] || fail 'service release root must be canonical'
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
for command_name in gh jq sha256sum flock mktemp find stat realpath sudo date; do
  command -v "$command_name" >/dev/null || fail "missing command: $command_name"
done

exec 9>"$WORK_ROOT/.deploy.lock"
flock -n 9 || fail 'another service artifact pull is running'
IFS= read -r GH_TOKEN <"$token_file" || [[ -n ${GH_TOKEN:-} ]]
[[ -n ${GH_TOKEN:-} && $GH_TOKEN != *[[:space:]]* ]] || fail 'GitHub read-only token file is empty or malformed'
GH_HOST=github.com
GH_PROMPT_DISABLED=1
GH_CONFIG_DIR="$WORK_ROOT/.gh"
install -d -m 0700 -- "$GH_CONFIG_DIR"
export GH_TOKEN GH_HOST GH_PROMPT_DISABLED GH_CONFIG_DIR

branch_json=$(gh api -H 'Accept: application/vnd.github+json' "repos/${SOURCE_REPOSITORY}/branches/${SOURCE_BRANCH}") || fail 'unable to read the source branch'
source_sha=$(jq -er '.commit.sha' <<<"$branch_json") || fail 'source branch has no commit SHA'
[[ $source_sha =~ ^[0-9a-f]{40}$ ]] || fail 'source branch did not resolve to a Git commit'
runs_json=$(gh api -H 'Accept: application/vnd.github+json' "repos/${SOURCE_REPOSITORY}/actions/workflows/${SOURCE_WORKFLOW}/runs?branch=${SOURCE_BRANCH}&head_sha=${source_sha}&status=completed&per_page=100") || fail 'unable to read workflow runs'
run_id=$(jq -r --arg sha "$source_sha" --arg branch "$SOURCE_BRANCH" \
  '[.workflow_runs[]? | select(.head_sha == $sha and .head_branch == $branch and .status == "completed" and .conclusion == "success" and (.event == "push" or .event == "workflow_dispatch"))] | sort_by(.run_number, .run_attempt) | last | .id // empty' <<<"$runs_json") || fail 'invalid workflow runs response'
[[ $run_id =~ ^[1-9][0-9]*$ ]] || fail 'no successful service package run exists for the current source commit'
run_json=$(gh api -H 'Accept: application/vnd.github+json' "repos/${SOURCE_REPOSITORY}/actions/runs/${run_id}") || fail 'unable to verify the selected workflow run'
run_attempt=$(jq -er --arg sha "$source_sha" --arg branch "$SOURCE_BRANCH" --argjson id "$run_id" \
  'if .id == $id and .head_sha == $sha and .head_branch == $branch and .status == "completed" and .conclusion == "success" and (.event == "push" or .event == "workflow_dispatch") then .run_attempt else error("selected run changed") end' <<<"$run_json") || fail 'selected workflow run no longer matches the source branch'
[[ $run_attempt =~ ^[1-9][0-9]*$ ]] || fail 'workflow attempt is invalid'

current_metadata="$SERVICE_ROOT/current/_powerforge/deployment.json"
if [[ -L $SERVICE_ROOT/current && -f $current_metadata && ! -L $current_metadata ]]; then
  current_target=$(realpath -e -- "$SERVICE_ROOT/current") || fail 'unable to resolve the current release'
  if [[ $current_target == "$SERVICE_ROOT/releases/"* ]] && jq -e --arg sha "$source_sha" --arg run "$run_id" --arg attempt "$run_attempt" \
      '.sourceSha == $sha and .workflowRunId == $run and .workflowRunAttempt == $attempt' "$current_metadata" >/dev/null 2>&1; then
    printf 'service=%s source=%s run=%s attempt=%s already deployed\n' "$service" "$source_sha" "$run_id" "$run_attempt"
    exit 0
  fi
fi

workflow_stage="/tmp/powerforge-service-${service}"
[[ ! -e $workflow_stage && ! -L $workflow_stage ]] || fail 'fixed service staging path already exists'
download_root=$(mktemp -d "$WORK_ROOT/.download.XXXXXXXX")
cleanup() {
  [[ ! -d $download_root ]] || rm -rf -- "$download_root"
  [[ ! -d $workflow_stage ]] || rm -rf -- "$workflow_stage"
}
trap cleanup EXIT
gh run download "$run_id" --repo "$SOURCE_REPOSITORY" --name "$ARTIFACT_NAME" --dir "$download_root" || fail 'unable to download the selected workflow artifact'
mapfile -t entries < <(find "$download_root" -mindepth 1 -maxdepth 1 -printf '%f\n' | LC_ALL=C sort)
[[ ${#entries[@]} -eq 2 && ${entries[0]} == artifact.tar && ${entries[1]} == package.json ]] || fail 'service artifact must contain only artifact.tar and package.json'
archive="$download_root/artifact.tar"
package_metadata="$download_root/package.json"
[[ -f $archive && ! -L $archive && -s $archive && -f $package_metadata && ! -L $package_metadata && -s $package_metadata ]] || fail 'service artifact members must be non-empty regular files'
(( $(stat -c %s -- "$archive") <= 1073741824 && $(stat -c %s -- "$package_metadata") <= 1048576 )) || fail 'service artifact exceeds the promoter size limit'
artifact_sha=$(sha256sum -- "$archive")
artifact_sha=${artifact_sha%% *}
jq -e --arg repo "$SOURCE_REPOSITORY" --arg sha "$source_sha" --arg run "$run_id" --arg attempt "$run_attempt" --arg digest "$artifact_sha" \
  '.schemaVersion == 1 and .sourceRepository == $repo and .sourceSha == $sha and .workflowRunId == $run and .workflowRunAttempt == $attempt and .artifactSha256 == $digest' \
  "$package_metadata" >/dev/null || fail 'service package does not match the selected source, workflow run, and SHA-256'

install -d -m 0700 -- "$workflow_stage"
install -m 0600 -- "$archive" "$workflow_stage/artifact.tar"
jq -n --arg repo "$SOURCE_REPOSITORY" --arg sha "$source_sha" --arg run "$run_id" --arg attempt "$run_attempt" --arg digest "$artifact_sha" --arg time "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  '{schemaVersion:1,sourceRepository:$repo,sourceSha:$sha,workflowRunId:$run,workflowRunAttempt:$attempt,packageRunAttempt:$attempt,artifactSha256:$digest,deployedAtUtc:$time}' \
  >"$workflow_stage/deployment.json"
chmod 0600 -- "$workflow_stage/deployment.json"
latest_branch_json=$(gh api -H 'Accept: application/vnd.github+json' "repos/${SOURCE_REPOSITORY}/branches/${SOURCE_BRANCH}") || fail 'unable to recheck the source branch before promotion'
[[ $(jq -er '.commit.sha' <<<"$latest_branch_json") == "$source_sha" ]] || fail 'source branch advanced before promotion; a later pull will use the new commit'
unset GH_TOKEN
sudo -- /usr/local/sbin/powerforge-service-deploy --service "$service"
printf 'service=%s source=%s run=%s attempt=%s promoted\n' "$service" "$source_sha" "$run_id" "$run_attempt"
