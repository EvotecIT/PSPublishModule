#!/usr/bin/env bash
set -Eeuo pipefail

# The root-owned site configuration supplies paths and repository identity. The
# build and all network reads run as the site's unprivileged deployment account.
umask 077
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export PATH
for inherited_git_variable in ${!GIT_@}; do
  unset "$inherited_git_variable"
done
GIT_NO_REPLACE_OBJECTS=1
GIT_CONFIG_NOSYSTEM=1
GIT_CONFIG_GLOBAL=/dev/null
export GIT_NO_REPLACE_OBJECTS GIT_CONFIG_NOSYSTEM GIT_CONFIG_GLOBAL

fail() { printf 'powerforge-site-pull-deploy: %s\n' "$*" >&2; exit 1; }
[[ $# -ge 1 && $# -le 2 && $1 =~ ^[a-z0-9][a-z0-9.-]{0,62}$ ]] || fail 'usage: powerforge-site-pull-deploy <site> [--build-only]'
[[ $# -eq 1 || $2 == --build-only ]] || fail 'only --build-only is supported as a second argument'
[[ $(id -u) -ne 0 ]] || fail 'run as the dedicated site build account, not root'
site=$1
build_only=0
[[ $# -eq 1 ]] || build_only=1
config=/etc/powerforge/site-pull/${site}.env
for config_dir in /etc /etc/powerforge /etc/powerforge/site-pull; do
  [[ -d $config_dir && ! -L $config_dir && $(stat -c %u "$config_dir") -eq 0 ]] || fail "configuration directory is not root-controlled: $config_dir"
  config_dir_mode=$(stat -c %a "$config_dir")
  (( (8#$config_dir_mode & 0022) == 0 )) || fail "configuration directory is writable by another account: $config_dir"
done
[[ -f $config && ! -L $config ]] || fail "missing regular site configuration: $config"
[[ $(stat -c %u "$config") -eq 0 ]] || fail 'site configuration must be root-owned'
config_mode=$(stat -c %a "$config")
(( (8#$config_mode & 0022) == 0 )) || fail 'site configuration must not be group or world writable'
# shellcheck disable=SC1090
source "$config"

: "${SOURCE_REPOSITORY:?}"
: "${SOURCE_BRANCH:?}"
: "${SOURCE_NAME:?}"
: "${WEBSITE_DIRECTORY:?}"
: "${PIPELINE_CONFIG:?}"
: "${ENGINE_REPOSITORY_PATH:?}"
: "${ENGINE_SHA:?}"
: "${WORK_ROOT:?}"
: "${CURRENT_LINK:?}"
: "${PUBLIC_URL:?}"
: "${SMOKE_PATHS:=/}"
: "${DOTNET_ROOT:=/usr/lib/dotnet}"
[[ $SOURCE_NAME =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail 'invalid source repository name'
if [[ -n ${SOURCE_REPOSITORY_PATH:-} ]]; then
  [[ $SOURCE_REPOSITORY == "git@github.com:${SOURCE_NAME}.git" ]] || fail 'private source repository name and SSH URL disagree'
else
  [[ $SOURCE_REPOSITORY == "https://github.com/${SOURCE_NAME}.git" ]] || fail 'public source repository name and HTTPS URL disagree'
fi
[[ $SOURCE_BRANCH =~ ^[A-Za-z0-9._/-]+$ && $SOURCE_BRANCH != *..* && $SOURCE_BRANCH != /* ]] || fail 'invalid source branch'
[[ $WEBSITE_DIRECTORY =~ ^[A-Za-z0-9._/-]+$ && $WEBSITE_DIRECTORY != *..* && $WEBSITE_DIRECTORY != /* ]] || fail 'invalid website directory'
[[ $PIPELINE_CONFIG =~ ^[A-Za-z0-9._/-]+\.json$ && $PIPELINE_CONFIG != *..* && $PIPELINE_CONFIG != /* ]] || fail 'invalid pipeline config'
[[ $ENGINE_SHA =~ ^[0-9a-f]{40}$ ]] || fail 'engine revision must be an exact SHA'
[[ $WORK_ROOT == /* && $WORK_ROOT != / && $WORK_ROOT != /tmp && $WORK_ROOT != /var/tmp ]] || fail 'invalid work root'
[[ $CURRENT_LINK == /* && $CURRENT_LINK != / ]] || fail 'invalid current release link'
[[ $PUBLIC_URL =~ ^https://[A-Za-z0-9.-]+$ ]] || fail 'public URL must be an HTTPS origin'
[[ $DOTNET_ROOT == /* && -x $DOTNET_ROOT/dotnet ]] || fail 'DOTNET_ROOT must contain an executable dotnet host'
PATH=$DOTNET_ROOT:$PATH
export DOTNET_ROOT PATH
[[ -d $WORK_ROOT && ! -L $WORK_ROOT && $(stat -c %u "$WORK_ROOT") -eq $(id -u) ]] || fail 'work root must be a real directory owned by the build account'
[[ $(stat -c %a "$WORK_ROOT") == 700 ]] || fail 'work root must have mode 0700'
cd "$WORK_ROOT"
site_pull_config_sha=$(sha256sum "$config" | awk '{print $1}')

assert_root_checkout() {
  local checkout=$1 part mode unsafe
  [[ $checkout == /* && -d $checkout/.git && ! -L $checkout/.git ]] || fail "invalid root-owned checkout: $checkout"
  part=$checkout/.git
  while [[ $part != / ]]; do
    [[ -d $part && ! -L $part && $(stat -c %u "$part") -eq 0 ]] || fail "checkout path must be root-owned and free of symlinks: $part"
    mode=$(stat -c %a "$part")
    (( (8#$mode & 0022) == 0 )) || fail "checkout path must not be group or world writable: $part"
    part=${part%/*}
    [[ -n $part ]] || part=/
  done
  unsafe=$(find "$checkout/.git" -xdev \( ! -user root -o -perm /022 -o -type l \) -print -quit)
  [[ -z $unsafe ]] || fail "Git metadata is not exclusively root-controlled: $unsafe"
}
assert_root_checkout "$ENGINE_REPOSITORY_PATH"

for required in git dotnet tar jq curl sha256sum flock mktemp sudo; do
  command -v "$required" >/dev/null || fail "missing command: $required"
done
exec 9>"$WORK_ROOT/.deploy.lock"
flock -n 9 || fail 'another pull deployment is running'

git -c "safe.directory=$ENGINE_REPOSITORY_PATH" -C "$ENGINE_REPOSITORY_PATH" \
  cat-file -e "${ENGINE_SHA}^{commit}" || fail 'pinned engine revision is absent locally'
if [[ -n ${SOURCE_REPOSITORY_PATH:-} ]]; then
  source_snapshot=/var/lib/powerforge/site-sources/$site
  [[ -d $source_snapshot && ! -L $source_snapshot && $(stat -c %u "$source_snapshot") -eq 0 ]] || fail 'missing root-owned private source snapshot'
  [[ $(stat -c '%g %a' "$source_snapshot") == "$(id -g) 750" ]] || fail 'private source snapshot must be mode 0750 and shared only with the build account'
  [[ -f $source_snapshot/source.tar && ! -L $source_snapshot/source.tar && $(stat -c %u "$source_snapshot/source.tar") -eq 0 ]] || fail 'missing root-owned private source archive'
  [[ -f $source_snapshot/source.sha && ! -L $source_snapshot/source.sha && $(stat -c %u "$source_snapshot/source.sha") -eq 0 ]] || fail 'missing root-owned private source revision'
  [[ $(stat -c '%g %a' "$source_snapshot/source.tar") == "$(id -g) 640" ]] || fail 'private source archive has unexpected access mode'
  [[ $(stat -c '%g %a' "$source_snapshot/source.sha") == "$(id -g) 640" ]] || fail 'private source revision has unexpected access mode'
  remote_sha=$(<"$source_snapshot/source.sha")
else
  remote_line=$(git -C / -c protocol.file.allow=never ls-remote --exit-code "$SOURCE_REPOSITORY" "refs/heads/$SOURCE_BRANCH")
  remote_sha=${remote_line%%[[:space:]]*}
fi
[[ $remote_sha =~ ^[0-9a-f]{40}$ ]] || fail 'source branch did not resolve to an exact revision'
if [[ $build_only -eq 0 && -L $CURRENT_LINK && -f $CURRENT_LINK/_powerforge/deployment.json ]]; then
  if jq -e --arg source "$remote_sha" --arg engine "$ENGINE_SHA" --arg config "$site_pull_config_sha" \
    '.sourceSha == $source and .engineSha == $engine and .sitePullConfigSha256 == $config and .deploymentOrigin == "host-pull"' \
    "$CURRENT_LINK/_powerforge/deployment.json" >/dev/null; then
    printf 'already-current source=%s engine=%s\n' "$remote_sha" "$ENGINE_SHA"
    exit 0
  fi
fi
run_root=$(mktemp -d "$WORK_ROOT/.run.XXXXXXXX")
stage=''
pending_release=''
finished=0
cleanup() {
  if [[ $finished -eq 0 && -n $pending_release ]]; then
    sudo -n /usr/local/sbin/powerforge-site-deploy --site "$site" --rollback --release-id "$pending_release" 9>&- || true
  fi
  if [[ -n $stage && -d $stage && $stage == /tmp/powerforge-[0-9]*-1-$site ]]; then
    rm -rf -- "$stage"
  fi
  if [[ -d $run_root && $run_root == "$WORK_ROOT/.run."* ]]; then
    rm -rf -- "$run_root"
  fi
}
trap cleanup EXIT

if [[ -n ${SOURCE_REPOSITORY_PATH:-} ]]; then
  mkdir "$run_root/source"
  cp -- "$source_snapshot/source.tar" "$run_root/source.tar"
  archive_sha=$(git get-tar-commit-id <"$run_root/source.tar")
  [[ $archive_sha == "$remote_sha" ]] || fail 'private source archive and revision disagree'
  tar -xf "$run_root/source.tar" -C "$run_root/source"
  source_sha=$remote_sha
else
  git -C / -c protocol.file.allow=never clone --depth 1 --single-branch --branch "$SOURCE_BRANCH" \
    "$SOURCE_REPOSITORY" "$run_root/source"
  source_sha=$(git -C "$run_root/source" rev-parse HEAD)
fi
[[ $source_sha =~ ^[0-9a-f]{40}$ ]] || fail 'invalid fetched source revision'
[[ $source_sha == "$remote_sha" ]] || fail 'source branch changed during clone; retry on the next timer run'
website="$run_root/source/$WEBSITE_DIRECTORY"
[[ -f $website/$PIPELINE_CONFIG ]] || fail 'pipeline config is missing from fetched source'
if [[ -f $website/.powerforge/engine-lock.json ]]; then
  locked_engine=$(jq -er '.ref | select(type == "string")' "$website/.powerforge/engine-lock.json")
  [[ $locked_engine == "$ENGINE_SHA" ]] || fail 'configured engine revision disagrees with the website engine lock'
fi
printf 'source=%s engine=%s\n' "$source_sha" "$ENGINE_SHA"

# The engine archive comes from an exact commit in the host's trusted checkout.
# Building the archive avoids relying on a precompiled DLL with unknown provenance.
mkdir "$run_root/engine"
git -c "safe.directory=$ENGINE_REPOSITORY_PATH" -C "$ENGINE_REPOSITORY_PATH" \
  archive "$ENGINE_SHA" | tar -xf - -C "$run_root/engine"
export DOTNET_CLI_HOME="$WORK_ROOT/dotnet-home"
export NUGET_PACKAGES="$WORK_ROOT/nuget-packages"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"
(cd "$run_root/engine" && dotnet build PowerForge.Web.Cli/PowerForge.Web.Cli.csproj \
  --configuration Release --framework net10.0 --nologo \
  --configfile .github/nuget.public.config 9>&-)
cli="$run_root/engine/PowerForge.Web.Cli/bin/Release/net10.0/PowerForge.Web.Cli.dll"
[[ -s $cli ]] || fail 'pinned engine CLI build produced no executable'
(cd "$website" && dotnet "$cli" pipeline --config "$PIPELINE_CONFIG" --mode ci 9>&-)
site_output="$website/_site"
[[ -s $site_output/index.html ]] || fail 'website pipeline produced no index.html'

run_id=$(date -u +%s%N)
stage="/tmp/powerforge-${run_id}-1-${site}"
mkdir -m 0700 "$stage"
tar --dereference --hard-dereference --directory "$site_output" -cf "$stage/artifact.tar" \
  --exclude=.git --exclude=.github .
artifact_sha=$(sha256sum "$stage/artifact.tar" | awk '{print $1}')
jq -n --arg repository "$SOURCE_NAME" --arg source "$source_sha" \
  --arg engine "$ENGINE_SHA" --arg run "$run_id" --arg artifact "$artifact_sha" \
  --arg config "$site_pull_config_sha" \
  --arg time "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  '{schemaVersion:1,sourceRepository:$repository,sourceSha:$source,engineMode:"source",engineRepository:"EvotecIT/PSPublishModule",engineRef:$engine,engineSha:$engine,workflowRunId:$run,workflowRunAttempt:"1",artifactSha256:$artifact,deployedAtUtc:$time,deploymentOrigin:"host-pull",sitePullConfigSha256:$config}' \
  >"$stage/deployment.json"

if [[ $build_only -eq 1 ]]; then
  printf 'build-only source=%s engine=%s artifact=%s\n' "$source_sha" "$ENGINE_SHA" "$artifact_sha"
  finished=1
  exit 0
fi

promotion=$(sudo -n /usr/local/sbin/powerforge-site-deploy --site "$site" \
  --archive "$stage/artifact.tar" --metadata "$stage/deployment.json" --defer-public-verification 9>&-)
pending_release=$(sed -n 's/^POWERFORGE_RELEASE_ID=//p' <<<"$promotion" | tail -1)
[[ $pending_release =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$ ]] || fail 'promoter returned no valid release identity'

marker=$(curl -fsS --retry 3 --max-time 30 "${PUBLIC_URL}/_powerforge/deployment.json?powerforge-deploy=${run_id}-1")
jq -e --arg source "$source_sha" --arg artifact "$artifact_sha" --arg run "$run_id" \
  '.sourceSha == $source and .artifactSha256 == $artifact and .workflowRunId == $run and .workflowRunAttempt == "1"' \
  <<<"$marker" >/dev/null || fail 'public site does not serve the promoted release'
for smoke_path in $SMOKE_PATHS; do
  [[ $smoke_path == /* ]] || fail 'smoke path must start with /'
  separator='?'
  [[ $smoke_path != *\?* ]] || separator='&'
  curl -fsSL --max-redirs 5 --retry 3 --max-time 30 --output /dev/null \
    "${PUBLIC_URL}${smoke_path}${separator}powerforge-deploy=${run_id}-1"
done
sudo -n /usr/local/sbin/powerforge-site-deploy --site "$site" --finalize --release-id "$pending_release" 9>&-
finished=1
printf 'promoted=%s source=%s engine=%s\n' "$pending_release" "$source_sha" "$ENGINE_SHA"
