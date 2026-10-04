#!/usr/bin/env bash
set -euo pipefail
engine_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
rid=${1:-linux-x64}
case "$rid" in linux-x64|linux-arm64) ;; *) echo 'Usage: BuildEditorLinux.sh [linux-x64|linux-arm64]' >&2; exit 2;; esac
dotnet_command=${CONFECTORY_DOTNET:-dotnet}
output="$engine_root/editor/Builds/Linux/$rid"
cd "$engine_root"
common=(-c Release -p:EngineTargetFramework=net10.0 -p:UseSharedCompilation=false -m:1 --disable-build-servers --nologo)
"$dotnet_command" build editor/Confectory.Tool/Confectory.Tool.csproj "${common[@]}"
"$dotnet_command" build editor/Packs/CoreTools/Confectory.Editor.CoreTools.csproj "${common[@]}"
"$dotnet_command" publish editor/Confectory.Editor.Linux/Confectory.Editor.Linux.csproj "${common[@]}" -r "$rid" --self-contained true -o "$output"
"$dotnet_command" editor/Confectory.Tool/bin/Release/net10.0/Confectory.Tool.dll bundle-engine --recipe "$engine_root/editor/engine.xml" --output "$output/Engine"
cat > "$output/start.sh" <<'LAUNCH'
#!/usr/bin/env bash
set -euo pipefail
app_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
exec "$app_root/Confectory.Editor.Linux" "$@"
LAUNCH
chmod +x "$output/start.sh"
echo "LINUX_EDITOR_READY $output"
