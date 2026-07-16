#!/usr/bin/env bash
set -Eeuo pipefail

die() {
  printf 'powerforge-server-encrypted-capture: %s\n' "$*" >&2
  exit 64
}

(( EUID == 0 )) || die 'must run as root'
[[ "${1:-}" == '--recipient' ]] || die 'expected --recipient'
recipient="${2:-}"
shift 2

ignore_failed_read=0
if [[ "${1:-}" == '--ignore-failed-read' ]]; then
  ignore_failed_read=1
  shift
fi

[[ "${1:-}" == '--' ]] || die 'expected -- before capture paths'
shift
(( $# > 0 )) || die 'at least one capture path is required'
[[ "$recipient" =~ ^age1[0-9a-z]+$ ]] || die 'recipient must be an age public recipient'

for path in "$@"; do
  [[ "$path" =~ ^/[A-Za-z0-9._/-]+$ ]] || die "unsafe capture path: $path"
  [[ "$path" != *'//'* ]] || die "capture path contains an empty segment: $path"
  [[ ! "$path" =~ (^|/)\.{1,2}(/|$) ]] || die "capture path contains a traversal segment: $path"
done

tar_args=(-czf -)
if (( ignore_failed_read == 1 )); then
  tar_args+=(--ignore-failed-read)
fi
tar_args+=(--)

/usr/bin/tar "${tar_args[@]}" "$@" | /usr/bin/age -r "$recipient" -o -
