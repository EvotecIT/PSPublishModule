#!/usr/bin/env bash
set -Eeuo pipefail

# Exercise the unchanged root/configuration boundaries in a private mount namespace.
# Only the network download and dotnet installer/inventory are fixture boundaries.
if [[ ${1:-} != --namespace ]]; then
  [[ $(id -u) -eq 0 ]] || { echo 'Run this fixture as root.' >&2; exit 1; }
  repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)
  helper=${1:-$repo_root/Deployment/Linux/powerforge-site-sdk-install.sh}
  fixture=$(mktemp -d /tmp/powerforge-site-sdk-fixture.XXXXXXXX)
  trap 'rm -rf -- "$fixture"' EXIT
  umask 022
  mkdir -p "$fixture"/{etc/powerforge/site-pull,opt/powerforge-dotnet,srv/engine,bin}
  cp /etc/passwd /etc/group "$fixture/etc/"
  git -C "$fixture/srv/engine" init -q
  printf '%s\n' '{"sdk":{"version":"10.0.303","rollForward":"disable","allowPrerelease":false}}' >"$fixture/srv/engine/global.json"
  git -C "$fixture/srv/engine" add global.json
  git -C "$fixture/srv/engine" -c user.name=Fixture -c user.email=fixture@example.invalid commit -qm 'Fixture SDK lock'
  git -C "$fixture/srv/engine" rev-parse HEAD >"$fixture/engine-sha"
  unshare --mount --propagation private bash "$0" --namespace "$fixture" "$helper"
  exit 0
fi

fixture=$2
helper=$3
umask 022
mount --bind "$fixture/etc" /etc
mount --bind "$fixture/opt" /opt
mount --bind "$fixture/srv" /srv
mount --bind "$fixture/bin" /usr/local/bin
export PATH=/usr/local/bin:/usr/bin:/bin
engine_sha=$(cat "$fixture/engine-sha")
root=/opt/powerforge-dotnet
config=/etc/powerforge/site-pull/fixture.env

cat >"/usr/local/bin/curl" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
printf 'download\n' >>/srv/install.log
while (($#)); do
  if [[ $1 == -o ]]; then cp /srv/installer.sh "$2"; exit 0; fi
  shift
done
exit 1
EOF
cat >"/srv/installer.sh" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
printf '%s\n' "$*" >>/srv/install.log
version= channel= runtime= skip=0
while (($#)); do
  case $1 in
    --version) version=$2; shift 2 ;;
    --channel) channel=$2; shift 2 ;;
    --runtime) runtime=$2; shift 2 ;;
    --install-dir) [[ $2 == /opt/powerforge-dotnet ]]; shift 2 ;;
    --no-path) shift ;;
    --skip-non-versioned-files) skip=1; shift ;;
    *) exit 2 ;;
  esac
done
[[ ! -f /srv/installer-lies ]] || exit 0
if [[ $runtime == dotnet ]]; then
  [[ $skip == 1 ]] || { echo 'Runtime install would overwrite SDK host.' >&2; exit 3; }
  version=${version:-$channel.99}
  printf 'Microsoft.NETCore.App %s [/opt/powerforge-dotnet/shared/Microsoft.NETCore.App]\n' "$version" >>/opt/powerforge-dotnet/runtimes
else
  printf '%s [/opt/powerforge-dotnet/sdk]\n' "$version" >>/opt/powerforge-dotnet/sdks
fi
EOF
cat >"$root/dotnet" <<'EOF'
#!/usr/bin/env bash
case ${1:-} in
  --list-runtimes) cat /opt/powerforge-dotnet/runtimes ;;
  --list-sdks) cat /opt/powerforge-dotnet/sdks ;;
  *) exit 2 ;;
esac
EOF
chmod 755 /usr/local/bin/curl "$root/dotnet"
host_hash=$(sha256sum "$root/dotnet")

