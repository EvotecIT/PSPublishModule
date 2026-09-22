#!/usr/bin/env bash
set -Eeuo pipefail

# Fetch one private, read-only GitHub branch into a root-owned checkout. A
# separate unprivileged service builds an archive of the fetched commit.
umask 077
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export PATH
GIT_NO_REPLACE_OBJECTS=1
GIT_CONFIG_NOSYSTEM=1
GIT_CONFIG_GLOBAL=/dev/null
export GIT_NO_REPLACE_OBJECTS GIT_CONFIG_NOSYSTEM GIT_CONFIG_GLOBAL

fail() { printf 'powerforge-site-source-fetch: %s\n' "$*" >&2; exit 1; }
[[ $# -eq 1 && $1 =~ ^[a-z0-9][a-z0-9.-]{0,62}$ ]] || fail 'usage: powerforge-site-source-fetch <site>'
[[ $(id -u) -eq 0 ]] || fail 'run as root'
site=$1
config=/etc/powerforge/site-pull/${site}.env
for config_dir in /etc /etc/powerforge /etc/powerforge/site-pull /etc/powerforge/repository-ssh; do
  [[ -d $config_dir && ! -L $config_dir && $(stat -c %u "$config_dir") -eq 0 ]] || fail "configuration directory is not root-controlled: $config_dir"
  mode=$(stat -c %a "$config_dir")
  (( (8#$mode & 0022) == 0 )) || fail "configuration directory is writable by another account: $config_dir"
done
[[ -f $config && ! -L $config && $(stat -c %u "$config") -eq 0 ]] || fail 'missing root-owned site configuration'
mode=$(stat -c %a "$config")
(( (8#$mode & 0022) == 0 )) || fail 'site configuration must not be group or world writable'
# shellcheck disable=SC1090
source "$config"

: "${SOURCE_NAME:?}"
: "${SOURCE_BRANCH:?}"
: "${SOURCE_REPOSITORY:?}"
: "${SOURCE_REPOSITORY_PATH:?}"
: "${SOURCE_SSH_IDENTITY_FILE:?}"
: "${SOURCE_SSH_KNOWN_HOSTS_FILE:?}"
: "${WORK_ROOT:?}"
[[ $SOURCE_NAME =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail 'invalid source repository name'
[[ $SOURCE_REPOSITORY == "git@github.com:${SOURCE_NAME}.git" ]] || fail 'source repository name and SSH URL disagree'
[[ $SOURCE_BRANCH =~ ^[A-Za-z0-9._/-]+$ && $SOURCE_BRANCH != *..* && $SOURCE_BRANCH != /* ]] || fail 'invalid source branch'
[[ $SOURCE_REPOSITORY_PATH == /srv/evotec/* && -d $SOURCE_REPOSITORY_PATH/.git && ! -L $SOURCE_REPOSITORY_PATH ]] || fail 'invalid root-owned source checkout'
[[ $SOURCE_SSH_IDENTITY_FILE =~ ^/etc/powerforge/repository-ssh/[A-Za-z0-9_.-]+$ ]] || fail 'invalid SSH identity path'
[[ $SOURCE_SSH_KNOWN_HOSTS_FILE =~ ^/etc/powerforge/repository-ssh/[A-Za-z0-9_.-]+$ ]] || fail 'invalid known-hosts path'
for file in "$SOURCE_SSH_IDENTITY_FILE" "$SOURCE_SSH_KNOWN_HOSTS_FILE"; do
  [[ -f $file && ! -L $file && $(stat -c %u "$file") -eq 0 ]] || fail "missing root-owned SSH file: $file"
  mode=$(stat -c %a "$file")
  (( (8#$mode & 0022) == 0 )) || fail "SSH trust file is writable by another account: $file"
done
[[ $(stat -c %a "$SOURCE_SSH_IDENTITY_FILE") == 600 ]] || fail 'SSH identity must have mode 0600'
part=$SOURCE_REPOSITORY_PATH/.git
while [[ $part != / ]]; do
  [[ -d $part && ! -L $part && $(stat -c %u "$part") -eq 0 ]] || fail "source checkout path must be root-owned and free of symlinks: $part"
  mode=$(stat -c %a "$part")
  (( (8#$mode & 0022) == 0 )) || fail "source checkout path must not be group or world writable: $part"
  part=${part%/*}
  [[ -n $part ]] || part=/
done
unsafe=$(find "$SOURCE_REPOSITORY_PATH/.git" -xdev \( ! -user root -o -perm /022 -o -type l \) -print -quit)
[[ -z $unsafe ]] || fail "source Git metadata is not exclusively root-controlled: $unsafe"

ssh_command="ssh -F /dev/null -o BatchMode=yes -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes -o UserKnownHostsFile=$SOURCE_SSH_KNOWN_HOSTS_FILE -i $SOURCE_SSH_IDENTITY_FILE"
if git -c "safe.directory=$SOURCE_REPOSITORY_PATH" -C "$SOURCE_REPOSITORY_PATH" config --get-regexp '^url\..*\.insteadof$' >/dev/null; then
  fail 'source checkout must not rewrite Git repository URLs'
fi
GIT_SSH_COMMAND=$ssh_command git -c "safe.directory=$SOURCE_REPOSITORY_PATH" \
  -c protocol.file.allow=never -C "$SOURCE_REPOSITORY_PATH" fetch --no-tags \
  "$SOURCE_REPOSITORY" "+refs/heads/$SOURCE_BRANCH:refs/remotes/powerforge/$SOURCE_BRANCH"
source_sha=$(git -c "safe.directory=$SOURCE_REPOSITORY_PATH" -C "$SOURCE_REPOSITORY_PATH" \
  rev-parse --verify "refs/remotes/powerforge/${SOURCE_BRANCH}^{commit}")
[[ $source_sha =~ ^[0-9a-f]{40}$ ]] || fail 'fetched branch did not resolve to an exact revision'
[[ -d $WORK_ROOT && ! -L $WORK_ROOT ]] || fail 'missing build account work directory'
build_gid=$(stat -c %g "$WORK_ROOT")
[[ $(stat -c %u "$WORK_ROOT") -ne 0 && $(stat -c %a "$WORK_ROOT") == 700 ]] || fail 'build account work directory has unexpected ownership or mode'
snapshot=/var/lib/powerforge/site-sources/$site
install -d -o root -g root -m 755 /var/lib/powerforge/site-sources
install -d -o root -g "$build_gid" -m 750 "$snapshot"
if [[ -f $snapshot/source.tar && -f $snapshot/source.sha && ! -L $snapshot/source.tar && ! -L $snapshot/source.sha \
      && $(stat -c '%u %g %a' "$snapshot/source.tar") == "0 $build_gid 640" \
      && $(stat -c '%u %g %a' "$snapshot/source.sha") == "0 $build_gid 640" \
      && $(<"$snapshot/source.sha") == "$source_sha" ]]; then
  existing_sha=$(git get-tar-commit-id <"$snapshot/source.tar" || true)
  if [[ $existing_sha == "$source_sha" ]]; then
    printf 'source=%s already archived\n' "$source_sha"
    exit 0
  fi
fi
archive_tmp=$(mktemp "$snapshot/.source.XXXXXXXX.tar")
sha_tmp=$(mktemp "$snapshot/.source.XXXXXXXX.sha")
trap 'rm -f -- "$archive_tmp" "$sha_tmp"' EXIT
git -c "safe.directory=$SOURCE_REPOSITORY_PATH" -C "$SOURCE_REPOSITORY_PATH" \
  archive "$source_sha" >"$archive_tmp"
printf '%s\n' "$source_sha" >"$sha_tmp"
chown "root:$build_gid" "$archive_tmp" "$sha_tmp"
chmod 640 "$archive_tmp" "$sha_tmp"
mv -f -- "$archive_tmp" "$snapshot/source.tar"
mv -f -- "$sha_tmp" "$snapshot/source.sha"
printf 'source=%s\n' "$source_sha"
