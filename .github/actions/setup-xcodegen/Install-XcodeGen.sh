#!/usr/bin/env bash
set -euo pipefail

[[ "$(uname -s)" == Darwin ]] || { echo 'XcodeGen setup requires macOS.' >&2; exit 1; }
: "${GITHUB_WORKSPACE:?GITHUB_WORKSPACE is required}"
: "${RUNNER_TEMP:?RUNNER_TEMP is required}"
: "${GITHUB_PATH:?GITHUB_PATH is required}"
: "${GITHUB_OUTPUT:?GITHUB_OUTPUT is required}"

version_file="$GITHUB_WORKSPACE/${1:-.xcodegen-version}"
[[ -f "$version_file" && ! -L "$version_file" ]] || {
  echo 'The checked-in XcodeGen version file must be a regular file.' >&2
  exit 1
}
version="$(cat "$version_file")"
version="${version%$'\r'}"

# Each supported release is reviewed here, including its official asset digest.
case "$version" in
  2.46.0) archive_sha256='4d9e34b62172d645eed6457cac13fc222569974098ef4ee9c3368bedf0196806' ;;
  *) echo 'Unsupported XcodeGen version; update the shared release catalogue.' >&2; exit 1 ;;
esac

[[ -d "$RUNNER_TEMP" && "$RUNNER_TEMP" != *$'\n'* ]] || {
  echo 'RUNNER_TEMP must be an existing directory without a newline.' >&2
  exit 1
}
install_root="$(mktemp -d "$RUNNER_TEMP/powerforge-xcodegen.XXXXXX")"
keep_installation=0
trap 'if [[ "$keep_installation" != 1 ]]; then rm -r -- "$install_root"; fi' EXIT

archive="$install_root/xcodegen.zip"
curl --fail --location --silent --show-error --proto '=https' --proto-redir '=https' --connect-timeout 15 --max-time 120 \
  "https://github.com/yonaskolb/XcodeGen/releases/download/$version/xcodegen.zip" \
  --output "$archive"
actual_sha256="$(shasum -a 256 "$archive")"
actual_sha256="${actual_sha256%% *}"
[[ "$actual_sha256" == "$archive_sha256" ]] || {
  echo 'XcodeGen archive checksum does not match the reviewed release.' >&2
  exit 1
}
ditto -x -k "$archive" "$install_root"
rm -- "$archive"

# Preserve bin and share together: XcodeGen uses its bundled setting presets.
tool_path="$install_root/xcodegen/bin/xcodegen"
[[ -f "$tool_path" && ! -L "$tool_path" ]] || {
  echo 'The verified archive does not contain the expected executable.' >&2
  exit 1
}
chmod +x "$tool_path"
[[ "$("$tool_path" version)" == "Version: $version" ]] || {
  echo 'The installed XcodeGen executable reports an unexpected version.' >&2
  exit 1
}
printf '%s\n' "$install_root/xcodegen/bin" >> "$GITHUB_PATH"
printf 'tool-path=%s\nversion=%s\n' "$tool_path" "$version" >> "$GITHUB_OUTPUT"
keep_installation=1
echo "XcodeGen $version is ready for this job."
