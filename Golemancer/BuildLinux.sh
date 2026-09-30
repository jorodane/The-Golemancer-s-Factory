#!/usr/bin/env bash
set -euo pipefail
game_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd "$game_dir"
dotnet_command=${GOLEMANCER_DOTNET:-dotnet}
rid=${1:-linux-x64}
if (($#)); then shift; fi
case "$rid" in linux-x64|linux-arm64) ;; *) echo "Use linux-x64 or linux-arm64." >&2; exit 2;; esac
"$dotnet_command" msbuild tools/BuildPacks.proj -p:GolemancerTargetFramework=net10.0 -p:Configuration=Release -p:UseSharedCompilation=false -m:1 -v:minimal
"$dotnet_command" publish src/Golemancer.Linux/Golemancer.Linux.csproj -c Release -r "$rid" --self-contained true \
  -p:GolemancerTargetFramework=net10.0 -p:UseSharedCompilation=false -m:1 --disable-build-servers \
  -o "Builds/Linux/$rid" "$@"
echo "Ready: Builds/Linux/$rid/Golemancer.Linux. Start with ./StartLinux.sh."
