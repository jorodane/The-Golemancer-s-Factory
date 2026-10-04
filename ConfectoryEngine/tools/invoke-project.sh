#!/usr/bin/env bash
set -euo pipefail
engine_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
command=$1
shift
if [[ $# -eq 0 ]]; then
  echo 'Usage: build.sh <project folder or .packproject> [target]' >&2
  exit 2
fi
project_path=$(realpath -- "$1")
shift
dotnet_command=${CONFECTORY_DOTNET:-dotnet}
cd "$engine_root"
"$dotnet_command" build editor/Confectory.Tool/Confectory.Tool.csproj -c Release -p:EngineTargetFramework=net10.0 -p:UseSharedCompilation=false -m:1 --disable-build-servers --nologo -v:minimal
arguments=(editor/Confectory.Tool/bin/Release/net10.0/Confectory.Tool.dll "$command" --project "$project_path" --engine-root "$engine_root" --dotnet "$dotnet_command")
if [[ $# -gt 0 ]]; then arguments+=(--target "$1"); shift; fi
"$dotnet_command" "${arguments[@]}" "$@"
