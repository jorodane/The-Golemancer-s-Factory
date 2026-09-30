#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
dotnet msbuild tools/BuildPacks.proj -t:Build -m:1 -nologo -v:minimal "$@"
