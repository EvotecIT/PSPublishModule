#!/usr/bin/env bash
set -Eeuo pipefail

# Restore the exact SDK required by a site's pinned PowerForge engine.
umask 077
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export PATH
GIT_NO_REPLACE_OBJECTS=1
GIT_CONFIG_NOSYSTEM=1
GIT_CONFIG_GLOBAL=/dev/null
export GIT_NO_REPLACE_OBJECTS GIT_CONFIG_NOSYSTEM GIT_CONFIG_GLOBAL

fail() { printf 'powerforge-site-sdk-install: %s\n' "$*" >&2; exit 1; }
[[ $# -eq 1 && $1 =~ ^[a-z0-9][a-z0-9.-]{0,62}$ ]] || fail 'usage: powerforge-site-sdk-install <site>'
[[ $(id -u) -eq 0 ]] || fail 'run as root'
config=/etc/powerforge/site-pull/${1}.env
for config_dir in /etc /etc/powerforge /etc/powerforge/site-pull; do
  [[ -d $config_dir && ! -L $config_dir && $(stat -c %u "$config_dir") -eq 0 ]] || fail "configuration directory is not root-controlled: $config_dir"
  mode=$(stat -c %a "$config_dir")
  (( (8#$mode & 0022) == 0 )) || fail "configuration directory is writable by another account: $config_dir"
done
[[ -f $config && ! -L $config && $(stat -c %u "$config") -eq 0 ]] || fail 'missing root-owned site configuration'
mode=$(stat -c %a "$config")
(( (8#$mode & 0022) == 0 )) || fail 'site configuration must not be group or world writable'
# shellcheck disable=SC1090
source "$config"

: "${ENGINE_REPOSITORY_PATH:?}"
: "${ENGINE_SHA:?}"
: "${DOTNET_ROOT:?}"
[[ $ENGINE_REPOSITORY_PATH == /* && $ENGINE_REPOSITORY_PATH != / && -d $ENGINE_REPOSITORY_PATH/.git ]] || fail 'invalid local engine repository'
[[ $ENGINE_SHA =~ ^[0-9a-f]{40}$ ]] || fail 'engine revision must be an exact SHA'
[[ $DOTNET_ROOT == /opt/powerforge-dotnet ]] || fail 'SDK installation root must be /opt/powerforge-dotnet'
part=$ENGINE_REPOSITORY_PATH/.git
while [[ $part != / ]]; do
  [[ -d $part && ! -L $part && $(stat -c %u "$part") -eq 0 ]] || fail "engine checkout path must be root-owned and free of symlinks: $part"
  mode=$(stat -c %a "$part")
  (( (8#$mode & 0022) == 0 )) || fail "engine checkout path must not be group or world writable: $part"
  part=${part%/*}
  [[ -n $part ]] || part=/
done
unsafe=$(find "$ENGINE_REPOSITORY_PATH/.git" -xdev \( ! -user root -o -perm /022 -o -type l \) -print -quit)
[[ -z $unsafe ]] || fail "engine Git metadata is not exclusively root-controlled: $unsafe"
global_json=$(git -c "safe.directory=$ENGINE_REPOSITORY_PATH" -C "$ENGINE_REPOSITORY_PATH" show "${ENGINE_SHA}:global.json")
sdk_version=$(jq -er '.sdk.version | select(type == "string")' <<<"$global_json")
[[ $sdk_version =~ ^10\.0\.[0-9]+$ ]] || fail 'engine requires an unsupported SDK version'
jq -e '.sdk.rollForward == "disable" and .sdk.allowPrerelease == false' <<<"$global_json" >/dev/null || fail 'engine SDK lock is not exact'
runtime_channels=${DOTNET_RUNTIME_CHANNELS:-}
for channel in $runtime_channels; do
  [[ $channel == 8.0 || $channel == 10.0 ]] || fail "unsupported additional .NET runtime channel: $channel"
done
assert_sdk_tree() {
  local sdk_path mode unsafe
  for sdk_path in / /opt "$DOTNET_ROOT"; do
    [[ -d $sdk_path && ! -L $sdk_path && $(stat -c %u "$sdk_path") -eq 0 ]] || fail "SDK path is not root-controlled: $sdk_path"
    mode=$(stat -c %a "$sdk_path")
    (( (8#$mode & 0022) == 0 )) || fail "SDK path is writable by another account: $sdk_path"
  done
  unsafe=$(find "$DOTNET_ROOT" \( ! -user root -o -perm /022 -o -type l \) -print -quit)
  [[ -z $unsafe ]] || fail "SDK tree contains an unsafe path: $unsafe"
}
if [[ -e $DOTNET_ROOT || -L $DOTNET_ROOT ]]; then
  assert_sdk_tree
else
  install -d -o root -g root -m 755 "$DOTNET_ROOT"
  assert_sdk_tree
fi
has_runtime() {
  local required=$1 name version path
  [[ -x $DOTNET_ROOT/dotnet ]] || return 1
  while read -r name version path; do
    [[ $name == Microsoft.NETCore.App && $version == "$required".* && $path == "[$DOTNET_ROOT/shared/Microsoft.NETCore.App]" ]] && return 0
  done < <("$DOTNET_ROOT/dotnet" --list-runtimes)
  return 1
}

sdk_installed=0
if [[ -x $DOTNET_ROOT/dotnet ]] && "$DOTNET_ROOT/dotnet" --list-sdks | grep -Fqx "$sdk_version [$DOTNET_ROOT/sdk]"; then
  sdk_installed=1
fi
runtime_missing=0
for channel in $runtime_channels; do
  if ! has_runtime "$channel"; then
    runtime_missing=1
  fi
done
if [[ $sdk_installed -eq 1 && $runtime_missing -eq 0 ]]; then
  chmod -R a+rX "$DOTNET_ROOT"
  printf 'SDK %s and declared runtimes already installed\n' "$sdk_version"
  exit 0
fi

scratch=$(mktemp -d /tmp/powerforge-site-sdk.XXXXXXXX)
trap 'rm -rf -- "$scratch"' EXIT
curl --fail --silent --show-error --location --retry 3 --max-time 60 \
  https://dot.net/v1/dotnet-install.sh -o "$scratch/dotnet-install.sh"
if [[ $sdk_installed -eq 0 ]]; then
  (umask 022; bash "$scratch/dotnet-install.sh" --version "$sdk_version" --install-dir "$DOTNET_ROOT" --no-path)
  assert_sdk_tree
fi
"$DOTNET_ROOT/dotnet" --list-sdks | grep -Fqx "$sdk_version [$DOTNET_ROOT/sdk]" || fail 'exact SDK installation did not complete'
for channel in $runtime_channels; do
  if ! has_runtime "$channel"; then
    (umask 022; bash "$scratch/dotnet-install.sh" --channel "$channel" --runtime dotnet --install-dir "$DOTNET_ROOT" --no-path)
    assert_sdk_tree
  fi
  has_runtime "$channel" || fail "required .NET $channel runtime installation did not complete"
done
chmod -R a+rX "$DOTNET_ROOT"
printf 'SDK %s and declared runtimes installed\n' "$sdk_version"