declare_config() {
  printf 'ENGINE_REPOSITORY_PATH=/srv/engine\nENGINE_SHA=%s\nDOTNET_ROOT=%s\nDOTNET_RUNTIME_CHANNELS="%s"\nDOTNET_RUNTIME_VERSIONS="%s"\n' \
    "$engine_sha" "$root" "$1" "$2" >"$config"
}
reset_inventory() {
  printf '10.0.303 [/opt/powerforge-dotnet/sdk]\n' >"$root/sdks"
  printf 'Microsoft.NETCore.App 8.0.20 [/opt/powerforge-dotnet/shared/Microsoft.NETCore.App]\nMicrosoft.NETCore.App 10.0.11 [/opt/powerforge-dotnet/shared/Microsoft.NETCore.App]\n' >"$root/runtimes"
  : >/srv/install.log
}
run_helper() { bash "$helper" fixture >"$fixture/result.log" 2>&1; }
assert_exact() { grep -Fqx 'Microsoft.NETCore.App 10.0.12 [/opt/powerforge-dotnet/shared/Microsoft.NETCore.App]' "$root/runtimes"; }
assert_host() { [[ $(sha256sum "$root/dotnet") == "$host_hash" ]]; }

reset_inventory
declare_config '8.0' '10.0.12'
run_helper
assert_exact
grep -Fq -- '--version 10.0.12 --runtime dotnet' /srv/install.log
grep -Fq -- '--skip-non-versioned-files' /srv/install.log
grep -Fqx 'Microsoft.NETCore.App 10.0.11 [/opt/powerforge-dotnet/shared/Microsoft.NETCore.App]' "$root/runtimes"
assert_host
echo 'PASS: older runtime is retained and exact patch installed without replacing SDK host'

first_log=$(cat /srv/install.log)
run_helper
[[ $(cat /srv/install.log) == "$first_log" ]]
echo 'PASS: repeated run performs no download or installation'

reset_inventory
printf 'Microsoft.NETCore.App 10.0.13 [/opt/powerforge-dotnet/shared/Microsoft.NETCore.App]\n' >"$root/runtimes"
declare_config '' '10.0.12'
run_helper
assert_exact
echo 'PASS: newer patch does not satisfy an exact declaration'

reset_inventory
printf 'Microsoft.AspNetCore.App 10.0.12 [/opt/powerforge-dotnet/shared/Microsoft.AspNetCore.App]\nMicrosoft.NETCore.App 10.0.12 [/elsewhere/shared/Microsoft.NETCore.App]\n' >>"$root/runtimes"
run_helper
assert_exact
echo 'PASS: framework and installation root must match'

reset_inventory
declare_config '8.0 10.0' ''
run_helper
[[ ! -s /srv/install.log ]]
# Absence of the new option must also preserve existing channel-only configuration.
sed -i '/^DOTNET_RUNTIME_VERSIONS=/d' "$config"
run_helper
[[ ! -s /srv/install.log ]]
: >"$root/runtimes"
run_helper
grep -Fq -- '--channel 8.0 --runtime dotnet' /srv/install.log
grep -Fq -- '--channel 10.0 --runtime dotnet' /srv/install.log
assert_host
echo 'PASS: channel-only configurations retain no-op and missing-channel behavior'

reset_inventory
: >"$root/sdks"
declare_config '8.0' '10.0.12'
run_helper
grep -Fqx '10.0.303 [/opt/powerforge-dotnet/sdk]' "$root/sdks"
assert_exact
echo 'PASS: missing exact SDK and runtime are both installed'

for invalid in 9.0.1 10.0 10.0.12-preview 10.0.012 '../10.0.12'; do
  reset_inventory
  declare_config '' "$invalid"
  if run_helper; then echo "Accepted invalid exact runtime: $invalid" >&2; exit 1; fi
  grep -Fq 'unsupported exact .NET runtime version' "$fixture/result.log"
  [[ ! -s /srv/install.log ]]
done
echo 'PASS: invalid exact versions fail before network/install activity'

reset_inventory
declare_config '' '10.0.12'
touch /srv/installer-lies
if run_helper; then echo 'Accepted installer success without required inventory.' >&2; exit 1; fi
grep -Fq 'required .NET 10.0.12 runtime installation did not complete' "$fixture/result.log"
echo 'PASS: installer exit status alone cannot satisfy exact runtime requirement'

assert_host
echo 'All SDK/runtime installation fixture contracts passed.'
