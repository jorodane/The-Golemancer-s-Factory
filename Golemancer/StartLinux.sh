#!/usr/bin/env bash
set -euo pipefail
game_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
case $(uname -m) in x86_64) rid=linux-x64;; aarch64|arm64) rid=linux-arm64;; *) echo "Unsupported Linux CPU." >&2; exit 2;; esac
runtime="$game_dir/Builds/Linux/$rid/Golemancer.Linux"
if [[ ! -x "$runtime" ]]; then echo "Run ./BuildLinux.sh $rid first, or use the prebuilt Linux package." >&2; exit 1; fi
exec "$runtime" --root "$game_dir" "$@"
