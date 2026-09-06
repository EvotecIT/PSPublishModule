#!/usr/bin/env bash
set -euo pipefail

apply="${APPLY:-false}"
confirmation="${CONFIRMATION:-}"
expected_pv="${EXPECTED_PV:-/dev/sda3}"
expected_runner_name="${EXPECTED_RUNNER_NAME:-}"
expected_disk_bytes="${EXPECTED_DISK_BYTES:-136365211648}"
expected_partition_bytes="${EXPECTED_PARTITION_BYTES:-133088411648}"
expected_initial_lv_bytes="${EXPECTED_INITIAL_LV_BYTES:-66542632960}"
expected_initial_fs_bytes="${EXPECTED_INITIAL_FS_BYTES:-65174941696}"
profile_tolerance_bytes="${PROFILE_TOLERANCE_BYTES:-16777216}"
minimum_initial_vg_free_bytes="${MINIMUM_INITIAL_VG_FREE_BYTES:-64424509440}"
maximum_initial_vg_free_bytes="${MAXIMUM_INITIAL_VG_FREE_BYTES:-73014444032}"
minimum_expanded_lv_bytes="${MINIMUM_EXPANDED_LV_BYTES:-130000000000}"
minimum_expanded_fs_bytes="${MINIMUM_EXPANDED_FS_BYTES:-128000000000}"
confirmation_phrase="EXPAND-ALL-LINUX-RUNNER-ROOTS"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

trim() {
  local value="$1"
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "$value"
}

to_integer_bytes() {
  awk -v value="$1" 'BEGIN { printf "%.0f", value }'
}

within_tolerance() {
  local actual="$1"
  local expected="$2"
  local tolerance="$3"
  (( actual >= expected - tolerance && actual <= expected + tolerance ))
}

[[ "$(uname -s)" == "Linux" ]] || fail "This operation supports Linux only."
[[ "$EUID" -eq 0 ]] || fail "Root privileges are required; current uid is $EUID."
[[ "$apply" == "true" || "$apply" == "false" ]] || fail "APPLY must be exactly 'true' or 'false'."
[[ -n "${RUNNER_NAME:-}" ]] || fail "RUNNER_NAME is required."
[[ -n "$expected_runner_name" ]] || fail "EXPECTED_RUNNER_NAME is required."
[[ "$RUNNER_NAME" == "$expected_runner_name" ]] || fail "Scheduled runner '$RUNNER_NAME' does not match matrix target '$expected_runner_name'."
case "$RUNNER_NAME" in
  github-runner-linux|github-runner-linux-01|github-runner-linux-02|github-runner-linux-03|github-runner-linux-04|github-runner-linux-05) ;;
  *) fail "Runner '$RUNNER_NAME' is not in the approved runner allowlist." ;;
esac

for numeric_value in \
  "$expected_disk_bytes" \
  "$expected_partition_bytes" \
  "$expected_initial_lv_bytes" \
  "$expected_initial_fs_bytes" \
  "$profile_tolerance_bytes" \
  "$minimum_initial_vg_free_bytes" \
  "$maximum_initial_vg_free_bytes" \
  "$minimum_expanded_lv_bytes" \
  "$minimum_expanded_fs_bytes"; do
  [[ "$numeric_value" =~ ^[0-9]+$ ]] || fail "Capacity profile values must be non-negative integers."
done

for command_name in awk df findmnt flock lsblk lvextend lvs mkdir pgrep pvs readlink resize2fs stat uname; do
  command -v "$command_name" >/dev/null 2>&1 || fail "Required command '$command_name' is unavailable."
done

lock_directory="/run/powerforge-linux-root-capacity"
if [[ ! -d "$lock_directory" ]]; then
  mkdir --mode=0700 "$lock_directory" 2>/dev/null || [[ -d "$lock_directory" ]] || fail "Unable to create the operation-lock directory."
fi
[[ "$(stat -Lc '%u:%g:%a' "$lock_directory")" == "0:0:700" ]] || fail "Operation-lock directory ownership or mode is unsafe."
exec 9>"$lock_directory/operation.lock"
flock -n 9 || fail "Another Linux root-capacity operation is already running on this host."

worker_count="$(pgrep -xc Runner.Worker || true)"
[[ "$worker_count" -eq 1 ]] || fail "Expected exactly this workflow's Runner.Worker process; found $worker_count."

