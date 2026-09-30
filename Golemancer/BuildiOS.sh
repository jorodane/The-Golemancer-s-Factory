#!/usr/bin/env bash
set -euo pipefail
game_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd "$game_dir"
if [[ $(uname -s) != Darwin ]]; then echo "The iOS app build requires macOS, Xcode and the .NET iOS workload. See docs/IOS.md." >&2; exit 2; fi
xcode-select -p >/dev/null
dotnet_command=${GOLEMANCER_DOTNET:-dotnet}
target=${1:-simulator}
if (($#)); then shift; fi
case "$target" in
  simulator) if [[ $(uname -m) == arm64 ]]; then rid=iossimulator-arm64; else rid=iossimulator-x64; fi; args=(build);;
  device) rid=ios-arm64; args=(publish -p:ArchiveOnBuild=true -p:BuildIpa=true);;
  *) echo "Use simulator or device." >&2; exit 2;;
esac
"$dotnet_command" msbuild tools/BuildPacks.proj -p:GolemancerTargetFramework=net10.0 -p:Configuration=Release -p:UseSharedCompilation=false -m:1 -v:minimal
"$dotnet_command" "${args[@]}" src/Golemancer.iOS/Golemancer.iOS.csproj -c Release \
  -p:RuntimeIdentifier="$rid" -p:GolemancerTargetFramework=net10.0 -p:UseSharedCompilation=false -m:1 --disable-build-servers "$@"
echo "iOS build completed for $rid. Run the device/simulator smoke gate in docs/IOS.md."
