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

required_paths=()
optional_paths=()
optional_mode="$ignore_failed_read"
while (($# > 0)); do
  if [[ "$1" == '--optional' ]]; then
    (( ignore_failed_read == 0 )) || die '--optional cannot follow --ignore-failed-read'
    (( optional_mode == 0 )) || die '--optional may be specified only once'
    optional_mode=1
    shift
    (($# > 0)) || die '--optional requires at least one capture path'
    continue
  fi
  path="$1"
  [[ "$path" =~ ^/[A-Za-z0-9._/-]+$ ]] || die "unsafe capture path: $path"
  [[ "$path" != *'//'* ]] || die "capture path contains an empty segment: $path"
  [[ ! "$path" =~ (^|/)\.{1,2}(/|$) ]] || die "capture path contains a traversal segment: $path"
  if (( optional_mode == 1 )); then
    optional_paths+=("$path")
  else
    required_paths+=("$path")
  fi
  shift
done

capture_paths=()
for path in "${required_paths[@]}"; do
  [[ -e "$path" || -L "$path" ]] || die "required capture path is missing: $path"
  capture_paths+=("$path")
done
for path in "${optional_paths[@]}"; do
  if [[ -e "$path" || -L "$path" ]]; then
    capture_paths+=("$path")
  fi
done

if ((${#capture_paths[@]} > 0)); then
  /usr/bin/tar -czf - -- "${capture_paths[@]}" | /usr/bin/age -r "$recipient" -o -
else
  /usr/bin/tar -czf - --files-from /dev/null | /usr/bin/age -r "$recipient" -o -
fi