root_source="$(trim "$(findmnt -n -o SOURCE /)")"
root_filesystem="$(trim "$(findmnt -n -o FSTYPE /)")"
root_major_minor="$(trim "$(findmnt -n -o MAJ:MIN /)")"
[[ -n "$root_source" && -n "$root_major_minor" ]] || fail "Unable to resolve the root filesystem device."
[[ "$root_filesystem" == "ext4" ]] || fail "Expected the verified ext4 root filesystem; found '$root_filesystem'."

matching_lvs=()
matching_vgs=()
matching_lv_names=()
matching_lv_sizes=()
while IFS='|' read -r raw_lv_path raw_vg_name raw_lv_name raw_lv_size; do
  lv_path="$(trim "$raw_lv_path")"
  vg_name="$(trim "$raw_vg_name")"
  lv_name="$(trim "$raw_lv_name")"
  lv_size="$(to_integer_bytes "$(trim "$raw_lv_size")")"
  [[ -n "$lv_path" && -e "$lv_path" ]] || continue
  candidate_major_minor="$(lsblk -dn -o MAJ:MIN "$lv_path" 2>/dev/null | awk 'NF { print $1; exit }')"
  if [[ "$candidate_major_minor" == "$root_major_minor" ]]; then
    matching_lvs+=("$lv_path")
    matching_vgs+=("$vg_name")
    matching_lv_names+=("$lv_name")
    matching_lv_sizes+=("$lv_size")
  fi
done < <(lvs --noheadings --separator '|' --units b --nosuffix -o lv_path,vg_name,lv_name,lv_size)

[[ "${#matching_lvs[@]}" -eq 1 ]] || fail "Expected exactly one LVM logical volume for root major:minor '$root_major_minor'; found ${#matching_lvs[@]}."
lv_path="${matching_lvs[0]}"
vg_name="${matching_vgs[0]}"
lv_name="${matching_lv_names[0]}"
lv_size_bytes="${matching_lv_sizes[0]}"

matching_pvs=()
matching_pv_sizes=()
while IFS='|' read -r raw_pv_name raw_vg_name raw_pv_size; do
  pv_name="$(trim "$raw_pv_name")"
  candidate_vg="$(trim "$raw_vg_name")"
  pv_size="$(to_integer_bytes "$(trim "$raw_pv_size")")"
  if [[ "$candidate_vg" == "$vg_name" ]]; then
    matching_pvs+=("$pv_name")
    matching_pv_sizes+=("$pv_size")
  fi
done < <(pvs --noheadings --separator '|' --units b --nosuffix -o pv_name,vg_name,pv_size)

[[ "${#matching_pvs[@]}" -eq 1 ]] || fail "Volume group '$vg_name' must contain exactly one physical volume; found ${#matching_pvs[@]}."
actual_pv="$(readlink -f "${matching_pvs[0]}")"
pv_size_bytes="${matching_pv_sizes[0]}"
expected_pv_resolved="$(readlink -f "$expected_pv")"
[[ -n "$expected_pv_resolved" && "$actual_pv" == "$expected_pv_resolved" ]] || fail "Root volume group '$vg_name' uses '$actual_pv', not expected '$expected_pv'."

pv_type="$(lsblk -dn -o TYPE "$actual_pv" | awk 'NF { print $1; exit }')"
pv_parent="$(lsblk -dn -o PKNAME "$actual_pv" | awk 'NF { print $1; exit }')"
[[ "$pv_type" == "part" ]] || fail "Expected '$actual_pv' to be a partition; found type '$pv_type'."
[[ "/dev/$pv_parent" == "/dev/sda" ]] || fail "Expected '$actual_pv' to belong to /dev/sda; found parent '/dev/$pv_parent'."

disk_bytes="$(lsblk -bdn -o SIZE /dev/sda | awk 'NF { print $1; exit }')"
partition_bytes="$(lsblk -bdn -o SIZE "$actual_pv" | awk 'NF { print $1; exit }')"
[[ "$disk_bytes" =~ ^[0-9]+$ && "$partition_bytes" =~ ^[0-9]+$ ]] || fail "Unable to read disk and partition sizes."
[[ "$disk_bytes" -eq "$expected_disk_bytes" ]] || fail "Disk size '$disk_bytes' does not match verified profile '$expected_disk_bytes'."
[[ "$partition_bytes" -eq "$expected_partition_bytes" ]] || fail "Partition size '$partition_bytes' does not match verified profile '$expected_partition_bytes'."
within_tolerance "$pv_size_bytes" "$partition_bytes" "$profile_tolerance_bytes" || fail "PV size '$pv_size_bytes' is outside the verified partition-size tolerance."

