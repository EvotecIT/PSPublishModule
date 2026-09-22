#!/usr/bin/env bash
set -Eeuo pipefail

# The root-owned site configuration supplies paths and repository identity. The
# build and all network reads run as the site's unprivileged deployment account.
umask 077
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export PATH

fail() { printf 'powerforge-site-pull-deploy: %s\n' "$*" >&2; exit 1; }
[[ $# -ge 1 && $# -le 2 && $1 =~ ^[a-z0-9][a-z0-9.-]{0,62}$ ]] || fail 'usage: powerforge-site-pull-deploy <site> [--build-only]'
[[ $# -eq 1 || $2 == --build-only ]] || fail 'only --build-only is supported as a second argument'
[[ $(id -u) -ne 0 ]] || fail 'run as the dedicated site build account, not root'
site=$1
build_only=0
[[ $# -eq 1 ]] || build_only=1
config=/etc/powerforge/site-pull/${site}.env
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
[[ $SOURCE_REPOSITORY =~ ^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\.git$ ]] || fail 'source repository must be a GitHub HTTPS URL'
[[ $SOURCE_NAME =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail 'invalid source repository name'
[[ $SOURCE_REPOSITORY == "https://github.com/${SOURCE_NAME}.git" ]] || fail 'source repository name and URL disagree'
[[ $SOURCE_BRANCH =~ ^[A-Za-z0-9._/-]+$ && $SOURCE_BRANCH != *..* && $SOURCE_BRANCH != /* ]] || fail 'invalid source branch'
[[ $WEBSITE_DIRECTORY =~ ^[A-Za-z0-9._/-]+$ && $WEBSITE_DIRECTORY != *..* && $WEBSITE_DIRECTORY != /* ]] || fail 'invalid website directory'
[[ $PIPELINE_CONFIG =~ ^[A-Za-z0-9._/-]+\.json$ && $PIPELINE_CONFIG != *..* && $PIPELINE_CONFIG != /* ]] || fail 'invalid pipeline config'
[[ $ENGINE_REPOSITORY_PATH == /* && -d $ENGINE_REPOSITORY_PATH/.git ]] || fail 'invalid local engine repository'
[[ $ENGINE_SHA =~ ^[0-9a-f]{40}$ ]] || fail 'engine revision must be an exact SHA'
[[ $WORK_ROOT == /* && $WORK_ROOT != / && $WORK_ROOT != /tmp && $WORK_ROOT != /var/tmp ]] || fail 'invalid work root'
[[ $CURRENT_LINK == /* && $CURRENT_LINK != / ]] || fail 'invalid current release link'
[[ $PUBLIC_URL =~ ^https://[A-Za-z0-9.-]+$ ]] || fail 'public URL must be an HTTPS origin'
[[ -d $WORK_ROOT && ! -L $WORK_ROOT && $(stat -c %u "$WORK_ROOT") -eq $(id -u) ]] || fail 'work root must be a real directory owned by the build account'
[[ $(stat -c %a "$WORK_ROOT") == 700 ]] || fail 'work root must have mode 0700'

for required in git dotnet tar jq curl sha256sum flock mktemp sudo; do
  command -v "$required" >/dev/null || fail "missing command: $required"
done
exec 9>"$WORK_ROOT/.deploy.lock"
flock -n 9 || fail 'another pull deployment is running'

git -c "safe.directory=$ENGINE_REPOSITORY_PATH" -C "$ENGINE_REPOSITORY_PATH" \
  cat-file -e "${ENGINE_SHA}^{commit}" || fail 'pinned engine revision is absent locally'
remote_line=$(git -c protocol.file.allow=never ls-remote --exit-code "$SOURCE_REPOSITORY" "refs/heads/$SOURCE_BRANCH")
remote_sha=${remote_line%%[[:space:]]*}
[[ $remote_sha =~ ^[0-9a-f]{40}$ ]] || fail 'source branch did not resolve to an exact revision'
if [[ $build_only -eq 0 && -L $CURRENT_LINK && -f $CURRENT_LINK/_powerforge/deployment.json ]]; then
  if jq -e --arg source "$remote_sha" --arg engine "$ENGINE_SHA" \
    '.sourceSha == $source and .engineSha == $engine and .deploymentOrigin == "host-pull"' \
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
    sudo -n /usr/local/sbin/powerforge-site-deploy --site "$site" --rollback --release-id "$pending_release" || true
  fi
  if [[ -n $stage && -d $stage && $stage == /tmp/powerforge-[0-9]*-1-$site ]]; then
    rm -rf -- "$stage"
  fi
  if [[ -d $run_root && $run_root == "$WORK_ROOT/.run."* ]]; then
    rm -rf -- "$run_root"
  fi
}
trap cleanup EXIT

git -c protocol.file.allow=never clone --depth 1 --single-branch --branch "$SOURCE_BRANCH" \
  "$SOURCE_REPOSITORY" "$run_root/source"
source_sha=$(git -C "$run_root/source" rev-parse HEAD)
[[ $source_sha =~ ^[0-9a-f]{40}$ ]] || fail 'invalid fetched source revision'
[[ $source_sha == "$remote_sha" ]] || fail 'source branch changed during clone; retry on the next timer run'
printf 'source=%s engine=%s\n' "$source_sha" "$ENGINE_SHA"

# The engine archive comes from an exact commit in the host's trusted checkout.
# Building the archive avoids relying on a precompiled DLL with unknown provenance.
mkdir "$run_root/engine"
git -c "safe.directory=$ENGINE_REPOSITORY_PATH" -C "$ENGINE_REPOSITORY_PATH" \
  archive "$ENGINE_SHA" | tar -xf - -C "$run_root/engine"
export DOTNET_CLI_HOME="$WORK_ROOT/dotnet-home"
export NUGET_PACKAGES="$WORK_ROOT/nuget-packages"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"
dotnet build "$run_root/engine/PowerForge.Web.Cli/PowerForge.Web.Cli.csproj" \
  --configuration Release --framework net10.0 --nologo \
  --configfile "$run_root/engine/.github/nuget.public.config"
cli="$run_root/engine/PowerForge.Web.Cli/bin/Release/net10.0/PowerForge.Web.Cli.dll"
[[ -s $cli ]] || fail 'pinned engine CLI build produced no executable'
website="$run_root/source/$WEBSITE_DIRECTORY"
[[ -f $website/$PIPELINE_CONFIG ]] || fail 'pipeline config is missing from fetched source'
(cd "$website" && dotnet "$cli" pipeline --config "$PIPELINE_CONFIG" --mode ci)
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
  --arg time "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  '{schemaVersion:1,sourceRepository:$repository,sourceSha:$source,engineMode:"source",engineRepository:"EvotecIT/PSPublishModule",engineRef:$engine,engineSha:$engine,workflowRunId:$run,workflowRunAttempt:"1",artifactSha256:$artifact,deployedAtUtc:$time,deploymentOrigin:"host-pull"}' \
  >"$stage/deployment.json"

if [[ $build_only -eq 1 ]]; then
  printf 'build-only source=%s engine=%s artifact=%s\n' "$source_sha" "$ENGINE_SHA" "$artifact_sha"
  finished=1
  exit 0
fi

promotion=$(sudo -n /usr/local/sbin/powerforge-site-deploy --site "$site" \
  --archive "$stage/artifact.tar" --metadata "$stage/deployment.json" --defer-public-verification)
pending_release=$(sed -n 's/^POWERFORGE_RELEASE_ID=//p' <<<"$promotion" | tail -1)
[[ $pending_release =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$ ]] || fail 'promoter returned no valid release identity'

marker=$(curl -fsS --retry 3 --max-time 30 "${PUBLIC_URL}/_powerforge/deployment.json?powerforge-deploy=${run_id}-1")
jq -e --arg source "$source_sha" --arg artifact "$artifact_sha" --arg run "$run_id" \
  '.sourceSha == $source and .artifactSha256 == $artifact and .workflowRunId == $run and .workflowRunAttempt == "1"' \
  <<<"$marker" >/dev/null || fail 'public site does not serve the promoted release'
for smoke_path in $SMOKE_PATHS; do
  [[ $smoke_path == /* ]] || fail 'smoke path must start with /'
  curl -fsS --retry 3 --max-time 30 --output /dev/null "${PUBLIC_URL}${smoke_path}"
done
sudo -n /usr/local/sbin/powerforge-site-deploy --site "$site" --finalize --release-id "$pending_release"
finished=1
printf 'promoted=%s source=%s engine=%s\n' "$pending_release" "$source_sha" "$ENGINE_SHA"
