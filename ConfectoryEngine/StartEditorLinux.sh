#!/usr/bin/env bash
set -euo pipefail
engine_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
case "$(uname -m)" in x86_64) rid=linux-x64;; aarch64|arm64) rid=linux-arm64;; *) echo 'Unsupported CPU architecture' >&2; exit 2;; esac
exec "$engine_root/editor/Builds/Linux/$rid/start.sh" --engine-root "$engine_root" --dotnet "${CONFECTORY_DOTNET:-dotnet}" "$@"