vg_free_raw="$(vgs --noheadings --units b --nosuffix -o vg_free "$vg_name" | awk 'NF { print $1; exit }')"
vg_free_bytes="$(to_integer_bytes "$vg_free_raw")"
[[ "$vg_free_bytes" =~ ^[0-9]+$ ]] || fail "Unable to read free bytes for volume group '$vg_name'."

root_before_bytes="$(df -B1 --output=size / | awk 'NR == 2 { print $1 }')"
[[ "$root_before_bytes" =~ ^[0-9]+$ ]] || fail "Unable to read the root filesystem size."

echo "Runner: $RUNNER_NAME"
echo "Root: source=$root_source filesystem=$root_filesystem major_minor=$root_major_minor"
echo "LVM: pv=$actual_pv vg=$vg_name lv=$lv_name lv_path=$lv_path"
echo "Capacity bytes: disk=$disk_bytes partition=$partition_bytes pv=$pv_size_bytes vg_free=$vg_free_bytes lv=$lv_size_bytes filesystem=$root_before_bytes"

state=""
if (( vg_free_bytes >= minimum_initial_vg_free_bytes && vg_free_bytes <= maximum_initial_vg_free_bytes )) &&
   within_tolerance "$lv_size_bytes" "$expected_initial_lv_bytes" "$profile_tolerance_bytes" &&
   within_tolerance "$root_before_bytes" "$expected_initial_fs_bytes" "$profile_tolerance_bytes"; then
  state="ready"
elif (( vg_free_bytes < 64 * 1024 * 1024 && lv_size_bytes >= minimum_expanded_lv_bytes )); then
  if (( root_before_bytes >= minimum_expanded_fs_bytes )); then
    state="filesystem-expanded"
  elif within_tolerance "$root_before_bytes" "$expected_initial_fs_bytes" "$profile_tolerance_bytes" ||
       (( root_before_bytes > expected_initial_fs_bytes && root_before_bytes < minimum_expanded_fs_bytes )); then
    state="filesystem-recovery"
  fi
fi

[[ -n "$state" ]] || fail "Capacity state is outside the verified initial, recovery, and completed profiles."

if [[ "$apply" == "false" ]]; then
  if [[ "$state" == "ready" ]]; then
    echo "Dry run: the LV can consume the verified free extents, then ext4 can grow to the LV boundary."
  elif [[ "$state" == "filesystem-recovery" ]]; then
    echo "Dry run: the LV is already expanded and ext4 can safely resume growth to the LV boundary."
  else
    echo "Dry run: the LV and ext4 filesystem are expanded; apply will idempotently verify ext4 against the LV boundary."
  fi
  exit 0
fi

[[ "$confirmation" == "$confirmation_phrase" ]] || fail "Apply requires confirmation phrase '$confirmation_phrase'."

if [[ "$state" == "ready" ]]; then
  lvextend --extents '+100%FREE' "$lv_path"
fi

resize2fs "$lv_path"

vg_free_after_raw="$(vgs --noheadings --units b --nosuffix -o vg_free "$vg_name" | awk 'NF { print $1; exit }')"
vg_free_after_bytes="$(to_integer_bytes "$vg_free_after_raw")"
lv_size_after_raw="$(lvs --noheadings --units b --nosuffix -o lv_size "$lv_path" | awk 'NF { print $1; exit }')"
lv_size_after_bytes="$(to_integer_bytes "$lv_size_after_raw")"
root_after_bytes="$(df -B1 --output=size / | awk 'NR == 2 { print $1 }')"
[[ "$vg_free_after_bytes" =~ ^[0-9]+$ && "$lv_size_after_bytes" =~ ^[0-9]+$ && "$root_after_bytes" =~ ^[0-9]+$ ]] || fail "Unable to verify capacity after expansion."
(( vg_free_after_bytes < 64 * 1024 * 1024 )) || fail "Volume group still has at least 64 MiB free after expansion."
(( lv_size_after_bytes >= minimum_expanded_lv_bytes )) || fail "Logical volume did not reach the verified expanded-size range."
(( root_after_bytes >= minimum_expanded_fs_bytes )) || fail "Root filesystem did not reach the verified expanded-size range."

echo "Expansion verified: lv_bytes=$lv_size_after_bytes filesystem_bytes_before=$root_before_bytes filesystem_bytes_after=$root_after_bytes vg_free_bytes_after=$vg_free_after_bytes"
df -h /
lsblk -o NAME,TYPE,FSTYPE,SIZE,MOUNTPOINTS /dev/sda
